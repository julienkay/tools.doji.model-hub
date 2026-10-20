using System;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Reuses shared manifest inspector UX for `ProjectManifestAsset` ScriptableObject manifests.
    /// </summary>
    [CustomEditor(typeof(ProjectManifestAsset), false)]
    internal sealed class ProjectManifestAssetInspector : UnityEditor.Editor
    {
        private const float ContentInset = 14f;
        private ManifestDocumentEditor _editor;
        private bool _suppressUnsavedPrompt;

        public override void OnInspectorGUI()
        {
            EnsureEditor();

            var previousGuiEnabled = GUI.enabled;
            GUI.enabled = true;
            try
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(ContentInset);
                    using (new EditorGUILayout.VerticalScope())
                    {
                        _editor.Draw();
                    }
                }
            }
            finally
            {
                GUI.enabled = previousGuiEnabled;
            }
        }

        private void OnDisable()
        {
            if (!_suppressUnsavedPrompt && _editor is { HasUserEdits: true, HasPendingChanges: true })
            {
                var targetObject = target;
                EditorApplication.delayCall += () =>
                {
                    _suppressUnsavedPrompt = true;
                    _editor?.PromptForUnsavedChanges(
                        targetObject,
                        () =>
                        {
                            Selection.activeObject = targetObject;
                            if (targetObject != null)
                            {
                                EditorGUIUtility.PingObject(targetObject);
                            }
                        });
                    _suppressUnsavedPrompt = false;
                };
            }
        }

        private void EnsureEditor()
        {
            if (_editor != null)
            {
                return;
            }

            _editor = new ManifestDocumentEditor(
                new ProjectManifestAssetDocumentStore((ProjectManifestAsset)target),
                new ManifestDocumentEditorOptions
                {
                    Title = "Model Dependencies",
                    ShowVersion = false,
                    ShowOpenModelHubButton = true,
                    ShowReadOnlyInfoBox = false,
                    RequestRepaint = Repaint,
                    AfterApply = () =>
                    {
                        Repaint();
                        InternalEditorUtility.RepaintAllViews();
                    }
                });
        }
    }
}
