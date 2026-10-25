using System;
using Doji.AI.ModelHub;
using Doji.Downloads;
using Unity.InferenceEngine;
using UnityEngine;

namespace Doji.AI.ModelHub.Samples
{
    /// <summary>
    /// Loads one model with <see cref="Hub.LoadModelAsync(string,string,IProgress{DownloadProgress},System.Threading.CancellationToken)"/>.
    /// The same call works in the Editor and in players, whether the project bundles the model or downloads it.
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
        /// Hugging Face model id declared in a project or package manifest.
        /// </summary>
        public string RepoId => _repoId;

        /// <summary>
        /// Repository path of the model file, as declared in the manifest.
        /// </summary>
        public string ModelFile => _modelFile;

        /// <summary>
        /// Last load status for sample diagnostics.
        /// </summary>
        public string Status => _status;

        /// <summary>
        /// Last loaded model, or null when loading failed.
        /// </summary>
        public Model LoadedModel => _loadedModel;

        private async void Start()
        {
            if (_loadOnStart)
            {
                await ReloadAsync();
            }
        }

        [ContextMenu("Reload Model")]
        public async void Reload()
        {
            await ReloadAsync();
        }

        private async Awaitable ReloadAsync()
        {
            _loadedModel = null;
            _status = $"Loading '{_repoId}:{_modelFile}'...";
            try
            {
                var progress = new Progress<DownloadProgress>(value =>
                    _status = $"Loading '{_repoId}:{_modelFile}': {value.DownloadedBytes} of {value.TotalBytes?.ToString() ?? "?"} bytes");
                _loadedModel = await Hub.LoadModelAsync(_repoId, _modelFile, progress, destroyCancellationToken);
                _status = $"Loaded '{_repoId}:{_modelFile}' with {_loadedModel.inputs.Count} inputs.";
                Debug.Log(_status, this);
            }
            catch (OperationCanceledException)
            {
                _status = "Canceled.";
            }
            catch (Exception exception)
            {
                _status = $"Load failed: {exception.Message}";
                Debug.LogError(_status, this);
            }
        }
    }
}
