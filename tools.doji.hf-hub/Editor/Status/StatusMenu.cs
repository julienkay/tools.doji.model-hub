using UnityEditor;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Provides menu entry points for the Model Hub window, sync action, and project settings.
    /// </summary>
    internal static class StatusMenu
    {
        [MenuItem("Tools/Doji/HF Hub/Show Status")]
        private static void ShowStatus()
        {
            ModelHubWindow.OpenWindow();
        }

        [MenuItem("Tools/Doji/HF Hub/Import All")]
        private static void SyncModels()
        {
            EditorOperations.StartBackgroundSync();
            ProjectSettingsProvider.Refresh();
        }

        [MenuItem("Tools/Doji/HF Hub/Open Project Settings")]
        private static void OpenProjectSettings()
        {
            SettingsService.OpenProjectSettings(EditorOperations.ProjectSettingsPath);
        }
    }
}
