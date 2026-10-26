using UnityEditor;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Provides menu entry points for the Model Hub window, sync action, and project settings.
    /// </summary>
    internal static class StatusMenu
    {
        [MenuItem("Tools/Doji/AI Model Hub/Show Status")]
        private static void ShowStatus()
        {
            ModelHubWindow.OpenWindow();
        }

        [MenuItem("Tools/Doji/AI Model Hub/Set Up All Models")]
        private static void SyncModels()
        {
            EditorOperations.StartBackgroundSync();
            ProjectSettingsProvider.Refresh();
        }

        [MenuItem("Tools/Doji/AI Model Hub/Open Project Settings")]
        private static void OpenProjectSettings()
        {
            SettingsService.OpenProjectSettings(EditorOperations.ProjectSettingsPath);
        }
    }
}
