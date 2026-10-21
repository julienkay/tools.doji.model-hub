using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine.UIElements;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Project Settings page: models this project declares itself, and where downloads are cached.
    /// </summary>
    internal static class ProjectSettingsProvider
    {
        private const string StyleSheet = "Settings.uss";
        private static WeakReference<VisualElement> _currentRoot;

        [SettingsProvider]
        private static SettingsProvider CreateSettingsProvider()
        {
            return new SettingsProvider(EditorOperations.ProjectSettingsPath, SettingsScope.Project)
            {
                label = "AI Model Hub",
                activateHandler = (_, rootElement) => Build(rootElement),
                deactivateHandler = () => _currentRoot = null,
                keywords = new HashSet<string>(new[] { "AI", "Model", "Hub", "HuggingFace", "Hugging Face", "Sentis", "Inference", "Cache", "Download" })
            };
        }

        /// <summary>
        /// Reloads the page after the project manifest or cache changed elsewhere. Keeps unapplied edits.
        /// </summary>
        internal static void Refresh()
        {
            if (_currentRoot == null || !_currentRoot.TryGetTarget(out var root) || root.panel == null)
            {
                return;
            }

            var editor = root.Q<ManifestEditorElement>();
            if (editor != null && !editor.HasPendingChanges)
            {
                editor.Reload();
            }

            UpdateCacheSection(root);
        }

        private static void Build(VisualElement rootElement)
        {
            var root = UiKit.Element("ps");
            UiKit.ApplyTheme(root, ManifestEditorElement.StyleSheet, StyleSheet);
            rootElement.Add(root);
            _currentRoot = new WeakReference<VisualElement>(root);

            var header = UiKit.Element("ps-header");
            header.Add(UiKit.Text("AI Model Hub", "hf-title"));
            header.Add(UiKit.Spacer());
            header.Add(UiKit.Button("Open Model Hub", () => ModelHubWindow.OpenWindow()));
            root.Add(header);
            root.Add(UiKit.Text(
                "Packages bring their own model lists. Add models here that this project needs directly.",
                "ps-intro"));

            var models = UiKit.Element("hf-section");
            models.Add(new ManifestEditorElement(
                new ProjectSettingsManifestDocumentStore(),
                new ManifestEditorOptions
                {
                    Title = "Project models",
                    EmptyMessage = "No project models. Packages can still bring their own.",
                    AfterApply = InternalEditorUtility.RepaintAllViews
                }));
            root.Add(models);

            root.Add(BuildCacheSection());

            root.Add(UiKit.Text(
                $"Sentis {StatusService.GetCurrentSentisVersion()}. Imported models are built for this version and re-imported when it changes.",
                "ps-footer"));
            UpdateCacheSection(root);
        }

        private static VisualElement BuildCacheSection()
        {
            var settings = ProjectSettings.instance;
            var section = UiKit.Section("Download cache");
            var card = UiKit.Element("hf-card");

            var location = new RadioButtonGroup(null, new List<string> { "Default folder", "Custom folder" })
            {
                name = "cacheLocation",
                value = settings.UseDefaultCacheLocation ? 0 : 1
            };
            location.AddToClassList("ps-location");
            card.Add(location);

            var custom = UiKit.Element("ps-custom");
            custom.name = "customRow";
            var customField = new TextField { name = "customPath", value = settings.CustomCacheLocation ?? string.Empty, isDelayed = true };
            customField.textEdition.placeholder = "Choose a folder";
            custom.Add(customField);
            custom.Add(UiKit.Button("Browse…", () =>
            {
                var folder = EditorUtility.OpenFolderPanel("Choose Cache Folder", customField.value, string.Empty);
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    customField.value = folder;
                }
            }));
            card.Add(custom);

            var path = UiKit.Element("ps-path");
            path.Add(UiKit.Text("In use", "hf-kv__key"));
            var pathValue = UiKit.Text(string.Empty, "ps-path__value");
            pathValue.name = "effectivePath";
            path.Add(pathValue);
            path.Add(UiKit.LinkButton(UiKit.RevealInFileBrowserLabel, () => ProjectSettingsLinks.RevealPath(CachePaths.GetCacheRoot())));
            card.Add(path);

            var envNote = UiKit.Callout(
                UiTone.Info,
                null,
                $"The {CachePaths.CacheRootEnvironmentVariable} environment variable is set, so it overrides this setting.");
            envNote.name = "envNote";
            envNote.AddToClassList("ps-env-note");
            card.Add(envNote);

            card.Add(UiKit.Text(
                "All Unity projects on this machine share the cache, so each model downloads only once.",
                "ps-card-hint"));
            section.Add(card);

            location.RegisterValueChangedCallback(evt =>
            {
                settings.UpdateCacheLocation(evt.newValue == 0, customField.value);
                UpdateCacheSection(section);
            });
            customField.RegisterValueChangedCallback(evt =>
            {
                settings.UpdateCacheLocation(location.value == 0, evt.newValue);
                UpdateCacheSection(section);
            });
            return section;
        }

        private static void UpdateCacheSection(VisualElement root)
        {
            var location = root.Q<RadioButtonGroup>("cacheLocation");
            if (location == null)
            {
                return;
            }

            var isOverridden = CachePaths.IsCacheRootOverriddenByEnvironment;
            location.SetEnabled(!isOverridden);
            var customRow = root.Q("customRow");
            UiKit.SetVisible(customRow, location.value == 1);
            customRow?.SetEnabled(!isOverridden);
            UiKit.SetVisible(root.Q("envNote"), isOverridden);

            var effectivePath = root.Q<Label>("effectivePath");
            if (effectivePath != null)
            {
                effectivePath.text = CachePaths.GetCacheRoot();
                effectivePath.tooltip = effectivePath.text;
            }
        }
    }
}
