using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Doji.Downloads;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.Networking;

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// Finds and loads runtime models. In the Editor, models come from this project's import records. In players,
    /// they come from the registry the build wrote: bundled in StreamingAssets, or downloaded into the device cache.
    /// </summary>
    internal static class RuntimeModels
    {
        internal const string RegistryRelativePath = Hub.RuntimeStreamingRelativeRoot + "/model-hub-runtime.json";

        private static RuntimeRegistry _registry;

        /// <summary>
        /// Editor hook: returns this project's models with <see cref="RuntimeModelFile.localPath"/> set. Set by the
        /// Editor assembly; null in players.
        /// </summary>
        internal static Func<RuntimeRegistry> EditorCatalog { get; set; }

        /// <summary>
        /// Test hook that replaces the registry a player would read.
        /// </summary>
        internal static RuntimeRegistry RegistryOverride { get; set; }

        /// <summary>
        /// Converter for ONNX models that a player downloads. <see cref="Hub.RegisterOnnxConverter"/> sets it.
        /// </summary>
        internal static IOnnxModelConverter OnnxConverter { get; set; }

        internal static string OnnxConverterId { get; set; }

        /// <summary>
        /// Editor hook: the Editor's ONNX converter, for <see cref="Hub.LoadModelFromFile"/>. Set by the Editor assembly.
        /// </summary>
        internal static IOnnxModelConverter EditorOnnxConverter { get; set; }

        /// <summary>
        /// The registered converter's id, else the id the build recorded for the converter it shipped.
        /// </summary>
        private static string CurrentConverterId => !string.IsNullOrWhiteSpace(OnnxConverterId)
            ? OnnxConverterId
            : (RegistryOverride ?? _registry)?.onnxConverterId;

        /// <summary>
        /// Whether StreamingAssets is a folder on this platform. On Android it is inside the APK, and on the Web it is a URL.
        /// </summary>
        internal static bool StreamingAssetsIsFolder =>
            Application.platform != RuntimePlatform.Android && Application.platform != RuntimePlatform.WebGLPlayer;

        internal static void ResetForTests()
        {
            _registry = null;
        }

        public static async Task<RuntimeRegistry> GetCatalogAsync()
        {
            var catalog = TryGetCatalogWithoutWaiting();
            if (catalog != null)
            {
                return catalog;
            }

            var json = await ReadStreamingAssetTextAsync(RegistryRelativePath);
            return SetRegistry(RuntimeRegistry.FromJson(json));
        }

        public static RuntimeRegistry GetCatalog()
        {
            var catalog = TryGetCatalogWithoutWaiting();
            if (catalog != null)
            {
                return catalog;
            }

            if (!StreamingAssetsIsFolder)
            {
                throw new InvalidOperationException(
                    "On Android and the Web, AI Model Hub reads its registry asynchronously. Use Hub.LoadModelAsync or Hub.FromPretrainedAsync.");
            }

            var path = Hub.GetRuntimeAbsolutePath(RegistryRelativePath);
            return SetRegistry(RuntimeRegistry.FromJson(File.Exists(path) ? File.ReadAllText(path) : null));
        }

        private static RuntimeRegistry SetRegistry(RuntimeRegistry registry)
        {
            _registry = registry;
            return registry;
        }

        private static RuntimeRegistry TryGetCatalogWithoutWaiting()
        {
            if (RegistryOverride != null)
            {
                return RegistryOverride;
            }

            if (Application.isEditor && EditorCatalog != null)
            {
                return EditorCatalog();
            }

            return _registry;
        }

        public static (RuntimePackageEntry Package, RuntimeModelEntry Model, RuntimeModelFile File) Find(
            RuntimeRegistry catalog,
            string packageId,
            string repoId,
            string repoPath)
        {
            if (catalog.TryFind(packageId, repoId, repoPath, out var package, out var model, out var file))
            {
                return (package, model, file);
            }

            var known = catalog.GetKnownFiles(repoId);
            var owner = packageId == null ? string.Empty : $" for package '{packageId}'";
            throw new FileNotFoundException(known.Count == 0
                ? $"AI Model Hub has no model '{repoId}'{owner}. Add it to a manifest and import it in Window > AI Model Hub."
                : $"AI Model Hub model '{repoId}'{owner} has no file '{repoPath}'. Its files are: {string.Join(", ", known)}.");
        }

        public static (RuntimePackageEntry Package, RuntimeModelEntry Model, RuntimeDataFile File) FindDataFile(
            RuntimeRegistry catalog,
            string packageId,
            string repoId,
            string repoPath)
        {
            if (catalog.TryFindDataFile(packageId, repoId, repoPath, out var package, out var model, out var file))
            {
                return (package, model, file);
            }

            var known = catalog.GetKnownDataFiles(repoId);
            var owner = packageId == null ? string.Empty : $" for package '{packageId}'";
            throw new FileNotFoundException(catalog.GetKnownFiles(repoId).Count == 0 && known.Count == 0
                ? $"AI Model Hub has no model '{repoId}'{owner}. Add it to a manifest and import it in Window > AI Model Hub."
                : $"AI Model Hub model '{repoId}'{owner} has no file '{repoPath}'. List it in the manifest's files. Its other files are: {string.Join(", ", known)}.");
        }

        /// <summary>
        /// Reads one data file, downloading it first when needed.
        /// </summary>
        public static async Task<byte[]> LoadDataAsync(
            RuntimeModelEntry model,
            RuntimeDataFile file,
            IProgress<DownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            if (TryGetDataPathWithoutWaiting(model, file, out var localPath))
            {
                return File.ReadAllBytes(localPath);
            }

            if (model.Delivery == ModelDelivery.Bundle)
            {
                // Android and the Web: data files are small, so they are read into memory instead of copied.
                return await ReadStreamingAssetBytesAsync(file.relativePath, progress, cancellationToken);
            }

            return File.ReadAllBytes(await EnsureDataDownloadedAsync(model, file, progress, cancellationToken));
        }

        /// <summary>
        /// Reads one data file without waiting, where <see cref="Load"/> can load a model.
        /// </summary>
        public static byte[] LoadData(RuntimeModelEntry model, RuntimeDataFile file)
        {
            if (TryGetDataPathWithoutWaiting(model, file, out var localPath))
            {
                return File.ReadAllBytes(localPath);
            }

            throw new InvalidOperationException(model.Delivery == ModelDelivery.Download
                ? $"AI Model Hub file '{model.repoId}:{file.repoPath}' is downloaded on first use. Use Hub.ReadFileAsync or Hub.ReadTextAsync, or Hub.EnsureAvailableAsync on a loading screen."
                : $"AI Model Hub file '{model.repoId}:{file.repoPath}' is inside the build package on this platform. Use Hub.ReadFileAsync or Hub.ReadTextAsync.");
        }

        /// <summary>
        /// Returns the path of a data file that is already a file on this device.
        /// </summary>
        internal static bool TryGetDataPathWithoutWaiting(RuntimeModelEntry model, RuntimeDataFile file, out string path)
        {
            path = null;
            if (!string.IsNullOrWhiteSpace(file.localPath))
            {
                path = File.Exists(file.localPath) ? file.localPath : null;
                if (path == null)
                {
                    throw new FileNotFoundException(
                        $"AI Model Hub file '{model.repoId}:{file.repoPath}' is not downloaded in this project. Download it in Window > AI Model Hub.",
                        file.localPath);
                }

                return true;
            }

            if (model.Delivery == ModelDelivery.Bundle)
            {
                if (!StreamingAssetsIsFolder)
                {
                    return false;
                }

                path = Hub.GetRuntimeAbsolutePath(file.relativePath);
                return true;
            }

            var downloaded = CachePaths.GetSnapshotFilePath(model.CacheRepoId, model.commit, file.repoPath);
            if (CacheBlobs.IsComplete(downloaded, file.source?.size ?? 0))
            {
                path = downloaded;
                return true;
            }

            return false;
        }

        private static Task<string> EnsureDataDownloadedAsync(
            RuntimeModelEntry model,
            RuntimeDataFile file,
            IProgress<DownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            return RuntimeDownloader.EnsureFileAsync(
                model.CacheRepoId,
                model.revision,
                model.commit,
                file.source ?? new RuntimeSourceFile { repoPath = file.repoPath },
                progress,
                cancellationToken);
        }

        /// <summary>
        /// Loads one runtime model, downloading or converting it first when needed.
        /// </summary>
        public static async Task<Model> LoadAsync(
            RuntimeModelEntry model,
            RuntimeModelFile file,
            IProgress<DownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            if (TryGetLocalPathWithoutWaiting(model, file, out var localPath))
            {
                return ModelLoader.Load(localPath);
            }

            if (model.Delivery == ModelDelivery.Bundle)
            {
                if (Application.platform == RuntimePlatform.WebGLPlayer)
                {
                    var bytes = await ReadStreamingAssetBytesAsync(file.relativePath, progress, cancellationToken);
                    using var stream = new MemoryStream(bytes, false);
                    return ModelLoader.Load(stream);
                }

                return ModelLoader.Load(await CopyBundledModelAsync(file, progress, cancellationToken));
            }

            return ModelLoader.Load(await EnsureDownloadedAsync(model, file, progress, cancellationToken));
        }

        /// <summary>
        /// Loads one runtime model without waiting: in the Editor, from a bundled file on platforms where
        /// StreamingAssets is a folder, or from a model that was already downloaded.
        /// </summary>
        public static Model Load(RuntimeModelEntry model, RuntimeModelFile file)
        {
            if (TryGetLocalPathWithoutWaiting(model, file, out var localPath))
            {
                return ModelLoader.Load(localPath);
            }

            throw new InvalidOperationException(model.Delivery == ModelDelivery.Download
                ? $"AI Model Hub model '{model.repoId}:{file.repoPath}' is downloaded on first use. Use Hub.LoadModelAsync, or Hub.EnsureAvailableAsync on a loading screen."
                : $"AI Model Hub model '{model.repoId}:{file.repoPath}' is inside the build package on this platform. Use Hub.LoadModelAsync.");
        }

        /// <summary>
        /// Makes every file of a model available on the device without loading it, for loading screens.
        /// </summary>
        public static async Task EnsureAvailableAsync(RuntimeModelEntry model, IProgress<DownloadProgress> progress, CancellationToken cancellationToken)
        {
            foreach (var file in model.files ?? Array.Empty<RuntimeModelFile>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (TryGetLocalPathWithoutWaiting(model, file, out _))
                {
                    continue;
                }

                if (model.Delivery == ModelDelivery.Download)
                {
                    await EnsureDownloadedAsync(model, file, progress, cancellationToken);
                }
                else if (Application.platform == RuntimePlatform.Android)
                {
                    await CopyBundledModelAsync(file, progress, cancellationToken);
                }
            }

            if (model.Delivery != ModelDelivery.Download)
            {
                return;
            }

            foreach (var file in model.dataFiles ?? Array.Empty<RuntimeDataFile>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryGetDataPathWithoutWaiting(model, file, out _))
                {
                    await EnsureDataDownloadedAsync(model, file, progress, cancellationToken);
                }
            }
        }

        /// <summary>
        /// Returns the path of a runtime model that is already a file on this device.
        /// </summary>
        internal static bool TryGetLocalPathWithoutWaiting(RuntimeModelEntry model, RuntimeModelFile file, out string path)
        {
            path = null;
            if (!string.IsNullOrWhiteSpace(file.localPath))
            {
                path = File.Exists(file.localPath) ? file.localPath : null;
                if (path == null)
                {
                    throw new FileNotFoundException(
                        $"AI Model Hub model '{model.repoId}:{file.repoPath}' is not set up in this project. Download or convert it in Window > AI Model Hub.",
                        file.localPath);
                }

                return true;
            }

            if (model.Delivery == ModelDelivery.Bundle)
            {
                if (StreamingAssetsIsFolder)
                {
                    path = Hub.GetRuntimeAbsolutePath(file.relativePath);
                    return true;
                }

                var copied = GetBundledCopyPath(file);
                if (File.Exists(copied))
                {
                    path = copied;
                    return true;
                }

                return false;
            }

            var downloaded = GetDownloadedRuntimePath(model, file);
            if (downloaded != null && File.Exists(downloaded))
            {
                path = downloaded;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Where a downloaded model's runtime file is: the Sentis file in the cache, or the converted ONNX file.
        /// Null when the ONNX conversion key cannot be computed yet.
        /// </summary>
        private static string GetDownloadedRuntimePath(RuntimeModelEntry model, RuntimeModelFile file)
        {
            if (!file.IsOnnx)
            {
                return CachePaths.GetSnapshotFilePath(model.CacheRepoId, model.commit, file.repoPath);
            }

            return CurrentConverterId == null || string.IsNullOrWhiteSpace(file.source?.etag)
                ? null
                : ConvertedModelStore.GetModelPath(model.repoId, GetConversionKey(file));
        }

        private static string GetConversionKey(RuntimeModelFile file)
        {
            return ConvertedModelStore.ComputeKey(CachePaths.NormalizeEtag(file.source.etag), CurrentConverterId, file.recipeHash);
        }

        private static async Task<string> EnsureDownloadedAsync(
            RuntimeModelEntry model,
            RuntimeModelFile file,
            IProgress<DownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            if (file.IsOnnx && OnnxConverter == null)
            {
                throw new InvalidOperationException(
                    $"AI Model Hub model '{model.repoId}:{file.repoPath}' is an ONNX file that this player downloads, and needs an ONNX converter. Install the tools.doji.onnx-model-loader package, or bundle the model.");
            }

            foreach (var companion in file.companions ?? Array.Empty<RuntimeSourceFile>())
            {
                await RuntimeDownloader.EnsureFileAsync(model.CacheRepoId, model.revision, model.commit, companion, progress, cancellationToken);
            }

            var sourcePath = await RuntimeDownloader.EnsureFileAsync(model.CacheRepoId, model.revision, model.commit, file.source, progress, cancellationToken);
            if (!file.IsOnnx)
            {
                return sourcePath;
            }

            var key = GetConversionKey(file);
            if (ConvertedModelStore.TryGet(model.repoId, key, out var convertedPath))
            {
                return convertedPath;
            }

            var recipe = file.ToRecipe();
            var result = OnnxConverter.Convert(new OnnxConversionRequest(sourcePath, recipe));
            var warnings = new System.Collections.Generic.List<string>();
            var converted = OnnxConversionSettings.Apply(result.Model, recipe, warnings);
            foreach (var warning in warnings)
            {
                Debug.LogWarning($"AI Model Hub: {model.repoId}:{file.repoPath}: {warning}");
            }

            return ConvertedModelStore.Save(model.repoId, key, converted, new ConvertedModelRecord
            {
                repository = model.repoId,
                repoPath = file.repoPath,
                sourceEtag = CachePaths.NormalizeEtag(file.source.etag),
                converterId = CurrentConverterId,
                formatGeneration = TryGetCatalogWithoutWaiting()?.formatGeneration,
                recipeHash = file.recipeHash
            });
        }

        /// <summary>
        /// Android keeps StreamingAssets inside the APK. Each bundled model is copied once to persistentDataPath and
        /// loaded from there, so it is never held in memory twice.
        /// </summary>
        private static async Task<string> CopyBundledModelAsync(RuntimeModelFile file, IProgress<DownloadProgress> progress, CancellationToken cancellationToken)
        {
            var target = GetBundledCopyPath(file);
            if (File.Exists(target))
            {
                return target;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target) ?? string.Empty);
            var temporary = target + ".incomplete";
            using (var request = new UnityWebRequest(Hub.GetRuntimeUrl(file.relativePath), UnityWebRequest.kHttpVerbGET))
            {
                request.downloadHandler = new DownloadHandlerFile(temporary) { removeFileOnAbort = true };
                await SendAsync(request, progress, cancellationToken);
            }

            if (File.Exists(target))
            {
                File.Delete(target);
            }

            File.Move(temporary, target);
            return target;
        }

        private static string GetBundledCopyPath(RuntimeModelFile file)
        {
            var version = string.IsNullOrWhiteSpace(Application.version) ? "0" : Application.version;
            return Path.Combine(GetBundledCopiesRoot(), version, file.relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        /// <summary>
        /// Folder of the Android copies of bundled models, one subfolder per app version.
        /// </summary>
        internal static string GetBundledCopiesRoot()
        {
            return Path.Combine(CachePaths.GetDataRoot(), "bundled");
        }

        private static async Task<string> ReadStreamingAssetTextAsync(string relativePath)
        {
            if (StreamingAssetsIsFolder)
            {
                var path = Hub.GetRuntimeAbsolutePath(relativePath);
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }

            using var request = UnityWebRequest.Get(Hub.GetRuntimeUrl(relativePath));
            try
            {
                await SendAsync(request, null, CancellationToken.None);
            }
            catch (IOException) when (request.responseCode == 404)
            {
                return null;
            }

            return request.downloadHandler.text;
        }

        private static async Task<byte[]> ReadStreamingAssetBytesAsync(string relativePath, IProgress<DownloadProgress> progress, CancellationToken cancellationToken)
        {
            using var request = UnityWebRequest.Get(Hub.GetRuntimeUrl(relativePath));
            await SendAsync(request, progress, cancellationToken);
            return request.downloadHandler.data;
        }

        private static async Task SendAsync(UnityWebRequest request, IProgress<DownloadProgress> progress, CancellationToken cancellationToken)
        {
            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    request.Abort();
                    cancellationToken.ThrowIfCancellationRequested();
                }

                progress?.Report(new DownloadProgress((long)request.downloadedBytes, null));
                await Task.Yield();
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                throw new IOException($"Reading '{request.url}' failed: {request.error}");
            }
        }
    }
}
