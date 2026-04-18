using System;
using System.IO;
using Unity.InferenceEngine;

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// Wraps one synchronized repository snapshot in a `from_pretrained(...)` style runtime API.
    /// </summary>
    public sealed class HfHubPretrainedRepository
    {
        private readonly HfHubResolvedRuntimeModel _resolvedModel;

        internal HfHubPretrainedRepository(HfHubResolvedRuntimeModel resolvedModel)
        {
            _resolvedModel = resolvedModel ?? throw new ArgumentNullException(nameof(resolvedModel));
        }

        /// <summary>
        /// Manifest owner id that published this synchronized repository snapshot.
        /// </summary>
        public string PackageId => _resolvedModel.PackageId;

        /// <summary>
        /// Package version associated with this synchronized repository snapshot.
        /// </summary>
        public string PackageVersion => _resolvedModel.PackageVersion;

        /// <summary>
        /// Canonical HuggingFace repository id for this synchronized snapshot.
        /// </summary>
        public string RepoId => _resolvedModel.RepoId;

        /// <summary>
        /// User-facing dependency name from the declaring manifest.
        /// </summary>
        public string DisplayName => _resolvedModel.DisplayName;

        /// <summary>
        /// Tries to resolve one generated Sentis payload by repository-relative ONNX path.
        /// </summary>
        public bool TryGetSentisPath(string repoPath, out string filePath)
        {
            return _resolvedModel.TryGetSentisPath(repoPath, out filePath);
        }

        /// <summary>
        /// Resolves one generated Sentis payload by repository-relative ONNX path or throws when missing.
        /// </summary>
        public string GetSentisPath(string repoPath)
        {
            if (TryGetSentisPath(repoPath, out var filePath))
            {
                return filePath;
            }

            throw new FileNotFoundException(
                $"HF Hub runtime model '{RepoId}:{repoPath}' was not found under '{UnityEngine.Application.streamingAssetsPath}'.");
        }

        /// <summary>
        /// Loads one generated Sentis payload by repository-relative ONNX path.
        /// </summary>
        public Model LoadModel(string repoPath)
        {
            return ModelLoader.Load(GetSentisPath(repoPath));
        }
    }
}
