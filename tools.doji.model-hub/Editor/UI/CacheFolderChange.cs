using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// The two folders a user can choose.
    /// </summary>
    internal enum CacheFolderKind
    {
        /// <summary>
        /// The Hugging Face cache with the downloads.
        /// </summary>
        HubCache,

        /// <summary>
        /// AI Model Hub's data folder with the index and the converted models.
        /// </summary>
        DataRoot
    }

    /// <summary>
    /// Changes a folder from the Editor: asks whether to move the existing content, moves it, stores the new choice,
    /// and on request sets the user's Hugging Face variable so Python follows. Every step that changes the disk or the
    /// user's configuration asks first.
    /// </summary>
    [InitializeOnLoad]
    internal static class CacheFolderChange
    {
        private const string DialogTitle = "AI Model Hub";

        static CacheFolderChange()
        {
            CacheFolderSettings.Changed += OnFoldersChanged;
        }

        /// <summary>
        /// The folder a choice resolves to: the custom folder, else the default the environment variables decide.
        /// </summary>
        public static string GetEffectiveFolder(CacheFolderKind kind, string customFolder)
        {
            var custom = CacheFolderSettings.Normalize(customFolder);
            if (custom != null)
            {
                return custom;
            }

            return kind == CacheFolderKind.HubCache ? CachePaths.GetDefaultCacheRoot() : CachePaths.GetDefaultDataRoot();
        }

        /// <summary>
        /// The user variable that makes <c>huggingface_hub</c> use <paramref name="hubCacheFolder"/>: <c>HF_HOME</c> set
        /// to the parent when the folder is named <c>hub</c>, as in the Hugging Face layout, and no <c>HF_HUB_CACHE</c>
        /// overrides it. Otherwise <c>HF_HUB_CACHE</c> set to the folder.
        /// </summary>
        public static (string Name, string Value) GetPythonVariable(string hubCacheFolder, IEnvironmentVariables environment = null)
        {
            var env = environment ?? CacheFolders.Environment;
            var folder = CacheFolderSettings.Normalize(hubCacheFolder);
            var parent = Path.GetDirectoryName(folder);
            var hubCacheIsSet = !string.IsNullOrWhiteSpace(env.GetForNewPrograms(CachePaths.HubCacheEnvironmentVariable)) ||
                                !string.IsNullOrWhiteSpace(env.GetForNewPrograms(CachePaths.LegacyHubCacheEnvironmentVariable));
            return string.Equals(Path.GetFileName(folder), "hub", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(parent) && !hubCacheIsSet
                ? (CachePaths.HomeEnvironmentVariable, parent)
                : (CachePaths.HubCacheEnvironmentVariable, folder);
        }

        /// <summary>
        /// Applies a new choice for one folder. <paramref name="customFolder"/> null follows the default.
        /// </summary>
        public static void Apply(CacheFolderKind kind, string customFolder, bool setPythonVariable)
        {
            var current = CacheFolderSettings.Current;
            var choice = new CacheFolderChoice
            {
                HubCacheFolder = kind == CacheFolderKind.HubCache ? CacheFolderSettings.Normalize(customFolder) : current.HubCacheFolder,
                DataFolder = kind == CacheFolderKind.DataRoot ? CacheFolderSettings.Normalize(customFolder) : current.DataFolder
            };
            var oldFolder = kind == CacheFolderKind.HubCache ? CachePaths.GetCacheRoot() : CachePaths.GetDataRoot();
            var newFolder = GetEffectiveFolder(kind, customFolder);

            if (SamePath(oldFolder, newFolder))
            {
                Save(choice, kind, setPythonVariable);
                return;
            }

            if (BackgroundSyncManager.IsRunning)
            {
                EditorUtility.DisplayDialog(DialogTitle, "A sync is running. Change the folder after it ends, or cancel it first.", "OK");
                return;
            }

            CacheMovePlan plan;
            try
            {
                plan = CacheFolderMover.Plan(oldFolder, newFolder);
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                EditorUtility.DisplayDialog(DialogTitle, exception.Message, "OK");
                return;
            }

            if (plan.IsEmpty)
            {
                Save(choice, kind, setPythonVariable);
                return;
            }

            var what = kind == CacheFolderKind.HubCache ? "downloaded models" : "converted models and index";
            var how = plan.Method == CacheMoveMethod.Rename
                ? "Both folders are on the same drive, so the content is renamed into place at once."
                : "The new folder is on another drive. AI Model Hub copies the content, checks every file, and then asks before it deletes the old folder.";
            var answer = EditorUtility.DisplayDialogComplex(
                DialogTitle,
                $"'{oldFolder}' holds {UiKit.FormatBytes(plan.FileBytes)} of {what} ({plan.FileCount} files, {plan.LinkCount} links).\n\n" +
                $"Move it to '{newFolder}'?\n\n{how}\n\nDon't Move uses the new folder and leaves the old content where it is.",
                "Move",
                "Cancel",
                "Don't Move");
            switch (answer)
            {
                case 1:
                    return;
                case 2:
                    Save(choice, kind, setPythonVariable);
                    return;
            }

            if (plan.Method == CacheMoveMethod.Rename)
            {
                try
                {
                    var result = CacheFolderMover.Move(plan);
                    Save(choice, kind, setPythonVariable);
                    ReportKept(result);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    EditorUtility.DisplayDialog(DialogTitle, $"The move stopped: {exception.Message}\n\nThe folder setting is unchanged.", "OK");
                }

                return;
            }

            StartCopy(plan, choice, kind, setPythonVariable);
        }

        private static void StartCopy(CacheMovePlan plan, CacheFolderChoice choice, CacheFolderKind kind, bool setPythonVariable)
        {
            var cancellation = new CancellationTokenSource();
            var progressId = Progress.Start("AI Model Hub: move folder", $"{plan.Source} to {plan.Destination}");
            Progress.RegisterCancelCallback(progressId, () =>
            {
                cancellation.Cancel();
                return true;
            });
            var progress = new Progress<float>(value => Progress.Report(progressId, value));
            Task.Run(() => CacheFolderMover.Move(plan, progress, cancellation.Token)).ContinueWith(task => EditorMainThread.Post(() =>
            {
                Progress.Remove(progressId);
                cancellation.Dispose();
                if (task.IsCanceled || task.Exception?.GetBaseException() is OperationCanceledException)
                {
                    EditorUtility.DisplayDialog(DialogTitle, $"The move was canceled. The folder setting is unchanged. Files already copied stay in '{plan.Destination}'.", "OK");
                    return;
                }

                if (task.IsFaulted)
                {
                    EditorUtility.DisplayDialog(DialogTitle, $"The move stopped: {task.Exception?.GetBaseException().Message}\n\nThe folder setting is unchanged and the old folder was not deleted.", "OK");
                    return;
                }

                var result = task.Result;
                Save(choice, kind, setPythonVariable);
                if (EditorUtility.DisplayDialog(
                        DialogTitle,
                        $"Copied {UiKit.FormatBytes(result.CopiedBytes)} to '{plan.Destination}' and checked every file. AI Model Hub uses the new folder now.\n\n" +
                        $"Delete the old folder '{plan.Source}'? Other programs that still use it, such as Python with an old environment, then download again.",
                        "Delete Old Folder",
                        "Keep It"))
                {
                    try
                    {
                        CacheFolderMover.DeleteSource(result);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        EditorUtility.DisplayDialog(DialogTitle, $"Some files in '{plan.Source}' could not be deleted: {exception.Message}", "OK");
                    }
                }

                ReportKept(result);
            }), TaskScheduler.Default);
        }

        private static void Save(CacheFolderChoice choice, CacheFolderKind kind, bool setPythonVariable)
        {
            CacheFolderSettings.Save(choice);
            if (kind == CacheFolderKind.HubCache && setPythonVariable && choice.HubCacheFolder != null)
            {
                OfferPythonVariable(choice.HubCacheFolder);
            }
        }

        private static void OfferPythonVariable(string hubCacheFolder)
        {
            var environment = CacheFolders.Environment;
            var (name, value) = GetPythonVariable(hubCacheFolder, environment);
            if (!environment.CanReadForNewPrograms)
            {
                EditorUtility.DisplayDialog(
                    DialogTitle,
                    $"To make Python use this folder, set {name}={value} where your tools read it, for example in your shell profile. Apps started from the Dock or a launcher do not read shell profiles.",
                    "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    DialogTitle,
                    $"Set the user environment variable {name} to '{value}'?\n\nPython's huggingface_hub and other programs started from now on then use this cache folder. Programs that run now, including Unity Hub, keep their old environment until you restart them.",
                    $"Set {name}",
                    "Don't Set"))
            {
                return;
            }

            try
            {
                environment.SetForUser(name, value);
            }
            catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                EditorUtility.DisplayDialog(DialogTitle, $"{name} could not be set: {exception.Message}", "OK");
            }

            OnFoldersChanged();
        }

        private static void ReportKept(CacheMoveResult result)
        {
            if (result.KeptInDestination.Count == 0)
            {
                return;
            }

            UnityEngine.Debug.Log(
                $"AI Model Hub moved '{result.Plan.Source}' to '{result.Plan.Destination}'. {result.KeptInDestination.Count} files already existed there and were kept, for example {result.KeptInDestination[0]}.");
        }

        private static void OnFoldersChanged()
        {
            EditorRuntimeCatalog.Invalidate();
            ModelHubWindow.RefreshOpenWindows();
            ProjectSettingsProvider.Refresh();
            CacheFolderPreferences.Refresh();
        }

        private static bool SamePath(string left, string right)
        {
            var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
                comparison);
        }
    }
}
