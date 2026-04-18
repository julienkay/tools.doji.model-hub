using System;
using System.IO;
using System.Linq;
using Unity.InferenceEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Evaluates local cache and generated artifact state for discovered model dependencies.
    /// </summary>
    internal sealed class HfHubStatusService
    {
        private readonly HfHubRawCacheService _rawCacheService;

        /// <summary>
        /// Creates status service with optional raw-cache service injection for tests.
        /// </summary>
        public HfHubStatusService(HfHubRawCacheService rawCacheService = null)
        {
            _rawCacheService = rawCacheService ?? new HfHubRawCacheService();
        }

        /// <summary>
        /// Builds local dependency status report from an existing manifest discovery report.
        /// </summary>
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
            string sentisAssetPath = null;
            var importMetadataAssetPath = HfHubProjectPaths.GetImportMetadataFilePath(
                dependency.PackageId,
                dependency.ModelId);
            var importMetadataAbsolutePath = HfHubProjectPaths.ToProjectAbsolutePath(importMetadataAssetPath);

            if (!File.Exists(revisionRefPath))
            {
                return Missing(dependency, "Raw cache revision ref is missing.", revisionRefPath, sentisAssetPath, importMetadataAssetPath);
            }

            string resolvedCommitHash;
            try
            {
                resolvedCommitHash = File.ReadAllText(revisionRefPath).Trim();
            }
            catch (Exception exception)
            {
                return Failed(
                    dependency,
                    $"Could not read raw cache revision ref: {exception.Message}",
                    revisionRefPath,
                    sentisAssetPath,
                    importMetadataAssetPath);
            }

            if (string.IsNullOrWhiteSpace(resolvedCommitHash))
            {
                return Failed(
                    dependency,
                    "Raw cache revision ref is empty.",
                    revisionRefPath,
                    sentisAssetPath,
                    importMetadataAssetPath);
            }

            var rawFilePath = HfHubCachePaths.GetSnapshotFilePath(
                dependency.Repository,
                resolvedCommitHash,
                dependency.SourceFile);
            var rawMetadataPath = HfHubCachePaths.GetSnapshotMetadataPath(
                dependency.Repository,
                resolvedCommitHash,
                dependency.SourceFile);

            if (!File.Exists(rawFilePath))
            {
                return Missing(
                    dependency,
                    "Raw source file is missing from cache.",
                    revisionRefPath,
                    rawFilePath,
                    rawMetadataPath,
                    sentisAssetPath,
                    importMetadataAssetPath);
            }

            if (!File.Exists(rawMetadataPath))
            {
                return Missing(
                    dependency,
                    "Raw cache metadata file is missing.",
                    revisionRefPath,
                    rawFilePath,
                    rawMetadataPath,
                    sentisAssetPath,
                    importMetadataAssetPath);
            }

            if (!_rawCacheService.TryReadMetadata(rawMetadataPath, out var rawMetadata))
            {
                return Failed(
                    dependency,
                    "Raw cache metadata is invalid.",
                    revisionRefPath,
                    rawFilePath,
                    rawMetadataPath,
                    sentisAssetPath,
                    importMetadataAssetPath);
            }

            var rawMismatchMessage = GetRawMismatchMessage(dependency, rawFilePath, resolvedCommitHash, rawMetadata);
            if (rawMismatchMessage != null)
            {
                return Stale(
                    dependency,
                    rawMismatchMessage,
                    revisionRefPath,
                    rawFilePath,
                    rawMetadataPath,
                    rawMetadata,
                    sentisAssetPath,
                    importMetadataAssetPath);
            }

            sentisAssetPath = HfHubProjectPaths.GetRuntimeSentisAssetPath(
                dependency.Repository,
                resolvedCommitHash,
                dependency.SourceFile);
            var sentisAbsolutePath = HfHubProjectPaths.ToAbsolutePath(sentisAssetPath);

            if (!File.Exists(sentisAbsolutePath))
            {
                return Missing(
                    dependency,
                    "Runtime .sentis asset is missing.",
                    revisionRefPath,
                    rawFilePath,
                    rawMetadataPath,
                    rawMetadata,
                    sentisAssetPath,
                    importMetadataAssetPath);
            }

            if (!File.Exists(importMetadataAbsolutePath))
            {
                return Missing(
                    dependency,
                    "Import metadata file is missing.",
                    revisionRefPath,
                    rawFilePath,
                    rawMetadataPath,
                    rawMetadata,
                    sentisAssetPath,
                    importMetadataAssetPath);
            }

            if (!HfHubProjectImportService.TryReadMetadata(importMetadataAbsolutePath, out var importedMetadata))
            {
                return Failed(
                    dependency,
                    "Import metadata is invalid.",
                    revisionRefPath,
                    rawFilePath,
                    rawMetadataPath,
                    rawMetadata,
                    sentisAssetPath,
                    importMetadataAssetPath);
            }

            var importMismatchMessage = GetImportMismatchMessage(
                dependency,
                rawFilePath,
                rawMetadataPath,
                sentisAssetPath,
                rawMetadata,
                importedMetadata);
            if (importMismatchMessage != null)
            {
                return Stale(
                    dependency,
                    importMismatchMessage,
                    revisionRefPath,
                    rawFilePath,
                    rawMetadataPath,
                    rawMetadata,
                    sentisAssetPath,
                    importMetadataAssetPath,
                    importedMetadata);
            }

            return new HfHubDependencyStatus(
                dependency,
                HfHubDependencyState.Ready,
                "Raw cache and runtime .sentis are current.",
                revisionRefPath,
                rawFilePath,
                rawMetadataPath,
                rawMetadata,
                sentisAssetPath,
                importMetadataAssetPath,
                importedMetadata);
        }

        private static string GetRawMismatchMessage(
            HfHubDiscoveredModelDependency dependency,
            string rawFilePath,
            string resolvedCommitHash,
            HfHubRawCacheMetadata rawMetadata)
        {
            if (!string.Equals(rawMetadata.Repository, dependency.Repository, StringComparison.Ordinal))
            {
                return "Raw cache repository differs from manifest.";
            }

            if (!string.Equals(rawMetadata.RequestedRevision, dependency.Revision, StringComparison.Ordinal))
            {
                return "Raw cache revision differs from manifest.";
            }

            if (!string.Equals(rawMetadata.ResolvedCommitHash, resolvedCommitHash, StringComparison.Ordinal))
            {
                return "Raw cache commit differs from revision ref.";
            }

            if (!string.Equals(rawMetadata.SourceFile, dependency.SourceFile, StringComparison.Ordinal))
            {
                return "Raw cache source path differs from manifest.";
            }

            if (!string.Equals(rawMetadata.CachedFilePath, rawFilePath, StringComparison.Ordinal))
            {
                return "Raw cache file path metadata differs from current cache layout.";
            }

            return null;
        }

        private static string GetImportMismatchMessage(
            HfHubDiscoveredModelDependency dependency,
            string rawFilePath,
            string rawMetadataPath,
            string sentisAssetPath,
            HfHubRawCacheMetadata rawMetadata,
            HfHubImportedModelMetadata importedMetadata)
        {
            if (!string.Equals(importedMetadata.PackageId, dependency.PackageId, StringComparison.Ordinal))
            {
                return "Import metadata package id differs from current dependency.";
            }

            if (!string.Equals(importedMetadata.ModelId, dependency.ModelId, StringComparison.Ordinal))
            {
                return "Import metadata model id differs from current dependency.";
            }

            if (!string.Equals(importedMetadata.ManifestHash, dependency.ManifestHash, StringComparison.Ordinal))
            {
                return "Import metadata manifest hash differs from current manifest.";
            }

            if (!string.Equals(importedMetadata.Repository, dependency.Repository, StringComparison.Ordinal))
            {
                return "Import metadata repository differs from manifest.";
            }

            if (!string.Equals(importedMetadata.RequestedRevision, dependency.Revision, StringComparison.Ordinal))
            {
                return "Import metadata revision differs from manifest.";
            }

            if (!string.Equals(importedMetadata.ResolvedCommitHash, rawMetadata.ResolvedCommitHash, StringComparison.Ordinal))
            {
                return "Import metadata commit differs from cached raw file.";
            }

            if (!string.Equals(importedMetadata.SourceFile, dependency.SourceFile, StringComparison.Ordinal))
            {
                return "Import metadata source path differs from manifest.";
            }

            if (!string.Equals(importedMetadata.RawFilePath, rawFilePath, StringComparison.Ordinal))
            {
                return "Import metadata raw file path differs from current cache path.";
            }

            if (!string.Equals(importedMetadata.RawMetadataPath, rawMetadataPath, StringComparison.Ordinal))
            {
                return "Import metadata raw metadata path differs from current cache path.";
            }

            if (!string.Equals(importedMetadata.SentisVersion, GetCurrentSentisVersion(), StringComparison.Ordinal))
            {
                return "Import metadata Sentis version differs from installed Sentis version.";
            }

            if (!string.Equals(importedMetadata.SentisAssetPath, sentisAssetPath, StringComparison.Ordinal))
            {
                return "Import metadata .sentis asset path differs from current asset path.";
            }

            return null;
        }

        internal static string GetCurrentSentisVersion()
        {
            return UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ModelAsset).Assembly)?.version ?? "unknown";
        }

        private static HfHubDependencyStatus Missing(
            HfHubDiscoveredModelDependency dependency,
            string message,
            string revisionRefPath,
            string sentisAssetPath,
            string importMetadataPath)
        {
            return Missing(
                dependency,
                message,
                revisionRefPath,
                null,
                null,
                null,
                sentisAssetPath,
                importMetadataPath);
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
            return Missing(
                dependency,
                message,
                revisionRefPath,
                rawFilePath,
                rawMetadataPath,
                null,
                sentisAssetPath,
                importMetadataPath);
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
            string sentisAssetPath,
            string importMetadataPath)
        {
            return Failed(
                dependency,
                message,
                revisionRefPath,
                null,
                null,
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
