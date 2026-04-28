using System;
using Doji.AI.ModelHub;
using Unity.InferenceEngine;
using UnityEngine;

namespace Doji.AI.ModelHub.Samples
{
    /// <summary>
    /// Demonstrates project-owned runtime lookup and `.sentis` load through HF Hub locator data.
    /// </summary>
    public sealed class RuntimeLocatorSample : MonoBehaviour
    {
        [SerializeField]
        private string _repoId = "onnx-community/tiny-clip-test-ONNX";

        [SerializeField]
        private string _modelFile = "onnx/model.onnx";

        [SerializeField]
        private bool _loadOnStart = true;

        [SerializeField]
        [TextArea]
        private string _status = "Idle";

        private Model _loadedModel;

        /// <summary>
        /// Stable HuggingFace model id declared in project or package manifest.
        /// </summary>
        public string RepoId => _repoId;

        /// <summary>
        /// Repository-relative ONNX file to load from generated runtime snapshot.
        /// </summary>
        public string ModelFile => _modelFile;

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

            if (!Hub.TryResolveRuntimeModel(_repoId, out var resolvedModel))
            {
                _status = $"Resolve failed: '{_repoId}' not found. Sync models first.";
                Debug.LogWarning(_status, this);
                return;
            }

            try
            {
                _loadedModel = Hub.LoadRuntimeModel(_repoId, _modelFile);
                resolvedModel.TryGetSentisPath(_modelFile, out var sentisPath);
                _status = $"Loaded '{resolvedModel}' from '{sentisPath}'.";
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
