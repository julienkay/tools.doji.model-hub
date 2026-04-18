using System;
using System.IO;
using System.Linq;
using Unity.InferenceEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Evaluates local cache and generated artifact state for discovered repository dependencies.
    /// </summary>
    internal sealed class HfHubStatusService
    {
        private readonly HfHubRawCacheService _rawCacheService;

        public HfHubStatusService(HfHubRawCacheService rawCacheService = null)
        {
            _rawCacheService = rawCacheService ?? new HfHubRawCacheService();
        }

        public HfHubStatusReport BuildStatusReport(HfHubDiscoveryReport discoveryReport)
        {
            var dependencyStatuses = discoveryReport.Dependencies
                .Select(BuildDependencyStatus)
                .ToArray();

            return new HfHubStatusReport(
                discoveryReport.Issues.ToArray(),
                dependencyStatuses);
        }

        private HfHubDependencyStatus BuildDependencyStatus(HfHubDiscoveredModelDependency dependency)
        {
            var revisionRefPath = HfHubCachePaths.GetRevisionRefPath(dependency.Repository, dependency.Revision);
            string runtimePreviewAssetPath = null;
            var importMetadataAssetPath = HfHubProjectPaths.GetImportMetadataFilePath(
                dependency.PackageId,
                dependency.ModelId);
            var importMetadataAbsolutePath = HfHubProjectPaths.ToProjectAbsolutePath(importMetadataAssetPath);

            if (!File.Exists(revisionRefPath))
            {
                return Missing(dependency, "Raw cache revision ref is missing.", revisionRefPath, null, null, runtimePreviewAssetPath, importMetadataAssetPath);
            }

            string resolvedCommitHash;
            try
            {
                resolvedCommitHash = File.ReadAllText(revisionRefPath).Trim();
            }
            catch (Exception exception)
            {
                return Failed(dependency, $"Could not read raw cache revision ref: {exception.Message}", revisionRefPath, null, null, runtimePreviewAssetPath, importMetadataAssetPath);
            }

            var snapshotRoot = HfHubCachePaths.GetSnapshotRoot(dependency.Repository, resolvedCommitHash);
            var rawMetadataPath = HfHubCachePaths.GetSnapshotMetadataPath(dependency.Repository, resolvedCommitHash);
            if (!Directory.Exists(snapshotRoot))
            {
                return Missing(dependency, "Raw snapshot root is missing.", revisionRefPath, snapshotRoot, rawMetadataPath, runtimePreviewAssetPath, importMetadataAssetPath);
            }

            if (!_rawCacheService.TryReadMetadata(rawMetadataPath, out var rawMetadata))
            {
                return Failed(dependency, "Raw cache metadata is invalid.", revisionRefPath, snapshotRoot, rawMetadataPath, runtimePreviewAssetPath, importMetadataAssetPath);
            }

            if (!string.Equals(rawMetadata.Repository, dependency.Repository, StringComparison.Ordinal) ||
                !string.Equals(rawMetadata.RequestedRevision, dependency.Revision, StringComparison.Ordinal) ||
                !string.Equals(rawMetadata.ResolvedCommitHash, resolvedCommitHash, StringComparison.Ordinal))
            {
                return Stale(dependency, "Raw cache metadata differs from manifest or revision ref.", revisionRefPath, snapshotRoot, rawMetadataPath, rawMetadata, runtimePreviewAssetPath, importMetadataAssetPath);
            }

            if (!File.Exists(importMetadataAbsolutePath))
            {
                return Missing(dependency, "Import metadata file is missing.", revisionRefPath, snapshotRoot, rawMetadataPath, rawMetadata, runtimePreviewAssetPath, importMetadataAssetPath);
            }

            if (!HfHubProjectImportService.TryReadMetadata(importMetadataAbsolutePath, out var importedMetadata))
            {
                return Failed(dependency, "Import metadata is invalid.", revisionRefPath, snapshotRoot, rawMetadataPath, rawMetadata, runtimePreviewAssetPath, importMetadataAssetPath);
            }

            if (!string.Equals(importedMetadata.PackageId, dependency.PackageId, StringComparison.Ordinal) ||
                !string.Equals(importedMetadata.ModelId, dependency.ModelId, StringComparison.Ordinal) ||
                !string.Equals(importedMetadata.ManifestHash, dependency.ManifestHash, StringComparison.Ordinal) ||
                !string.Equals(importedMetadata.Repository, dependency.Repository, StringComparison.Ordinal) ||
                !string.Equals(importedMetadata.RequestedRevision, dependency.Revision, StringComparison.Ordinal) ||
                !string.Equals(importedMetadata.ResolvedCommitHash, rawMetadata.ResolvedCommitHash, StringComparison.Ordinal) ||
                !string.Equals(importedMetadata.RawSnapshotRootPath, snapshotRoot, StringComparison.Ordinal) ||
                !string.Equals(importedMetadata.RawMetadataPath, rawMetadataPath, StringComparison.Ordinal) ||
                !string.Equals(importedMetadata.SentisVersion, GetCurrentSentisVersion(), StringComparison.Ordinal))
            {
                runtimePreviewAssetPath = importedMetadata.RuntimeFiles?.FirstOrDefault(file => file.Kind == "sentis")?.AssetPath;
                return Stale(dependency, "Import metadata differs from current repo snapshot or Sentis version.", revisionRefPath, snapshotRoot, rawMetadataPath, rawMetadata, runtimePreviewAssetPath, importMetadataAssetPath, importedMetadata);
            }

            var runtimeFiles = importedMetadata.RuntimeFiles ?? Array.Empty<HfHubImportedRuntimeFileMetadata>();
            if (runtimeFiles.Length == 0)
            {
                return Missing(dependency, "Generated runtime files are missing.", revisionRefPath, snapshotRoot, rawMetadataPath, rawMetadata, runtimePreviewAssetPath, importMetadataAssetPath);
            }

            foreach (var runtimeFile in runtimeFiles)
            {
                if (!File.Exists(HfHubProjectPaths.ToAbsolutePath(runtimeFile.AssetPath)))
                {
                    return Missing(dependency, "One or more generated runtime files are missing.", revisionRefPath, snapshotRoot, rawMetadataPath, rawMetadata, runtimePreviewAssetPath, importMetadataAssetPath);
                }
            }

            runtimePreviewAssetPath = runtimeFiles.FirstOrDefault(file => file.Kind == "sentis")?.AssetPath;
            return new HfHubDependencyStatus(
                dependency,
                HfHubDependencyState.Ready,
                "Raw repo snapshot and generated runtime files are current.",
                revisionRefPath,
                snapshotRoot,
                rawMetadataPath,
                rawMetadata,
                runtimePreviewAssetPath,
                importMetadataAssetPath,
                importedMetadata);
        }

        internal static string GetCurrentSentisVersion()
        {
            return UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ModelAsset).Assembly)?.version ?? "unknown";
        }

        private static HfHubDependencyStatus Missing(
            HfHubDiscoveredModelDependency dependency,
            string message,
            string revisionRefPath,
            string rawFilePath,
            string rawMetadataPath,
            string sentisAssetPath,
            string importMetadataPath)
        {
            return Missing(dependency, message, revisionRefPath, rawFilePath, rawMetadataPath, null, sentisAssetPath, importMetadataPath);
        }

        private static HfHubDependencyStatus Missing(
            HfHubDiscoveredModelDependency dependency,
            string message,
            string revisionRefPath,
            string rawFilePath,
            string rawMetadataPath,
            HfHubRawCacheMetadata rawMetadata,
            string sentisAssetPath,
            string importMetadataPath)
        {
            return new HfHubDependencyStatus(
                dependency,
                HfHubDependencyState.Missing,
                message,
                revisionRefPath,
                rawFilePath,
                rawMetadataPath,
                rawMetadata,
                sentisAssetPath,
                importMetadataPath,
                null);
        }

        private static HfHubDependencyStatus Stale(
            HfHubDiscoveredModelDependency dependency,
            string message,
            string revisionRefPath,
            string rawFilePath,
            string rawMetadataPath,
            HfHubRawCacheMetadata rawMetadata,
            string sentisAssetPath,
            string importMetadataPath,
            HfHubImportedModelMetadata importedMetadata = null)
        {
            return new HfHubDependencyStatus(
                dependency,
                HfHubDependencyState.Stale,
                message,
                revisionRefPath,
                rawFilePath,
                rawMetadataPath,
                rawMetadata,
                sentisAssetPath,
                importMetadataPath,
                importedMetadata);
        }

        private static HfHubDependencyStatus Failed(
            HfHubDiscoveredModelDependency dependency,
            string message,
            string revisionRefPath,
            string rawFilePath,
            string rawMetadataPath,
            string sentisAssetPath,
            string importMetadataPath)
        {
            return Failed(
                dependency,
                message,
                revisionRefPath,
                rawFilePath,
                rawMetadataPath,
                null,
                sentisAssetPath,
                importMetadataPath);
        }

        private static HfHubDependencyStatus Failed(
            HfHubDiscoveredModelDependency dependency,
            string message,
            string revisionRefPath,
            string rawFilePath,
            string rawMetadataPath,
            HfHubRawCacheMetadata rawMetadata,
            string sentisAssetPath,
            string importMetadataPath)
        {
            return new HfHubDependencyStatus(
                dependency,
                HfHubDependencyState.Failed,
                message,
                revisionRefPath,
                rawFilePath,
                rawMetadataPath,
                rawMetadata,
                sentisAssetPath,
                importMetadataPath,
                null);
        }
    }
}
