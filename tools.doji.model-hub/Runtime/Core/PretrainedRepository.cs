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

        /// <summary>
        /// Repository paths of the other files this package uses: configs, tokenizers, and similar files.
        /// </summary>
        public IReadOnlyList<string> DataFiles => (_model.dataFiles ?? Array.Empty<RuntimeDataFile>()).Select(file => file.repoPath).ToArray();

        /// <summary>
        /// Whether this package uses a model file or a data file at this path.
        /// </summary>
        public bool HasFile(string repoPath)
        {
            return _model.FindFile(repoPath) != null || _model.FindDataFile(repoPath) != null;
        }

        /// <summary>
        /// Reads one of <see cref="DataFiles"/>. Works on every platform.
        /// </summary>
        public Task<byte[]> ReadFileAsync(
            string repoPath,
            IProgress<DownloadProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            return RuntimeModels.LoadDataAsync(_model, GetDataFile(repoPath), progress, cancellationToken);
        }

        /// <summary>
        /// Reads one of <see cref="DataFiles"/> as UTF-8 text.
        /// </summary>
        public async Task<string> ReadTextAsync(
            string repoPath,
            IProgress<DownloadProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            return Hub.DecodeText(await ReadFileAsync(repoPath, progress, cancellationToken));
        }

        /// <summary>
        /// Reads without waiting, where <see cref="LoadModel"/> can.
        /// </summary>
        public byte[] ReadFile(string repoPath)
        {
            return RuntimeModels.LoadData(_model, GetDataFile(repoPath));
        }

        public string ReadText(string repoPath)
        {
            return Hub.DecodeText(ReadFile(repoPath));
        }

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
        /// Returns the path of the runtime model's Sentis file, downloading or converting it first when needed, for
        /// loaders that read the file themselves. Not on the Web, where a bundled model is not a file.
        /// </summary>
        public Task<string> GetModelPathAsync(
            string repoPath,
            IProgress<DownloadProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            return RuntimeModels.GetPathAsync(_model, GetFile(repoPath), progress, cancellationToken);
        }

        /// <summary>
        /// Returns the path of the runtime model's Sentis file without waiting, where <see cref="LoadModel"/> can load it.
        /// </summary>
        public string GetModelPath(string repoPath)
        {
            return RuntimeModels.GetPath(_model, GetFile(repoPath));
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

        private RuntimeDataFile GetDataFile(string repoPath)
        {
            return _model.FindDataFile(repoPath) ?? throw new System.IO.FileNotFoundException(
                $"AI Model Hub model '{RepoId}' has no file '{repoPath}'. List it in the manifest's files. Its other files are: {string.Join(", ", DataFiles)}.");
        }

        private RuntimeModelFile GetFile(string repoPath)
        {
            return _model.FindFile(repoPath) ?? throw new System.IO.FileNotFoundException(
                $"AI Model Hub model '{RepoId}' has no file '{repoPath}'. Its files are: {string.Join(", ", Files)}.");
        }
    }
}
