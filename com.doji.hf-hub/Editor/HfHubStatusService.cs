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
            var sentisAssetPath = HfHubProjectPaths.GetGeneratedSentisAssetPath(
                dependency.PackageId,
                dependency.ModelId,
                dependency.SourceFile);
            var sentisAbsolutePath = HfHubProjectPaths.ToAbsolutePath(sentisAssetPath);
            var importMetadataAssetPath = HfHubProjectPaths.GetGeneratedMetadataAssetPath(
                dependency.PackageId,
                dependency.ModelId);
            var importMetadataAbsolutePath = HfHubProjectPaths.ToAbsolutePath(importMetadataAssetPath);

            if (!File.Exists(revisionRefPath))
            {
                return Missing(dependency, "Raw cache revision ref not found.", revisionRefPath, sentisAssetPath, importMetadataAssetPath);
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
                    $"Failed to read raw cache revision ref: {exception.Message}",
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
                    "Cached raw source file not found.",
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
                    "Raw cache metadata not found.",
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

            if (!File.Exists(sentisAbsolutePath))
            {
                return Missing(
                    dependency,
                    "Generated .sentis artifact not found.",
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
                    "Generated import metadata not found.",
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
                    "Generated import metadata is invalid.",
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
                "Ready.",
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
                return "Raw cache repository does not match manifest.";
            }

            if (!string.Equals(rawMetadata.RequestedRevision, dependency.Revision, StringComparison.Ordinal))
            {
                return "Raw cache revision does not match manifest.";
            }

            if (!string.Equals(rawMetadata.ResolvedCommitHash, resolvedCommitHash, StringComparison.Ordinal))
            {
                return "Raw cache commit does not match current revision ref.";
            }

            if (!string.Equals(rawMetadata.SourceFile, dependency.SourceFile, StringComparison.Ordinal))
            {
                return "Raw cache source file does not match manifest.";
            }

            if (!string.Equals(rawMetadata.CachedFilePath, rawFilePath, StringComparison.Ordinal))
            {
                return "Raw cache file path metadata does not match current cache layout.";
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
                return "Generated import metadata package id does not match current dependency.";
            }

            if (!string.Equals(importedMetadata.ModelId, dependency.ModelId, StringComparison.Ordinal))
            {
                return "Generated import metadata model id does not match current dependency.";
            }

            if (!string.Equals(importedMetadata.ManifestHash, dependency.ManifestHash, StringComparison.Ordinal))
            {
                return "Generated import metadata manifest hash is stale.";
            }

            if (!string.Equals(importedMetadata.Repository, dependency.Repository, StringComparison.Ordinal))
            {
                return "Generated import metadata repository does not match manifest.";
            }

            if (!string.Equals(importedMetadata.RequestedRevision, dependency.Revision, StringComparison.Ordinal))
            {
                return "Generated import metadata revision does not match manifest.";
            }

            if (!string.Equals(importedMetadata.ResolvedCommitHash, rawMetadata.ResolvedCommitHash, StringComparison.Ordinal))
            {
                return "Generated import metadata commit does not match cached raw file.";
            }

            if (!string.Equals(importedMetadata.SourceFile, dependency.SourceFile, StringComparison.Ordinal))
            {
                return "Generated import metadata source file does not match manifest.";
            }

            if (!string.Equals(importedMetadata.RawFilePath, rawFilePath, StringComparison.Ordinal))
            {
                return "Generated import metadata raw file path is stale.";
            }

            if (!string.Equals(importedMetadata.RawMetadataPath, rawMetadataPath, StringComparison.Ordinal))
            {
                return "Generated import metadata raw metadata path is stale.";
            }

            if (!string.Equals(importedMetadata.SentisVersion, GetCurrentSentisVersion(), StringComparison.Ordinal))
            {
                return "Generated .sentis artifact was produced with a different Sentis version.";
            }

            if (!string.Equals(importedMetadata.SentisAssetPath, sentisAssetPath, StringComparison.Ordinal))
            {
                return "Generated import metadata .sentis asset path is stale.";
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
