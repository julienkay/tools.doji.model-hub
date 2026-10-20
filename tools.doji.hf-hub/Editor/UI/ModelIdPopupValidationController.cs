using System;
using System.Threading.Tasks;
using UnityEditor;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Shares transient owner/model popup validation without writing persistent cache state.
    /// </summary>
    internal sealed class ModelIdPopupValidationController
    {
        private readonly Func<string> _getCurrentModelId;
        private readonly Func<bool> _isInputFocused;
        private readonly Action _requestRepaint;
        private int _validationVersion;
        private string _lastQueuedModelId;

        public ModelIdPopupValidationController(Func<string> getCurrentModelId, Func<bool> isInputFocused, Action requestRepaint)
        {
            _getCurrentModelId = getCurrentModelId ?? throw new ArgumentNullException(nameof(getCurrentModelId));
            _isInputFocused = isInputFocused ?? throw new ArgumentNullException(nameof(isInputFocused));
            _requestRepaint = requestRepaint ?? (() => { });
            State = PopupValidationState.Idle;
        }

        public PopupValidationState State { get; private set; }
        public string Message { get; private set; }

        public bool IsValid => State == PopupValidationState.Valid;

        public void Reset()
        {
            _validationVersion++;
            _lastQueuedModelId = null;
            State = PopupValidationState.Idle;
            Message = null;
        }

        public void QueueValidationIfReady()
        {
            var modelId = _getCurrentModelId();
            if (string.IsNullOrWhiteSpace(modelId) ||
                _isInputFocused() ||
                string.Equals(_lastQueuedModelId, modelId, StringComparison.Ordinal) ||
                State == PopupValidationState.Checking)
            {
                return;
            }

            _lastQueuedModelId = modelId;
            State = PopupValidationState.Checking;
            Message = "Checking Hugging Face repository availability.";
            _requestRepaint();
            _ = ValidateRepositoryAsync(modelId, _validationVersion);
        }

        private async Task ValidateRepositoryAsync(string modelId, int validationVersion)
        {
            PopupValidationState nextState;
            string nextMessage;

            try
            {
                var existsState = await ManifestModelValidationController.ValidateDependencyAsync(modelId, writePersistentCache: false);
                nextState = existsState.Status == ManifestModelValidationStatus.Valid
                    ? PopupValidationState.Valid
                    : PopupValidationState.Invalid;
                nextMessage = existsState.Status == ManifestModelValidationStatus.Valid
                    ? "Repository exists on Hugging Face."
                    : existsState.Message ?? "Repository was not found on Hugging Face.";
            }
            catch (Exception exception)
            {
                nextState = PopupValidationState.Invalid;
                nextMessage = exception.Message;
            }

            EditorApplication.delayCall += () =>
            {
                if (validationVersion != _validationVersion ||
                    !string.Equals(_getCurrentModelId(), modelId, StringComparison.Ordinal))
                {
                    return;
                }

                State = nextState;
                Message = nextMessage;
                _lastQueuedModelId = modelId;
                _requestRepaint();
            };
        }
    }

    internal enum PopupValidationState
    {
        Idle,
        Checking,
        Valid,
        Invalid
    }
}
