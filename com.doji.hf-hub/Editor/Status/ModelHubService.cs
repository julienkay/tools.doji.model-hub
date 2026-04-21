using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Transforms dependency-level status into repository-centric Model Hub inventory data.
    /// </summary>
    internal sealed class ModelHubService
    {
        /// <summary>
        /// Collapses dependency-level status into one row per repository and requested revision for Model Hub.
        /// </summary>
        public ModelHubReport BuildReport(StatusReport statusReport)
        {
            var items = (statusReport?.DependencyStatuses ?? Array.Empty<DependencyStatus>())
                .GroupBy(
                    status => $"{status.Dependency.Repository}\n{status.Dependency.Revision}",
                    StringComparer.Ordinal)
                .Select(BuildItem)
                .OrderBy(item => item.Repository, StringComparer.Ordinal)
                .ThenBy(item => item.RequestedRevision, StringComparer.Ordinal)
                .ToArray();
            AddRevisionConflictDiagnostics(items);

            return new ModelHubReport(items, statusReport?.DiscoveryIssues ?? Array.Empty<ManifestDiscoveryIssue>());
        }

        private static ModelHubItem BuildItem(IGrouping<string, DependencyStatus> group)
        {
            var dependents = group
                .OrderBy(item => item.Dependency.PackageDisplayName, StringComparer.Ordinal)
                .ThenBy(item => item.Dependency.ModelId, StringComparer.Ordinal)
                .ToArray();
            var primary = dependents[0];
            var runtimeFiles = dependents
                .SelectMany(item => item.ImportedMetadata?.RuntimeFiles ?? Array.Empty<ImportedRuntimeFileMetadata>())
                .GroupBy(file => file.AssetPath, StringComparer.Ordinal)
                .Select(files => files.First())
                .OrderBy(file => file.AssetPath, StringComparer.Ordinal)
                .ToArray();
            var diagnostics = dependents
                .Where(item => item.NeedsAttention && !string.IsNullOrWhiteSpace(GetUserFacingDependencyMessage(item)))
                .Select(item => new ModelHubDiagnostic(
                    GetDependencyDiagnosticSeverity(item.State),
                    $"{item.Dependency.PackageDisplayName} / {item.Dependency.ModelId}: {GetUserFacingDependencyMessage(item)}"))
                .Concat(
                    dependents
                        .SelectMany(item => item.ImportedMetadata?.Diagnostics ?? Array.Empty<ImportDiagnostic>())
                        .Where(diagnostic => !string.IsNullOrWhiteSpace(diagnostic?.Message))
                        .Select(diagnostic => new ModelHubDiagnostic(
                            string.IsNullOrWhiteSpace(diagnostic.Severity) ? "info" : diagnostic.Severity,
                            diagnostic.Message)))
                .GroupBy(
                    diagnostic => $"{diagnostic.Severity}\n{diagnostic.Message}",
                    StringComparer.Ordinal)
                .Select(grouped => grouped.First())
                .ToArray();

            var runtimeSnapshotAssetPath = primary.ImportedMetadata != null
                ? ProjectPaths.GetRuntimeSnapshotFolder(primary.Dependency.Repository, primary.ImportedMetadata.ResolvedCommitHash)
                : null;
            var rawSnapshotRootPath = dependents
                .Select(item => item.RawFilePath)
                .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
            var resolvedCommitHash = dependents
                .Select(item => item.ImportedMetadata?.ResolvedCommitHash ?? item.RawMetadata?.ResolvedCommitHash)
                .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
            // Multiple manifest owners can point at the same cached repo snapshot, so sum each cached file once.
            var estimatedCacheSizeBytes = dependents
                .SelectMany(item => item.RawMetadata?.Files ?? Array.Empty<RawCacheFileMetadata>())
                .GroupBy(
                    file => file.CachedFilePath ?? file.RepoPath,
                    StringComparer.Ordinal)
                .Select(files => files.First())
                .Select(file => file.SizeInBytes)
                .Where(size => size.HasValue)
                .Sum(size => size.Value);
            if (estimatedCacheSizeBytes <= 0)
            {
                // Imported metadata keeps the last known cache-size estimate even after raw cache was removed.
                estimatedCacheSizeBytes = dependents
                    .Select(item => item.ImportedMetadata?.RawCacheSizeInBytes)
                    .Where(size => size.HasValue)
                    .Select(size => size.Value)
                    .DefaultIfEmpty(0)
                    .Max();
            }
            var cacheSizeOnDiskBytes = CalculateCacheSizeOnDisk(dependents);
            var estimatedRuntimeSizeBytes = runtimeFiles
                .Select(file => file.SizeInBytes)
                .Where(size => size.HasValue)
                .Sum(size => size.Value);
            var runtimeSizeOnDiskBytes = CalculateRuntimeSizeOnDisk(runtimeFiles);

            return new ModelHubItem(
                primary.Dependency.Repository,
                primary.Dependency.Revision,
                resolvedCommitHash,
                rawSnapshotRootPath,
                runtimeSnapshotAssetPath,
                cacheSizeOnDiskBytes,
                estimatedCacheSizeBytes > 0 ? estimatedCacheSizeBytes : null,
                runtimeSizeOnDiskBytes,
                estimatedRuntimeSizeBytes > 0 ? estimatedRuntimeSizeBytes : null,
                GetAggregateState(dependents),
                dependents,
                diagnostics,
                runtimeFiles);
        }

        private static string GetDependencyDiagnosticSeverity(DependencyState state)
        {
            return state == DependencyState.Failed ? "error" : "warning";
        }

        private static string GetUserFacingDependencyMessage(DependencyStatus status)
        {
            if (status == null)
            {
                return null;
            }

            return status.ReasonCode switch
            {
                DependencyReasonCode.ImportBindingMissing => "Model needs import into this project.",
                DependencyReasonCode.SharedImportMetadataMissing => "Generated runtime artifacts are missing. Re-import this model.",
                DependencyReasonCode.RuntimeFilesMissing => "Generated runtime files are missing. Re-import this model.",
                DependencyReasonCode.RawCacheRevisionRefMissing => "Model needs download before it can be imported.",
                DependencyReasonCode.RawSnapshotRootMissing => "Cached source files are missing. Download this model again before importing.",
                DependencyReasonCode.RawCacheMetadataInvalid => "Cached source metadata is invalid. Download this model again.",
                DependencyReasonCode.ImportBindingInvalid => "Project import binding metadata is invalid. Re-import this model.",
                DependencyReasonCode.SharedImportMetadataInvalid => "Generated runtime metadata is invalid. Re-import this model.",
                DependencyReasonCode.RuntimeFileMissing => BuildRuntimeFileMissingMessage(status.MaintainerMessage),
                DependencyReasonCode.ImportBindingStale => "Imported files are out of date with current manifest, cache snapshot, or Sentis version. Re-import this model.",
                DependencyReasonCode.SharedImportMetadataStale => "Imported files are out of date with current manifest, cache snapshot, or Sentis version. Re-import this model.",
                DependencyReasonCode.RevisionConflict => null,
                DependencyReasonCode.RawCacheMetadataMismatch => "Cached source files no longer match the declared revision. Download this model again.",
                DependencyReasonCode.RawCacheRevisionRefUnreadable => "Cached revision pointer could not be read. Download this model again.",
                DependencyReasonCode.RawCacheRemovedAfterImport => null,
                DependencyReasonCode.RawCacheMetadataMissingAfterImport => null,
                _ => status.MaintainerMessage
            };
        }

        private static string BuildRuntimeFileMissingMessage(string maintainerMessage)
        {
            const string prefix = "Generated runtime file is missing:";
            if (string.IsNullOrWhiteSpace(maintainerMessage) ||
                !maintainerMessage.StartsWith(prefix, StringComparison.Ordinal))
            {
                return "Generated runtime file is missing. Re-import this model.";
            }

            return $"Generated runtime file is missing. Re-import this model. ({maintainerMessage.Substring(prefix.Length).Trim()})";
        }

        private static void AddRevisionConflictDiagnostics(IReadOnlyList<ModelHubItem> items)
        {
            foreach (var repositoryGroup in items.GroupBy(item => item.Repository, StringComparer.Ordinal))
            {
                var revisions = repositoryGroup
                    .Select(item => item.RequestedRevision)
                    .Where(revision => !string.IsNullOrWhiteSpace(revision))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(revision => revision, StringComparer.Ordinal)
                    .ToArray();
                if (revisions.Length <= 1)
                {
                    continue;
                }

                var owners = repositoryGroup
                    .SelectMany(item => item.Dependents)
                    .Select(status => $"{status.Dependency.PackageDisplayName} / {status.Dependency.ModelId}")
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(label => label, StringComparer.Ordinal)
                    .ToArray();
                var errorMessage =
                    $"Repository revision conflict: '{repositoryGroup.Key}' is declared at multiple requested revisions ({string.Join(", ", revisions)}). HF Hub supports only one revision per repository per project. Resolve manifests to one revision before importing or removing files. Conflicting dependents: {string.Join(", ", owners)}.";
                foreach (var item in repositoryGroup)
                {
                    item.AddDiagnostic(new ModelHubDiagnostic("error", errorMessage));
                }
            }
        }

        private static long? CalculateCacheSizeOnDisk(IReadOnlyList<DependencyStatus> dependents)
        {
            var filePaths = dependents
                .SelectMany(item => item.RawMetadata?.Files ?? Array.Empty<RawCacheFileMetadata>())
                .Select(file => file.CachedFilePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (filePaths.Length == 0)
            {
                return null;
            }

            long totalBytes = 0;
            var foundAny = false;
            foreach (var path in filePaths)
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                totalBytes += new FileInfo(path).Length;
                foundAny = true;
            }

            return foundAny ? totalBytes : null;
        }

        private static long? CalculateRuntimeSizeOnDisk(IReadOnlyList<ImportedRuntimeFileMetadata> runtimeFiles)
        {
            if (runtimeFiles == null || runtimeFiles.Count == 0)
            {
                return null;
            }

            long totalBytes = 0;
            var foundAny = false;
            foreach (var runtimeFile in runtimeFiles)
            {
                var assetPath = runtimeFile.AssetPath;
                if (string.IsNullOrWhiteSpace(assetPath))
                {
                    continue;
                }

                var absolutePath = ProjectPaths.ToAbsolutePath(assetPath);
                if (!File.Exists(absolutePath))
                {
                    continue;
                }

                totalBytes += new FileInfo(absolutePath).Length;
                foundAny = true;
            }

            return foundAny ? totalBytes : null;
        }

        private static ModelHubItemState GetAggregateState(IReadOnlyList<DependencyStatus> dependents)
        {
            if (dependents.Any(item => item.State == DependencyState.Failed))
            {
                return ModelHubItemState.Error;
            }

            if (dependents.Any(item => item.State == DependencyState.Missing || item.State == DependencyState.Stale))
            {
                return ModelHubItemState.ActionRequired;
            }

            return ModelHubItemState.Ready;
        }
    }
}
