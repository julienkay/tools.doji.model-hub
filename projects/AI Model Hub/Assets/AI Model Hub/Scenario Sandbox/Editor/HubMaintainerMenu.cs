using Doji.AI.ModelHub.Editor;
using UnityEditor;

namespace Doji.AI.ModelHub.Sandbox.Editor
{
    /// <summary>
    /// Maintainer-only Tools > Doji > AI Model Hub menu entries. They live in the companion project so
    /// consuming projects do not get them. The package keeps its startup prompt and its window.
    /// </summary>
    internal static class HubMaintainerMenu
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

        [MenuItem("Tools/Doji/AI Model Hub/Model Cache Disk Usage")]
        private static void ShowCacheDiskUsage()
        {
            CacheDiskUsageWindow.OpenWindow();
        }

        [MenuItem("Tools/Doji/AI Model Hub/Debug/Show Auto Prompt &2")]
        private static void ShowAutoPrompt()
        {
            AutoPrompt.ShowPromptForCurrentStatus();
        }
    }
}
