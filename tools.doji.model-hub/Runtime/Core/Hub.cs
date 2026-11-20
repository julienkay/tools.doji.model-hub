using System;
using System.Collections.Generic;
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
            var (_, model, file) = await RuntimeModels.FindAsync(catalog, packageId, repoId, repoPath);
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
        /// Reads a file of a model that is not a model itself, such as <c>config.json</c> or a tokenizer file, by
        /// repository id and the path the package's manifest lists. It reaches the device like the model: bundled, or
        /// downloaded on first use. Works on every platform.
        /// </summary>
        public static Task<byte[]> ReadFileAsync(
            string repoId,
            string repoPath,
            IProgress<DownloadProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            return ReadFileAsync(null, repoId, repoPath, progress, cancellationToken);
        }

        /// <summary>
        /// Reads a file of one package's model. Use it when two packages use the same repository with different files.
        /// </summary>
        public static async Task<byte[]> ReadFileAsync(
            string packageId,
            string repoId,
            string repoPath,
            IProgress<DownloadProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            var catalog = await RuntimeModels.GetCatalogAsync();
            var (_, model, file) = await RuntimeModels.FindDataFileAsync(catalog, packageId, repoId, repoPath);
            return await RuntimeModels.LoadDataAsync(model, file, progress, cancellationToken);
        }

        /// <summary>
        /// Reads a text file of a model as UTF-8, like
        /// <see cref="ReadFileAsync(string,string,IProgress{DownloadProgress},CancellationToken)"/>.
        /// </summary>
        public static Task<string> ReadTextAsync(
            string repoId,
            string repoPath,
            IProgress<DownloadProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            return ReadTextAsync(null, repoId, repoPath, progress, cancellationToken);
        }

        public static async Task<string> ReadTextAsync(
            string packageId,
            string repoId,
            string repoPath,
            IProgress<DownloadProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            return DecodeText(await ReadFileAsync(packageId, repoId, repoPath, progress, cancellationToken));
        }

        /// <summary>
        /// Reads a file of a model without waiting, where <see cref="LoadModel(string,string)"/> can load a model.
        /// </summary>
        public static byte[] ReadFile(string repoId, string repoPath)
        {
            return ReadFile(null, repoId, repoPath);
        }

        public static byte[] ReadFile(string packageId, string repoId, string repoPath)
        {
            var (_, model, file) = RuntimeModels.FindDataFile(RuntimeModels.GetCatalog(), packageId, repoId, repoPath);
            return RuntimeModels.LoadData(model, file);
        }

        public static string ReadText(string repoId, string repoPath)
        {
            return ReadText(null, repoId, repoPath);
        }

        public static string ReadText(string packageId, string repoId, string repoPath)
        {
            return DecodeText(ReadFile(packageId, repoId, repoPath));
        }

        /// <summary>
        /// UTF-8, without a byte order mark, like <c>File.ReadAllText</c>.
        /// </summary>
        internal static string DecodeText(byte[] bytes)
        {
            if (bytes == null)
            {
                return null;
            }

            var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            return hasBom
                ? System.Text.Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3)
                : System.Text.Encoding.UTF8.GetString(bytes);
        }

        /// <summary>
        /// Downloads (and for ONNX files, converts) every file of a model, data files included, without loading it, for
        /// loading screens.
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
            var (_, model, _) = await RuntimeModels.FindAsync(catalog, packageId, repoId, null);
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
            var (package, model, _) = await RuntimeModels.FindAsync(catalog, packageId, repoId, null);
            return new PretrainedRepository(package, model);
        }

        /// <summary>
        /// Lists the model files of one package that this project includes, for model pickers. Optional files that the
        /// project did not turn on are not listed. On Android and the Web use <see cref="GetModelFilesAsync"/>.
        /// </summary>
        public static IReadOnlyList<HubModelFile> GetModelFiles(string packageId)
        {
            return HubModelFile.FromRegistry(RuntimeModels.GetCatalog(), packageId);
        }

        public static async Task<IReadOnlyList<HubModelFile>> GetModelFilesAsync(string packageId)
        {
            return HubModelFile.FromRegistry(await RuntimeModels.GetCatalogAsync(), packageId);
        }

        /// <summary>
        /// Loads a model file that you got without AI Model Hub, for example one your app downloaded. Loads
        /// <c>.sentis</c> files directly. Converts <c>.onnx</c> files in the Editor, and in players with the converter
        /// that <see cref="RegisterOnnxConverter"/> sets (<c>tools.doji.onnx-model-loader</c> when it is installed).
        /// </summary>
        public static Model LoadModelFromFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A file path is required.", nameof(path));
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Model file '{path}' does not exist.", path);
            }

            if (!string.Equals(Path.GetExtension(path), ".onnx", StringComparison.OrdinalIgnoreCase))
            {
                return ModelLoader.Load(path);
            }

            var converter = RuntimeModels.OnnxConverter ?? (Application.isEditor ? RuntimeModels.EditorOnnxConverter : null);
            if (converter == null)
            {
                throw new InvalidOperationException(
                    $"'{path}' is an ONNX file, and this player has no ONNX converter. Install the tools.doji.onnx-model-loader package, or load a .sentis file.");
            }

            return converter.Convert(new OnnxConversionRequest(path)).Model;
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
        /// Path of one data file inside StreamingAssets: <c>model-hub/files/owner/name/scheduler/scheduler_config.json</c>.
        /// </summary>
        internal static string GetRuntimeDataRelativePath(string repository, string repoPath)
        {
            return $"{RuntimeStreamingRelativeRoot}/files/{SanitizeRelativePath(repository)}/{SanitizeRelativePath(repoPath)}";
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
