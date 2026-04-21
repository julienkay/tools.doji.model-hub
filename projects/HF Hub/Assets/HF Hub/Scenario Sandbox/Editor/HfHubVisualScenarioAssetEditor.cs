using UnityEditor;
using UnityEngine;

namespace Doji.AI.ModelHub.Sandbox.Editor
{
    /// <summary>
    /// Adds maintainer workflow buttons for applying and cleaning visual HF Hub scenarios.
    /// </summary>
    [CustomEditor(typeof(HfHubVisualScenarioAsset))]
    public sealed class HfHubVisualScenarioAssetEditor : UnityEditor.Editor
    {
        [MenuItem("Tools/Doji/HF Hub/Debug/Create Visual Scenario Sample")]
        private static void CreateSampleAsset()
        {
            const string defaultAssetPath = "Assets/HF Hub/Scenario Sandbox/HF Hub Visual Scenario.asset";
            var assetPath = AssetDatabase.GenerateUniqueAssetPath(defaultAssetPath);
            var scenario = ScriptableObject.CreateInstance<HfHubVisualScenarioAsset>();
            AssetDatabase.CreateAsset(scenario, assetPath);
            HfHubVisualScenarioService.PopulateWithRecommendedEntries(scenario);
            AssetDatabase.SaveAssets();
            Selection.activeObject = scenario;
            EditorGUIUtility.PingObject(scenario);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();
            serializedObject.ApplyModifiedProperties();

            var scenario = (HfHubVisualScenarioAsset)target;

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Writes fake local HF Hub cache, import metadata, and runtime files for visual Model Hub review. The sandbox now writes one generated project manifest per scenario entry, so repeated repo ids simulate multiple manifests that depend on the same repo. Repeated repos still share project import/runtime slots because real HF Hub imports are repo-scoped.",
                MessageType.Info);
            EditorGUILayout.LabelField("Generated Output Root", HfHubVisualScenarioService.GetGeneratedOutputRootAssetPath(scenario), EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Populate Recommended Entries"))
                {
                    HfHubVisualScenarioService.PopulateWithRecommendedEntries(scenario);
                }

                if (GUILayout.Button("Apply Scenario"))
                {
                    HfHubVisualScenarioService.ApplyScenario(scenario);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open Model Hub"))
                {
                    HfHubVisualScenarioService.OpenModelHubWindow();
                }

                if (GUILayout.Button("Cleanup Scenario"))
                {
                    HfHubVisualScenarioService.CleanupScenario(scenario);
                }
            }
        }
    }
}
