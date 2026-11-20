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

            if (dependency.IsNotEnabled)
            {
                return NotEnabled(dependency, revisionRefPath, bindingMetadataAssetPath);
            }

            if (!IsVisualScenarioDependency(dependency) &&
                RepositoryValidationCacheService.TryGetCachedRepositoryExists(dependency.Repository, out var repositoryExists) &&
                !repositoryExists)
            {
                return Failed(
                    dependency,
                    DependencyReasonCode.InvalidRepository,
                    "Model was not found on Hugging Face.",
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
                    "Every model file is optional, and none is turned on. Turn one on in the Files tab.",
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
                    "Set up in this project. Its downloaded files were removed from the cache on this machine.",
                    out var importedOnlyStatus))
                {
                    return importedOnlyStatus;
                }

                return Missing(
                    dependency,
                    DependencyReasonCode.RawCacheRevisionRefMissing,
                    "Not downloaded on this machine.",
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
                    $"Could not read which commit the cache holds: {exception.Message}",
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
                    "Set up in this project. Its downloaded files were removed from the cache on this machine.",
                    out var importedOnlyStatus))
                {
                    return importedOnlyStatus;
                }

                return Missing(
                    dependency,
                    DependencyReasonCode.RawSnapshotRootMissing,
                    "The downloaded files are missing from the cache.",
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
                    "AI Model Hub does not have this model's file list, so it cannot check that every file is downloaded. Update to check.",
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
                    $"Not downloaded yet: {string.Join(", ", missingSelectedFiles)}.",
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

            // From here on every selected file is cached; what is left is this project's import. It is cheap unless
            // ONNX files have no converted model in the shared store yet.
            ImportBindingMetadata knownBinding = null;
            SharedImportMetadata knownShared = null;
            DependencyStatus Pending(DependencyStatus status)
            {
                var pending = ProjectImportService.GetPendingConversions(
                    dependency.Repository,
                    rawMetadata.Files,
                    selectedOnnxRepoPaths,
                    _recipeSource);
                return pending.Length == 0
                    ? status
                    : WithPendingConversions(status, pending, GetConversionCause(dependency, knownBinding, knownShared, pending));
            }

            if (!File.Exists(bindingMetadataAbsolutePath))
            {
                return Pending(Missing(
                    dependency,
                    DependencyReasonCode.ImportBindingMissing,
                    "Not set up in this project yet.",
                    revisionRefPath,
                    snapshotRoot,
                    rawMetadataPath,
                    rawMetadata,
                    runtimePreviewAssetPath,
                    bindingMetadataAssetPath));
            }

            if (!ProjectImportService.TryReadBindingMetadata(bindingMetadataAbsolutePath, out var bindingMetadata))
            {
                // The record is a hint: an unreadable one is rewritten like a missing one.
                return Pending(Missing(
                    dependency,
                    DependencyReasonCode.ImportBindingInvalid,
                    "The project's setup record is damaged. Set the model up again.",
                    revisionRefPath,
                    snapshotRoot,
                    rawMetadataPath,
                    rawMetadata,
                    runtimePreviewAssetPath,
                    bindingMetadataAssetPath));
            }

            knownBinding = bindingMetadata;

            var staleBinding = ProjectImportService.DescribeStaleBinding(bindingMetadata, dependency, resolvedCommitHash, expectedRecipeSetHash);
            if (staleBinding != null)
            {
                return Pending(Stale(
                    dependency,
                    DependencyReasonCode.ImportBindingStale,
                    staleBinding,
                    revisionRefPath,
                    snapshotRoot,
                    rawMetadataPath,
                    rawMetadata,
                    runtimePreviewAssetPath,
                    bindingMetadataAssetPath,
                    bindingMetadata));
            }

            var sharedMetadataAbsolutePath = ProjectPaths.ToProjectAbsolutePath(bindingMetadata.SharedImportMetadataPath);
            if (!File.Exists(sharedMetadataAbsolutePath))
            {
                return Pending(Missing(
                    dependency,
                    DependencyReasonCode.SharedImportMetadataMissing,
                    "The converted model is missing. Convert it again.",
                    revisionRefPath,
                    snapshotRoot,
                    rawMetadataPath,
                    rawMetadata,
                    runtimePreviewAssetPath,
                    bindingMetadataAssetPath,
                    bindingMetadata));
            }

            if (!ProjectImportService.TryReadSharedMetadata(sharedMetadataAbsolutePath, out var sharedMetadata))
            {
                return Pending(Missing(
                    dependency,
                    DependencyReasonCode.SharedImportMetadataInvalid,
                    "The converted model's record is damaged. Convert it again.",
                    revisionRefPath,
                    snapshotRoot,
                    rawMetadataPath,
                    rawMetadata,
                    runtimePreviewAssetPath,
                    bindingMetadataAssetPath,
                    bindingMetadata));
            }

            knownShared = sharedMetadata;
            var staleShared = ProjectImportService.DescribeStaleShared(sharedMetadata, rawMetadata, snapshotRoot, rawMetadataPath, selectedModelRepoPaths);
            if (staleShared != null)
            {
                return Pending(Stale(
                    dependency,
                    DependencyReasonCode.SharedImportMetadataStale,
                    staleShared,
                    revisionRefPath,
                    snapshotRoot,
                    rawMetadataPath,
                    rawMetadata,
                    runtimePreviewAssetPath,
                    bindingMetadataAssetPath,
                    bindingMetadata,
                    sharedMetadata));
            }

            var runtimeFiles = sharedMetadata.RuntimeFiles ?? Array.Empty<ImportedRuntimeFileMetadata>();
            if (runtimeFiles.Length == 0)
            {
                return Pending(Missing(
                    dependency,
                    DependencyReasonCode.RuntimeFilesMissing,
                    "The converted model files are missing. Set the model up again.",
                    revisionRefPath,
                    snapshotRoot,
                    rawMetadataPath,
                    rawMetadata,
                    runtimePreviewAssetPath,
                    bindingMetadataAssetPath,
                    bindingMetadata,
                    sharedMetadata));
            }

            foreach (var runtimeFile in runtimeFiles)
            {
                if (string.IsNullOrWhiteSpace(runtimeFile.FilePath) || !File.Exists(runtimeFile.FilePath))
                {
                    return Pending(Missing(
                        dependency,
                        DependencyReasonCode.RuntimeFileMissing,
                        $"A converted model file is missing: {runtimeFile.RepoPath} ({runtimeFile.FilePath}).",
                        revisionRefPath,
                        snapshotRoot,
                        rawMetadataPath,
                        rawMetadata,
                        runtimePreviewAssetPath,
                        bindingMetadataAssetPath,
                        bindingMetadata,
                        sharedMetadata));
                }
            }

            runtimePreviewAssetPath = runtimeFiles.FirstOrDefault()?.FilePath;
            return new DependencyStatus(
                dependency,
                DependencyState.Ready,
                DependencyReasonCode.None,
                "Downloaded and set up.",
                revisionRefPath,
                snapshotRoot,
                rawMetadataPath,
                rawMetadata,
                runtimePreviewAssetPath,
                bindingMetadataAssetPath,
                bindingMetadata,
                sharedMetadata);
        }

        private static DependencyStatus WithPendingConversions(
            DependencyStatus status,
            IReadOnlyList<string> pendingConversions,
            ConversionCause cause)
        {
            return new DependencyStatus(
                status.Dependency,
                status.State,
                status.ReasonCode,
                status.MaintainerMessage,
                status.RevisionRefPath,
                status.RawFilePath,
                status.RawMetadataPath,
                status.RawMetadata,
                status.SentisAssetPath,
                status.BindingMetadataPath,
                status.Binding,
                status.Shared,
                pendingConversions,
                cause);
        }

        /// <summary>
        /// Explains why files need conversion: a new Inference Engine format, changed conversion settings, or a first
        /// conversion. Reads the shared record through the binding when status stopped before reading it.
        /// </summary>
        private ConversionCause GetConversionCause(
            DiscoveredModelDependency dependency,
            ImportBindingMetadata binding,
            SharedImportMetadata shared,
            IReadOnlyList<string> pendingConversions)
        {
            if (shared == null &&
                !string.IsNullOrWhiteSpace(binding?.SharedImportMetadataPath) &&
                ProjectImportService.TryReadSharedMetadata(ProjectPaths.ToProjectAbsolutePath(binding.SharedImportMetadataPath), out var readShared))
            {
                shared = readShared;
            }

            var currentVersion = ProjectImportService.GetSentisVersion();
            var recordedVersion = shared?.SentisVersion ?? binding?.SentisVersion;
            if (!string.IsNullOrWhiteSpace(recordedVersion) && !SentisCompatibility.AreCompatible(recordedVersion, currentVersion))
            {
                return ConversionCause.FormatChanged;
            }

            var settingsChanged = pendingConversions.Any(repoPath =>
                (shared?.RuntimeFiles ?? Array.Empty<ImportedRuntimeFileMetadata>()).Any(file =>
                    string.Equals(file?.RepoPath, repoPath, StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(file.RecipeHash) &&
                    !string.Equals(
                        file.RecipeHash,
                        OnnxImportMetadataUtility.ComputeRecipeHash(_recipeSource.GetEffectiveRecipe(dependency.Repository, repoPath).Recipe),
                        StringComparison.Ordinal)));
            return settingsChanged ? ConversionCause.SettingsChanged : ConversionCause.FirstConversion;
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

            // Without the raw cache the commit and recipe set are known only from the binding itself; the check still
            // catches another revision, selection, repository, or format generation.
            if (string.IsNullOrWhiteSpace(bindingMetadata.ResolvedCommitHash) ||
                ProjectImportService.DescribeStaleBinding(bindingMetadata, dependency, bindingMetadata.ResolvedCommitHash, bindingMetadata.RecipeSetHash) != null)
            {
                return false;
            }

            var sharedMetadataAbsolutePath = ProjectPaths.ToProjectAbsolutePath(bindingMetadata.SharedImportMetadataPath);
            if (!ProjectImportService.TryReadSharedMetadata(sharedMetadataAbsolutePath, out var sharedMetadata))
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

            var runtimeFiles = sharedMetadata.RuntimeFiles ?? Array.Empty<ImportedRuntimeFileMetadata>();
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
                bindingMetadata,
                sharedMetadata);
            return true;
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
            ImportBindingMetadata bindingMetadata = null,
            SharedImportMetadata sharedMetadata = null)
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
                bindingMetadata,
                sharedMetadata);
        }

        /// <summary>
        /// Status of an optional model that the project did not enable. It reads the download cache only to show the
        /// model's size and license when another project already downloaded it.
        /// </summary>
        private DependencyStatus NotEnabled(
            DiscoveredModelDependency dependency,
            string revisionRefPath,
            string bindingMetadataPath)
        {
            RawCacheMetadata rawMetadata = null;
            try
            {
                if (File.Exists(revisionRefPath))
                {
                    var commit = File.ReadAllText(revisionRefPath).Trim();
                    if (!string.IsNullOrWhiteSpace(commit))
                    {
                        rawMetadata = _rawCacheService.ReadSnapshot(dependency.Repository, dependency.Revision, commit);
                    }
                }
            }
            catch (Exception)
            {
                rawMetadata = null;
            }

            return new DependencyStatus(
                dependency,
                DependencyState.NotEnabled,
                DependencyReasonCode.NotEnabled,
                $"Optional model. This project does not use it yet, so AI Model Hub does not set it up or bundle it. " +
                $"Enable it in Window > AI Model Hub, or run model_hub_set_model --repo {dependency.Repository} --enabled true.",
                revisionRefPath,
                null,
                null,
                rawMetadata,
                null,
                bindingMetadataPath,
                null,
                null);
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
            ImportBindingMetadata bindingMetadata = null,
            SharedImportMetadata sharedMetadata = null)
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
                bindingMetadata,
                sharedMetadata);
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
            ImportBindingMetadata bindingMetadata = null,
            SharedImportMetadata sharedMetadata = null)
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
                bindingMetadata,
                sharedMetadata);
        }
    }
}
