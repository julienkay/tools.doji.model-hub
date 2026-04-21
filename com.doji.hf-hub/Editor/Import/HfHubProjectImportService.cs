using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Converts cached repository snapshots into runtime files under `StreamingAssets` and records editor-only import metadata.
    /// </summary>
    internal sealed class HfHubProjectImportService : IHfHubProjectImportService
    {
        private readonly IHfHubOnnxModelConverter _onnxModelConverter;

        public HfHubProjectImportService(IHfHubOnnxModelConverter onnxModelConverter = null)
        {
            _onnxModelConverter = onnxModelConverter ?? new HfHubEditorOnnxModelConverter();
        }

        /// <summary>
        /// Returns generated runtime snapshot artifact if still current, otherwise converts cached ONNX files into `.sentis` runtime assets.
        /// </summary>
        public Task<HfHubImportedModelArtifact> ImportAsync(
            HfHubDiscoveredModelDependency dependency,
            HfHubCachedSnapshot cachedSnapshot,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var snapshotRootAssetPath = HfHubProjectPaths.GetRuntimeSnapshotFolder(
                dependency.Repository,
                cachedSnapshot.Metadata?.ResolvedCommitHash);
            var metadataFilePath = HfHubProjectPaths.GetImportMetadataFilePath(
                dependency.PackageId,
                dependency.ModelId);
            var metadataAbsolutePath = HfHubProjectPaths.ToProjectAbsolutePath(metadataFilePath);

            if (TryReadMetadata(metadataAbsolutePath, out var existingMetadata) &&
                IsUpToDate(existingMetadata, dependency, cachedSnapshot, snapshotRootAssetPath))
            {
                return Task.FromResult(new HfHubImportedModelArtifact(snapshotRootAssetPath, metadataFilePath, existingMetadata));
            }

            var runtimeFiles = new List<HfHubImportedRuntimeFileMetadata>();
            var onnxFileCount = 0;
            var sentisFileCount = 0;
            var conversionFailures = new List<string>();
            var diagnostics = new List<HfHubImportDiagnostic>();
            foreach (var snapshotFile in cachedSnapshot.Metadata?.Files ?? Array.Empty<HfHubRawCacheFileMetadata>())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.Equals(Path.GetExtension(snapshotFile.RepoPath), ".onnx", StringComparison.OrdinalIgnoreCase))
                {
                    onnxFileCount++;
                    var runtimeSentisAssetPath = HfHubProjectPaths.GetRuntimeSentisAssetPath(
                        dependency.Repository,
                        cachedSnapshot.Metadata?.ResolvedCommitHash,
                        snapshotFile.RepoPath);
                    var runtimeSentisAbsolutePath = HfHubProjectPaths.ToAbsolutePath(runtimeSentisAssetPath);
                    EnsureAssetFolderExists(Path.GetDirectoryName(runtimeSentisAssetPath)?.Replace('\\', '/')
                        ?? throw new InvalidOperationException("Missing runtime sentis output directory."));

                    Model model;
                    try
                    {
                        model = _onnxModelConverter.ConvertFromFile(snapshotFile.CachedFilePath);
                    }
                    catch (Exception exception)
                    {
                        var failureMessage = $"Failed to convert ONNX file '{snapshotFile.RepoPath}' from repository '{dependency.Repository}'. {exception.Message}";
                        conversionFailures.Add(failureMessage);
                        diagnostics.Add(new HfHubImportDiagnostic
                        {
                            Severity = "warning",
                            Message = $"Partial ONNX conversion: skipped {snapshotFile.RepoPath} because {exception.Message}"
                        });
                        Debug.LogWarning(failureMessage);
                        continue;
                    }

                    ModelWriter.Save(runtimeSentisAbsolutePath, model);
                    sentisFileCount++;
                    runtimeFiles.Add(new HfHubImportedRuntimeFileMetadata
                    {
                        RepoPath = snapshotFile.RepoPath,
                        Kind = "sentis",
                        AssetPath = runtimeSentisAssetPath,
                        RelativePath = HfHubProjectPaths.ToStreamingAssetsRelativePath(runtimeSentisAssetPath),
                        SizeInBytes = new FileInfo(runtimeSentisAbsolutePath).Length
                    });
                }
            }

            if (onnxFileCount > 0 && sentisFileCount == 0)
            {
                throw new InvalidOperationException(
                    conversionFailures.Count > 0
                        ? conversionFailures[0]
                        : $"Failed to convert ONNX files from repository '{dependency.Repository}'.");
            }

            var metadata = CreateMetadata(dependency, cachedSnapshot, runtimeFiles, diagnostics);
            WriteMetadata(metadataAbsolutePath, metadata);

            foreach (var runtimeFile in runtimeFiles)
            {
                AssetDatabase.ImportAsset(
                    runtimeFile.AssetPath,
                    ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            }

            AssetDatabase.SaveAssets();
            return Task.FromResult(new HfHubImportedModelArtifact(snapshotRootAssetPath, metadataFilePath, metadata));
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
            HfHubCachedSnapshot cachedSnapshot,
            string snapshotRootAssetPath)
        {
            if (metadata == null)
            {
                return false;
            }

            if (!string.Equals(metadata.PackageId, dependency.PackageId, StringComparison.Ordinal) ||
                !string.Equals(metadata.ModelId, dependency.ModelId, StringComparison.Ordinal) ||
                !string.Equals(metadata.ManifestHash, dependency.ManifestHash, StringComparison.Ordinal) ||
                !string.Equals(metadata.Repository, dependency.Repository, StringComparison.Ordinal) ||
                !string.Equals(metadata.RequestedRevision, dependency.Revision, StringComparison.Ordinal) ||
                !string.Equals(metadata.ResolvedCommitHash, cachedSnapshot.Metadata?.ResolvedCommitHash, StringComparison.Ordinal) ||
                !string.Equals(metadata.RawSnapshotRootPath, cachedSnapshot.SnapshotRootPath, StringComparison.Ordinal) ||
                !string.Equals(metadata.SentisVersion, GetSentisVersion(), StringComparison.Ordinal))
            {
                return false;
            }

            var importedFiles = metadata.RuntimeFiles ?? Array.Empty<HfHubImportedRuntimeFileMetadata>();
            if (importedFiles.Length == 0)
            {
                return false;
            }

            if (!Directory.Exists(HfHubProjectPaths.ToAbsolutePath(snapshotRootAssetPath)))
            {
                return false;
            }

            return importedFiles.All(file => File.Exists(HfHubProjectPaths.ToAbsolutePath(file.AssetPath)));
        }

        private static HfHubImportedModelMetadata CreateMetadata(
            HfHubDiscoveredModelDependency dependency,
            HfHubCachedSnapshot cachedSnapshot,
            IReadOnlyList<HfHubImportedRuntimeFileMetadata> runtimeFiles,
            IReadOnlyList<HfHubImportDiagnostic> diagnostics)
        {
            var runtimeRegistryAssetPath = HfHubProjectPaths.GetRuntimeRegistryAssetPath(dependency.PackageId);
            return new HfHubImportedModelMetadata
            {
                PackageId = dependency.PackageId,
                PackageVersion = dependency.PackageVersion,
                ModelId = dependency.ModelId,
                ManifestHash = dependency.ManifestHash,
                Repository = dependency.Repository,
                RequestedRevision = dependency.Revision,
                ResolvedCommitHash = cachedSnapshot.Metadata?.ResolvedCommitHash,
                RawSnapshotRootPath = cachedSnapshot.SnapshotRootPath,
                RawMetadataPath = cachedSnapshot.MetadataPath,
                RawCacheSizeInBytes = cachedSnapshot.Metadata?.Files?
                    .Select(file => file.SizeInBytes)
                    .Where(size => size.HasValue)
                    .Sum(size => size.Value),
                SentisVersion = GetSentisVersion(),
                UnityVersion = Application.unityVersion,
                RuntimeRegistryAssetPath = runtimeRegistryAssetPath,
                RuntimeRegistryRelativePath = HfHubProjectPaths.ToStreamingAssetsRelativePath(runtimeRegistryAssetPath),
                Diagnostics = diagnostics?.ToArray() ?? Array.Empty<HfHubImportDiagnostic>(),
                RuntimeFiles = runtimeFiles.ToArray(),
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
