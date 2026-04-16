using System.Linq;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Provides a minimal editor status surface for validating package manifest discovery during development.
    /// </summary>
    internal static class HfHubStatusMenu
    {
        [MenuItem("Tools/Doji/HF Hub/Show Status")]
        private static void ShowStatus()
        {
            var report = HfHubPackageDiscovery.DiscoverInstalledPackageManifests();
            var consoleReport = BuildConsoleReport(report);
            var dialogMessage = BuildDialogMessage(report);

            if (report.Issues.Count > 0)
            {
                Debug.LogWarning(consoleReport);
            }
            else
            {
                Debug.Log(consoleReport);
            }

            EditorUtility.DisplayDialog("HF Hub Status", dialogMessage, "OK");
        }

        [MenuItem("Tools/Doji/HF Hub/Sync Models")]
        private static void SyncModels()
        {
            var discoveryReport = HfHubPackageDiscovery.DiscoverInstalledPackageManifests();
            var syncService = new HfHubSyncService();

            HfHubSyncReport syncReport;
            try
            {
                syncReport = syncService.SyncAsync(discoveryReport, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (System.Exception exception)
            {
                Debug.LogError($"HF Hub sync failed: {exception.Message}");
                EditorUtility.DisplayDialog("HF Hub Sync", $"Sync failed.\n\n{exception.Message}", "OK");
                return;
            }

            var consoleReport = BuildSyncConsoleReport(syncReport);
            if (syncReport.FailureCount > 0 || syncReport.DiscoveryIssues.Count > 0)
            {
                Debug.LogWarning(consoleReport);
            }
            else
            {
                Debug.Log(consoleReport);
            }

            EditorUtility.DisplayDialog("HF Hub Sync", BuildSyncDialogMessage(syncReport), "OK");
        }

        private static string BuildDialogMessage(HfHubDiscoveryReport report)
        {
            if (report.Dependencies.Count == 0 && report.Issues.Count == 0)
            {
                return "No hf-hub package manifests were found in the installed packages.";
            }

            var packageCount = report.Dependencies
                .Select(dependency => dependency.PackageId)
                .Concat(report.Issues.Select(issue => issue.PackageId))
                .Distinct()
                .Count();

            var builder = new StringBuilder();
            builder.AppendLine($"Packages with manifests: {packageCount}");
            builder.AppendLine($"Discovered model dependencies: {report.Dependencies.Count}");
            builder.AppendLine($"Discovery issues: {report.Issues.Count}");

            if (report.Issues.Count > 0)
            {
                builder.AppendLine();
                builder.Append("Check the Console for detailed manifest validation errors.");
            }

            return builder.ToString().TrimEnd();
        }

        private static string BuildConsoleReport(HfHubDiscoveryReport report)
        {
            var builder = new StringBuilder();
            builder.AppendLine("HF Hub package manifest status");

            if (report.Dependencies.Count == 0 && report.Issues.Count == 0)
            {
                builder.AppendLine("- No hf-hub package manifests were found in the installed packages.");
                return builder.ToString().TrimEnd();
            }

            foreach (var packageGroup in report.Dependencies.GroupBy(dependency => dependency.PackageId).OrderBy(group => group.Key))
            {
                var firstDependency = packageGroup.First();
                builder.AppendLine($"- {firstDependency.PackageDisplayName} ({firstDependency.PackageId} {firstDependency.PackageVersion})");

                foreach (var dependency in packageGroup.OrderBy(model => model.ModelId))
                {
                    builder.AppendLine(
                        $"  - {dependency.ModelId}: {dependency.Repository}@{dependency.Revision} :: {dependency.SourceFile}");
                }
            }

            if (report.Issues.Count > 0)
            {
                builder.AppendLine("Issues:");
                foreach (var issue in report.Issues.OrderBy(issue => issue.PackageId).ThenBy(issue => issue.ManifestPath))
                {
                    builder.AppendLine($"- {issue.PackageId}: {issue.Message}");
                    builder.AppendLine($"  {issue.ManifestPath}");
                }
            }

            return builder.ToString().TrimEnd();
        }

        private static string BuildSyncDialogMessage(HfHubSyncReport report)
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

        private static string BuildSyncConsoleReport(HfHubSyncReport report)
        {
            var builder = new StringBuilder();
            builder.AppendLine("HF Hub sync report");

            foreach (var result in report.ModelResults.OrderBy(result => result.Dependency.PackageId).ThenBy(result => result.Dependency.ModelId))
            {
                if (result.Success)
                {
                    builder.AppendLine($"- OK {result.Dependency.PackageId}/{result.Dependency.ModelId}");
                    builder.AppendLine($"  {result.CachedFile.FilePath}");
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
