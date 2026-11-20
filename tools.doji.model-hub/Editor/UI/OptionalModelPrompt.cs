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
        /// The prompt text. The primary button names what enabling does (download, convert) and the download size.
        /// </summary>
        internal static string BuildMessage(string repository)
        {
            return $"{repository} is an optional model that this project does not use yet.";
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
