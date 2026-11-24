using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Prompts once per editor session when discovered dependencies are missing, stale, or failed.
    /// </summary>
    [InitializeOnLoad]
    internal static class AutoPrompt
    {
        private const string PromptShownSessionKey = "Doji.AI.ModelHub.AutoPromptShown";
        private static bool _isQueued;
        private static bool _loggedForCurrentDomain;

        static AutoPrompt()
        {
            QueuePrompt();
            EditorApplication.projectChanged += QueuePrompt;
        }

        internal static bool ShouldPrompt(StatusReport report)
        {
            return report != null &&
                   report.NeedsStartupPrompt &&
                   (report.DependencyStatuses.Count > 0 || report.DiscoveryIssues.Count > 0);
        }

        internal static void ShowPromptForCurrentStatus()
        {
            var statusReport = EditorOperations.CreateStatusReport();
            AutoPromptWindow.ShowWindow(statusReport);
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

            var statusReport = EditorOperations.CreateStatusReport();
            if (!ShouldPrompt(statusReport))
            {
                return;
            }

            LogReminderIfNeeded(statusReport);

            if (SessionState.GetBool(PromptShownSessionKey, false))
            {
                return;
            }

            SessionState.SetBool(PromptShownSessionKey, true);
            AutoPromptWindow.ShowWindow(statusReport);
        }

        private static void LogReminderIfNeeded(StatusReport statusReport)
        {
            if (!ShouldLogReminder(statusReport))
            {
                return;
            }

            _loggedForCurrentDomain = true;
            var contextObject = ProjectSettingsLinks.TryGetSingleManifestContextObject(statusReport.DependencyStatuses);
            Debug.Log(
                EditorOperations.BuildUnresolvedDependencyReminder(statusReport),
                contextObject);
        }

        internal static bool ShouldLogReminder(StatusReport report)
        {
            return !_loggedForCurrentDomain &&
                report != null &&
                report.NeedsStartupPrompt &&
                !AutoPromptWindow.IsOpen();
        }

        private static bool IsTestRunnerOpen()
        {
            return Resources.FindObjectsOfTypeAll<EditorWindow>()
                .Any(window => string.Equals(window.GetType().FullName, "UnityEditor.TestRunner.TestRunnerWindow", System.StringComparison.Ordinal));
        }
    }
}
