using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Doji.Downloads;
using Unity.InferenceEngine;

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// Loads the models of one package from the ways a user can name them: a file of the package's manifest, a file
    /// path of their own, a <see cref="HubModelReference"/>, or nothing for the package's default model. Packages
    /// keep one instance and call it from their factory methods. <see cref="PretrainedModel{T}"/> uses it for you.
    /// </summary>
    public sealed class HubModelLoader
    {
        /// <param name="packageId">Your package id, for example <c>tools.doji.midas</c>.</param>
        /// <param name="repoId">The repository of the default model, for example <c>julienkay/sentis-MiDaS</c>.</param>
        /// <param name="defaultRepoPath">The default model's path in that repository.</param>
        public HubModelLoader(string packageId, string repoId, string defaultRepoPath)
        {
            if (string.IsNullOrWhiteSpace(packageId))
            {
                throw new ArgumentException("A package id is required.", nameof(packageId));
            }

            if (string.IsNullOrWhiteSpace(repoId))
            {
                throw new ArgumentException("A repository id is required.", nameof(repoId));
            }

            if (string.IsNullOrWhiteSpace(defaultRepoPath))
            {
                throw new ArgumentException("A default model path is required.", nameof(defaultRepoPath));
            }

            PackageId = packageId;
            RepoId = repoId;
            DefaultRepoPath = defaultRepoPath;
        }

        public string PackageId { get; }

        /// <summary>
        /// The repository that a bare repository path refers to.
        /// </summary>
        public string RepoId { get; }

        public string DefaultRepoPath { get; }

        /// <summary>
        /// The model that loads when the user names none.
        /// </summary>
        public HubModelReference DefaultModel => new HubModelReference(PackageId, RepoId, DefaultRepoPath);

        /// <summary>
        /// Whether a string names a file of the user's own: a file that exists, or any absolute path. A missing
        /// absolute path then fails with its path instead of a lookup in the manifest.
        /// </summary>
        public static bool IsFilePath(string model)
        {
            return !string.IsNullOrWhiteSpace(model) && (File.Exists(model) || Path.IsPathRooted(model));
        }

        /// <summary>
        /// Turns a string that is not a file path into a reference: null or empty for <see cref="DefaultModel"/>,
        /// <c>owner/name:path</c> for a file of another repository the package declares, anything else for a path in
        /// <see cref="RepoId"/>.
        /// </summary>
        public HubModelReference ToReference(string model)
        {
            if (string.IsNullOrWhiteSpace(model))
            {
                return DefaultModel;
            }

            var separator = model.IndexOf(':');
            return separator > 0 && model.IndexOf('/') < separator
                ? new HubModelReference(PackageId, model.Substring(0, separator), model.Substring(separator + 1))
                : new HubModelReference(PackageId, RepoId, model);
        }

        /// <summary>
        /// Loads a model named by a string. Works on every platform; a model that the project downloads at runtime
        /// is fetched on first use.
        /// </summary>
        /// <param name="model">
        /// Null for <see cref="DefaultModel"/>. A path of a <c>.sentis</c> or <c>.onnx</c> file loads that file
        /// (<see cref="Hub.LoadModelFromFile"/>). Anything else is a file of the package's manifest, see
        /// <see cref="ToReference"/>.
        /// </param>
        public Task<LoadedModel> LoadAsync(
            string model = null,
            IProgress<DownloadProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            return IsFilePath(model)
                ? Task.FromResult(LoadFile(model))
                : LoadAsync(ToReference(model), progress, cancellationToken);
        }

        /// <summary>
        /// Loads a model chosen with the <see cref="HubModelAttribute"/> picker or from <see cref="Hub.GetModelFiles"/>.
        /// Null loads <see cref="DefaultModel"/>.
        /// </summary>
        public async Task<LoadedModel> LoadAsync(
            HubModelReference model,
            IProgress<DownloadProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            model ??= DefaultModel;
            try
            {
                return new LoadedModel(await model.LoadAsync(progress, cancellationToken), model, GetName(model));
            }
            catch (Exception exception) when (IsHubFile(model) && !(exception is OperationCanceledException))
            {
                throw LoadException(model, exception);
            }
        }

        /// <summary>
        /// Loads without waiting, like <see cref="LoadAsync(string,IProgress{DownloadProgress},CancellationToken)"/>.
        /// Works in the Editor and for bundled models on Windows, macOS, Linux, and iOS.
        /// </summary>
        public LoadedModel Load(string model = null)
        {
            return IsFilePath(model) ? LoadFile(model) : Load(ToReference(model));
        }

        public LoadedModel Load(HubModelReference model)
        {
            model ??= DefaultModel;
            try
            {
                return new LoadedModel(model.Load(), model, GetName(model));
            }
            catch (Exception exception) when (IsHubFile(model))
            {
                throw LoadException(model, exception);
            }
        }

        private static LoadedModel LoadFile(string path)
        {
            return new LoadedModel(Hub.LoadModelFromFile(path), null, Path.GetFileNameWithoutExtension(path));
        }

        private static string GetName(HubModelReference model)
        {
            return model.ModelAsset != null ? model.ModelAsset.name : Path.GetFileNameWithoutExtension(model.RepoPath ?? string.Empty);
        }

        private static bool IsHubFile(HubModelReference model)
        {
            return !model.IsEmpty && model.ModelAsset == null;
        }

        private InvalidOperationException LoadException(HubModelReference model, Exception exception)
        {
            return new InvalidOperationException(
                $"{PackageId} could not load model '{model}'. Include the file in Window > AI Model Hub. {exception.Message}",
                exception);
        }
    }

    /// <summary>
    /// A model that <see cref="HubModelLoader"/> loaded, with where it came from.
    /// </summary>
    public sealed class LoadedModel
    {
        internal LoadedModel(Model model, HubModelReference source, string name)
        {
            Model = model;
            Source = source;
            Name = name;
        }

        public Model Model { get; }

        /// <summary>
        /// The reference it was loaded from, or null for a file path of the user's own.
        /// </summary>
        public HubModelReference Source { get; }

        /// <summary>
        /// A short name for logs and texture names: the file name without its extension, or the model asset's name.
        /// </summary>
        public string Name { get; }
    }
}
