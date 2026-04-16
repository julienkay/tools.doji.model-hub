using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Shared editor workflows for inspecting and syncing HF Hub dependencies.
    /// </summary>
    internal static class HfHubEditorOperations
    {
        /// <summary>
        /// Canonical Project Settings path for the package's editor surface.
        /// </summary>
        public const string ProjectSettingsPath = "Project/Doji/AI/AI Model Hub";

        /// <summary>
        /// Evaluates current local dependency status for all discovered package manifests.
        /// </summary>
        public static HfHubStatusReport CreateStatusReport()
        {
            var discoveryReport = HfHubPackageDiscovery.DiscoverInstalledPackageManifests();
            return new HfHubStatusService().BuildStatusReport(discoveryReport);
        }

        /// <summary>
        /// Starts or resumes background sync using editor progress UI instead of blocking the editor.
        /// </summary>
        public static void StartBackgroundSync()
        {
            HfHubBackgroundSyncManager.StartOrResume();
        }

        /// <summary>
        /// Builds short status summary for dialogs and settings UI.
        /// </summary>
        public static string BuildStatusDialogMessage(HfHubStatusReport report)
        {
            if (report.DependencyStatuses.Count == 0 && report.DiscoveryIssues.Count == 0)
            {
                return "No hf-hub package manifests were found in the installed packages.";
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
            builder.AppendLine($"Discovery issues: {report.DiscoveryIssues.Count}");
            return builder.ToString().TrimEnd();
        }

        /// <summary>
        /// Builds detailed status output for Console and settings UI.
        /// </summary>
        public static string BuildStatusConsoleReport(HfHubStatusReport report)
        {
            var builder = new StringBuilder();
            builder.AppendLine("HF Hub status report");

            if (report.DependencyStatuses.Count == 0 && report.DiscoveryIssues.Count == 0)
            {
                builder.AppendLine("- No hf-hub package manifests were found in the installed packages.");
                return builder.ToString().TrimEnd();
            }

            foreach (var packageGroup in report.DependencyStatuses.GroupBy(status => status.Dependency.PackageId).OrderBy(group => group.Key))
            {
                var firstStatus = packageGroup.First();
                builder.AppendLine($"- {firstStatus.Dependency.PackageDisplayName} ({firstStatus.Dependency.PackageId} {firstStatus.Dependency.PackageVersion})");

                foreach (var status in packageGroup.OrderBy(item => item.Dependency.ModelId))
                {
                    builder.AppendLine($"  - [{status.State}] {status.Dependency.ModelId}");
                    builder.AppendLine($"    {status.Message}");
                }
            }

            if (report.DiscoveryIssues.Count > 0)
            {
                builder.AppendLine("Discovery issues:");
                foreach (var issue in report.DiscoveryIssues.OrderBy(issue => issue.PackageId).ThenBy(issue => issue.ManifestPath))
                {
                    builder.AppendLine($"- {issue.PackageId}: {issue.Message}");
                }
            }

            return builder.ToString().TrimEnd();
        }

        /// <summary>
        /// Builds short sync summary for modal dialog.
        /// </summary>
        public static string BuildSyncDialogMessage(HfHubSyncReport report)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"Synced models: {report.SuccessCount}");
            builder.AppendLine($"Failed models: {report.FailureCount}");
            builder.AppendLine($"Discovery issues: {report.DiscoveryIssues.Count}");

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
        public static string BuildSyncConsoleReport(HfHubSyncReport report)
        {
            var builder = new StringBuilder();
            builder.AppendLine("HF Hub sync report");

            foreach (var result in report.ModelResults.OrderBy(result => result.Dependency.PackageId).ThenBy(result => result.Dependency.ModelId))
            {
                if (result.Success)
                {
                    builder.AppendLine($"- OK {result.Dependency.PackageId}/{result.Dependency.ModelId}");
                    builder.AppendLine($"  raw: {result.CachedFile.FilePath}");
                    builder.AppendLine($"  sentis: {result.ImportedArtifact.SentisAssetPath}");
                }
                else
                {
                    builder.AppendLine($"- FAIL {result.Dependency.PackageId}/{result.Dependency.ModelId}");
                    builder.AppendLine($"  {result.ErrorMessage}");
                }
            }

            if (report.DiscoveryIssues.Count > 0)
            {
                builder.AppendLine("Discovery issues:");
                foreach (var issue in report.DiscoveryIssues.OrderBy(issue => issue.PackageId).ThenBy(issue => issue.ManifestPath))
                {
                    builder.AppendLine($"- {issue.PackageId}: {issue.Message}");
                }
            }

            return builder.ToString().TrimEnd();
        }
    }
}
