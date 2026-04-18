using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Converts cached ONNX files into runtime `.sentis` artifacts under `StreamingAssets` and records editor-only import metadata.
    /// </summary>
    internal sealed class HfHubProjectImportService : IHfHubProjectImportService
    {
        private readonly IHfHubOnnxModelConverter _onnxModelConverter;

        /// <summary>
        /// Creates import service with injectable ONNX conversion contract for editor/runtime architecture parity.
        /// </summary>
        public HfHubProjectImportService(IHfHubOnnxModelConverter onnxModelConverter = null)
        {
            _onnxModelConverter = onnxModelConverter ?? new HfHubEditorOnnxModelConverter();
        }

        /// <summary>
        /// Returns generated runtime `.sentis` artifact if still current, otherwise converts cached ONNX and serializes `.sentis` into `StreamingAssets`.
        /// </summary>
        public Task<HfHubImportedModelArtifact> ImportAsync(
            HfHubDiscoveredModelDependency dependency,
            HfHubCachedRawFile cachedRawFile,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.Equals(Path.GetExtension(cachedRawFile.FilePath), ".onnx", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"HF Hub currently imports only ONNX source files, but '{cachedRawFile.FilePath}' is not an .onnx file.");
            }

            var sentisAssetPath = HfHubProjectPaths.GetRuntimeSentisAssetPath(
                dependency.Repository,
                cachedRawFile.Metadata?.ResolvedCommitHash,
                dependency.SourceFile);
            var metadataFilePath = HfHubProjectPaths.GetImportMetadataFilePath(
                dependency.PackageId,
                dependency.ModelId);
            var sentisAbsolutePath = HfHubProjectPaths.ToAbsolutePath(sentisAssetPath);
            var metadataAbsolutePath = HfHubProjectPaths.ToProjectAbsolutePath(metadataFilePath);

            if (TryReadMetadata(metadataAbsolutePath, out var existingMetadata) &&
                IsUpToDate(existingMetadata, dependency, cachedRawFile, sentisAssetPath, sentisAbsolutePath))
            {
                return Task.FromResult(new HfHubImportedModelArtifact(sentisAssetPath, metadataFilePath, existingMetadata));
            }

            EnsureAssetFolderExists(Path.GetDirectoryName(sentisAssetPath)?.Replace('\\', '/')
                ?? throw new InvalidOperationException("Missing runtime output directory."));

            var model = _onnxModelConverter.ConvertFromFile(cachedRawFile.FilePath);
            ModelWriter.Save(sentisAbsolutePath, model);
            var metadata = CreateMetadata(dependency, cachedRawFile, sentisAssetPath);
            WriteMetadata(metadataAbsolutePath, metadata);

            AssetDatabase.ImportAsset(
                sentisAssetPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            AssetDatabase.SaveAssets();

            return Task.FromResult(new HfHubImportedModelArtifact(sentisAssetPath, metadataFilePath, metadata));
        }

        internal static bool TryReadMetadata(string metadataAbsolutePath, out HfHubImportedModelMetadata metadata)
        {
            metadata = null;
            if (!File.Exists(metadataAbsolutePath))
            {
                return false;
            }

            try
            {
                metadata = JsonConvert.DeserializeObject<HfHubImportedModelMetadata>(File.ReadAllText(metadataAbsolutePath));
                return metadata != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsUpToDate(
            HfHubImportedModelMetadata metadata,
            HfHubDiscoveredModelDependency dependency,
            HfHubCachedRawFile cachedRawFile,
            string sentisAssetPath,
            string sentisAbsolutePath)
        {
            if (metadata == null || !File.Exists(sentisAbsolutePath))
            {
                return false;
            }

            return string.Equals(metadata.PackageId, dependency.PackageId, StringComparison.Ordinal) &&
                   string.Equals(metadata.ModelId, dependency.ModelId, StringComparison.Ordinal) &&
                   string.Equals(metadata.ManifestHash, dependency.ManifestHash, StringComparison.Ordinal) &&
                   string.Equals(metadata.Repository, dependency.Repository, StringComparison.Ordinal) &&
                   string.Equals(metadata.RequestedRevision, dependency.Revision, StringComparison.Ordinal) &&
                   string.Equals(metadata.ResolvedCommitHash, cachedRawFile.Metadata?.ResolvedCommitHash, StringComparison.Ordinal) &&
                   string.Equals(metadata.SourceFile, dependency.SourceFile, StringComparison.Ordinal) &&
                   string.Equals(metadata.RawFilePath, cachedRawFile.FilePath, StringComparison.Ordinal) &&
                   string.Equals(metadata.SentisVersion, GetSentisVersion(), StringComparison.Ordinal) &&
                   string.Equals(metadata.SentisAssetPath, sentisAssetPath, StringComparison.Ordinal);
        }

        private static HfHubImportedModelMetadata CreateMetadata(
            HfHubDiscoveredModelDependency dependency,
            HfHubCachedRawFile cachedRawFile,
            string sentisAssetPath)
        {
            var runtimeRegistryAssetPath = HfHubProjectPaths.GetRuntimeRegistryAssetPath(dependency.PackageId);
            var runtimeSentisAssetPath = HfHubProjectPaths.GetRuntimeSentisAssetPath(
                dependency.Repository,
                cachedRawFile.Metadata?.ResolvedCommitHash,
                dependency.SourceFile);

            return new HfHubImportedModelMetadata
            {
                PackageId = dependency.PackageId,
                PackageVersion = dependency.PackageVersion,
                ModelId = dependency.ModelId,
                ModelDisplayName = dependency.ModelDisplayName,
                ManifestHash = dependency.ManifestHash,
                Repository = dependency.Repository,
                RequestedRevision = dependency.Revision,
                ResolvedCommitHash = cachedRawFile.Metadata?.ResolvedCommitHash,
                SourceFile = dependency.SourceFile,
                RawFilePath = cachedRawFile.FilePath,
                RawMetadataPath = cachedRawFile.MetadataPath,
                SentisVersion = GetSentisVersion(),
                UnityVersion = Application.unityVersion,
                SentisAssetPath = runtimeSentisAssetPath,
                RuntimeRegistryAssetPath = runtimeRegistryAssetPath,
                RuntimeRegistryRelativePath = HfHubProjectPaths.ToStreamingAssetsRelativePath(runtimeRegistryAssetPath),
                RuntimeSentisAssetPath = runtimeSentisAssetPath,
                RuntimeSentisRelativePath = HfHubProjectPaths.ToStreamingAssetsRelativePath(runtimeSentisAssetPath),
                ImportedAtUtc = DateTime.UtcNow.ToString("O")
            };
        }

        private static void WriteMetadata(string metadataAbsolutePath, HfHubImportedModelMetadata metadata)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(metadataAbsolutePath) ?? throw new InvalidOperationException("Missing metadata directory."));

            var temporaryPath = metadataAbsolutePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(metadata, Formatting.Indented));

            if (File.Exists(metadataAbsolutePath))
            {
                File.Delete(metadataAbsolutePath);
            }

            File.Move(temporaryPath, metadataAbsolutePath);
        }

        private static void EnsureAssetFolderExists(string assetFolderPath)
        {
            if (AssetDatabase.IsValidFolder(assetFolderPath))
            {
                return;
            }

            var parts = assetFolderPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !string.Equals(parts[0], "Assets", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Expected Unity asset folder under Assets, got '{assetFolderPath}'.");
            }

            var currentFolder = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var nextFolder = $"{currentFolder}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(nextFolder))
                {
                    AssetDatabase.CreateFolder(currentFolder, parts[i]);
                }

                currentFolder = nextFolder;
            }
        }

        private static string GetSentisVersion()
        {
            return UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ModelAsset).Assembly)?.version ?? "unknown";
        }

    }
}
