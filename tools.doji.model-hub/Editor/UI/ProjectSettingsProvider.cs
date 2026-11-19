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

            root.Add(BuildDeliverySection());
            root.Add(BuildCacheSection());

            root.Add(UiKit.Text(
                $"Sentis {ProjectImportService.GetSentisVersion()}, format generation {SentisCompatibility.GetGeneration(ProjectImportService.GetSentisVersion())}. Sentis updates reuse converted models; only a new format generation converts them again.",
                "ps-footer"));
            UpdateCacheSection(root);
        }

        private static VisualElement BuildDeliverySection()
        {
            var settings = ProjectSettings.instance;
            var section = UiKit.Section("In builds");
            var card = UiKit.Element("hf-card");
            var choices = new List<string> { ModelHubWindow.DeliveryLabel(ModelDelivery.Bundle), ModelHubWindow.DeliveryLabel(ModelDelivery.Download) };
            var field = new DropdownField("Models", choices, settings.DefaultDelivery == ModelDelivery.Download ? 1 : 0);
            field.RegisterValueChangedCallback(evt =>
                settings.SetDefaultDelivery(choices.IndexOf(evt.newValue) == 1 ? ModelDelivery.Download : ModelDelivery.Bundle));
            card.Add(field);
            card.Add(UiKit.Text(
                "Bundled models ship in StreamingAssets. Downloaded models are fetched from Hugging Face the first time a player loads them, at the commit tested in this project, into the device's cache. Desktop players use the user's Hugging Face cache, so models Python or other apps downloaded are reused. Override this per model in the Model Hub.",
                "ps-card-hint"));
            section.Add(card);
            return section;
        }

        private static VisualElement BuildCacheSection()
        {
            var section = UiKit.Section("Folders", trailing: UiKit.Button("Change in Preferences…", CacheFolderPreferences.Open));
            var card = UiKit.Element("hf-card");
            card.name = "cacheFolders";
            section.Add(card);
            return section;
        }

        private static void UpdateCacheSection(VisualElement root)
        {
            var card = root.Q("cacheFolders");
            if (card == null)
            {
                return;
            }

            card.Clear();
            var report = CacheFolders.Describe();
            var stale = CacheFolders.BuildStaleMessage(report.StaleVariables);
            if (stale != null)
            {
                card.Add(UiKit.Callout(UiTone.Warning, "Restart Unity Hub to use a changed variable", stale));
            }

            card.Add(UiKit.Text("Download cache", "hf-overline"));
            card.Add(CacheFolderPreferences.BuildInUseRow(report.HubCache));
            card.Add(UiKit.Text("Converted models and index", "hf-overline"));
            card.Add(CacheFolderPreferences.BuildInUseRow(report.DataRoot));
            card.Add(UiKit.Text(
                "These folders are set per user for every project, in Preferences > AI Model Hub. Unity projects and Python tools that use huggingface_hub share the download cache, so each model downloads only once.",
                "ps-card-hint"));
        }
    }
}
