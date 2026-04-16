using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Project Settings surface for inspecting local HF Hub dependency state and triggering sync.
    /// </summary>
    internal static class HfHubProjectSettingsProvider
    {
        private static HfHubStatusReport _statusReport;
        private static Vector2 _scrollPosition;

        [SettingsProvider]
        private static SettingsProvider CreateSettingsProvider()
        {
            var provider = new SettingsProvider(HfHubEditorOperations.ProjectSettingsPath, SettingsScope.Project)
            {
                label = "AI Model Hub",
                guiHandler = _ => DrawGui(),
                activateHandler = (_, _) => Refresh(),
                keywords = new HashSet<string>(new[]
                {
                    "AI",
                    "Model",
                    "Hub",
                    "HuggingFace",
                    "Sentis",
                    "Inference",
                    "Cache"
                })
            };

            return provider;
        }

        private static void DrawGui()
        {
            _statusReport ??= HfHubEditorOperations.CreateStatusReport();

            EditorGUILayout.LabelField("AI Model Hub", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(HfHubEditorOperations.BuildStatusDialogMessage(_statusReport), GetHelpBoxMessageType(_statusReport));

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Refresh Status", GUILayout.Width(120)))
                {
                    Refresh();
                }

                if (GUILayout.Button("Sync Models", GUILayout.Width(120)))
                {
                    HfHubEditorOperations.RunInteractiveSync();
                    Refresh();
                }
            }

            EditorGUILayout.Space();

            if (_statusReport.DependencyStatuses.Count == 0 && _statusReport.DiscoveryIssues.Count == 0)
            {
                EditorGUILayout.HelpBox("No hf-hub package manifests were found in the installed packages.", MessageType.Info);
                return;
            }

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            if (_statusReport.DiscoveryIssues.Count > 0)
            {
                EditorGUILayout.LabelField("Discovery Issues", EditorStyles.boldLabel);
                foreach (var issue in _statusReport.DiscoveryIssues.OrderBy(issue => issue.PackageId).ThenBy(issue => issue.ManifestPath))
                {
                    EditorGUILayout.HelpBox($"{issue.PackageId}: {issue.Message}", MessageType.Warning);
                }

                EditorGUILayout.Space();
            }

            EditorGUILayout.LabelField("Model Dependencies", EditorStyles.boldLabel);
            foreach (var status in _statusReport.DependencyStatuses.OrderBy(item => item.Dependency.PackageId).ThenBy(item => item.Dependency.ModelId))
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(
                        $"{status.Dependency.PackageDisplayName} / {status.Dependency.ModelDisplayName}",
                        EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("State", status.State.ToString());
                    EditorGUILayout.LabelField("Reason", status.Message);
                    EditorGUILayout.LabelField("Source", $"{status.Dependency.Repository}@{status.Dependency.Revision} :: {status.Dependency.SourceFile}");

                    if (!string.IsNullOrWhiteSpace(status.RawFilePath))
                    {
                        EditorGUILayout.LabelField("Raw Cache", status.RawFilePath);
                    }

                    if (!string.IsNullOrWhiteSpace(status.SentisAssetPath))
                    {
                        EditorGUILayout.LabelField(".sentis", status.SentisAssetPath);
                    }
                }
            }

            EditorGUILayout.EndScrollView();
        }

        internal static void Refresh()
        {
            _statusReport = HfHubEditorOperations.CreateStatusReport();
        }

        private static MessageType GetHelpBoxMessageType(HfHubStatusReport report)
        {
            if (report.FailedCount > 0 || report.DiscoveryIssues.Count > 0)
            {
                return MessageType.Warning;
            }

            if (report.MissingCount > 0 || report.StaleCount > 0)
            {
                return MessageType.Info;
            }

            return MessageType.None;
        }
    }
}
