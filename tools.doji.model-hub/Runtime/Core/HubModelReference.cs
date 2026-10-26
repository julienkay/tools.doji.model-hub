using System;
using System.Threading;
using System.Threading.Tasks;
using Doji.Downloads;
using Unity.InferenceEngine;
using UnityEngine;

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// A serializable choice of model: a file that a package declares in its AI Model Hub manifest, or a
    /// <see cref="ModelAsset"/> of your own. Add <see cref="HubModelAttribute"/> to a field to show a picker of the
    /// package's model files in the Inspector.
    /// </summary>
    [Serializable]
    public sealed class HubModelReference
    {
        [SerializeField]
        private string _packageId;

        [SerializeField]
        private string _repoId;

        [SerializeField]
        private string _repoPath;

        [SerializeField]
        private ModelAsset _modelAsset;

        public HubModelReference()
        {
        }

        /// <summary>
        /// References a file that a package declares, for example
        /// <c>new HubModelReference("tools.doji.midas", "julienkay/sentis-MiDaS", "onnx/midas_v21_small_256.onnx")</c>.
        /// </summary>
        public HubModelReference(string packageId, string repoId, string repoPath)
        {
            _packageId = packageId;
            _repoId = repoId;
            _repoPath = repoPath;
        }

        /// <summary>
        /// References a model asset of your own instead of a package's file.
        /// </summary>
        public HubModelReference(ModelAsset modelAsset)
        {
            _modelAsset = modelAsset;
        }

        /// <summary>
        /// Package whose manifest declares the file. Null for a model asset.
        /// </summary>
        public string PackageId => _modelAsset != null ? null : NullIfEmpty(_packageId);

        public string RepoId => _modelAsset != null ? null : NullIfEmpty(_repoId);

        public string RepoPath => _modelAsset != null ? null : NullIfEmpty(_repoPath);

        /// <summary>
        /// The model asset of your own, or null for a package's file.
        /// </summary>
        public ModelAsset ModelAsset => _modelAsset;

        /// <summary>
        /// Whether the reference names neither a file nor a model asset.
        /// </summary>
        public bool IsEmpty => _modelAsset == null && (RepoId == null || RepoPath == null);

        /// <summary>
        /// Loads the model. A downloaded model is fetched on first use. Works on every platform.
        /// </summary>
        public Task<Model> LoadAsync(IProgress<DownloadProgress> progress = null, CancellationToken cancellationToken = default)
        {
            return _modelAsset != null
                ? Task.FromResult(ModelLoader.Load(_modelAsset))
                : Hub.LoadModelAsync(PackageId, RequireRepoId(), RepoPath, progress, cancellationToken);
        }

        /// <summary>
        /// Loads the model without waiting, where <see cref="Hub.LoadModel(string,string,string)"/> can.
        /// </summary>
        public Model Load()
        {
            return _modelAsset != null
                ? ModelLoader.Load(_modelAsset)
                : Hub.LoadModel(PackageId, RequireRepoId(), RepoPath);
        }

        public override string ToString()
        {
            if (_modelAsset != null)
            {
                return _modelAsset.name;
            }

            return IsEmpty ? "(none)" : $"{RepoId}:{RepoPath}";
        }

        private string RequireRepoId()
        {
            if (IsEmpty)
            {
                throw new InvalidOperationException("The model reference names no model. Choose a model file or a model asset.");
            }

            return RepoId;
        }

        private static string NullIfEmpty(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }

    /// <summary>
    /// Shows a picker of one package's model files for a <see cref="HubModelReference"/> field, plus a choice for a
    /// model asset of your own. Without a package id, the picker lists the model files of every package.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class HubModelAttribute : PropertyAttribute
    {
        public HubModelAttribute(string packageId = null)
        {
            PackageId = packageId;
        }

        /// <summary>
        /// Lists the model files of the package that a <see cref="PretrainedModelAttribute"/> on
        /// <paramref name="modelType"/> names, for example <c>[HubModel(typeof(Midas))]</c>.
        /// </summary>
        public HubModelAttribute(Type modelType)
        {
            PackageId = PretrainedModelAttribute.Get(modelType)?.PackageId;
        }

        public string PackageId { get; }
    }
}
