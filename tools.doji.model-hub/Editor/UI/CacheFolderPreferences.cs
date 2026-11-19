using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.UIElements;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Preferences page for the two folders AI Model Hub uses on this machine. The choice is per user, for every
    /// project, and applies at once.
    /// </summary>
    internal static class CacheFolderPreferences
    {
        public const string PreferencesPath = "Preferences/Doji/AI/AI Model Hub";
        private const string StyleSheet = "Settings.uss";
        private static WeakReference<VisualElement> _currentRoot;

        [SettingsProvider]
        private static SettingsProvider CreateSettingsProvider()
        {
            return new SettingsProvider(PreferencesPath, SettingsScope.User)
            {
                label = "AI Model Hub",
                activateHandler = (_, rootElement) => Build(rootElement),
                deactivateHandler = () => _currentRoot = null,
                keywords = new HashSet<string>(new[] { "AI", "Model", "Hub", "Hugging Face", "HF_HOME", "HF_HUB_CACHE", "Cache", "Folder", "Disk" })
            };
        }

        /// <summary>
        /// Opens this page.
        /// </summary>
        public static void Open()
        {
            SettingsService.OpenUserPreferences(PreferencesPath);
        }

        /// <summary>
        /// Rebuilds the page after a folder changed.
        /// </summary>
        internal static void Refresh()
        {
            if (_currentRoot == null || !_currentRoot.TryGetTarget(out var root) || root.panel == null)
            {
                return;
            }

            var parent = root.parent;
            root.RemoveFromHierarchy();
            Build(parent);
        }

        private static void Build(VisualElement rootElement)
        {
            var root = UiKit.Element("ps");
            UiKit.ApplyTheme(root, StyleSheet);
            rootElement.Add(root);
            _currentRoot = new WeakReference<VisualElement>(root);

            var header = UiKit.Element("ps-header");
            header.Add(UiKit.Text("AI Model Hub", "hf-title"));
            header.Add(UiKit.Spacer());
            header.Add(UiKit.Button("Open Model Hub", () => ModelHubWindow.OpenWindow()));
            root.Add(header);
            root.Add(UiKit.Text(
                "Where AI Model Hub keeps files on this machine. The choice is yours, for every project, and applies at once. Players keep using the environment variables or the default.",
                "ps-intro"));

            var report = CacheFolders.Describe();
            var stale = CacheFolders.BuildStaleMessage(report.StaleVariables);
            if (stale != null)
            {
                root.Add(UiKit.Callout(UiTone.Warning, "Restart Unity Hub to use a changed variable", stale));
            }

            root.Add(BuildFolderSection(
                CacheFolderKind.HubCache,
                "Download cache",
                "Hugging Face default (shared with Python)",
                CacheFolderSettings.GetCustomHubCache(),
                report.HubCache,
                "The default follows HF_HUB_CACHE, then HF_HOME/hub, then ~/.cache/huggingface/hub, like Python's huggingface_hub, so both reuse each other's downloads."));
            root.Add(BuildFolderSection(
                CacheFolderKind.DataRoot,
                "Converted models and index",
                "Default",
                CacheFolderSettings.GetCustomDataRoot(),
                report.DataRoot,
                "Sentis models converted from ONNX, shared by every project, and AI Model Hub's index of the cache. The default follows DOJI_MODEL_HUB_DATA_ROOT, else a per-user folder."));
        }

        private static VisualElement BuildFolderSection(
            CacheFolderKind kind,
            string title,
            string defaultLabel,
            string customFolder,
            CacheFolderInfo inUse,
            string hint)
        {
            var section = UiKit.Section(title);
            var card = UiKit.Element("hf-card");

            var location = new RadioButtonGroup(null, new List<string> { defaultLabel, "Custom folder" })
            {
                value = customFolder == null ? 0 : 1
            };
            location.AddToClassList("ps-location");
            card.Add(location);

            var custom = UiKit.Element("ps-custom");
            var customField = new TextField { value = customFolder ?? string.Empty };
            customField.textEdition.placeholder = "Choose a folder";
            custom.Add(customField);
            custom.Add(UiKit.Button("Browse…", () =>
            {
                var folder = EditorUtility.OpenFolderPanel("Choose Folder", string.IsNullOrWhiteSpace(customField.value) ? inUse.Path : customField.value, string.Empty);
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    customField.value = folder;
                }
            }));
            card.Add(custom);

            Toggle pythonToggle = null;
            if (kind == CacheFolderKind.HubCache)
            {
                pythonToggle = new Toggle("Also set it for Python")
                {
                    tooltip = "Sets HF_HOME (or HF_HUB_CACHE) for your user account, so huggingface_hub and other programs started from now on use this folder. AI Model Hub asks before it changes the variable."
                };
                card.Add(pythonToggle);
            }

            var apply = UiKit.PrimaryButton("Apply", () =>
            {
                var folder = location.value == 1 ? customField.value : null;
                if (location.value == 1 && string.IsNullOrWhiteSpace(folder))
                {
                    EditorUtility.DisplayDialog("AI Model Hub", "Choose a folder first.", "OK");
                    return;
                }

                CacheFolderChange.Apply(kind, folder, pythonToggle?.value == true);
            });
            card.Add(apply);

            void UpdateState()
            {
                UiKit.SetVisible(custom, location.value == 1);
                if (pythonToggle != null)
                {
                    UiKit.SetVisible(pythonToggle, location.value == 1);
                }

                var chosen = location.value == 1 ? CacheFolderSettings.Normalize(customField.value) : null;
                apply.SetEnabled(location.value == 1
                    ? chosen != null && (!string.Equals(chosen, customFolder, StringComparison.Ordinal) || pythonToggle?.value == true)
                    : customFolder != null);
            }

            location.RegisterValueChangedCallback(_ => UpdateState());
            customField.RegisterValueChangedCallback(_ => UpdateState());
            pythonToggle?.RegisterValueChangedCallback(_ => UpdateState());
            UpdateState();

            card.Add(BuildInUseRow(inUse));
            card.Add(UiKit.Text(hint, "ps-card-hint"));
            section.Add(card);
            return section;
        }

        /// <summary>
        /// The folder in use, its source, and its drive's free space.
        /// </summary>
        internal static VisualElement BuildInUseRow(CacheFolderInfo inUse)
        {
            var container = UiKit.Element();
            var path = UiKit.Element("ps-path");
            path.Add(UiKit.Text("In use", "hf-kv__key"));
            var pathValue = UiKit.Text(inUse.Path, "ps-path__value");
            pathValue.tooltip = inUse.Path;
            path.Add(pathValue);
            path.Add(UiKit.LinkButton(UiKit.RevealInFileBrowserLabel, () => ProjectSettingsLinks.RevealPath(inUse.Path)));
            container.Add(path);
            container.Add(UiKit.Text($"Set by: {CacheFolders.DescribeSource(inUse.Source)} · {DescribeFreeSpace(inUse.Drive)}", "ps-card-hint"));
            return container;
        }

        /// <summary>
        /// For example <c>120 GB free of 931.5 GB on G:\</c>.
        /// </summary>
        internal static string DescribeFreeSpace(DriveSpace drive)
        {
            return drive == null
                ? "free space unknown"
                : $"{UiKit.FormatBytes(drive.AvailableBytes)} free of {UiKit.FormatBytes(drive.TotalBytes)} on {drive.RootPath}";
        }
    }
}
