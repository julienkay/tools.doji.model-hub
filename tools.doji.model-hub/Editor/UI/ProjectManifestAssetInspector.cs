using UnityEditor;
using UnityEditorInternal;
using UnityEngine.UIElements;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Shows the shared manifest editor for <see cref="ProjectManifestAsset"/> ScriptableObject manifests.
    /// </summary>
    [CustomEditor(typeof(ProjectManifestAsset), false)]
    internal sealed class ProjectManifestAssetInspector : UnityEditor.Editor
    {
        private ManifestEditorElement _editor;

        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            UiKit.ApplyTheme(root, ManifestEditorElement.StyleSheet);
            root.AddToClassList("me-host");
            _editor = new ManifestEditorElement(
                new ProjectManifestAssetDocumentStore((ProjectManifestAsset)target),
                new ManifestEditorOptions
                {
                    ShowOpenModelHubButton = true,
                    AfterApply = InternalEditorUtility.RepaintAllViews
                });
            root.Add(_editor);
            return root;
        }

        private void OnDisable()
        {
            ManifestInspectorUnsavedChanges.PromptLater(_editor, target);
        }
    }
}
