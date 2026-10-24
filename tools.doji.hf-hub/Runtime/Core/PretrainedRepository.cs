using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Doji.Downloads;
using Unity.InferenceEngine;

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// One model of one package in a <c>from_pretrained(...)</c> style API.
    /// </summary>
    public sealed class PretrainedRepository
    {
        private readonly RuntimeModelEntry _model;

        internal PretrainedRepository(RuntimePackageEntry package, RuntimeModelEntry model)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            PackageId = package?.packageId;
            PackageVersion = package?.packageVersion;
        }

        /// <summary>
        /// Package whose manifest declares this model.
        /// </summary>
        public string PackageId { get; }

        public string PackageVersion { get; }

        /// <summary>
        /// Hugging Face repository id.
        /// </summary>
        public string RepoId => _model.repoId;

        /// <summary>
        /// The commit resolved in the Editor.
        /// </summary>
        public string Commit => _model.commit;

        public ModelDelivery Delivery => _model.Delivery;

        /// <summary>
        /// Repository paths of the model files this package uses.
        /// </summary>
        public IReadOnlyList<string> Files => (_model.files ?? Array.Empty<RuntimeModelFile>()).Select(file => file.repoPath).ToArray();

        public Task<Model> LoadModelAsync(
            string repoPath,
            IProgress<DownloadProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            return RuntimeModels.LoadAsync(_model, GetFile(repoPath), progress, cancellationToken);
        }

        /// <summary>
        /// Loads without waiting, where <see cref="Hub.LoadModel(string,string)"/> can.
        /// </summary>
        public Model LoadModel(string repoPath)
        {
            return RuntimeModels.Load(_model, GetFile(repoPath));
        }

        /// <summary>
        /// Returns the runtime model's path when it is already a file on this device.
        /// </summary>
        public bool TryGetModelPath(string repoPath, out string path)
        {
            path = null;
            var file = _model.FindFile(repoPath);
            return file != null && RuntimeModels.TryGetLocalPathWithoutWaiting(_model, file, out path);
        }

        public Task EnsureAvailableAsync(IProgress<DownloadProgress> progress = null, CancellationToken cancellationToken = default)
        {
            return RuntimeModels.EnsureAvailableAsync(_model, progress, cancellationToken);
        }

        private RuntimeModelFile GetFile(string repoPath)
        {
            return _model.FindFile(repoPath) ?? throw new System.IO.FileNotFoundException(
                $"HF Hub model '{RepoId}' has no file '{repoPath}'. Its files are: {string.Join(", ", Files)}.");
        }
    }
}
