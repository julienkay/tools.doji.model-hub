using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.InferenceEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Evaluates local cache, owner binding, and shared generated artifact state for discovered repository dependencies.
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
            var bindingMetadataAssetPath = HfHubProjectPaths.GetImportBindingMetadataFilePath(
                dependency.PackageId,
                dependency.ModelId);
            var bindingMetadataAbsolutePath = HfHubProjectPaths.ToProjectAbsolutePath(bindingMetadataAssetPath);

            if (!File.Exists(revisionRefPath))
            {
                if (TryBuildImportedOnlyStatus(
                    dependency,
                    bindingMetadataAbsolutePath,
                    bindingMetadataAssetPath,
                    HfHubDependencyReasonCode.RawCacheRemovedAfterImport,
                    "Generated runtime files are present. Raw cache has been removed from this machine.",
                    out var importedOnlyStatus))
                {
                    return importedOnlyStatus;
                }

                return Missing(
                    dependency,
                    HfHubDependencyReasonCode.RawCacheRevisionRefMissing,
                    "Raw cache revision ref is missing.",
                    revisionRefPath,
                    null,
                    null,
                    runtimePreviewAssetPath,
                    bindingMetadataAssetPath);
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
                    HfHubDependencyReasonCode.RawCacheRevisionRefUnreadable,
                    $"Could not read raw cache revision ref: {exception.Message}",
                    revisionRefPath,
                    null,
                    null,
                    runtimePreviewAssetPath,
                    bindingMetadataAssetPath);
            }

            var snapshotRoot = HfHubCachePaths.GetSnapshotRoot(dependency.Repository, resolvedCommitHash);
            var rawMetadataPath = HfHubCachePaths.GetSnapshotMetadataPath(dependency.Repository, resolvedCommitHash);
            if (!Directory.Exists(snapshotRoot))
            {
                if (TryBuildImportedOnlyStatus(
                    dependency,
                    bindingMetadataAbsolutePath,
                    bindingMetadataAssetPath,
                    HfHubDependencyReasonCode.RawCacheRemovedAfterImport,
                    "Generated runtime files are present. Raw cache snapshot has been removed from this machine.",
                    out var importedOnlyStatus))
                {
                    return importedOnlyStatus;
                }

                return Missing(
                    dependency,
                    HfHubDependencyReasonCode.RawSnapshotRootMissing,
                    "Raw snapshot root is missing.",
                    revisionRefPath,
                    snapshotRoot,
                    rawMetadataPath,
                    runtimePreviewAssetPath,
                    bindingMetadataAssetPath);
            }

            if (!_rawCacheService.TryReadMetadata(rawMetadataPath, out var rawMetadata))
            {
                if (TryBuildImportedOnlyStatus(
                    dependency,
                    bindingMetadataAbsolutePath,
                    bindingMetadataAssetPath,
                    HfHubDependencyReasonCode.RawCacheMetadataMissingAfterImport,
                    "Generated runtime files are present. Raw cache metadata has been removed from this machine.",
                    out var importedOnlyStatus))
                {
                    return importedOnlyStatus;
                }

                return Failed(
                    dependency,
                    HfHubDependencyReasonCode.RawCacheMetadataInvalid,
                    "Raw cache metadata is invalid.",
                    revisionRefPath,
                    snapshotRoot,
                    rawMetadataPath,
                    runtimePreviewAssetPath,
                    bindingMetadataAssetPath);
            }

            if (!string.Equals(rawMetadata.Repository, dependency.Repository, StringComparison.Ordinal) ||
                !string.Equals(rawMetadata.RequestedRevision, dependency.Revision, StringComparison.Ordinal) ||
                !string.Equals(rawMetadata.ResolvedCommitHash, resolvedCommitHash, StringComparison.Ordinal))
            {
                return Stale(
                    dependency,
                    HfHubDependencyReasonCode.RawCacheMetadataMismatch,
                    "Raw cache metadata differs from manifest or revision ref.",
                    revisionRefPath,
                    snapshotRoot,
                    rawMetadataPath,
                    rawMetadata,
                    runtimePreviewAssetPath,
                    bindingMetadataAssetPath);
            }

            if (!File.Exists(bindingMetadataAbsolutePath))
            {
                return Missing(
                    dependency,
                    HfHubDependencyReasonCode.ImportBindingMissing,
                    "Import binding metadata file is missing.",
                    revisionRefPath,
                    snapshotRoot,
                    rawMetadataPath,
                    rawMetadata,
                    runtimePreviewAssetPath,
                    bindingMetadataAssetPath);
            }

            if (!HfHubProjectImportService.TryReadBindingMetadata(bindingMetadataAbsolutePath, out var bindingMetadata))
            {
                return Failed(
                    dependency,
                    HfHubDependencyReasonCode.ImportBindingInvalid,
                    "Import binding metadata is invalid.",
                    revisionRefPath,
                    snapshotRoot,
                    rawMetadataPath,
                    rawMetadata,
                    runtimePreviewAssetPath,
                    bindingMetadataAssetPath);
            }

            var expectedSharedMetadataPath = HfHubProjectPaths.GetSharedImportMetadataFilePath(
                dependency.Repository,
                resolvedCommitHash,
                GetCurrentSentisVersion());
            if (!IsBindingCurrent(bindingMetadata, dependency, rawMetadata, expectedSharedMetadataPath))
            {
                return Stale(
                    dependency,
                    HfHubDependencyReasonCode.ImportBindingStale,
                    BuildBindingMismatchMessage(dependency, rawMetadata, bindingMetadata, expectedSharedMetadataPath),
                    revisionRefPath,
                    snapshotRoot,
                    rawMetadataPath,
                    rawMetadata,
                    runtimePreviewAssetPath,
                    bindingMetadataAssetPath,
                    BuildResolvedImportedMetadata(bindingMetadataAssetPath, bindingMetadata, null));
            }

            if (string.IsNullOrWhiteSpace(bindingMetadata.SharedImportMetadataPath))
            {
                return Failed(
                    dependency,
                    HfHubDependencyReasonCode.ImportBindingInvalid,
                    "Import binding metadata is invalid.",
                    revisionRefPath,
                    snapshotRoot,
                    rawMetadataPath,
                    rawMetadata,
                    runtimePreviewAssetPath,
                    bindingMetadataAssetPath);
            }

            var sharedMetadataAbsolutePath = HfHubProjectPaths.ToProjectAbsolutePath(bindingMetadata.SharedImportMetadataPath);
            if (!File.Exists(sharedMetadataAbsolutePath))
            {
                return Missing(
                    dependency,
                    HfHubDependencyReasonCode.SharedImportMetadataMissing,
                    "Shared import metadata file is missing.",
                    revisionRefPath,
                    snapshotRoot,
                    rawMetadataPath,
                    rawMetadata,
                    runtimePreviewAssetPath,
                    bindingMetadataAssetPath,
                    BuildResolvedImportedMetadata(bindingMetadataAssetPath, bindingMetadata, null));
            }

            if (!HfHubProjectImportService.TryReadSharedMetadata(sharedMetadataAbsolutePath, out var sharedMetadata))
            {
                return Failed(
                    dependency,
                    HfHubDependencyReasonCode.SharedImportMetadataInvalid,
                    "Shared import metadata is invalid.",
                    revisionRefPath,
                    snapshotRoot,
                    rawMetadataPath,
                    rawMetadata,
                    runtimePreviewAssetPath,
                    bindingMetadataAssetPath,
                    BuildResolvedImportedMetadata(bindingMetadataAssetPath, bindingMetadata, null));
            }

            if (!IsSharedCurrent(sharedMetadata, dependency, rawMetadata, snapshotRoot))
            {
                return Stale(
                    dependency,
                    HfHubDependencyReasonCode.SharedImportMetadataStale,
                    BuildSharedMismatchMessage(dependency, snapshotRoot, rawMetadataPath, rawMetadata, sharedMetadata),
                    revisionRefPath,
                    snapshotRoot,
                    rawMetadataPath,
                    rawMetadata,
                    runtimePreviewAssetPath,
                    bindingMetadataAssetPath,
                    BuildResolvedImportedMetadata(bindingMetadataAssetPath, bindingMetadata, sharedMetadata));
            }

            var importedMetadata = BuildResolvedImportedMetadata(bindingMetadataAssetPath, bindingMetadata, sharedMetadata);
            var runtimeFiles = importedMetadata.RuntimeFiles ?? Array.Empty<HfHubImportedRuntimeFileMetadata>();
            if (runtimeFiles.Length == 0)
            {
                return Missing(
                    dependency,
                    HfHubDependencyReasonCode.RuntimeFilesMissing,
                    "Generated runtime files are missing.",
                    revisionRefPath,
                    snapshotRoot,
                    rawMetadataPath,
                    rawMetadata,
                    runtimePreviewAssetPath,
                    bindingMetadataAssetPath,
                    importedMetadata);
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
                        bindingMetadataAssetPath,
                        importedMetadata);
                }
            }

            runtimePreviewAssetPath = runtimeFiles.FirstOrDefault(file => file.Kind == "sentis")?.AssetPath;
            return new HfHubDependencyStatus(
                dependency,
                HfHubDependencyState.Ready,
                HfHubDependencyReasonCode.None,
                "Raw repo snapshot, import binding, and generated runtime files are current.",
                revisionRefPath,
                snapshotRoot,
                rawMetadataPath,
                rawMetadata,
                runtimePreviewAssetPath,
                bindingMetadataAssetPath,
                importedMetadata);
        }

        internal static string GetCurrentSentisVersion()
        {
            return UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ModelAsset).Assembly)?.version ?? "unknown";
        }

        private static bool TryBuildImportedOnlyStatus(
            HfHubDiscoveredModelDependency dependency,
            string bindingMetadataAbsolutePath,
            string bindingMetadataAssetPath,
            HfHubDependencyReasonCode reasonCode,
            string maintainerMessage,
            out HfHubDependencyStatus status)
        {
            status = null;

            if (!HfHubProjectImportService.TryReadBindingMetadata(bindingMetadataAbsolutePath, out var bindingMetadata))
            {
                return false;
            }

            if (!string.Equals(bindingMetadata.PackageId, dependency.PackageId, StringComparison.Ordinal) ||
                !string.Equals(bindingMetadata.ModelId, dependency.ModelId, StringComparison.Ordinal) ||
                !string.Equals(bindingMetadata.ManifestHash, dependency.ManifestHash, StringComparison.Ordinal) ||
                !string.Equals(bindingMetadata.Repository, dependency.Repository, StringComparison.Ordinal) ||
                !string.Equals(bindingMetadata.RequestedRevision, dependency.Revision, StringComparison.Ordinal) ||
                !string.Equals(bindingMetadata.SentisVersion, GetCurrentSentisVersion(), StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(bindingMetadata.SharedImportMetadataPath))
            {
                return false;
            }

            var sharedMetadataAbsolutePath = HfHubProjectPaths.ToProjectAbsolutePath(bindingMetadata.SharedImportMetadataPath);
            if (!HfHubProjectImportService.TryReadSharedMetadata(sharedMetadataAbsolutePath, out var sharedMetadata))
            {
                return false;
            }

            if (!string.Equals(sharedMetadata.Repository, dependency.Repository, StringComparison.Ordinal) ||
                !string.Equals(sharedMetadata.ResolvedCommitHash, bindingMetadata.ResolvedCommitHash, StringComparison.Ordinal) ||
                !string.Equals(sharedMetadata.SentisVersion, GetCurrentSentisVersion(), StringComparison.Ordinal))
            {
                return false;
            }

            var importedMetadata = BuildResolvedImportedMetadata(bindingMetadataAssetPath, bindingMetadata, sharedMetadata);
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
                bindingMetadataAssetPath,
                importedMetadata);
            return true;
        }

        private static bool IsBindingCurrent(
            HfHubImportBindingMetadata bindingMetadata,
            HfHubDiscoveredModelDependency dependency,
            HfHubRawCacheMetadata rawMetadata,
            string expectedSharedMetadataPath)
        {
            if (bindingMetadata == null)
            {
                return false;
            }

            return string.Equals(bindingMetadata.PackageId, dependency.PackageId, StringComparison.Ordinal) &&
                string.Equals(bindingMetadata.ModelId, dependency.ModelId, StringComparison.Ordinal) &&
                string.Equals(bindingMetadata.ManifestHash, dependency.ManifestHash, StringComparison.Ordinal) &&
                string.Equals(bindingMetadata.Repository, dependency.Repository, StringComparison.Ordinal) &&
                string.Equals(bindingMetadata.RequestedRevision, dependency.Revision, StringComparison.Ordinal) &&
                string.Equals(bindingMetadata.ResolvedCommitHash, rawMetadata.ResolvedCommitHash, StringComparison.Ordinal) &&
                string.Equals(bindingMetadata.SentisVersion, GetCurrentSentisVersion(), StringComparison.Ordinal) &&
                string.Equals(bindingMetadata.SharedImportMetadataPath, expectedSharedMetadataPath, StringComparison.Ordinal);
        }

        private static bool IsSharedCurrent(
            HfHubSharedImportMetadata sharedMetadata,
            HfHubDiscoveredModelDependency dependency,
            HfHubRawCacheMetadata rawMetadata,
            string snapshotRoot)
        {
            if (sharedMetadata == null)
            {
                return false;
            }

            return string.Equals(sharedMetadata.Repository, dependency.Repository, StringComparison.Ordinal) &&
                string.Equals(sharedMetadata.ResolvedCommitHash, rawMetadata.ResolvedCommitHash, StringComparison.Ordinal) &&
                string.Equals(sharedMetadata.RawSnapshotRootPath, snapshotRoot, StringComparison.Ordinal) &&
                string.Equals(sharedMetadata.RawMetadataPath, HfHubCachePaths.GetSnapshotMetadataPath(dependency.Repository, rawMetadata.ResolvedCommitHash), StringComparison.Ordinal) &&
                string.Equals(sharedMetadata.SentisVersion, GetCurrentSentisVersion(), StringComparison.Ordinal) &&
                string.Equals(sharedMetadata.RuntimeSnapshotAssetPath, HfHubProjectPaths.GetRuntimeSnapshotFolder(dependency.Repository, rawMetadata.ResolvedCommitHash), StringComparison.Ordinal);
        }

        private static HfHubImportedModelMetadata BuildResolvedImportedMetadata(
            string bindingMetadataPath,
            HfHubImportBindingMetadata bindingMetadata,
            HfHubSharedImportMetadata sharedMetadata)
        {
            return new HfHubImportedModelMetadata
            {
                PackageId = bindingMetadata?.PackageId,
                PackageVersion = bindingMetadata?.PackageVersion,
                ModelId = bindingMetadata?.ModelId,
                ManifestHash = bindingMetadata?.ManifestHash,
                Repository = bindingMetadata?.Repository,
                RequestedRevision = bindingMetadata?.RequestedRevision,
                ResolvedCommitHash = sharedMetadata?.ResolvedCommitHash ?? bindingMetadata?.ResolvedCommitHash,
                RawSnapshotRootPath = sharedMetadata?.RawSnapshotRootPath,
                RawMetadataPath = sharedMetadata?.RawMetadataPath,
                RawCacheSizeInBytes = sharedMetadata?.RawCacheSizeInBytes,
                SentisVersion = sharedMetadata?.SentisVersion ?? bindingMetadata?.SentisVersion,
                UnityVersion = sharedMetadata?.UnityVersion,
                RuntimeRegistryAssetPath = bindingMetadata?.RuntimeRegistryAssetPath,
                RuntimeRegistryRelativePath = bindingMetadata?.RuntimeRegistryRelativePath,
                BindingMetadataPath = bindingMetadataPath,
                SharedImportMetadataPath = bindingMetadata?.SharedImportMetadataPath,
                Diagnostics = sharedMetadata?.Diagnostics ?? Array.Empty<HfHubImportDiagnostic>(),
                RuntimeFiles = sharedMetadata?.RuntimeFiles ?? Array.Empty<HfHubImportedRuntimeFileMetadata>(),
                ImportedAtUtc = sharedMetadata?.ImportedAtUtc
            };
        }

        private static string BuildBindingMismatchMessage(
            HfHubDiscoveredModelDependency dependency,
            HfHubRawCacheMetadata rawMetadata,
            HfHubImportBindingMetadata bindingMetadata,
            string expectedSharedMetadataPath)
        {
            var mismatches = new List<string>();

            AddMismatch(mismatches, "package id", bindingMetadata.PackageId, dependency.PackageId);
            AddMismatch(mismatches, "model id", bindingMetadata.ModelId, dependency.ModelId);
            AddMismatch(mismatches, "manifest hash", bindingMetadata.ManifestHash, dependency.ManifestHash);
            AddMismatch(mismatches, "repository", bindingMetadata.Repository, dependency.Repository);
            AddMismatch(mismatches, "requested revision", bindingMetadata.RequestedRevision, dependency.Revision);
            AddMismatch(mismatches, "resolved commit", bindingMetadata.ResolvedCommitHash, rawMetadata.ResolvedCommitHash);
            AddMismatch(mismatches, "Sentis version", bindingMetadata.SentisVersion, GetCurrentSentisVersion());
            AddMismatch(mismatches, "shared metadata path", bindingMetadata.SharedImportMetadataPath, expectedSharedMetadataPath);

            return mismatches.Count == 0
                ? "Import binding metadata differs from current dependency state."
                : $"Import binding metadata is stale: {string.Join("; ", mismatches)}.";
        }

        private static string BuildSharedMismatchMessage(
            HfHubDiscoveredModelDependency dependency,
            string snapshotRoot,
            string rawMetadataPath,
            HfHubRawCacheMetadata rawMetadata,
            HfHubSharedImportMetadata sharedMetadata)
        {
            var mismatches = new List<string>();

            AddMismatch(mismatches, "repository", sharedMetadata.Repository, dependency.Repository);
            AddMismatch(mismatches, "resolved commit", sharedMetadata.ResolvedCommitHash, rawMetadata.ResolvedCommitHash);
            AddMismatch(mismatches, "raw snapshot root", sharedMetadata.RawSnapshotRootPath, snapshotRoot);
            AddMismatch(mismatches, "raw metadata path", sharedMetadata.RawMetadataPath, rawMetadataPath);
            AddMismatch(mismatches, "Sentis version", sharedMetadata.SentisVersion, GetCurrentSentisVersion());
            AddMismatch(mismatches, "runtime snapshot asset path", sharedMetadata.RuntimeSnapshotAssetPath, HfHubProjectPaths.GetRuntimeSnapshotFolder(dependency.Repository, rawMetadata.ResolvedCommitHash));

            return mismatches.Count == 0
                ? "Shared import metadata differs from current repo snapshot or Sentis version."
                : $"Shared import metadata is stale: {string.Join("; ", mismatches)}.";
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
            string bindingMetadataPath)
        {
            return Missing(dependency, reasonCode, maintainerMessage, revisionRefPath, rawFilePath, rawMetadataPath, null, sentisAssetPath, bindingMetadataPath);
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
            string bindingMetadataPath,
            HfHubImportedModelMetadata importedMetadata = null)
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
                bindingMetadataPath,
                importedMetadata);
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
            string bindingMetadataPath,
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
                bindingMetadataPath,
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
            string bindingMetadataPath)
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
                bindingMetadataPath);
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
            string bindingMetadataPath,
            HfHubImportedModelMetadata importedMetadata = null)
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
                bindingMetadataPath,
                importedMetadata);
        }
    }
}
