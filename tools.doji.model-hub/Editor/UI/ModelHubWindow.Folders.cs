using UnityEngine.UIElements;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// The folders in use, at the bottom of the navigation, and a warning when an environment variable that decides
    /// them changed after Unity started.
    /// </summary>
    internal sealed partial class ModelHubWindow
    {
        private VisualElement _foldersPanel;
        private VisualElement _foldersWarning;

        private VisualElement BuildFoldersPanel()
        {
            _foldersPanel = UiKit.Element("mh-folders");
            _foldersPanel.tooltip = "The folders AI Model Hub uses on this machine. Click to change them.";
            _foldersPanel.RegisterCallback<ClickEvent>(_ => CacheFolderPreferences.Open());
            return _foldersPanel;
        }

        private VisualElement BuildFoldersWarning()
        {
            _foldersWarning = UiKit.Element("mh-folders-warning", "hf-hidden");
            return _foldersWarning;
        }

        private void UpdateFolders()
        {
            if (_foldersPanel == null)
            {
                return;
            }

            var report = CacheFolders.Describe();
            _foldersPanel.Clear();
            _foldersPanel.Add(BuildFolderEntry("Download cache", report.HubCache));
            _foldersPanel.Add(BuildFolderEntry("Converted models", report.DataRoot));

            _foldersWarning.Clear();
            var stale = CacheFolders.BuildStaleMessage(report.StaleVariables);
            UiKit.SetVisible(_foldersWarning, stale != null);
            if (stale != null)
            {
                _foldersWarning.Add(UiKit.Callout(
                    UiTone.Warning,
                    "This Editor uses an old environment variable",
                    stale,
                    UiKit.Button("Choose Folders…", CacheFolderPreferences.Open)));
            }
        }

        private static VisualElement BuildFolderEntry(string title, CacheFolderInfo folder)
        {
            var entry = UiKit.Element("mh-folders__entry");
            entry.tooltip = $"{folder.Path}\nSet by: {CacheFolders.DescribeSource(folder.Source)}\n{CacheFolderPreferences.DescribeFreeSpace(folder.Drive)}";
            entry.Add(UiKit.Text(title.ToUpperInvariant(), "hf-overline"));
            entry.Add(UiKit.Text(folder.Path, "mh-folders__path"));
            var free = folder.Drive == null ? "free space unknown" : $"{UiKit.FormatBytes(folder.Drive.AvailableBytes)} free";
            entry.Add(UiKit.Text($"{free} · {ShortSource(folder.Source)}", "mh-folders__meta"));
            return entry;
        }

        private static string ShortSource(string source)
        {
            return source switch
            {
                CachePaths.EditorSettingSource => "Editor setting",
                CachePaths.DefaultSource or null => "default",
                _ => source
            };
        }
    }
}
