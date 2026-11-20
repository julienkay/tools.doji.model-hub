using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Shared editor workflows for inspecting and syncing AI Model Hub dependencies.
    /// </summary>
    internal static class EditorOperations
    {
        private static readonly IProjectModelRecipeSource RecipeSource = new ProjectModelRecipeSource();

        /// <summary>
        /// Canonical Project Settings path for the package's editor surface.
        /// </summary>
        public const string ProjectSettingsPath = "Project/Doji/AI/AI Model Hub";

        /// <summary>
        /// Evaluates current local dependency status for all discovered package and project manifests.
        /// </summary>
        public static StatusReport CreateStatusReport()
        {
            var discoveryReport = EditorManifestDiscovery.DiscoverInstalledManifests();
            return new StatusService().BuildStatusReport(discoveryReport);
        }

        /// <summary>
        /// Builds repository-centric inventory data for the Model Hub window.
        /// </summary>
        public static ModelHubReport CreateModelHubReport()
        {
            return new ModelHubService().BuildReport(CreateStatusReport());
        }

        /// <summary>
        /// Starts or resumes background sync using editor progress UI instead of blocking the editor.
        /// </summary>
        public static void StartBackgroundSync()
        {
            BackgroundSyncManager.StartOrResume();
        }

        /// <summary>
        /// Starts background sync for one repository entry from Model Hub.
        /// </summary>
        public static void StartBackgroundSync(ModelHubItem item)
        {
            if (item == null)
            {
                return;
            }

            if (item.HasRevisionConflict)
            {
                UnityEngine.Debug.LogWarning(
                    $"AI Model Hub cannot set up '{item.Repository}': manifests request different revisions of it. Change them to request one revision first.");
                return;
            }

            if (item.IsNotEnabled)
            {
                UnityEngine.Debug.LogWarning(
                    $"AI Model Hub: '{item.Repository}' is an optional model that this project has not enabled. Enable it first.");
                return;
            }

            if (item.HasSelectionRequired)
            {
                UnityEngine.Debug.LogWarning(
                    $"AI Model Hub cannot set up '{item.Repository}' until you turn on at least one of its optional model files in the Files tab.");
                return;
            }

            BackgroundSyncManager.StartForRepository(item.Repository, item.RequestedRevision, SyncOperationMode.Full);
        }

        /// <summary>
        /// Enables or disables optional models for their owners in this project, and refreshes every surface that shows
        /// them. Models that the manifest does not mark optional are left out. Does not start a sync. Returns one line
        /// per change.
        /// </summary>
        public static string[] SetOptionalModelEnabled(
            System.Collections.Generic.IEnumerable<DiscoveredModelDependency> dependencies,
            bool isEnabled)
        {
            var source = new ProjectOptionalFileSelectionSource();
            var changes = (dependencies ?? Enumerable.Empty<DiscoveredModelDependency>())
                .Where(dependency => dependency != null && dependency.IsOptionalModel)
                .Where(dependency => source.SetModelEnabled(dependency, isEnabled))
                .Select(dependency => $"{(isEnabled ? "Enabled" : "Disabled")} optional model '{dependency.Repository}' for {dependency.PackageId}.")
                .ToArray();
            if (changes.Length > 0)
            {
                DiscoveredDependencyCache.Invalidate();
                EditorRuntimeCatalog.Invalidate();
                ModelHubWindow.RefreshOpenWindows();
                ProjectSettingsProvider.Refresh();
                BackgroundSyncManager.NotifyStateChanged();
            }

            return changes;
        }

        /// <summary>
        /// Removes one repository entry's generated runtime artifacts from project.
        /// </summary>
        public static void RemoveImportedArtifacts(ModelHubItem item)
        {
            if (item == null)
            {
                return;
            }

            if (item.HasRevisionConflict)
            {
                UnityEngine.Debug.LogWarning(
                    $"AI Model Hub cannot remove '{item.Repository}' from this project while manifests request different revisions of it.");
                return;
            }

            new ProjectRemovalService().Remove(item);
        }

        /// <summary>
        /// Removes one repository entry's raw cache snapshot from this machine while keeping project assets.
        /// </summary>
        public static void RemoveRawCache(ModelHubItem item)
        {
            if (item == null)
            {
                return;
            }

            if (item.HasRevisionConflict)
            {
                UnityEngine.Debug.LogWarning(
                    $"AI Model Hub cannot remove '{item.Repository}' from the cache while manifests request different revisions of it.");
                return;
            }

            new ProjectRemovalService().RemoveRawCache(item);
        }

        /// <summary>
        /// Persists repo file recipe edits and starts background sync so generated artifacts match new settings.
        /// </summary>
        public static void ApplyProjectOverridesAndReimport(ModelHubItem item, System.Collections.Generic.IReadOnlyDictionary<string, OnnxConversionRecipe> recipesByRepoPath)
        {
            if (item == null || recipesByRepoPath == null)
            {
                return;
            }

            ApplyProjectOverrides(item, recipesByRepoPath);
            StartBackgroundSync(item);
        }

        /// <summary>
        /// Persists repo file recipe edits as project overrides, without starting a sync. Returns the repository
        /// paths it wrote.
        /// </summary>
        public static string[] ApplyProjectOverrides(ModelHubItem item, System.Collections.Generic.IReadOnlyDictionary<string, OnnxConversionRecipe> recipesByRepoPath)
        {
            if (item == null || recipesByRepoPath == null)
            {
                return System.Array.Empty<string>();
            }

            var written = new System.Collections.Generic.List<string>();
            foreach (var file in item.Files)
            {
                if (!recipesByRepoPath.TryGetValue(file.RepoPath, out var recipe))
                {
                    continue;
                }

                RecipeSource.SetProjectOverride(item.Repository, file.RepoPath, recipe);
                written.Add(file.RepoPath);
            }

            return written.ToArray();
        }

        /// <summary>
        /// Builds short status summary for dialogs and settings UI.
        /// </summary>
        public static string BuildStatusDialogMessage(StatusReport report)
        {
            if (report.DependencyStatuses.Count == 0 && report.DiscoveryIssues.Count == 0)
            {
                return "No package or project manifest lists an AI model.";
            }

            var packageCount = report.DependencyStatuses
                .Select(status => status.Dependency.PackageId)
                .Concat(report.DiscoveryIssues.Select(issue => issue.PackageId))
                .Distinct()
                .Count();

            var builder = new StringBuilder();
            builder.AppendLine($"Packages with manifests: {packageCount}");
            builder.AppendLine($"Ready models: {report.ReadyCount}");
            builder.AppendLine($"Missing models: {report.MissingCount}");
            builder.AppendLine($"Stale models: {report.StaleCount}");
            builder.AppendLine($"Failed models: {report.FailedCount}");
            if (report.NotEnabledCount > 0)
            {
                builder.AppendLine($"Optional models not enabled: {report.NotEnabledCount}");
            }

            builder.AppendLine($"Manifest errors: {report.DiscoveryIssues.Count}");
            return builder.ToString().TrimEnd();
        }

        /// <summary>
        /// Builds short reminder text for editor-session console nudges when dependencies still need action.
        /// </summary>
        public static string BuildUnresolvedDependencyReminder(StatusReport report)
        {
            if (report == null || !report.NeedsStartupPrompt)
            {
                return "AI Model Hub: every model is ready.";
            }

            var unresolvedCount = report.DependencyStatuses.Count(status => status.NeedsStartupPrompt);
            var issueCount = report.DiscoveryIssues.Count;
            var builder = new StringBuilder();
            builder.Append("AI Model Hub: some models need setup.");

            if (unresolvedCount > 0 || issueCount > 0)
            {
                builder.Append(' ');
                builder.Append('(');
                builder.Append($"{unresolvedCount} ");
                builder.Append(unresolvedCount == 1 ? "model" : "models");

                if (issueCount > 0)
                {
                    builder.Append($", {issueCount} manifest error");
                    if (issueCount != 1)
                    {
                        builder.Append('s');
                    }
                }

                builder.Append(')');
            }

            builder.Append($" Open Window > AI Model Hub or {ProjectSettingsPath} to set them up.");
            return builder.ToString();
        }

        /// <summary>
        /// Builds detailed status output for Console and settings UI.
        /// </summary>
        public static string BuildStatusConsoleReport(StatusReport report)
        {
            var builder = new StringBuilder();
            builder.AppendLine("AI Model Hub status report");

            if (report.DependencyStatuses.Count == 0 && report.DiscoveryIssues.Count == 0)
            {
                builder.AppendLine("- No package or project manifest lists an AI model.");
                return builder.ToString().TrimEnd();
            }

            foreach (var packageGroup in report.DependencyStatuses.GroupBy(status => status.Dependency.PackageId).OrderBy(group => group.Key))
            {
                var firstStatus = packageGroup.First();
                var ownerLabel = ProjectManifestDiscovery.IsProjectManifestOwnerId(firstStatus.Dependency.PackageId)
                    ? ProjectManifestDiscovery.GetOwnerDisplayName(firstStatus.Dependency.PackageId, firstStatus.Dependency.PackageDisplayName)
                    : $"{firstStatus.Dependency.PackageDisplayName} ({firstStatus.Dependency.PackageId} {firstStatus.Dependency.PackageVersion})";
                builder.AppendLine($"- {ownerLabel}");

                foreach (var status in packageGroup.OrderBy(item => item.Dependency.ModelId))
                {
                    builder.AppendLine($"  - [{status.State}] {status.Dependency.ModelId}");
                    builder.AppendLine($"    {status.MaintainerMessage}");
                }
            }

            if (report.DiscoveryIssues.Count > 0)
            {
                builder.AppendLine("Manifest errors:");
                foreach (var issue in report.DiscoveryIssues.OrderBy(issue => issue.PackageId).ThenBy(issue => issue.ManifestPath))
                {
                    builder.AppendLine($"- {ProjectManifestDiscovery.GetOwnerDisplayName(issue.PackageId)}: {issue.Message}");
                }
            }

            return builder.ToString().TrimEnd();
        }

        /// <summary>
        /// Builds short sync summary for modal dialog.
        /// </summary>
        public static string BuildSyncDialogMessage(SyncReport report)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"Synced models: {report.SuccessCount}");
            builder.AppendLine($"Failed models: {report.FailureCount}");
            builder.AppendLine($"Manifest errors: {report.DiscoveryIssues.Count}");

            if (report.FailureCount > 0 || report.DiscoveryIssues.Count > 0)
            {
                builder.AppendLine();
                builder.Append("Check Console for detailed sync output.");
            }

            return builder.ToString().TrimEnd();
        }

        /// <summary>
        /// Builds detailed sync output for Console.
        /// </summary>
        public static string BuildSyncConsoleReport(SyncReport report)
        {
            var builder = new StringBuilder();
            builder.AppendLine("AI Model Hub sync report");

            foreach (var result in report.ModelResults.OrderBy(result => result.Dependency.PackageId).ThenBy(result => result.Dependency.ModelId))
            {
                var ownerLabel = ProjectManifestDiscovery.GetOwnerDisplayName(
                    result.Dependency.PackageId,
                    result.Dependency.PackageDisplayName);
                if (result.Success)
                {
                    var rawSnapshotPath = result.CachedFile?.SnapshotRootPath;
                    var sentisArtifactPath = result.ImportedArtifact?.RuntimeFiles
                        .FirstOrDefault()
                        ?.FilePath;

                    builder.AppendLine($"- OK {ownerLabel}/{result.Dependency.ModelId}");
                    builder.AppendLine($"  downloaded files: {rawSnapshotPath ?? "<unknown>"}");
                    builder.AppendLine($"  mode: {result.Mode}");
                    builder.AppendLine($"  runtime model: {sentisArtifactPath ?? "<none>"}");
                }
                else
                {
                    builder.AppendLine($"- FAIL {ownerLabel}/{result.Dependency.ModelId}");
                    builder.AppendLine($"  {result.ErrorMessage}");
                }
            }

            if (report.DiscoveryIssues.Count > 0)
            {
                builder.AppendLine("Manifest errors:");
                foreach (var issue in report.DiscoveryIssues.OrderBy(issue => issue.PackageId).ThenBy(issue => issue.ManifestPath))
                {
                    builder.AppendLine($"- {ProjectManifestDiscovery.GetOwnerDisplayName(issue.PackageId)}: {issue.Message}");
                }
            }

            return builder.ToString().TrimEnd();
        }
    }
}
