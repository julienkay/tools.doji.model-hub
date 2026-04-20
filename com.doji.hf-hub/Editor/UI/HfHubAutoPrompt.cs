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

        [MenuItem("Tools/Doji/HF Hub/Debug/Show Auto Prompt &2")]
        private static void ShowPromptFromMenu()
        {
            ShowPromptForCurrentStatus();
        }

        internal static bool ShouldPrompt(HfHubStatusReport report)
        {
            return report != null &&
                   report.NeedsAttention &&
                   (report.DependencyStatuses.Count > 0 || report.DiscoveryIssues.Count > 0);
        }

        internal static void ShowPromptForCurrentStatus()
        {
            var statusReport = HfHubEditorOperations.CreateStatusReport();
            HfHubAutoPromptWindow.ShowWindow(statusReport);
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
            HfHubAutoPromptWindow.ShowWindow(statusReport);
        }

        private static bool IsTestRunnerOpen()
        {
            return Resources.FindObjectsOfTypeAll<EditorWindow>()
                .Any(window => string.Equals(window.GetType().FullName, "UnityEditor.TestRunner.TestRunnerWindow", System.StringComparison.Ordinal));
        }
    }
}
