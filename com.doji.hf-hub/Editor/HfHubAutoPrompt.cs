using System.Text;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Prompts once per editor session when discovered dependencies are missing, stale, or failed.
    /// </summary>
    [InitializeOnLoad]
    internal static class HfHubAutoPrompt
    {
        private const string PromptShownSessionKey = "Doji.AI.ModelHub.AutoPromptShown";
        private static bool _isQueued;

        static HfHubAutoPrompt()
        {
            QueuePrompt();
            EditorApplication.projectChanged += QueuePrompt;
        }

        internal static bool ShouldPrompt(HfHubStatusReport report)
        {
            return report != null &&
                   report.NeedsAttention &&
                   (report.DependencyStatuses.Count > 0 || report.DiscoveryIssues.Count > 0);
        }

        private static void QueuePrompt()
        {
            if (_isQueued)
            {
                return;
            }

            _isQueued = true;
            EditorApplication.delayCall += PromptIfNeeded;
        }

        private static void PromptIfNeeded()
        {
            _isQueued = false;

            if (IsTestRunnerOpen())
            {
                return;
            }

            if (Application.isBatchMode ||
                EditorApplication.isCompiling ||
                EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode)
            {
                QueuePrompt();
                return;
            }

            if (SessionState.GetBool(PromptShownSessionKey, false))
            {
                return;
            }

            var statusReport = HfHubEditorOperations.CreateStatusReport();
            if (!ShouldPrompt(statusReport))
            {
                return;
            }

            SessionState.SetBool(PromptShownSessionKey, true);

            var action = EditorUtility.DisplayDialogComplex(
                "AI Model Hub",
                BuildPromptMessage(statusReport),
                "Sync Now",
                "Later",
                "Open Settings");

            switch (action)
            {
                case 0:
                    HfHubEditorOperations.StartBackgroundSync();
                    HfHubProjectSettingsProvider.Refresh();
                    break;
                case 2:
                    SettingsService.OpenProjectSettings(HfHubEditorOperations.ProjectSettingsPath);
                    break;
            }
        }

        private static string BuildPromptMessage(HfHubStatusReport report)
        {
            var builder = new StringBuilder();
            builder.AppendLine("AI Model Hub found package model dependencies that need attention.");
            builder.AppendLine();
            builder.AppendLine($"Missing: {report.MissingCount}");
            builder.AppendLine($"Stale: {report.StaleCount}");
            builder.AppendLine($"Failed: {report.FailedCount}");
            builder.AppendLine($"Discovery issues: {report.DiscoveryIssues.Count}");
            builder.AppendLine();
            builder.Append("Sync now, postpone until later, or open Project Settings for details.");
            return builder.ToString();
        }

        private static bool IsTestRunnerOpen()
        {
            return Resources.FindObjectsOfTypeAll<EditorWindow>()
                .Any(window => string.Equals(window.GetType().FullName, "UnityEditor.TestRunner.TestRunnerWindow", System.StringComparison.Ordinal));
        }
    }
}
