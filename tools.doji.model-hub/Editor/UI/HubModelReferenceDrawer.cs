using System;
using System.Collections.Generic;
using System.Linq;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// The model files a <see cref="HubModelReference"/> picker offers: every model file the package declares, and
    /// whether this project includes it.
    /// </summary>
    internal static class HubModelPicker
    {
        internal enum FileState
        {
            /// <summary>The project includes the file and it is set up.</summary>
            Included,

            /// <summary>An optional file the project did not turn on.</summary>
            NotTurnedOn,

            /// <summary>The project uses the file, but it is not in the cache yet.</summary>
            NeedsDownload,

            /// <summary>The file is downloaded, but its ONNX model waits for a conversion to Sentis.</summary>
            NeedsConversion,

            /// <summary>The file is downloaded; only this project's quick import step is missing.</summary>
            NeedsSetup
        }

        internal sealed class Entry
        {
            public string PackageId;
            public string OwnerName;
            public string RepoId;
            public string Revision;
            public string RepoPath;
            public string Label;
            public FileState State;
            public ConversionCause ConversionCause;
            public DiscoveredModelDependency Dependency;

            public string DisplayName => HubModelFile.GetDisplayName(Label, RepoPath);

            public bool Matches(string repoId, string repoPath)
            {
                return string.Equals(RepoId, repoId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(RepoPath, repoPath, StringComparison.Ordinal);
            }
        }

        private static Entry[] _entries;

        static HubModelPicker()
        {
            BackgroundSyncManager.SyncStateChanged += Invalidate;
            EditorApplication.projectChanged += Invalidate;
        }

        public static void Invalidate()
        {
            _entries = null;
        }

        /// <summary>
        /// Lists the model files of one package, or of every package when <paramref name="packageId"/> is null.
        /// </summary>
        public static IReadOnlyList<Entry> GetEntries(string packageId)
        {
            _entries ??= Build(EditorOperations.CreateStatusReport(), Hub.GetModelFiles(null));
            return _entries
                .Where(entry => packageId == null || string.Equals(entry.PackageId, packageId, StringComparison.Ordinal))
                .ToArray();
        }

        internal static Entry[] Build(StatusReport report, IReadOnlyList<HubModelFile> includedFiles)
        {
            var included = new HashSet<string>(
                (includedFiles ?? Array.Empty<HubModelFile>()).Select(file => Key(file.PackageId, file.RepoId, file.RepoPath)),
                StringComparer.OrdinalIgnoreCase);
            var entries = new List<Entry>();
            foreach (var status in report?.DependencyStatuses ?? Array.Empty<DependencyStatus>())
            {
                var dependency = status.Dependency;
                var selected = new HashSet<string>(dependency.SelectedFiles, StringComparer.Ordinal);
                var files = dependency.HasExplicitFileSelection
                    ? dependency.FileEntries.Where(file => ModelFiles.IsModelFile(file.Path)).ToArray()
                    : (status.Shared?.RuntimeFiles ?? Array.Empty<ImportedRuntimeFileMetadata>())
                        .Select(file => new ModelDependencyFileDefinition(file.RepoPath))
                        .ToArray();
                foreach (var file in files)
                {
                    var state = included.Contains(Key(dependency.PackageId, dependency.Repository, file.Path))
                        ? FileState.Included
                        : file.Optional && !selected.Contains(file.Path) ? FileState.NotTurnedOn
                        : GetSetupState(status, file.Path);
                    entries.Add(new Entry
                    {
                        PackageId = dependency.PackageId,
                        OwnerName = ModelStatus.GetOwnerDisplayName(dependency),
                        RepoId = dependency.Repository,
                        Revision = dependency.Revision,
                        RepoPath = file.Path,
                        Label = file.Label,
                        Dependency = dependency,
                        State = state,
                        ConversionCause = state == FileState.NeedsConversion ? status.ConversionCause : ConversionCause.None
                    });
                }
            }

            return entries.ToArray();
        }

        private static FileState GetSetupState(DependencyStatus status, string repoPath)
        {
            if (!status.IsImportPending)
            {
                return FileState.NeedsDownload;
            }

            return status.PendingConversions.Contains(repoPath, StringComparer.Ordinal)
                ? FileState.NeedsConversion
                : FileState.NeedsSetup;
        }

        /// <summary>
        /// Downloads, converts, and sets up the file's repository, like the button in AI Model Hub.
        /// </summary>
        public static void SetUp(Entry entry)
        {
            BackgroundSyncManager.StartForRepository(entry.RepoId, entry.Revision, SyncOperationMode.Full);
            Invalidate();
        }

        /// <summary>
        /// Turns on an optional file for this project and starts downloading it.
        /// </summary>
        public static void TurnOn(Entry entry)
        {
            new ProjectOptionalFileSelectionSource().SetEnabled(entry.Dependency, entry.RepoPath, true);
            Invalidate();
            ModelHubWindow.RefreshOpenWindows();
            BackgroundSyncManager.StartForRepository(entry.RepoId, entry.Revision, SyncOperationMode.Full);
        }

        private static string Key(string packageId, string repoId, string repoPath)
        {
            return $"{packageId}\n{repoId}\n{repoPath}";
        }
    }

    /// <summary>
    /// Inspector picker for <see cref="HubModelReference"/> fields: a dropdown of the package's model files, plus a
    /// choice for a model asset of your own. A status line under the dropdown says when the chosen file does not
    /// load yet, and offers the fix.
    /// </summary>
    [CustomPropertyDrawer(typeof(HubModelReference))]
    [CustomPropertyDrawer(typeof(HubModelAttribute))]
    internal sealed class HubModelReferenceDrawer : PropertyDrawer
    {
        internal const string NoneChoice = "None";
        internal const string AssetChoice = "Model asset of your own…";
        private const string NotDownloadedText = "Not downloaded yet";
        private const string DownloadTooltip =
            "Download fetches the model from Hugging Face and sets it up for this project. Builds then include it.";
        private const string StyleSheetPath = UiKit.StylesFolder + "HubModelField.uss";

        internal enum StatusKind
        {
            None,
            NotIncluded,
            NeedsDownload,
            NeedsConversion,
            NeedsSetup,
            Working,
            NotDeclared
        }

        /// <summary>
        /// What the status line under the dropdown shows.
        /// </summary>
        internal readonly struct StatusInfo
        {
            public StatusInfo(StatusKind kind, string text, string tooltip, string action)
            {
                Kind = kind;
                Text = text;
                Tooltip = tooltip;
                Action = action;
            }

            public StatusKind Kind { get; }
            public string Text { get; }
            public string Tooltip { get; }
            public string Action { get; }
        }

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement();
            root.AddToClassList("hm-field");
            root.AddToClassList(EditorGUIUtility.isProSkin ? "hm-field--dark" : "hm-field--light");
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            if (styleSheet != null)
            {
                root.styleSheets.Add(styleSheet);
            }

            var packageId = (attribute as HubModelAttribute)?.PackageId;
            var packageProperty = property.FindPropertyRelative("_packageId");
            var repoProperty = property.FindPropertyRelative("_repoId");
            var pathProperty = property.FindPropertyRelative("_repoPath");
            var assetProperty = property.FindPropertyRelative("_modelAsset");
            if (packageProperty == null || repoProperty == null || pathProperty == null || assetProperty == null)
            {
                root.Add(new HelpBox("[HubModel] works only on HubModelReference fields.", HelpBoxMessageType.Error));
                return root;
            }

            var entries = HubModelPicker.GetEntries(packageId);
            var choices = BuildChoices(entries, packageId == null);
            var dropdown = new DropdownField(property.displayName, choices.Select(choice => choice.Text).ToList(), 0);
            dropdown.AddToClassList(BaseField<string>.alignedFieldUssClassName);
            var assetField = new ObjectField("Model Asset") { objectType = typeof(ModelAsset), allowSceneObjects = false };
            assetField.AddToClassList(BaseField<UnityEngine.Object>.alignedFieldUssClassName);
            assetField.BindProperty(assetProperty);

            var status = new VisualElement();
            status.AddToClassList("hm-status");
            var statusText = new Label();
            statusText.AddToClassList("hm-status__text");
            var track = new VisualElement();
            track.AddToClassList("hm-status__track");
            var fill = new VisualElement();
            fill.AddToClassList("hm-status__fill");
            track.Add(fill);
            HubModelPicker.Entry current = null;
            var statusButton = new Button();
            statusButton.AddToClassList("hm-status__button");
            status.Add(statusText);
            status.Add(track);
            status.Add(statusButton);

            root.Add(dropdown);
            root.Add(assetField);
            root.Add(status);

            // Line the status up with the dropdown's input, the way Unity indents a field's value.
            // Unity sizes aligned labels after the first layout, so follow the input whenever it moves.
            var input = dropdown.Q(className: BaseField<string>.inputUssClassName) ?? dropdown;
            void Align()
            {
                var offset = input.worldBound.xMin - root.worldBound.xMin;
                if (!float.IsNaN(offset) && offset > 0f)
                {
                    status.style.marginLeft = offset;
                }
            }

            input.RegisterCallback<GeometryChangedEvent>(_ => Align());
            root.RegisterCallback<GeometryChangedEvent>(_ => Align());

            var wasSyncing = BackgroundSyncManager.IsRunning;

            void Refresh()
            {
                property.serializedObject.UpdateIfRequiredOrScript();
                entries = HubModelPicker.GetEntries(packageId);
                choices = BuildChoices(entries, packageId == null);
                var isAsset = assetProperty.objectReferenceValue != null || dropdown.value == AssetChoice;
                current = isAsset ? null : entries.FirstOrDefault(entry => entry.Matches(repoProperty.stringValue, pathProperty.stringValue));
                var choiceTexts = choices.Select(choice => choice.Text).ToList();
                string text;
                if (isAsset)
                {
                    text = AssetChoice;
                }
                else if (current != null)
                {
                    text = choices.First(choice => choice.Entry == current).Text;
                }
                else if (string.IsNullOrWhiteSpace(pathProperty.stringValue))
                {
                    text = NoneChoice;
                }
                else
                {
                    text = $"{HubModelFile.GetDisplayName(null, pathProperty.stringValue)} (not in the manifest)";
                    choiceTexts.Add(text);
                }

                dropdown.choices = choiceTexts;
                dropdown.SetValueWithoutNotify(text);
                dropdown.tooltip = isAsset || string.IsNullOrWhiteSpace(pathProperty.stringValue)
                    ? null
                    : $"{repoProperty.stringValue}:{pathProperty.stringValue}";
                assetField.style.display = isAsset ? DisplayStyle.Flex : DisplayStyle.None;
                UpdateStatus();
            }

            void UpdateStatus()
            {
                var isAsset = assetField.style.display == DisplayStyle.Flex;
                BackgroundSyncManager.TryGetProgress(out var progress, out _);
                var info = GetStatus(isAsset ? null : current, isAsset ? null : pathProperty.stringValue, BackgroundSyncManager.IsRunning, progress);
                status.style.display = info.Kind == StatusKind.None ? DisplayStyle.None : DisplayStyle.Flex;
                status.EnableInClassList("hm-status--warning", info.Kind == StatusKind.NotDeclared);
                status.EnableInClassList("hm-status--info", info.Kind != StatusKind.NotDeclared);
                status.tooltip = info.Tooltip;
                statusText.text = info.Text;
                track.style.display = info.Kind == StatusKind.Working ? DisplayStyle.Flex : DisplayStyle.None;
                fill.style.width = Length.Percent(Mathf.Clamp01(progress) * 100f);
                statusButton.text = info.Action;
                statusButton.style.display = info.Action == null ? DisplayStyle.None : DisplayStyle.Flex;
            }

            void RunAction(HubModelPicker.Entry entry)
            {
                if (entry == null)
                {
                    return;
                }

                switch (GetStatus(entry, entry.RepoPath, BackgroundSyncManager.IsRunning, 0f).Kind)
                {
                    case StatusKind.NotIncluded:
                        HubModelPicker.TurnOn(entry);
                        break;
                    case StatusKind.NeedsDownload:
                    case StatusKind.NeedsConversion:
                    case StatusKind.NeedsSetup:
                        HubModelPicker.SetUp(entry);
                        break;
                    case StatusKind.Working:
                        ModelHubWindow.OpenWindow(entry.RepoId, entry.Revision);
                        break;
                }

                Refresh();
            }

            dropdown.RegisterValueChangedCallback(evt =>
            {
                var choice = choices.FirstOrDefault(item => item.Text == evt.newValue);
                if (evt.newValue == AssetChoice)
                {
                    SetFile(property, null, null, null);
                }
                else if (choice != null)
                {
                    assetProperty.objectReferenceValue = null;
                    SetFile(property, choice.Entry?.PackageId, choice.Entry?.RepoId, choice.Entry?.RepoPath);
                }

                Refresh();
            });
            assetField.RegisterValueChangedCallback(_ => Refresh());
            statusButton.clicked += () => RunAction(current);

            // Follow a download that the status line started, and show the file as ready when it ends.
            root.schedule.Execute(() =>
            {
                var syncing = BackgroundSyncManager.IsRunning;
                if (syncing != wasSyncing)
                {
                    wasSyncing = syncing;
                    Refresh();
                }
                else if (syncing && status.style.display == DisplayStyle.Flex)
                {
                    UpdateStatus();
                }
            }).Every(250);

            Refresh();
            return root;
        }

        /// <summary>
        /// Chooses the status line for the selected file. Nothing shows for a file that loads, a model asset, or None.
        /// </summary>
        internal static StatusInfo GetStatus(HubModelPicker.Entry entry, string repoPath, bool syncing, float progress)
        {
            if (entry == null)
            {
                return string.IsNullOrWhiteSpace(repoPath)
                    ? default
                    : new StatusInfo(
                        StatusKind.NotDeclared,
                        "No package manifest in this project lists this file",
                        $"'{repoPath}' does not load. Choose another model, or add the file to a package manifest.",
                        null);
            }

            var percent = progress > 0f ? $" {Mathf.RoundToInt(Mathf.Clamp01(progress) * 100f)}%" : string.Empty;
            switch (entry.State)
            {
                // To the user an optional file that is not turned on is a model that is not downloaded yet.
                // Its Download button turns the file on first.
                case HubModelPicker.FileState.NotTurnedOn:
                    return new StatusInfo(StatusKind.NotIncluded, NotDownloadedText, DownloadTooltip, "Download");
                case HubModelPicker.FileState.NeedsDownload when syncing:
                    return Working($"Downloading…{percent}");
                case HubModelPicker.FileState.NeedsDownload:
                    return new StatusInfo(StatusKind.NeedsDownload, NotDownloadedText, DownloadTooltip, "Download");
                case HubModelPicker.FileState.NeedsConversion when syncing:
                    return Working($"Converting…{percent}");
                case HubModelPicker.FileState.NeedsConversion:
                    return new StatusInfo(
                        StatusKind.NeedsConversion,
                        entry.ConversionCause switch
                        {
                            ConversionCause.SettingsChanged => "Import settings changed, convert it again",
                            ConversionCause.FormatChanged => "New Sentis model format, convert it again",
                            _ => "Downloaded, needs a conversion to Sentis",
                        },
                        "Convert turns the ONNX model into a Sentis model for this project. It takes CPU, memory, and time for large models.",
                        "Convert");
                case HubModelPicker.FileState.NeedsSetup when syncing:
                    return Working($"Setting up…{percent}");
                case HubModelPicker.FileState.NeedsSetup:
                    return new StatusInfo(
                        StatusKind.NeedsSetup,
                        "Downloaded, not set up for this project yet",
                        "Set up adds the downloaded model to this project. It takes a moment.",
                        "Set up");
                default:
                    return default;
            }
        }

        private static StatusInfo Working(string text)
        {
            return new StatusInfo(StatusKind.Working, text, "AI Model Hub works on the model in the background.", "Details");
        }

        private sealed class Choice
        {
            public string Text;
            public HubModelPicker.Entry Entry;
        }

        private static List<Choice> BuildChoices(IReadOnlyList<HubModelPicker.Entry> entries, bool showPackage)
        {
            var showRepository = entries.Select(entry => entry.RepoId).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
            var choices = new List<Choice> { new() { Text = NoneChoice } };
            foreach (var entry in entries)
            {
                var parts = new List<string>();
                if (showPackage)
                {
                    parts.Add(entry.OwnerName);
                }

                if (showRepository)
                {
                    parts.Add(entry.RepoId.Substring(entry.RepoId.IndexOf('/') + 1));
                }

                parts.Add(entry.DisplayName);
                var text = string.Join(" · ", parts);

                if (entry.State is HubModelPicker.FileState.NotTurnedOn or HubModelPicker.FileState.NeedsDownload)
                {
                    text += " (not downloaded)";
                }

                // Dropdown choices must be unique; two files with one label fall back to their paths.
                if (choices.Any(choice => choice.Text == text))
                {
                    text = $"{text} ({entry.RepoPath})";
                }

                choices.Add(new Choice { Text = text, Entry = entry });
            }

            choices.Add(new Choice { Text = AssetChoice });
            return choices;
        }

        private static void SetFile(SerializedProperty property, string packageId, string repoId, string repoPath)
        {
            property.FindPropertyRelative("_packageId").stringValue = packageId ?? string.Empty;
            property.FindPropertyRelative("_repoId").stringValue = repoId ?? string.Empty;
            property.FindPropertyRelative("_repoPath").stringValue = repoPath ?? string.Empty;
            property.serializedObject.ApplyModifiedProperties();
        }
    }
}
