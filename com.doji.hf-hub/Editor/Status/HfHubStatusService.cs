using System;
using System.Collections.Generic;
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
                if (TryBuildImportedOnlyStatus(
                    dependency,
                    importMetadataAbsolutePath,
                    importMetadataAssetPath,
                    HfHubDependencyReasonCode.RawCacheRemovedAfterImport,
                    "Generated runtime files are present. Raw cache has been removed from this machine.",
                    out var importedOnlyStatus))
                {
                    return importedOnlyStatus;
                }

                return Missing(dependency, HfHubDependencyReasonCode.RawCacheRevisionRefMissing, "Raw cache revision ref is missing.", revisionRefPath, null, null, runtimePreviewAssetPath, importMetadataAssetPath);
            }

            string resolvedCommitHash;
            try
            {
                resolvedCommitHash = File.ReadAllText(revisionRefPath).Trim();
            }
            catch (Exception exception)
            {
                return Failed(dependency, HfHubDependencyReasonCode.RawCacheRevisionRefUnreadable, $"Could not read raw cache revision ref: {exception.Message}", revisionRefPath, null, null, runtimePreviewAssetPath, importMetadataAssetPath);
            }

            var snapshotRoot = HfHubCachePaths.GetSnapshotRoot(dependency.Repository, resolvedCommitHash);
            var rawMetadataPath = HfHubCachePaths.GetSnapshotMetadataPath(dependency.Repository, resolvedCommitHash);
            if (!Directory.Exists(snapshotRoot))
            {
                if (TryBuildImportedOnlyStatus(
                    dependency,
                    importMetadataAbsolutePath,
                    importMetadataAssetPath,
                    HfHubDependencyReasonCode.RawCacheRemovedAfterImport,
                    "Generated runtime files are present. Raw cache snapshot has been removed from this machine.",
                    out var importedOnlyStatus))
                {
                    return importedOnlyStatus;
                }

                return Missing(dependency, HfHubDependencyReasonCode.RawSnapshotRootMissing, "Raw snapshot root is missing.", revisionRefPath, snapshotRoot, rawMetadataPath, runtimePreviewAssetPath, importMetadataAssetPath);
            }

            if (!_rawCacheService.TryReadMetadata(rawMetadataPath, out var rawMetadata))
            {
                if (TryBuildImportedOnlyStatus(
                    dependency,
                    importMetadataAbsolutePath,
                    importMetadataAssetPath,
                    HfHubDependencyReasonCode.RawCacheMetadataMissingAfterImport,
                    "Generated runtime files are present. Raw cache metadata has been removed from this machine.",
                    out var importedOnlyStatus))
                {
                    return importedOnlyStatus;
                }

                return Failed(dependency, HfHubDependencyReasonCode.RawCacheMetadataInvalid, "Raw cache metadata is invalid.", revisionRefPath, snapshotRoot, rawMetadataPath, runtimePreviewAssetPath, importMetadataAssetPath);
            }

            if (!string.Equals(rawMetadata.Repository, dependency.Repository, StringComparison.Ordinal) ||
                !string.Equals(rawMetadata.RequestedRevision, dependency.Revision, StringComparison.Ordinal) ||
                !string.Equals(rawMetadata.ResolvedCommitHash, resolvedCommitHash, StringComparison.Ordinal))
            {
                return Stale(dependency, HfHubDependencyReasonCode.RawCacheMetadataMismatch, "Raw cache metadata differs from manifest or revision ref.", revisionRefPath, snapshotRoot, rawMetadataPath, rawMetadata, runtimePreviewAssetPath, importMetadataAssetPath);
            }

            if (!File.Exists(importMetadataAbsolutePath))
            {
                return Missing(dependency, HfHubDependencyReasonCode.ImportMetadataMissing, "Import metadata file is missing.", revisionRefPath, snapshotRoot, rawMetadataPath, rawMetadata, runtimePreviewAssetPath, importMetadataAssetPath);
            }

            if (!HfHubProjectImportService.TryReadMetadata(importMetadataAbsolutePath, out var importedMetadata))
            {
                return Failed(dependency, HfHubDependencyReasonCode.ImportMetadataInvalid, "Import metadata is invalid.", revisionRefPath, snapshotRoot, rawMetadataPath, rawMetadata, runtimePreviewAssetPath, importMetadataAssetPath);
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
                return Stale(
                    dependency,
                    HfHubDependencyReasonCode.ImportMetadataStale,
                    BuildImportMetadataMismatchMessage(dependency, snapshotRoot, rawMetadataPath, rawMetadata, importedMetadata),
                    revisionRefPath,
                    snapshotRoot,
                    rawMetadataPath,
                    rawMetadata,
                    runtimePreviewAssetPath,
                    importMetadataAssetPath,
                    importedMetadata);
            }

            var runtimeFiles = importedMetadata.RuntimeFiles ?? Array.Empty<HfHubImportedRuntimeFileMetadata>();
            if (runtimeFiles.Length == 0)
            {
                return Missing(dependency, HfHubDependencyReasonCode.RuntimeFilesMissing, "Generated runtime files are missing.", revisionRefPath, snapshotRoot, rawMetadataPath, rawMetadata, runtimePreviewAssetPath, importMetadataAssetPath);
            }

            foreach (var runtimeFile in runtimeFiles)
            {
                if (!File.Exists(HfHubProjectPaths.ToAbsolutePath(runtimeFile.AssetPath)))
                {
                    return Missing(
                        dependency,
                        HfHubDependencyReasonCode.RuntimeFileMissing,
                        $"Generated runtime file is missing: {runtimeFile.RelativePath ?? runtimeFile.AssetPath}.",
                        revisionRefPath,
                        snapshotRoot,
                        rawMetadataPath,
                        rawMetadata,
                        runtimePreviewAssetPath,
                        importMetadataAssetPath);
                }
            }

            runtimePreviewAssetPath = runtimeFiles.FirstOrDefault(file => file.Kind == "sentis")?.AssetPath;
            return new HfHubDependencyStatus(
                dependency,
                HfHubDependencyState.Ready,
                HfHubDependencyReasonCode.None,
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

        private static bool TryBuildImportedOnlyStatus(
            HfHubDiscoveredModelDependency dependency,
            string importMetadataAbsolutePath,
            string importMetadataAssetPath,
            HfHubDependencyReasonCode reasonCode,
            string maintainerMessage,
            out HfHubDependencyStatus status)
        {
            status = null;

            if (!HfHubProjectImportService.TryReadMetadata(importMetadataAbsolutePath, out var importedMetadata))
            {
                return false;
            }

            if (!string.Equals(importedMetadata.PackageId, dependency.PackageId, StringComparison.Ordinal) ||
                !string.Equals(importedMetadata.ModelId, dependency.ModelId, StringComparison.Ordinal) ||
                !string.Equals(importedMetadata.ManifestHash, dependency.ManifestHash, StringComparison.Ordinal) ||
                !string.Equals(importedMetadata.Repository, dependency.Repository, StringComparison.Ordinal) ||
                !string.Equals(importedMetadata.RequestedRevision, dependency.Revision, StringComparison.Ordinal) ||
                !string.Equals(importedMetadata.SentisVersion, GetCurrentSentisVersion(), StringComparison.Ordinal))
            {
                return false;
            }

            var runtimeFiles = importedMetadata.RuntimeFiles ?? Array.Empty<HfHubImportedRuntimeFileMetadata>();
            if (runtimeFiles.Length == 0)
            {
                return false;
            }

            foreach (var runtimeFile in runtimeFiles)
            {
                if (!File.Exists(HfHubProjectPaths.ToAbsolutePath(runtimeFile.AssetPath)))
                {
                    return false;
                }
            }

            var runtimePreviewAssetPath = runtimeFiles.FirstOrDefault(file => file.Kind == "sentis")?.AssetPath;
            status = new HfHubDependencyStatus(
                dependency,
                HfHubDependencyState.Ready,
                reasonCode,
                maintainerMessage,
                null,
                null,
                null,
                null,
                runtimePreviewAssetPath,
                importMetadataAssetPath,
                importedMetadata);
            return true;
        }

        private static string BuildImportMetadataMismatchMessage(
            HfHubDiscoveredModelDependency dependency,
            string snapshotRoot,
            string rawMetadataPath,
            HfHubRawCacheMetadata rawMetadata,
            HfHubImportedModelMetadata importedMetadata)
        {
            var mismatches = new List<string>();

            AddMismatch(mismatches, "package id", importedMetadata.PackageId, dependency.PackageId);
            AddMismatch(mismatches, "model id", importedMetadata.ModelId, dependency.ModelId);
            AddMismatch(mismatches, "manifest hash", importedMetadata.ManifestHash, dependency.ManifestHash);
            AddMismatch(mismatches, "repository", importedMetadata.Repository, dependency.Repository);
            AddMismatch(mismatches, "requested revision", importedMetadata.RequestedRevision, dependency.Revision);
            AddMismatch(mismatches, "resolved commit", importedMetadata.ResolvedCommitHash, rawMetadata.ResolvedCommitHash);
            AddMismatch(mismatches, "raw snapshot root", importedMetadata.RawSnapshotRootPath, snapshotRoot);
            AddMismatch(mismatches, "raw metadata path", importedMetadata.RawMetadataPath, rawMetadataPath);
            AddMismatch(mismatches, "Sentis version", importedMetadata.SentisVersion, GetCurrentSentisVersion());

            return mismatches.Count == 0
                ? "Import metadata differs from current repo snapshot or Sentis version."
                : $"Import metadata is stale: {string.Join("; ", mismatches)}.";
        }

        private static void AddMismatch(List<string> mismatches, string fieldName, string actualValue, string expectedValue)
        {
            if (!string.Equals(actualValue, expectedValue, StringComparison.Ordinal))
            {
                mismatches.Add($"{fieldName} expected `{expectedValue}` but found `{actualValue}`");
            }
        }

        private static HfHubDependencyStatus Missing(
            HfHubDiscoveredModelDependency dependency,
            HfHubDependencyReasonCode reasonCode,
            string maintainerMessage,
            string revisionRefPath,
            string rawFilePath,
            string rawMetadataPath,
            string sentisAssetPath,
            string importMetadataPath)
        {
            return Missing(dependency, reasonCode, maintainerMessage, revisionRefPath, rawFilePath, rawMetadataPath, null, sentisAssetPath, importMetadataPath);
        }

        private static HfHubDependencyStatus Missing(
            HfHubDiscoveredModelDependency dependency,
            HfHubDependencyReasonCode reasonCode,
            string maintainerMessage,
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
                reasonCode,
                maintainerMessage,
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
            HfHubDependencyReasonCode reasonCode,
            string maintainerMessage,
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
                reasonCode,
                maintainerMessage,
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
            HfHubDependencyReasonCode reasonCode,
            string maintainerMessage,
            string revisionRefPath,
            string rawFilePath,
            string rawMetadataPath,
            string sentisAssetPath,
            string importMetadataPath)
        {
            return Failed(
                dependency,
                reasonCode,
                maintainerMessage,
                revisionRefPath,
                rawFilePath,
                rawMetadataPath,
                null,
                sentisAssetPath,
                importMetadataPath);
        }

        private static HfHubDependencyStatus Failed(
            HfHubDiscoveredModelDependency dependency,
            HfHubDependencyReasonCode reasonCode,
            string maintainerMessage,
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
                reasonCode,
                maintainerMessage,
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
