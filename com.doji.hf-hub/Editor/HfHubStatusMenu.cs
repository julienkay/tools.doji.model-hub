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
            var report = HfHubEditorOperations.CreateStatusReport();
            var consoleReport = HfHubEditorOperations.BuildStatusConsoleReport(report);
            var dialogMessage = HfHubEditorOperations.BuildStatusDialogMessage(report);

            if (report.NeedsAttention)
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
            HfHubEditorOperations.RunInteractiveSync();
            HfHubProjectSettingsProvider.Refresh();
        }

        [MenuItem("Tools/Doji/HF Hub/Open Project Settings")]
        private static void OpenProjectSettings()
        {
            SettingsService.OpenProjectSettings(HfHubEditorOperations.ProjectSettingsPath);
        }
    }
}
