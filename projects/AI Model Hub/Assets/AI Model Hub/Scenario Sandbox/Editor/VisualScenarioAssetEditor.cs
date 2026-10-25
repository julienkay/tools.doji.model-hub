using UnityEditor;
using UnityEngine;

namespace Doji.AI.ModelHub.Sandbox.Editor
{
    /// <summary>
    /// Adds maintainer workflow buttons for applying and cleaning visual AI Model Hub scenarios.
    /// </summary>
    [CustomEditor(typeof(VisualScenarioAsset))]
    public sealed class VisualScenarioAssetEditor : UnityEditor.Editor
    {
        [MenuItem("Tools/Doji/AI Model Hub/Debug/Create Visual Scenario Sample")]
        private static void CreateSampleAsset()
        {
            const string defaultAssetPath = "Assets/AI Model Hub/Scenario Sandbox/AI Model Hub Visual Scenario.asset";
            var assetPath = AssetDatabase.GenerateUniqueAssetPath(defaultAssetPath);
            var scenario = ScriptableObject.CreateInstance<VisualScenarioAsset>();
            AssetDatabase.CreateAsset(scenario, assetPath);
            VisualScenarioService.PopulateWithRecommendedEntries(scenario);
            AssetDatabase.SaveAssets();
            Selection.activeObject = scenario;
            EditorGUIUtility.PingObject(scenario);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();
            serializedObject.ApplyModifiedProperties();

            var scenario = (VisualScenarioAsset)target;

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Writes fake local AI Model Hub cache, import metadata, and runtime files for visual Model Hub review. Entries can now fake whole-repo or explicit selected-file manifests. The sandbox writes one generated project manifest per scenario entry, so repeated repo ids simulate multiple manifests that depend on the same repo. Repeated repos still share project import/runtime slots because real AI Model Hub imports are repo-scoped.",
                MessageType.Info);
            EditorGUILayout.LabelField("Generated Output Root", VisualScenarioService.GetGeneratedOutputRootAssetPath(scenario), EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Populate Recommended Entries"))
                {
                    VisualScenarioService.PopulateWithRecommendedEntries(scenario);
                }

                if (GUILayout.Button("Apply Scenario"))
                {
                    VisualScenarioService.ApplyScenario(scenario);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open Model Hub"))
                {
                    VisualScenarioService.OpenModelHubWindow();
                }

                if (GUILayout.Button("Cleanup Scenario"))
                {
                    VisualScenarioService.CleanupScenario(scenario);
                }
            }
        }
    }
}
