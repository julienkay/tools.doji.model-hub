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
        private readonly IProjectModelRecipeSource _recipeSource;
        private readonly IRemoteSnapshotSizeEstimateService _remoteSnapshotSizeEstimateService;

        public ModelHubService(IProjectModelRecipeSource recipeSource = null, IRemoteSnapshotSizeEstimateService remoteSnapshotSizeEstimateService = null)
        {
            _recipeSource = recipeSource ?? new ProjectModelRecipeSource();
            _remoteSnapshotSizeEstimateService = remoteSnapshotSizeEstimateService ?? new RemoteSnapshotSizeEstimateService();
        }

        /// <summary>
        /// Collapses dependency-level status into one row per repository and requested revision for Model Hub.
        /// </summary>
        public ModelHubReport BuildReport(StatusReport statusReport)
        {
            var items = (statusReport?.DependencyStatuses ?? Array.Empty<DependencyStatus>())
                .Where(status => status.ReasonCode != DependencyReasonCode.InvalidRepository)
                .GroupBy(
                    status => $"{status.Dependency.Repository}\n{status.Dependency.Revision}",
                    StringComparer.Ordinal)
                .Select(group => BuildItem(group))
                .OrderBy(item => item.Repository, StringComparer.Ordinal)
                .ThenBy(item => item.RequestedRevision, StringComparer.Ordinal)
                .ToArray();
            AddRevisionConflictDiagnostics(items);

            return new ModelHubReport(items, statusReport?.DiscoveryIssues ?? Array.Empty<ManifestDiscoveryIssue>());
        }

        private ModelHubItem BuildItem(IGrouping<string, DependencyStatus> group)
        {
            var dependents = group
                .OrderBy(item => item.Dependency.PackageDisplayName, StringComparer.Ordinal)
                .ThenBy(item => item.Dependency.ModelId, StringComparer.Ordinal)
                .ToArray();
            var primary = dependents[0];
            var runtimeFiles = dependents
                .SelectMany(item => item.ImportedMetadata?.RuntimeFiles ?? Array.Empty<ImportedRuntimeFileMetadata>())
                .GroupBy(file => file.FilePath, StringComparer.Ordinal)
                .Select(files => files.First())
                .OrderBy(file => file.RepoPath, StringComparer.Ordinal)
                .ToArray();
            var diagnostics = dependents
                .Where(item => item.NeedsAttention && !string.IsNullOrWhiteSpace(GetUserFacingDependencyMessage(item)))
                .Select(item => new ModelHubDiagnostic(
                    GetDependencyDiagnosticSeverity(item.State, item.ReasonCode),
                    GetUserFacingDependencyMessage(item)))
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

            var runtimeModelFolder = runtimeFiles
                .Where(file => string.Equals(file.Format, "onnx", StringComparison.Ordinal))
                .Select(file => Path.GetDirectoryName(file.FilePath))
                .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
            var rawSnapshotRootPath = dependents
                .Select(item => item.RawFilePath)
                .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
            var resolvedCommitHash = dependents
                .Select(item => item.ImportedMetadata?.ResolvedCommitHash ?? item.RawMetadata?.ResolvedCommitHash)
                .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
            var repositoryFiles = GetRepositoryInventory(primary.Dependency.Repository, primary.Dependency.Revision, dependents);
            var selectedRepoPaths = BuildSelectedRepoPathSet(dependents, repositoryFiles);
            var estimatedCacheSizeBytes = CalculateSelectedSourceSize(repositoryFiles, selectedRepoPaths);
            if (!estimatedCacheSizeBytes.HasValue)
            {
                estimatedCacheSizeBytes = dependents
                    .Select(item => item.ImportedMetadata?.RawCacheSizeInBytes)
                    .Where(size => size.HasValue)
                    .Select(size => size.Value)
                    .DefaultIfEmpty(0)
                    .Max();
                if (estimatedCacheSizeBytes <= 0)
                {
                    estimatedCacheSizeBytes = null;
                }
            }

            var repositorySizeBytes = CalculateRepositorySize(repositoryFiles);
            var cacheSizeOnDiskBytes = CalculateCacheSizeOnDisk(dependents, selectedRepoPaths);
            var runtimeSizeOnDiskBytes = CalculateRuntimeSizeOnDisk(runtimeFiles);
            var fileItems = BuildFileItems(primary.Dependency.Repository, dependents, runtimeFiles, repositoryFiles, selectedRepoPaths);

            return new ModelHubItem(
                primary.Dependency.Repository,
                primary.Dependency.Revision,
                resolvedCommitHash,
                rawSnapshotRootPath,
                runtimeModelFolder,
                cacheSizeOnDiskBytes,
                estimatedCacheSizeBytes,
                repositorySizeBytes,
                runtimeSizeOnDiskBytes,
                GetAggregateState(dependents),
                dependents,
                diagnostics,
                runtimeFiles,
                fileItems,
                GetModelCard(primary.Dependency.Repository, primary.Dependency.Revision, dependents));
        }

        /// <summary>
        /// The model card's license fields from the download cache, else from the lookup made before download.
        /// </summary>
        private ModelCardMetadata GetModelCard(string repository, string requestedRevision, IReadOnlyList<DependencyStatus> dependents)
        {
            var cached = dependents
                .Select(item => item.RawMetadata?.ModelCard)
                .FirstOrDefault(card => card != null);
            if (cached != null)
            {
                return cached;
            }

            return _remoteSnapshotSizeEstimateService.TryGetModelCard(repository, requestedRevision, out var modelCard)
                ? modelCard
                : null;
        }

        private IReadOnlyList<ModelHubFileItem> BuildFileItems(
            string repository,
            IReadOnlyList<DependencyStatus> dependents,
            IReadOnlyList<ImportedRuntimeFileMetadata> runtimeFiles,
            IReadOnlyList<RepositoryFileMetadata> repositoryFiles,
            IReadOnlyCollection<string> selectedRepoPaths)
        {
            var rawFilesByRepoPath = dependents
                .SelectMany(item => item.RawMetadata?.Files ?? Array.Empty<RawCacheFileMetadata>())
                .GroupBy(file => file.RepoPath, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var runtimeFilesByRepoPath = (runtimeFiles ?? Array.Empty<ImportedRuntimeFileMetadata>())
                .Where(file => string.Equals(file.Kind, "sentis", StringComparison.Ordinal))
                .GroupBy(file => file.RepoPath, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var inventoryByRepoPath = (repositoryFiles ?? Array.Empty<RepositoryFileMetadata>())
                .GroupBy(file => file.RepoPath, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var selectedOwnerCounts = BuildSelectedOwnerCounts(dependents, repositoryFiles);

            return inventoryByRepoPath.Keys
                .Concat(rawFilesByRepoPath.Keys)
                .Concat(runtimeFilesByRepoPath.Keys)
                .Where(path => !ModelDependencyDefinition.IsRepoMetadataFile(path))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(repoPath =>
                {
                    inventoryByRepoPath.TryGetValue(repoPath, out var repositoryFile);
                    rawFilesByRepoPath.TryGetValue(repoPath, out var rawFile);
                    runtimeFilesByRepoPath.TryGetValue(repoPath, out var runtimeFile);
                    var isDependencySelected = selectedRepoPaths?.Contains(repoPath) == true;
                    selectedOwnerCounts.TryGetValue(repoPath, out var selectedOwnerCount);
                    var effectiveRecipe = isDependencySelected && ModelDependencyDefinition.IsOnnxFile(repoPath)
                        ? _recipeSource.GetEffectiveRecipe(repository, repoPath)
                        : null;
                    return new ModelHubFileItem(
                        repoPath,
                        isDependencySelected,
                        selectedOwnerCount,
                        rawFile?.CachedFilePath,
                        repositoryFile?.SizeInBytes ?? rawFile?.SizeInBytes,
                        GetFileSizeOnDisk(rawFile?.CachedFilePath),
                        runtimeFile,
                        GetRuntimeFileSizeOnDisk(runtimeFile),
                        effectiveRecipe?.Recipe,
                        effectiveRecipe?.SourceLabel,
                        effectiveRecipe?.OverrideAssetPath,
                        effectiveRecipe?.HasProjectOverride == true);
                })
                .ToArray();
        }

        private static string GetDependencyDiagnosticSeverity(DependencyState state, DependencyReasonCode reasonCode)
        {
            if (state == DependencyState.Failed)
            {
                return "error";
            }

            return reasonCode == DependencyReasonCode.RawCacheFilesMissing ? "info" : "warning";
        }

        private static string GetUserFacingDependencyMessage(DependencyStatus status)
        {
            if (status == null)
            {
                return null;
            }

            return status.ReasonCode switch
            {
                DependencyReasonCode.InvalidRepository => "Repository was not found on Hugging Face.",
                DependencyReasonCode.ImportBindingMissing => "Model needs import into this project.",
                DependencyReasonCode.SharedImportMetadataMissing => "Generated runtime artifacts are missing. Re-import this model.",
                DependencyReasonCode.RuntimeFilesMissing => "Generated runtime files are missing. Re-import this model.",
                DependencyReasonCode.RawCacheRevisionRefMissing => "Model needs download before it can be imported.",
                DependencyReasonCode.RawSnapshotRootMissing => "Cached source files are missing. Download this model again before importing.",
                DependencyReasonCode.RawCacheFilesMissing => "Cached source files are missing. Update the model cache.",
                DependencyReasonCode.ImportBindingInvalid => "Project import binding metadata is invalid. Re-import this model.",
                DependencyReasonCode.SharedImportMetadataInvalid => "Generated runtime metadata is invalid. Re-import this model.",
                DependencyReasonCode.RuntimeFileMissing => BuildRuntimeFileMissingMessage(status.MaintainerMessage),
                DependencyReasonCode.ImportBindingStale => "Imported files are out of date with current manifest, cache snapshot, or Sentis version. Re-import this model.",
                DependencyReasonCode.SharedImportMetadataStale => "Imported files are out of date with current manifest, cache snapshot, or Sentis version. Re-import this model.",
                DependencyReasonCode.RevisionConflict => null,
                DependencyReasonCode.RawCacheRevisionRefUnreadable => "Cached revision pointer could not be read. Download this model again.",
                DependencyReasonCode.SelectionRequired => "Choose at least one optional model file for this manifest owner before downloading or importing.",
                DependencyReasonCode.InvalidSelection => status.MaintainerMessage,
                DependencyReasonCode.RawCacheRemovedAfterImport => null,
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

        private IReadOnlyList<RepositoryFileMetadata> GetRepositoryInventory(
            string repository,
            string requestedRevision,
            IReadOnlyList<DependencyStatus> dependents)
        {
            var repositoryFiles = dependents
                .SelectMany(item => item.RawMetadata?.RepositoryFiles ?? Array.Empty<RepositoryFileMetadata>())
                .Where(file => !ModelDependencyDefinition.IsRepoMetadataFile(file.RepoPath))
                .GroupBy(file => file.RepoPath, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(file => file.RepoPath, StringComparer.Ordinal)
                .ToArray();
            if (repositoryFiles.Length > 0)
            {
                return repositoryFiles;
            }

            repositoryFiles = dependents
                .SelectMany(item => item.ImportedMetadata?.RepositoryFiles ?? Array.Empty<RepositoryFileMetadata>())
                .Where(file => !ModelDependencyDefinition.IsRepoMetadataFile(file.RepoPath))
                .GroupBy(file => file.RepoPath, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(file => file.RepoPath, StringComparer.Ordinal)
                .ToArray();
            if (repositoryFiles.Length > 0)
            {
                return repositoryFiles;
            }

            if (_remoteSnapshotSizeEstimateService.TryGetRepositoryInventory(repository, requestedRevision, out var persistedInventory))
            {
                return persistedInventory
                    .Where(file => !ModelDependencyDefinition.IsRepoMetadataFile(file.RepoPath))
                    .ToArray();
            }

            return Array.Empty<RepositoryFileMetadata>();
        }

        private static HashSet<string> BuildSelectedRepoPathSet(
            IReadOnlyList<DependencyStatus> dependents,
            IReadOnlyList<RepositoryFileMetadata> repositoryFiles)
        {
            var inventoryPaths = (repositoryFiles ?? Array.Empty<RepositoryFileMetadata>())
                .Select(file => file.RepoPath)
                .ToArray();
            var selectedRepoPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var dependent in dependents ?? Array.Empty<DependencyStatus>())
            {
                foreach (var repoPath in dependent.Dependency.GetSelectedRepoPaths(
                             inventoryPaths,
                             (dependent.RawMetadata?.Files ?? Array.Empty<RawCacheFileMetadata>()).Select(file => file.RepoPath)))
                {
                    selectedRepoPaths.Add(repoPath);
                }
            }

            return selectedRepoPaths;
        }

        private static Dictionary<string, int> BuildSelectedOwnerCounts(
            IReadOnlyList<DependencyStatus> dependents,
            IReadOnlyList<RepositoryFileMetadata> repositoryFiles)
        {
            var inventoryPaths = (repositoryFiles ?? Array.Empty<RepositoryFileMetadata>())
                .Select(file => file.RepoPath)
                .ToArray();
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var dependent in dependents ?? Array.Empty<DependencyStatus>())
            {
                foreach (var repoPath in dependent.Dependency.GetSelectedRepoPaths(
                             inventoryPaths,
                             (dependent.RawMetadata?.Files ?? Array.Empty<RawCacheFileMetadata>()).Select(file => file.RepoPath)))
                {
                    counts[repoPath] = counts.TryGetValue(repoPath, out var count) ? count + 1 : 1;
                }
            }

            return counts;
        }

        private static long? CalculateSelectedSourceSize(
            IReadOnlyList<RepositoryFileMetadata> repositoryFiles,
            IReadOnlyCollection<string> selectedRepoPaths)
        {
            var files = (repositoryFiles ?? Array.Empty<RepositoryFileMetadata>())
                .Where(file => selectedRepoPaths?.Contains(file.RepoPath) == true)
                .Select(file => file.SizeInBytes)
                .Where(size => size.HasValue)
                .Select(size => size.Value)
                .ToArray();
            return files.Length > 0 ? files.Sum() : null;
        }

        private static long? CalculateRepositorySize(IReadOnlyList<RepositoryFileMetadata> repositoryFiles)
        {
            var files = (repositoryFiles ?? Array.Empty<RepositoryFileMetadata>())
                .Select(file => file.SizeInBytes)
                .Where(size => size.HasValue)
                .Select(size => size.Value)
                .ToArray();
            return files.Length > 0 ? files.Sum() : null;
        }

        private static long? CalculateCacheSizeOnDisk(IReadOnlyList<DependencyStatus> dependents, IReadOnlyCollection<string> selectedRepoPaths)
        {
            var filePaths = dependents
                .SelectMany(item => item.RawMetadata?.Files ?? Array.Empty<RawCacheFileMetadata>())
                .Where(file => selectedRepoPaths?.Contains(file.RepoPath) == true)
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

                totalBytes += CacheFiles.GetLength(path) ?? 0;
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
            // Only converted models take space of their own: a Sentis runtime model is the downloaded file.
            foreach (var runtimeFile in runtimeFiles.Where(file => string.Equals(file.Format, "onnx", StringComparison.Ordinal)))
            {
                var size = GetFileSizeOnDisk(runtimeFile.FilePath);
                if (!size.HasValue)
                {
                    continue;
                }

                totalBytes += size.Value;
                foundAny = true;
            }

            return foundAny ? totalBytes : null;
        }

        private static long? GetFileSizeOnDisk(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            return CacheFiles.GetLength(path);
        }

        private static long? GetRuntimeFileSizeOnDisk(ImportedRuntimeFileMetadata runtimeFile)
        {
            return runtimeFile == null ? null : GetFileSizeOnDisk(runtimeFile.FilePath);
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
