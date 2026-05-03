using System;
using UnityEditor;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Collects one HuggingFace owner/model identifier and appends it to the project-scoped manifest.
    /// </summary>
    internal sealed class AddProjectModelPopup : PopupWindowContent
    {
        private const float PopupWidth = 320f;
        private const float PopupHeight = 164f;
        private const float ContentPaddingLeft = 10f;
        private const float ContentPaddingTop = 8f;
        private const string OwnerFieldControlName = "ProjectModelOwnerField";
        private const string ModelFieldControlName = "ProjectModelNameField";
        private readonly Action<string, string, string[]> _onAdd;
        private readonly ModelIdPopupValidationController _validationController;
        private string _owner = string.Empty;
        private string _modelName = string.Empty;
        private string[] _selectedFiles = Array.Empty<string>();
        private bool _focusRequested = true;

        public AddProjectModelPopup(Action<string, string, string[]> onAdd)
        {
            _onAdd = onAdd;
            _validationController = new ModelIdPopupValidationController(
                () => CurrentModelId,
                IsInputFieldFocused,
                () => editorWindow?.Repaint());
        }

        public override Vector2 GetWindowSize()
        {
            return new Vector2(PopupWidth, PopupHeight);
        }

        public override void OnGUI(Rect rect)
        {
            var contentRect = new Rect(
                rect.x + ContentPaddingLeft,
                rect.y + ContentPaddingTop,
                Mathf.Max(0f, rect.width - (ContentPaddingLeft * 2f)),
                Mathf.Max(0f, rect.height - ContentPaddingTop - 6f));
            using (new GUILayout.AreaScope(contentRect))
            {
                EditorGUILayout.LabelField("Add model to project via HF model id", EditorStyles.boldLabel);

                GUI.SetNextControlName(OwnerFieldControlName);
                EditorGUI.BeginChangeCheck();
                _owner = EditorGUILayout.TextField("Owner", _owner);
                GUI.SetNextControlName(ModelFieldControlName);
                _modelName = EditorGUILayout.TextField("Model", _modelName);
                if (EditorGUI.EndChangeCheck())
                {
                    HandleInputChanged();
                }

                if (_focusRequested && Event.current.type == EventType.Repaint)
                {
                    _focusRequested = false;
                    EditorGUI.FocusTextInControl(OwnerFieldControlName);
                }

                using (new EditorGUI.DisabledScope(!CanAdd))
                {
                    var buttonRect = GUILayoutUtility.GetRect(120f, EditorGUIUtility.singleLineHeight);
                    if (GUI.Button(buttonRect, BuildFileScopeButtonLabel()))
                    {
                        PopupWindow.Show(
                            buttonRect,
                            new ManifestFileSelectionPopup(
                                CurrentModelId,
                                "main",
                                _selectedFiles,
                                files =>
                                {
                                    _selectedFiles = files ?? Array.Empty<string>();
                                    editorWindow?.Repaint();
                                },
                                () => editorWindow?.Repaint()));
                    }
                }

                QueueValidationIfIdle();
                DrawValidationStatus();

                EditorGUILayout.Space(4f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(!CanSubmit))
                    {
                        if (GUILayout.Button("Add", GUILayout.Width(90f)) || (CanSubmit && HandleSubmit()))
                        {
                            editorWindow.Close();
                            _onAdd?.Invoke(
                                _owner.Trim(),
                                _modelName.Trim(),
                                _selectedFiles.Length > 0 ? _selectedFiles : null);
                        }
                    }
                }
            }
        }

        private bool CanAdd => !string.IsNullOrWhiteSpace(_owner) && !string.IsNullOrWhiteSpace(_modelName);
        private bool CanSubmit => CanAdd && _validationController.IsValid;

        private string CurrentModelId => CanAdd
            ? $"{_owner.Trim()}/{_modelName.Trim()}"
            : null;

        private void HandleInputChanged()
        {
            _validationController.Reset();
        }

        private void QueueValidationIfIdle()
        {
            _validationController.QueueValidationIfReady();
        }

        private void DrawValidationStatus()
        {
            switch (_validationController.State)
            {
                case PopupValidationState.Checking:
                    EditorGUILayout.HelpBox(_validationController.Message, MessageType.None);
                    break;
                case PopupValidationState.Invalid:
                    EditorGUILayout.HelpBox(_validationController.Message, MessageType.Warning);
                    break;
            }
        }

        private static bool HandleSubmit()
        {
            var currentEvent = Event.current;
            if (currentEvent.type != EventType.KeyDown || currentEvent.keyCode is not (KeyCode.Return or KeyCode.KeypadEnter))
            {
                return false;
            }

            currentEvent.Use();
            return true;
        }

        private static bool IsInputFieldFocused()
        {
            var focusedControl = GUI.GetNameOfFocusedControl();
            return string.Equals(focusedControl, OwnerFieldControlName, StringComparison.Ordinal) ||
                string.Equals(focusedControl, ModelFieldControlName, StringComparison.Ordinal);
        }

        private string BuildFileScopeButtonLabel()
        {
            return _selectedFiles.Length == 0
                ? "Files: All files"
                : _selectedFiles.Length == 1
                    ? "Files: 1 file"
                    : $"Files: {_selectedFiles.Length} files";
        }
    }
}
