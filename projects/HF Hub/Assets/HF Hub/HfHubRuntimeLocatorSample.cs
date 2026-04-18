using System;
using Doji.AI.ModelHub;
using Unity.InferenceEngine;
using UnityEngine;

namespace Doji.AI.ModelHub.Samples
{
    /// <summary>
    /// Demonstrates project-owned runtime lookup and `.sentis` load through HF Hub locator data.
    /// </summary>
    public sealed class HfHubRuntimeLocatorSample : MonoBehaviour
    {
        [SerializeField]
        private string _modelId = "onnx-community/tiny-clip-test-ONNX";

        [SerializeField]
        private bool _loadOnStart = true;

        [SerializeField]
        [TextArea]
        private string _status = "Idle";

        private Model _loadedModel;

        /// <summary>
        /// Stable HF Hub manifest owner id to resolve against.
        /// </summary>
        /// Stable HuggingFace model id / repository id declared in project or package manifest.
        /// </summary>
        public string ModelId => _modelId;

        /// <summary>
        /// Last lookup or load status for sample diagnostics.
        /// </summary>
        public string Status => _status;

        /// <summary>
        /// Last loaded Sentis model instance, or null when resolution or load failed.
        /// </summary>
        public Model LoadedModel => _loadedModel;

        private void Start()
        {
            if (_loadOnStart)
            {
                Reload();
            }
        }

        /// <summary>
        /// Resolves generated runtime payload and loads serialized Sentis model from disk.
        /// </summary>
        [ContextMenu("Reload Model")]
        public void Reload()
        {
            _loadedModel = null;

            if (!ModelHub.TryResolveRuntimeModel(_modelId, out var resolvedModel))
            {
                _status = $"Resolve failed: '{_modelId}' not found. Sync models first.";
                Debug.LogWarning(_status, this);
                return;
            }

            try
            {
                _loadedModel = ModelHub.LoadRuntimeModel(_modelId);
                _status = $"Loaded '{resolvedModel.DisplayName}' from '{resolvedModel.PrimarySentisPath}'.";
                Debug.Log(_status, this);
            }
            catch (Exception exception)
            {
                _status = $"Load failed: {exception.Message}";
                Debug.LogError(_status, this);
            }
        }
    }
}
