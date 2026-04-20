using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Transforms dependency-level status into repository-centric Model Hub inventory data.
    /// </summary>
    internal sealed class HfHubModelHubService
    {
        /// <summary>
        /// Collapses dependency-level status into one row per repository and requested revision for Model Hub.
        /// </summary>
        public HfHubModelHubReport BuildReport(HfHubStatusReport statusReport)
        {
            var items = (statusReport?.DependencyStatuses ?? Array.Empty<HfHubDependencyStatus>())
                .GroupBy(
                    status => $"{status.Dependency.Repository}\n{status.Dependency.Revision}",
                    StringComparer.Ordinal)
                .Select(BuildItem)
                .OrderBy(item => item.Repository, StringComparer.Ordinal)
                .ThenBy(item => item.RequestedRevision, StringComparer.Ordinal)
                .ToArray();

            return new HfHubModelHubReport(items, statusReport?.DiscoveryIssues ?? Array.Empty<HfHubManifestDiscoveryIssue>());
        }

        private static HfHubModelHubItem BuildItem(IGrouping<string, HfHubDependencyStatus> group)
        {
            var dependents = group
                .OrderBy(item => item.Dependency.PackageDisplayName, StringComparer.Ordinal)
                .ThenBy(item => item.Dependency.ModelId, StringComparer.Ordinal)
                .ToArray();
            var primary = dependents[0];
            var runtimeFiles = dependents
                .SelectMany(item => item.ImportedMetadata?.RuntimeFiles ?? Array.Empty<HfHubImportedRuntimeFileMetadata>())
                .GroupBy(file => file.AssetPath, StringComparer.Ordinal)
                .Select(files => files.First())
                .OrderBy(file => file.AssetPath, StringComparer.Ordinal)
                .ToArray();
            var diagnostics = dependents
                .Where(item => item.NeedsAttention && !string.IsNullOrWhiteSpace(item.Message))
                .Select(item => $"{item.Dependency.PackageDisplayName} / {item.Dependency.ModelId}: {item.Message}")
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            var runtimeSnapshotAssetPath = primary.ImportedMetadata != null
                ? HfHubProjectPaths.GetRuntimeSnapshotFolder(primary.Dependency.Repository, primary.ImportedMetadata.ResolvedCommitHash)
                : null;
            var rawSnapshotRootPath = dependents
                .Select(item => item.RawFilePath)
                .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
            var resolvedCommitHash = dependents
                .Select(item => item.ImportedMetadata?.ResolvedCommitHash ?? item.RawMetadata?.ResolvedCommitHash)
                .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
            // Multiple manifest owners can point at the same cached repo snapshot, so sum each cached file once.
            var estimatedCacheSizeBytes = dependents
                .SelectMany(item => item.RawMetadata?.Files ?? Array.Empty<HfHubRawCacheFileMetadata>())
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

            return new HfHubModelHubItem(
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

        private static long? CalculateCacheSizeOnDisk(IReadOnlyList<HfHubDependencyStatus> dependents)
        {
            var filePaths = dependents
                .SelectMany(item => item.RawMetadata?.Files ?? Array.Empty<HfHubRawCacheFileMetadata>())
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

        private static long? CalculateRuntimeSizeOnDisk(IReadOnlyList<HfHubImportedRuntimeFileMetadata> runtimeFiles)
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

                var absolutePath = HfHubProjectPaths.ToAbsolutePath(assetPath);
                if (!File.Exists(absolutePath))
                {
                    continue;
                }

                totalBytes += new FileInfo(absolutePath).Length;
                foundAny = true;
            }

            return foundAny ? totalBytes : null;
        }

        private static HfHubModelHubItemState GetAggregateState(IReadOnlyList<HfHubDependencyStatus> dependents)
        {
            if (dependents.Any(item => item.State == HfHubDependencyState.Failed))
            {
                return HfHubModelHubItemState.Error;
            }

            if (dependents.Any(item => item.State == HfHubDependencyState.Missing || item.State == HfHubDependencyState.Stale))
            {
                return HfHubModelHubItemState.ActionRequired;
            }

            return HfHubModelHubItemState.Ready;
        }
    }
}
