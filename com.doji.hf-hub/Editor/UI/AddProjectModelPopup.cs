using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Collects one HuggingFace owner/model identifier and appends it to the project-scoped manifest.
    /// </summary>
    internal sealed class AddProjectModelPopup : PopupWindowContent
    {
        private const float PopupWidth = 320f;
        private const float PopupHeight = 132f;
        private const float ContentPaddingLeft = 10f;
        private const float ContentPaddingTop = 8f;
        private const string OwnerFieldControlName = "ProjectModelOwnerField";
        private const string ModelFieldControlName = "ProjectModelNameField";
        private readonly Action<string, string> _onAdd;
        private string _owner = string.Empty;
        private string _modelName = string.Empty;
        private bool _focusRequested = true;
        private int _validationVersion;
        private string _lastQueuedModelId;
        private ValidationState _validationState = ValidationState.Idle;
        private string _validationMessage;

        public AddProjectModelPopup(Action<string, string> onAdd)
        {
            _onAdd = onAdd;
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
                            _onAdd?.Invoke(_owner.Trim(), _modelName.Trim());
                        }
                    }
                }
            }
        }

        private bool CanAdd => !string.IsNullOrWhiteSpace(_owner) && !string.IsNullOrWhiteSpace(_modelName);
        private bool CanSubmit => CanAdd && _validationState == ValidationState.Valid;

        private string CurrentModelId => CanAdd
            ? $"{_owner.Trim()}/{_modelName.Trim()}"
            : null;

        private void HandleInputChanged()
        {
            _validationVersion++;
            _lastQueuedModelId = null;
            _validationState = ValidationState.Idle;
            _validationMessage = null;
        }

        private void QueueValidationIfIdle()
        {
            var modelId = CurrentModelId;
            if (string.IsNullOrWhiteSpace(modelId) ||
                IsInputFieldFocused() ||
                string.Equals(_lastQueuedModelId, modelId, StringComparison.Ordinal) ||
                _validationState == ValidationState.Checking)
            {
                return;
            }

            _lastQueuedModelId = modelId;
            _validationState = ValidationState.Checking;
            _validationMessage = "Checking Hugging Face repository availability.";
            editorWindow?.Repaint();
            _ = ValidateRepositoryAsync(modelId, _validationVersion);
        }

        private void DrawValidationStatus()
        {
            switch (_validationState)
            {
                case ValidationState.Checking:
                    EditorGUILayout.HelpBox(_validationMessage, MessageType.None);
                    break;
                case ValidationState.Invalid:
                    EditorGUILayout.HelpBox(_validationMessage, MessageType.Warning);
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

        private async Task ValidateRepositoryAsync(string modelId, int validationVersion)
        {
            ValidationState nextState;
            string nextMessage;

            try
            {
                var exists = await RepositoryValidationCacheService.GetOrStartRepositoryValidationAsync(
                    modelId,
                    async () =>
                    {
                        using var request = UnityWebRequest.Head($"https://huggingface.co/{modelId}");
                        request.timeout = 10;
                        await SendRequestAsync(request, CancellationToken.None);
                        return request.responseCode is >= 200 and < 400;
                    });
                nextState = exists ? ValidationState.Valid : ValidationState.Invalid;
                nextMessage = exists ? "Repository exists on Hugging Face." : "Repository was not found on Hugging Face.";
            }
            catch (Exception exception)
            {
                nextState = ValidationState.Invalid;
                nextMessage = exception.Message;
            }

            EditorApplication.delayCall += () =>
            {
                if (validationVersion != _validationVersion ||
                    !string.Equals(CurrentModelId, modelId, StringComparison.Ordinal))
                {
                    return;
                }

                _validationState = nextState;
                _validationMessage = nextMessage;
                _lastQueuedModelId = modelId;
                editorWindow?.Repaint();
            };
        }

        private static bool IsInputFieldFocused()
        {
            var focusedControl = GUI.GetNameOfFocusedControl();
            return string.Equals(focusedControl, OwnerFieldControlName, StringComparison.Ordinal) ||
                string.Equals(focusedControl, ModelFieldControlName, StringComparison.Ordinal);
        }

        private static Task SendRequestAsync(UnityWebRequest request, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            var operation = request.SendWebRequest();
            cancellationToken.Register(() =>
            {
                if (!request.isDone)
                {
                    request.Abort();
                }
            });
            operation.completed += _ =>
            {
                if (request.result == UnityWebRequest.Result.ConnectionError)
                {
                    tcs.TrySetException(new InvalidOperationException(request.error));
                    return;
                }

                tcs.TrySetResult(true);
            };
            return tcs.Task;
        }

        private enum ValidationState
        {
            Idle,
            Checking,
            Valid,
            Invalid
        }
    }
}
