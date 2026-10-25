using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Doji.Downloads;
using Unity.InferenceEngine;
using UnityEngine;

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// Loads the models that packages declare in their AI Model Hub manifests. The same calls work in the Editor and in
    /// players, whether the project bundles a model or downloads it at runtime.
    /// </summary>
    public static class Hub
    {
        /// <summary>
        /// Package manifest filename that declares remote model dependencies for a Unity package.
        /// </summary>
        public const string ManifestFileName = "model-hub-manifest.json";

        /// <summary>
        /// Copyable example manifest filename that package authors can use as a starting point.
        /// </summary>
        public const string ManifestTemplateFileName = "model-hub-manifest.template.json";

        /// <summary>
        /// Folder inside StreamingAssets that holds the registry and bundled models.
        /// </summary>
        public const string RuntimeStreamingRelativeRoot = "model-hub";

        /// <summary>
        /// Loads a model by repository id and the repository path the package declared, for example
        /// <c>onnx/model.onnx</c> or <c>sentis/model.sentis</c>. A downloaded model is fetched on first use.
        /// Works on every platform.
        /// </summary>
        public static Task<Model> LoadModelAsync(
            string repoId,
            string repoPath,
            IProgress<DownloadProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            return LoadModelAsync(null, repoId, repoPath, progress, cancellationToken);
        }

        /// <summary>
        /// Loads a model of one package. Use it when two packages use the same repository with different files.
        /// </summary>
        public static async Task<Model> LoadModelAsync(
            string packageId,
            string repoId,
            string repoPath,
            IProgress<DownloadProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            var catalog = await RuntimeModels.GetCatalogAsync();
            var (_, model, file) = RuntimeModels.Find(catalog, packageId, repoId, repoPath);
            return await RuntimeModels.LoadAsync(model, file, progress, cancellationToken);
        }

        /// <summary>
        /// Loads a model without waiting. Works in the Editor, for bundled models on platforms where StreamingAssets is
        /// a folder, and for downloaded models that are already on the device. Otherwise use
        /// <see cref="LoadModelAsync(string,string,IProgress{DownloadProgress},CancellationToken)"/>.
        /// </summary>
        public static Model LoadModel(string repoId, string repoPath)
        {
            return LoadModel(null, repoId, repoPath);
        }

        public static Model LoadModel(string packageId, string repoId, string repoPath)
        {
            var (_, model, file) = RuntimeModels.Find(RuntimeModels.GetCatalog(), packageId, repoId, repoPath);
            return RuntimeModels.Load(model, file);
        }

        /// <summary>
        /// Downloads (and for ONNX files, converts) every file of a model without loading it, for loading screens.
        /// Bundled models need nothing, except a one-time copy on Android.
        /// </summary>
        public static Task EnsureAvailableAsync(
            string repoId,
            IProgress<DownloadProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            return EnsureAvailableAsync(null, repoId, progress, cancellationToken);
        }

        public static async Task EnsureAvailableAsync(
            string packageId,
            string repoId,
            IProgress<DownloadProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            var catalog = await RuntimeModels.GetCatalogAsync();
            var (_, model, _) = RuntimeModels.Find(catalog, packageId, repoId, null);
            await RuntimeModels.EnsureAvailableAsync(model, progress, cancellationToken);
        }

        /// <summary>
        /// Opens a model in <c>from_pretrained(...)</c> style. On Android and the Web use <see cref="FromPretrainedAsync(string)"/>.
        /// </summary>
        public static PretrainedRepository FromPretrained(string repoId)
        {
            return FromPretrained(null, repoId);
        }

        public static PretrainedRepository FromPretrained(string packageId, string repoId)
        {
            var (package, model, _) = RuntimeModels.Find(RuntimeModels.GetCatalog(), packageId, repoId, null);
            return new PretrainedRepository(package, model);
        }

        public static Task<PretrainedRepository> FromPretrainedAsync(string repoId)
        {
            return FromPretrainedAsync(null, repoId);
        }

        public static async Task<PretrainedRepository> FromPretrainedAsync(string packageId, string repoId)
        {
            var catalog = await RuntimeModels.GetCatalogAsync();
            var (package, model, _) = RuntimeModels.Find(catalog, packageId, repoId, null);
            return new PretrainedRepository(package, model);
        }

        /// <summary>
        /// Sets the converter for ONNX models that players download. AI Model Hub uses <c>tools.doji.onnx-model-loader</c>
        /// when it is installed; call this to use another converter. The id must change whenever the converter's output
        /// can change, because converted models are found by it.
        /// </summary>
        public static void RegisterOnnxConverter(IOnnxModelConverter converter, string converterId)
        {
            if (converter != null && string.IsNullOrWhiteSpace(converterId))
            {
                throw new ArgumentException("A converter id is required.", nameof(converterId));
            }

            RuntimeModels.OnnxConverter = converter;
            RuntimeModels.OnnxConverterId = converter == null ? null : converterId;
        }

        /// <summary>
        /// Path of one runtime model inside StreamingAssets: <c>model-hub/models/owner/name/onnx/model.sentis</c>.
        /// </summary>
        internal static string GetRuntimeModelRelativePath(string repository, string repoPath)
        {
            var relativeRepoPath = SanitizeRelativePath(repoPath);
            var sentisPath = Path.ChangeExtension(relativeRepoPath, ".sentis")?.Replace('\\', '/');
            return $"{RuntimeStreamingRelativeRoot}/models/{SanitizeRelativePath(repository)}/{sentisPath}";
        }

        internal static string GetRuntimeAbsolutePath(string relativePath)
        {
            return Path.Combine(Application.streamingAssetsPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        /// <summary>
        /// URL of a StreamingAssets file for <c>UnityWebRequest</c>: a <c>jar:</c> URL on Android, an HTTP URL on the Web.
        /// </summary>
        internal static string GetRuntimeUrl(string relativePath)
        {
            var root = Application.streamingAssetsPath;
            return root.Contains("://", StringComparison.Ordinal)
                ? $"{root.TrimEnd('/')}/{relativePath}"
                : new Uri(GetRuntimeAbsolutePath(relativePath)).AbsoluteUri;
        }

        internal static string SanitizePathSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "unnamed";
            }

            var invalidCharacters = Path.GetInvalidFileNameChars();
            var sanitized = new string(value
                .Select(character => invalidCharacters.Contains(character) || character == '/' || character == '\\'
                    ? '-'
                    : character)
                .ToArray());

            return sanitized.Replace(" ", "-");
        }

        internal static string SanitizeRelativePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "unnamed";
            }

            return string.Join(
                "/",
                value.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(segment => segment != "." && segment != "..")
                    .Select(SanitizePathSegment));
        }
    }
}
