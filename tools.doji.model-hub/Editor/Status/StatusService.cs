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
    internal sealed class StatusService
    {
        private const string VisualScenarioGeneratedManifestPathSegment = "/Assets/AI Model Hub/Scenario Sandbox/Generated/";
        private readonly RawCacheService _rawCacheService;
        private readonly IProjectModelRecipeSource _recipeSource;

        public StatusService(RawCacheService rawCacheService = null, IProjectModelRecipeSource recipeSource = null)
        {
            _rawCacheService = rawCacheService ?? new RawCacheService();
            _recipeSource = recipeSource ?? new ProjectModelRecipeSource();
        }

        public StatusReport BuildStatusReport(DiscoveryReport discoveryReport)
        {
            var revisionConflicts = RevisionConflictDetector.Detect(discoveryReport?.Dependencies);
            var dependencyStatuses = discoveryReport.Dependencies
                .Select(dependency => BuildDependencyStatus(dependency, revisionConflicts))
                .ToArray();

            return new StatusReport(
                discoveryReport.Issues.ToArray(),
                dependencyStatuses);
        }

        private DependencyStatus BuildDependencyStatus(
            DiscoveredModelDependency dependency,
            IReadOnlyDictionary<string, RepositoryRevisionConflict> revisionConflicts)
        {
            var revisionRefPath = CachePaths.GetRevisionRefPath(dependency.Repository, dependency.Revision);
            string runtimePreviewAssetPath = null;
            var bindingMetadataAssetPath = ProjectPaths.GetImportBindingMetadataFilePath(
                dependency.PackageId,
                dependency.ModelId);
            var bindingMetadataAbsolutePath = ProjectPaths.ToProjectAbsolutePath(bindingMetadataAssetPath);

            if (!IsVisualScenarioDependency(dependency) &&
                RepositoryValidationCacheService.TryGetCachedRepositoryExists(dependency.Repository, out var repositoryExists) &&
                !repositoryExists)
            {
                return Failed(
                    dependency,
                    DependencyReasonCode.InvalidRepository,
                    "Repository was not found on Hugging Face.",
                    revisionRefPath,
                    null,
                    null,
                    runtimePreviewAssetPath,
                    bindingMetadataAssetPath);
            }

            if (revisionConflicts != null &&
                revisionConflicts.TryGetValue(dependency.Repository, out var revisionConflict))
            {
                return Failed(
                    dependency,
                    DependencyReasonCode.RevisionConflict,
                    revisionConflict.BuildMaintainerMessage(),
                    revisionRefPath,
                    null,
                    null,
                    runtimePreviewAssetPath,
                    bindingMetadataAssetPath);
            }

            if (dependency.RequiresOptionalModelSelection)
            {
                return Missing(
                    dependency,
                    DependencyReasonCode.SelectionRequired,
                    "This dependency has optional model files, but none are currently enabled for this project owner.",
                    revisionRefPath,
                    null,
                    null,
                    runtimePreviewAssetPath,
                    bindingMetadataAssetPath);
            }

            if (!File.Exists(revisionRefPath))
            {
                if (TryBuildImportedOnlyStatus(
                    dependency,
                    bindingMetadataAbsolutePath,
                    bindingMetadataAssetPath,
                    DependencyReasonCode.RawCacheRemovedAfterImport,
                    "Generated runtime files are present. Raw cache has been removed from this machine.",
                    out var importedOnlyStatus))
                {
                    return importedOnlyStatus;
                }

                return Missing(
                    dependency,
                    DependencyReasonCode.RawCacheRevisionRefMissing,
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
                    DependencyReasonCode.RawCacheRevisionRefUnreadable,
                    $"Could not read raw cache revision ref: {exception.Message}",
                    revisionRefPath,
                    null,
                    null,
                    runtimePreviewAssetPath,
                    bindingMetadataAssetPath);
            }

            var snapshotRoot = CachePaths.GetSnapshotRoot(dependency.Repository, resolvedCommitHash);
            var rawMetadataPath = CachePaths.GetSnapshotMetadataPath(dependency.Repository, resolvedCommitHash);
            if (!Directory.Exists(snapshotRoot))
            {
                if (TryBuildImportedOnlyStatus(
                    dependency,
                    bindingMetadataAbsolutePath,
                    bindingMetadataAssetPath,
                    DependencyReasonCode.RawCacheRemovedAfterImport,
                    "Generated runtime files are present. Raw cache snapshot has been removed from this machine.",
                    out var importedOnlyStatus))
                {
                    return importedOnlyStatus;
                }

                return Missing(
                    dependency,
                    DependencyReasonCode.RawSnapshotRootMissing,
                    "Raw snapshot root is missing.",
                    revisionRefPath,
                    snapshotRoot,
                    rawMetadataPath,
                    runtimePreviewAssetPath,
                    bindingMetadataAssetPath);
            }

            // The files on disk are the truth; AI Model Hub's index only adds the repository file list.
            var rawMetadata = _rawCacheService.ReadSnapshot(dependency.Repository, dependency.Revision, resolvedCommitHash);
            var repositoryFilePaths = (rawMetadata.RepositoryFiles ?? Array.Empty<RepositoryFileMetadata>())
                .Select(file => file.RepoPath)
                .ToArray();
            var fallbackCachedPaths = (rawMetadata.Files ?? Array.Empty<RawCacheFileMetadata>())
                .Select(file => file.RepoPath)
                .ToArray();
            if (dependency.SelectsWholeRepository && repositoryFilePaths.Length == 0)
            {
                return Missing(
                    dependency,
                    DependencyReasonCode.RawCacheFilesMissing,
                    "The repository file list is not known on this machine, so AI Model Hub cannot tell whether every file is downloaded. Update to check.",
                    revisionRefPath,
                    snapshotRoot,
                    rawMetadataPath,
                    rawMetadata,
                    runtimePreviewAssetPath,
                    bindingMetadataAssetPath);
            }

            var selectedRepoPaths = dependency.GetSelectedRepoPaths(repositoryFilePaths, fallbackCachedPaths).ToArray();

            var rawFilesByRepoPath = (rawMetadata.Files ?? Array.Empty<RawCacheFileMetadata>())
                .Where(file => file != null && !string.IsNullOrWhiteSpace(file.RepoPath))
                .ToDictionary(file => file.RepoPath, StringComparer.Ordinal);
            var missingSelectedFiles = selectedRepoPaths
                .Where(repoPath =>
                    !rawFilesByRepoPath.TryGetValue(repoPath, out var file) ||
                    string.IsNullOrWhiteSpace(file.CachedFilePath) ||
                    !File.Exists(file.CachedFilePath))
                .OrderBy(repoPath => repoPath, StringComparer.Ordinal)
                .ToArray();
            if (missingSelectedFiles.Length > 0)
            {
                return Missing(
                    dependency,
                    DependencyReasonCode.RawCacheFilesMissing,
                    $"Raw cache is missing selected file(s): {string.Join(", ", missingSelectedFiles)}.",
                    revisionRefPath,
                    snapshotRoot,
                    rawMetadataPath,
                    rawMetadata,
                    runtimePreviewAssetPath,
                    bindingMetadataAssetPath);
            }

            var selectionProblem = dependency.GetSelectionProblem(
                repositoryFilePaths.Length > 0 ? repositoryFilePaths : null,
                fallbackCachedPaths);
            var selectedOnnxRepoPaths = dependency.GetSelectedOnnxRepoPaths(repositoryFilePaths, fallbackCachedPaths).ToArray();
            var selectedModelRepoPaths = dependency.GetSelectedModelRepoPaths(
                repositoryFilePaths.Length > 0 ? repositoryFilePaths : null,
                fallbackCachedPaths).ToArray();
            if (selectionProblem != null)
            {
                return Failed(
                    dependency,
                    DependencyReasonCode.InvalidSelection,
                    selectionProblem,
                    revisionRefPath,
                    snapshotRoot,
                    rawMetadataPath,
                    rawMetadata,
                    runtimePreviewAssetPath,
                    bindingMetadataAssetPath);
            }

            var expectedRecipeSetHash = _recipeSource.ComputeRecipeSetHash(
                dependency.Repository,
                selectedOnnxRepoPaths);

            if (!File.Exists(bindingMetadataAbsolutePath))
            {
                return Missing(
                    dependency,
                    DependencyReasonCode.ImportBindingMissing,
                    "Import binding metadata file is missing.",
                    revisionRefPath,
                    snapshotRoot,
                    rawMetadataPath,
                    rawMetadata,
                    runtimePreviewAssetPath,
                    bindingMetadataAssetPath);
            }

            if (!ProjectImportService.TryReadBindingMetadata(bindingMetadataAbsolutePath, out var bindingMetadata))
            {
                return Failed(
                    dependency,
                    DependencyReasonCode.ImportBindingInvalid,
                    "Import binding metadata is invalid.",
                    revisionRefPath,
                    snapshotRoot,
                    rawMetadataPath,
                    rawMetadata,
                    runtimePreviewAssetPath,
                    bindingMetadataAssetPath);
            }

            var expectedSharedMetadataPath = ProjectPaths.GetSharedImportMetadataFilePath(
                dependency.Repository,
                resolvedCommitHash,
                GetCurrentSentisVersion(),
                expectedRecipeSetHash);
            if (!IsBindingCurrent(bindingMetadata, dependency, rawMetadata, expectedSharedMetadataPath, expectedRecipeSetHash))
            {
                return Stale(
                    dependency,
                    DependencyReasonCode.ImportBindingStale,
                    BuildBindingMismatchMessage(dependency, rawMetadata, bindingMetadata, expectedSharedMetadataPath, expectedRecipeSetHash),
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
                    DependencyReasonCode.ImportBindingInvalid,
                    "Import binding metadata is invalid.",
                    revisionRefPath,
                    snapshotRoot,
                    rawMetadataPath,
                    rawMetadata,
                    runtimePreviewAssetPath,
                    bindingMetadataAssetPath);
            }

            var sharedMetadataAbsolutePath = ProjectPaths.ToProjectAbsolutePath(bindingMetadata.SharedImportMetadataPath);
            if (!File.Exists(sharedMetadataAbsolutePath))
            {
                return Missing(
                    dependency,
                    DependencyReasonCode.SharedImportMetadataMissing,
                    "Shared import metadata file is missing.",
                    revisionRefPath,
                    snapshotRoot,
                    rawMetadataPath,
                    rawMetadata,
                    runtimePreviewAssetPath,
                    bindingMetadataAssetPath,
                    BuildResolvedImportedMetadata(bindingMetadataAssetPath, bindingMetadata, null));
            }

            if (!ProjectImportService.TryReadSharedMetadata(sharedMetadataAbsolutePath, out var sharedMetadata))
            {
                return Failed(
                    dependency,
                    DependencyReasonCode.SharedImportMetadataInvalid,
                    "Shared import metadata is invalid.",
                    revisionRefPath,
                    snapshotRoot,
                    rawMetadataPath,
                    rawMetadata,
                    runtimePreviewAssetPath,
                    bindingMetadataAssetPath,
                    BuildResolvedImportedMetadata(bindingMetadataAssetPath, bindingMetadata, null));
            }

            if (!IsSharedCurrent(sharedMetadata, dependency, rawMetadata, snapshotRoot, expectedRecipeSetHash, selectedModelRepoPaths))
            {
                return Stale(
                    dependency,
                    DependencyReasonCode.SharedImportMetadataStale,
                    BuildSharedMismatchMessage(dependency, snapshotRoot, rawMetadataPath, rawMetadata, sharedMetadata, expectedRecipeSetHash),
                    revisionRefPath,
                    snapshotRoot,
                    rawMetadataPath,
                    rawMetadata,
                    runtimePreviewAssetPath,
                    bindingMetadataAssetPath,
                    BuildResolvedImportedMetadata(bindingMetadataAssetPath, bindingMetadata, sharedMetadata));
            }

            var importedMetadata = BuildResolvedImportedMetadata(bindingMetadataAssetPath, bindingMetadata, sharedMetadata);
            var runtimeFiles = importedMetadata.RuntimeFiles ?? Array.Empty<ImportedRuntimeFileMetadata>();
            if (runtimeFiles.Length == 0)
            {
                return Missing(
                    dependency,
                    DependencyReasonCode.RuntimeFilesMissing,
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
                if (string.IsNullOrWhiteSpace(runtimeFile.FilePath) || !File.Exists(runtimeFile.FilePath))
                {
                    return Missing(
                        dependency,
                        DependencyReasonCode.RuntimeFileMissing,
                        $"Runtime model is missing: {runtimeFile.RepoPath} ({runtimeFile.FilePath}).",
                        revisionRefPath,
                        snapshotRoot,
                        rawMetadataPath,
                        rawMetadata,
                        runtimePreviewAssetPath,
                        bindingMetadataAssetPath,
                        importedMetadata);
                }
            }

            runtimePreviewAssetPath = runtimeFiles.FirstOrDefault()?.FilePath;
            return new DependencyStatus(
                dependency,
                DependencyState.Ready,
                DependencyReasonCode.None,
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
            DiscoveredModelDependency dependency,
            string bindingMetadataAbsolutePath,
            string bindingMetadataAssetPath,
            DependencyReasonCode reasonCode,
            string maintainerMessage,
            out DependencyStatus status)
        {
            status = null;

            if (!ProjectImportService.TryReadBindingMetadata(bindingMetadataAbsolutePath, out var bindingMetadata))
            {
                return false;
            }

            if (!string.Equals(bindingMetadata.PackageId, dependency.PackageId, StringComparison.Ordinal) ||
                !string.Equals(bindingMetadata.ModelId, dependency.ModelId, StringComparison.Ordinal) ||
                !string.Equals(bindingMetadata.ManifestHash, dependency.ManifestHash, StringComparison.Ordinal) ||
                !string.Equals(bindingMetadata.Repository, dependency.Repository, StringComparison.Ordinal) ||
                !string.Equals(bindingMetadata.RequestedRevision, dependency.Revision, StringComparison.Ordinal) ||
                !SentisCompatibility.AreCompatible(bindingMetadata.SentisVersion, GetCurrentSentisVersion()) ||
                !string.Equals(bindingMetadata.DependencySelectionHash, dependency.DependencySelectionHash, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(bindingMetadata.RecipeSetHash) ||
                string.IsNullOrWhiteSpace(bindingMetadata.SharedImportMetadataPath))
            {
                return false;
            }

            var sharedMetadataAbsolutePath = ProjectPaths.ToProjectAbsolutePath(bindingMetadata.SharedImportMetadataPath);
            if (!ProjectImportService.TryReadSharedMetadata(sharedMetadataAbsolutePath, out var sharedMetadata))
            {
                return false;
            }

            if (!string.Equals(sharedMetadata.Repository, dependency.Repository, StringComparison.Ordinal) ||
                !string.Equals(sharedMetadata.ResolvedCommitHash, bindingMetadata.ResolvedCommitHash, StringComparison.Ordinal) ||
                !SentisCompatibility.AreCompatible(sharedMetadata.SentisVersion, GetCurrentSentisVersion()) ||
                !string.Equals(sharedMetadata.RecipeSetHash, bindingMetadata.RecipeSetHash, StringComparison.Ordinal))
            {
                return false;
            }

            var selectedModelRepoPaths = dependency.GetSelectedModelRepoPaths(
                (sharedMetadata.RepositoryFiles ?? Array.Empty<RepositoryFileMetadata>()).Select(file => file.RepoPath),
                sharedMetadata.GetImportedOrSkippedModelRepoPaths())
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            var runtimeRepoPaths = sharedMetadata.GetImportedOrSkippedModelRepoPaths();
            if (!runtimeRepoPaths.SequenceEqual(selectedModelRepoPaths, StringComparer.Ordinal))
            {
                return false;
            }

            var importedMetadata = BuildResolvedImportedMetadata(bindingMetadataAssetPath, bindingMetadata, sharedMetadata);
            var runtimeFiles = importedMetadata.RuntimeFiles ?? Array.Empty<ImportedRuntimeFileMetadata>();
            if (runtimeFiles.Length == 0)
            {
                return false;
            }

            if (!ProjectImportService.HasRuntimeModels(runtimeFiles))
            {
                return false;
            }

            var runtimePreviewAssetPath = runtimeFiles.FirstOrDefault()?.FilePath;
            status = new DependencyStatus(
                dependency,
                DependencyState.Ready,
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
            ImportBindingMetadata bindingMetadata,
            DiscoveredModelDependency dependency,
            RawCacheMetadata rawMetadata,
            string expectedSharedMetadataPath,
            string expectedRecipeSetHash)
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
                SentisCompatibility.AreCompatible(bindingMetadata.SentisVersion, GetCurrentSentisVersion()) &&
                string.Equals(bindingMetadata.DependencySelectionHash, dependency.DependencySelectionHash, StringComparison.Ordinal) &&
                string.Equals(bindingMetadata.RecipeSetHash, expectedRecipeSetHash, StringComparison.Ordinal) &&
                string.Equals(bindingMetadata.SharedImportMetadataPath, expectedSharedMetadataPath, StringComparison.Ordinal);
        }

        private static bool IsSharedCurrent(
            SharedImportMetadata sharedMetadata,
            DiscoveredModelDependency dependency,
            RawCacheMetadata rawMetadata,
            string snapshotRoot,
            string expectedRecipeSetHash,
            IReadOnlyList<string> selectedModelRepoPaths)
        {
            if (sharedMetadata == null)
            {
                return false;
            }

            if (!string.Equals(sharedMetadata.Repository, dependency.Repository, StringComparison.Ordinal) ||
                !string.Equals(sharedMetadata.ResolvedCommitHash, rawMetadata.ResolvedCommitHash, StringComparison.Ordinal) ||
                !string.Equals(sharedMetadata.RawSnapshotRootPath, snapshotRoot, StringComparison.Ordinal) ||
                !string.Equals(sharedMetadata.RawMetadataPath, CachePaths.GetSnapshotMetadataPath(dependency.Repository, rawMetadata.ResolvedCommitHash), StringComparison.Ordinal) ||
                !SentisCompatibility.AreCompatible(sharedMetadata.SentisVersion, GetCurrentSentisVersion()) ||
                !string.Equals(sharedMetadata.RecipeSetHash, expectedRecipeSetHash, StringComparison.Ordinal) ||
                !(sharedMetadata.RuntimeFiles ?? Array.Empty<ImportedRuntimeFileMetadata>()).All(file =>
                    file != null &&
                    !string.IsNullOrWhiteSpace(file.FilePath) &&
                    (!string.Equals(file.Format, "onnx", StringComparison.Ordinal) ||
                     (!string.IsNullOrWhiteSpace(file.RecipeHash) && !string.IsNullOrWhiteSpace(file.ConversionPipelineVersion)))))
            {
                return false;
            }

            var sharedRepositoryPaths = (sharedMetadata.RepositoryFiles ?? Array.Empty<RepositoryFileMetadata>())
                .Select(file => file.RepoPath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            var rawRepositoryPaths = (rawMetadata.RepositoryFiles ?? Array.Empty<RepositoryFileMetadata>())
                .Select(file => file.RepoPath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            if (!sharedRepositoryPaths.SequenceEqual(rawRepositoryPaths, StringComparer.Ordinal))
            {
                return false;
            }

            var runtimeRepoPaths = sharedMetadata.GetImportedOrSkippedModelRepoPaths();
            return runtimeRepoPaths.SequenceEqual(
                (selectedModelRepoPaths ?? Array.Empty<string>())
                .OrderBy(path => path, StringComparer.Ordinal),
                StringComparer.Ordinal);
        }

        private static ImportedModelMetadata BuildResolvedImportedMetadata(
            string bindingMetadataPath,
            ImportBindingMetadata bindingMetadata,
            SharedImportMetadata sharedMetadata)
        {
            return new ImportedModelMetadata
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
                RepositoryFiles = sharedMetadata?.RepositoryFiles ?? Array.Empty<RepositoryFileMetadata>(),
                RawCacheSizeInBytes = sharedMetadata?.RawCacheSizeInBytes,
                SentisVersion = sharedMetadata?.SentisVersion ?? bindingMetadata?.SentisVersion,
                UnityVersion = sharedMetadata?.UnityVersion,
                RecipeSetHash = sharedMetadata?.RecipeSetHash ?? bindingMetadata?.RecipeSetHash,
                DependencySelectionHash = bindingMetadata?.DependencySelectionHash,
                BindingMetadataPath = bindingMetadataPath,
                SharedImportMetadataPath = bindingMetadata?.SharedImportMetadataPath,
                Diagnostics = sharedMetadata?.Diagnostics ?? Array.Empty<ImportDiagnostic>(),
                RuntimeFiles = sharedMetadata?.RuntimeFiles ?? Array.Empty<ImportedRuntimeFileMetadata>(),
                SkippedRepoPaths = sharedMetadata?.SkippedRepoPaths ?? Array.Empty<string>(),
                ImportedAtUtc = sharedMetadata?.ImportedAtUtc
            };
        }

        private static string BuildBindingMismatchMessage(
            DiscoveredModelDependency dependency,
            RawCacheMetadata rawMetadata,
            ImportBindingMetadata bindingMetadata,
            string expectedSharedMetadataPath,
            string expectedRecipeSetHash)
        {
            var mismatches = new List<string>();

            AddMismatch(mismatches, "package id", bindingMetadata.PackageId, dependency.PackageId);
            AddMismatch(mismatches, "model id", bindingMetadata.ModelId, dependency.ModelId);
            AddMismatch(mismatches, "manifest hash", bindingMetadata.ManifestHash, dependency.ManifestHash);
            AddMismatch(mismatches, "repository", bindingMetadata.Repository, dependency.Repository);
            AddMismatch(mismatches, "requested revision", bindingMetadata.RequestedRevision, dependency.Revision);
            AddMismatch(mismatches, "resolved commit", bindingMetadata.ResolvedCommitHash, rawMetadata.ResolvedCommitHash);
            AddMismatch(mismatches, "Sentis format generation", SentisCompatibility.GetGeneration(bindingMetadata.SentisVersion), SentisCompatibility.GetGeneration(GetCurrentSentisVersion()));
            AddMismatch(mismatches, "selection hash", bindingMetadata.DependencySelectionHash, dependency.DependencySelectionHash);
            AddMismatch(mismatches, "recipe set hash", bindingMetadata.RecipeSetHash, expectedRecipeSetHash);
            AddMismatch(mismatches, "shared metadata path", bindingMetadata.SharedImportMetadataPath, expectedSharedMetadataPath);

            return mismatches.Count == 0
                ? "Import binding metadata differs from current dependency state."
                : $"Import binding metadata is stale: {string.Join("; ", mismatches)}.";
        }

        private static string BuildSharedMismatchMessage(
            DiscoveredModelDependency dependency,
            string snapshotRoot,
            string rawMetadataPath,
            RawCacheMetadata rawMetadata,
            SharedImportMetadata sharedMetadata,
            string expectedRecipeSetHash)
        {
            var mismatches = new List<string>();

            AddMismatch(mismatches, "repository", sharedMetadata.Repository, dependency.Repository);
            AddMismatch(mismatches, "resolved commit", sharedMetadata.ResolvedCommitHash, rawMetadata.ResolvedCommitHash);
            AddMismatch(mismatches, "raw snapshot root", sharedMetadata.RawSnapshotRootPath, snapshotRoot);
            AddMismatch(mismatches, "raw metadata path", sharedMetadata.RawMetadataPath, rawMetadataPath);
            AddMismatch(mismatches, "Sentis format generation", SentisCompatibility.GetGeneration(sharedMetadata.SentisVersion), SentisCompatibility.GetGeneration(GetCurrentSentisVersion()));
            AddMismatch(mismatches, "recipe set hash", sharedMetadata.RecipeSetHash, expectedRecipeSetHash);
            AddMismatch(
                mismatches,
                "repository inventory",
                string.Join("|", (sharedMetadata.RepositoryFiles ?? Array.Empty<RepositoryFileMetadata>()).Select(file => file.RepoPath).OrderBy(path => path, StringComparer.Ordinal)),
                string.Join("|", (rawMetadata.RepositoryFiles ?? Array.Empty<RepositoryFileMetadata>()).Select(file => file.RepoPath).OrderBy(path => path, StringComparer.Ordinal)));
            AddMismatch(
                mismatches,
                "runtime file set",
                string.Join("|", sharedMetadata.GetImportedOrSkippedModelRepoPaths()),
                string.Join("|", dependency.GetSelectedModelRepoPaths(
                        (rawMetadata.RepositoryFiles ?? Array.Empty<RepositoryFileMetadata>()).Select(file => file.RepoPath),
                        (rawMetadata.Files ?? Array.Empty<RawCacheFileMetadata>()).Select(file => file.RepoPath))
                    .OrderBy(path => path, StringComparer.Ordinal)));

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

        private static bool IsVisualScenarioDependency(DiscoveredModelDependency dependency)
        {
            var manifestPath = dependency?.ManifestPath;
            if (string.IsNullOrWhiteSpace(manifestPath))
            {
                return false;
            }

            var normalizedPath = manifestPath.Replace('\\', '/');
            return normalizedPath.Contains(VisualScenarioGeneratedManifestPathSegment, StringComparison.Ordinal);
        }

        private static DependencyStatus Missing(
            DiscoveredModelDependency dependency,
            DependencyReasonCode reasonCode,
            string maintainerMessage,
            string revisionRefPath,
            string rawFilePath,
            string rawMetadataPath,
            string sentisAssetPath,
            string bindingMetadataPath)
        {
            return Missing(dependency, reasonCode, maintainerMessage, revisionRefPath, rawFilePath, rawMetadataPath, null, sentisAssetPath, bindingMetadataPath);
        }

        private static DependencyStatus Missing(
            DiscoveredModelDependency dependency,
            DependencyReasonCode reasonCode,
            string maintainerMessage,
            string revisionRefPath,
            string rawFilePath,
            string rawMetadataPath,
            RawCacheMetadata rawMetadata,
            string sentisAssetPath,
            string bindingMetadataPath,
            ImportedModelMetadata importedMetadata = null)
        {
            return new DependencyStatus(
                dependency,
                DependencyState.Missing,
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

        private static DependencyStatus Stale(
            DiscoveredModelDependency dependency,
            DependencyReasonCode reasonCode,
            string maintainerMessage,
            string revisionRefPath,
            string rawFilePath,
            string rawMetadataPath,
            RawCacheMetadata rawMetadata,
            string sentisAssetPath,
            string bindingMetadataPath,
            ImportedModelMetadata importedMetadata = null)
        {
            return new DependencyStatus(
                dependency,
                DependencyState.Stale,
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

        private static DependencyStatus Failed(
            DiscoveredModelDependency dependency,
            DependencyReasonCode reasonCode,
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

        private static DependencyStatus Failed(
            DiscoveredModelDependency dependency,
            DependencyReasonCode reasonCode,
            string maintainerMessage,
            string revisionRefPath,
            string rawFilePath,
            string rawMetadataPath,
            RawCacheMetadata rawMetadata,
            string sentisAssetPath,
            string bindingMetadataPath,
            ImportedModelMetadata importedMetadata = null)
        {
            return new DependencyStatus(
                dependency,
                DependencyState.Failed,
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
