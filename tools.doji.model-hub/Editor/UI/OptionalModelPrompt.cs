using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Asks the user to enable an optional model when code loads a file of it in the Editor. Batch mode and players
    /// never ask: the load fails with an error that says how to enable the model.
    /// </summary>
    internal static class OptionalModelPrompt
    {
        /// <summary>
        /// Shows the setup prompt for one optional model that the project did not enable, and blocks until the user
        /// decides. On consent it enables the model for every owner that declares it as not enabled (or only for
        /// <paramref name="packageId"/>) and starts its sync. Returns true when the model is enabled and its sync started.
        /// </summary>
        public static bool OfferEnable(string packageId, string repoId)
        {
            if (Application.isBatchMode)
            {
                return false;
            }

            var item = EditorOperations.CreateModelHubReport().Items.FirstOrDefault(candidate =>
                string.Equals(candidate.Repository, repoId, StringComparison.OrdinalIgnoreCase) &&
                candidate.NotEnabledDependents.Any(dependent => Matches(dependent, packageId)));
            if (item == null)
            {
                return false;
            }

            var dependents = item.NotEnabledDependents.Where(dependent => Matches(dependent, packageId)).ToArray();
            if (AutoPromptWindow.ShowOptionalModel(item, dependents) != AutoPromptWindow.OptionalModelChoice.Enable)
            {
                return false;
            }

            EditorOperations.SetOptionalModelEnabled(dependents.Select(dependent => dependent.Dependency), true);
            var start = BackgroundSyncManager.StartForRepository(item.Repository, item.RequestedRevision, SyncOperationMode.Full);
            return start.Started || start.AlreadyRunning;
        }

        /// <summary>
        /// The prompt text. It gives only the bytes that are not in the cache yet, and the disk space of the converted
        /// model when the model has ONNX files to convert.
        /// </summary>
        internal static string BuildMessage(string repository, long? pendingDownloadBytes, long? convertedSizeBytes)
        {
            string download;
            var size = UiKit.FormatBytes(pendingDownloadBytes);
            if (pendingDownloadBytes == 0)
            {
                download = "Its files are already on this machine, so nothing needs to download.";
            }
            else if (string.IsNullOrWhiteSpace(size))
            {
                download = "Enabling it downloads it from Hugging Face.";
            }
            else
            {
                download = $"Enabling it downloads {size} from Hugging Face.";
            }

            var converted = convertedSizeBytes > 0 ? UiKit.FormatBytes(convertedSizeBytes) : null;
            var conversion = string.IsNullOrWhiteSpace(converted)
                ? string.Empty
                : $" The converted model takes about {converted} more.";
            return $"{repository} is an optional model. This project does not use it yet. " +
                   $"{download}{conversion} " +
                   "The choice is saved in the project settings.";
        }

        /// <summary>
        /// Completes when the running sync ends, with true when it succeeded.
        /// </summary>
        public static async Task<bool> WaitForSyncAsync()
        {
            var run = await BackgroundSyncManager.WaitForRunToEndAsync();
            EditorRuntimeCatalog.Invalidate();
            return run == null || string.Equals(run.State, SyncRunStates.Succeeded, StringComparison.Ordinal);
        }

        private static bool Matches(DependencyStatus dependent, string packageId)
        {
            return packageId == null || string.Equals(dependent.Dependency.PackageId, packageId, StringComparison.Ordinal);
        }
    }
}
