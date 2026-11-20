using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
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
        /// Shows the dialog for one optional model that the project did not enable. On consent it enables the model for
        /// every owner that declares it as not enabled (or only for <paramref name="packageId"/>) and starts its sync.
        /// Returns true when the model is enabled and its sync started.
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
            var owners = string.Join(", ", dependents
                .Select(dependent => ModelStatus.GetOwnerDisplayName(dependent.Dependency))
                .Distinct(StringComparer.Ordinal));
            var size = UiKit.FormatBytes(item.SourceSizeBytes);
            var download = string.IsNullOrWhiteSpace(size) ? "Download size: unknown until AI Model Hub asks Hugging Face." : $"Download size: {size}.";
            if (!EditorUtility.DisplayDialog(
                    "Enable Optional Model",
                    $"{item.Repository} is an optional model of {owners}. It is not enabled in this project, so it is not downloaded.\n\n" +
                    $"Enable it and download it now? {download}\n\n" +
                    "The choice is saved in this project's settings, so builds include the model on every machine.",
                    "Enable and Download",
                    "Cancel"))
            {
                return false;
            }

            EditorOperations.SetOptionalModelEnabled(dependents.Select(dependent => dependent.Dependency), true);
            var start = BackgroundSyncManager.StartForRepository(item.Repository, item.RequestedRevision, SyncOperationMode.Full);
            return start.Started || start.AlreadyRunning;
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
