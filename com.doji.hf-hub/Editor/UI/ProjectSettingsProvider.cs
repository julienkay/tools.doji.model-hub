using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Project Settings surface for project-scoped Model Hub configuration.
    /// </summary>
    internal static class ProjectSettingsProvider
    {
        private const float ContentInset = 14f;
        private static readonly GUIContent UseDefaultCacheLocationContent = new("Use Default Cache Location", "Store raw HuggingFace snapshots in the platform default shared cache folder.");
        private static readonly GUIContent CustomCacheLocationContent = new("Custom Cache Location", "Override the shared cache root for this Unity project when the default location is disabled.");
        private static readonly GUIContent RevealCacheFolderContent = new("Reveal Cache Folder", "Reveal the effective shared cache folder used by this project.");
        private static readonly GUIContent OpenModelHubContent = new("Open Model Hub", "Open the main repository inventory window.");
        private static ManifestDocumentEditor _projectManifestEditor;

        [SettingsProvider]
        private static SettingsProvider CreateSettingsProvider()
        {
            return new SettingsProvider(EditorOperations.ProjectSettingsPath, SettingsScope.Project)
            {
                label = "AI Model Hub",
                guiHandler = _ => DrawGui(),
                keywords = new HashSet<string>(new[] { "AI", "Model", "Hub", "HuggingFace", "Sentis", "Inference", "Cache" })
            };
        }

        private static void DrawGui()
        {
            var settings = ProjectSettings.instance;
            EnsureProjectManifestEditor();

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(ContentInset);
                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField("AI Model Hub", EditorStyles.boldLabel);
                    EditorGUILayout.HelpBox(
                        "Project Settings stores configuration. Use Window > AI Model Hub to inspect imported repositories, dependents, and generated files.",
                        MessageType.Info);

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Cache", EditorStyles.boldLabel);

                    var useDefaultCacheLocation = EditorGUILayout.Toggle(UseDefaultCacheLocationContent, settings.UseDefaultCacheLocation);
                    var customCacheLocation = settings.CustomCacheLocation;
                    using (new EditorGUI.DisabledScope(useDefaultCacheLocation))
                    {
                        customCacheLocation = EditorGUILayout.DelayedTextField(CustomCacheLocationContent, customCacheLocation);
                    }

                    if (useDefaultCacheLocation != settings.UseDefaultCacheLocation ||
                        !string.Equals(customCacheLocation, settings.CustomCacheLocation, StringComparison.Ordinal))
                    {
                        settings.UpdateCacheLocation(useDefaultCacheLocation, customCacheLocation);
                    }

                    EditorGUILayout.Space();
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.LabelField("Default Cache", CachePaths.GetDefaultCacheRoot());
                        EditorGUILayout.LabelField("Effective Cache", CachePaths.GetCacheRoot());
                        EditorGUILayout.LabelField("Sentis Version", StatusService.GetCurrentSentisVersion());
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button(RevealCacheFolderContent, GUILayout.Width(150f)))
                        {
                            ProjectSettingsLinks.RevealPath(CachePaths.GetCacheRoot());
                        }

                        if (GUILayout.Button(OpenModelHubContent, GUILayout.Width(120f)))
                        {
                            ModelHubWindow.OpenWindow();
                        }
                    }

                    EditorGUILayout.Space();
                    EditorGUILayout.Space();
                    _projectManifestEditor.Draw();
                }
            }
        }

        internal static void Refresh()
        {
            _projectManifestEditor = null;
            if (SettingsWindowIsOpen())
            {
                SettingsService.OpenProjectSettings(EditorOperations.ProjectSettingsPath);
            }
        }

        private static void EnsureProjectManifestEditor()
        {
            _projectManifestEditor ??= new ManifestDocumentEditor(
                new ProjectSettingsManifestDocumentStore(),
                new ManifestDocumentEditorOptions
                {
                    Title = "Model Dependencies",
                    ShowVersion = false,
                    ShowOpenModelHubButton = false,
                    ShowReadOnlyInfoBox = false,
                    RequestRepaint = InternalEditorUtility.RepaintAllViews,
                    AfterApply = InternalEditorUtility.RepaintAllViews
                });
        }

        private static bool SettingsWindowIsOpen()
        {
            return Resources.FindObjectsOfTypeAll<EditorWindow>()
                .Any(window => string.Equals(window.GetType().FullName, "UnityEditor.SettingsWindow", StringComparison.Ordinal));
        }
    }
}
