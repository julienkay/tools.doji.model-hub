using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Converts cached repository snapshots into shared runtime files under `StreamingAssets` and writes owner bindings under `Library`.
    /// </summary>
    internal sealed class ProjectImportService : IProjectImportService
    {
        private readonly IOnnxModelConverter _onnxModelConverter;
        private readonly IProjectModelRecipeSource _recipeSource;

        public ProjectImportService(
            IOnnxModelConverter onnxModelConverter = null,
            IProjectModelRecipeSource recipeSource = null)
        {
            _onnxModelConverter = onnxModelConverter ?? new EditorOnnxModelConverter();
            _recipeSource = recipeSource ?? new ProjectModelRecipeSource();
        }

        /// <summary>
        /// Returns generated runtime snapshot artifact if binding and shared import are current, otherwise reuses or converts cached ONNX files into `.sentis` runtime assets.
        /// </summary>
        public Task<ImportedModelArtifact> ImportAsync(
            DiscoveredModelDependency dependency,
            CachedSnapshot cachedSnapshot,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sentisVersion = GetSentisVersion();
            var resolvedCommitHash = cachedSnapshot.Metadata?.ResolvedCommitHash
                ?? throw new InvalidOperationException($"Cached snapshot for '{dependency.Repository}' is missing resolved commit hash.");
            var snapshotRootAssetPath = ProjectPaths.GetRuntimeSnapshotFolder(
                dependency.Repository,
                resolvedCommitHash);
            var bindingMetadataFilePath = ProjectPaths.GetImportBindingMetadataFilePath(
                dependency.PackageId,
                dependency.ModelId);
            var bindingMetadataAbsolutePath = ProjectPaths.ToProjectAbsolutePath(bindingMetadataFilePath);
            var repositoryFiles = (cachedSnapshot.Metadata?.RepositoryFiles ?? Array.Empty<RepositoryFileMetadata>())
                .Select(file => file.RepoPath)
                .ToArray();
            var fallbackCachedFiles = (cachedSnapshot.Metadata?.Files ?? Array.Empty<RawCacheFileMetadata>())
                .Select(file => file.RepoPath)
                .ToArray();
            var selectedOnnxRepoPaths = dependency.GetSelectedOnnxRepoPaths(repositoryFiles, fallbackCachedFiles).ToArray();
            if (selectedOnnxRepoPaths.Length == 0)
            {
                throw new InvalidOperationException(
                    $"Dependency '{dependency.ModelId}' does not select any ONNX file. Select the whole repository or at least one `.onnx` file.");
            }

            var recipeSetHash = _recipeSource.ComputeRecipeSetHash(
                dependency.Repository,
                selectedOnnxRepoPaths);
            var sharedMetadataFilePath = ProjectPaths.GetSharedImportMetadataFilePath(
                dependency.Repository,
                resolvedCommitHash,
                sentisVersion,
                recipeSetHash);
            var sharedMetadataAbsolutePath = ProjectPaths.ToProjectAbsolutePath(sharedMetadataFilePath);

            if (TryReadResolvedMetadata(bindingMetadataAbsolutePath, out var existingMetadata) &&
                IsUpToDate(existingMetadata, dependency, cachedSnapshot, snapshotRootAssetPath, sharedMetadataFilePath, recipeSetHash))
            {
                return Task.FromResult(new ImportedModelArtifact(
                    snapshotRootAssetPath,
                    bindingMetadataFilePath,
                    existingMetadata,
                    sharedMetadataFilePath));
            }

            if (TryReadSharedMetadata(sharedMetadataAbsolutePath, out var existingSharedMetadata) &&
                IsSharedUpToDate(existingSharedMetadata, dependency, cachedSnapshot, snapshotRootAssetPath, sentisVersion, recipeSetHash, selectedOnnxRepoPaths))
            {
                var reusedBindingMetadata = CreateBindingMetadata(
                    dependency,
                    cachedSnapshot,
                    sentisVersion,
                    sharedMetadataFilePath,
                    recipeSetHash);
                WriteBindingMetadata(bindingMetadataAbsolutePath, reusedBindingMetadata);

                var reusedResolvedMetadata = CreateResolvedMetadata(
                    bindingMetadataFilePath,
                    reusedBindingMetadata,
                    existingSharedMetadata);
                return Task.FromResult(new ImportedModelArtifact(
                    snapshotRootAssetPath,
                    bindingMetadataFilePath,
                    reusedResolvedMetadata,
                    sharedMetadataFilePath));
            }

            var runtimeFiles = new List<ImportedRuntimeFileMetadata>();
            var onnxFileCount = 0;
            var sentisFileCount = 0;
            var conversionFailures = new List<string>();
            var skippedRepoPaths = new List<string>();
            var diagnostics = new List<ImportDiagnostic>();
            foreach (var snapshotFile in cachedSnapshot.Metadata?.Files ?? Array.Empty<RawCacheFileMetadata>())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!selectedOnnxRepoPaths.Contains(snapshotFile.RepoPath, StringComparer.Ordinal))
                {
                    continue;
                }

                onnxFileCount++;
                var runtimeSentisAssetPath = ProjectPaths.GetRuntimeSentisAssetPath(
                    dependency.Repository,
                    resolvedCommitHash,
                    snapshotFile.RepoPath);
                var runtimeSentisAbsolutePath = ProjectPaths.ToAbsolutePath(runtimeSentisAssetPath);
                EnsureAssetFolderExists(Path.GetDirectoryName(runtimeSentisAssetPath)?.Replace('\\', '/')
                    ?? throw new InvalidOperationException("Missing runtime sentis output directory."));

                var recipe = _recipeSource.GetEffectiveRecipe(dependency.Repository, snapshotFile.RepoPath).Recipe;
                OnnxConversionResult conversionResult;
                try
                {
                    conversionResult = _onnxModelConverter.Convert(
                        new OnnxConversionRequest(snapshotFile.CachedFilePath, recipe));
                }
                catch (Exception exception)
                {
                    var failureMessage = $"Failed to convert ONNX file '{snapshotFile.RepoPath}' from repository '{dependency.Repository}'. {exception.Message}";
                    conversionFailures.Add(failureMessage);
                    skippedRepoPaths.Add(snapshotFile.RepoPath);
                    diagnostics.Add(new ImportDiagnostic
                    {
                        Severity = "warning",
                        Message = $"Partial ONNX conversion: skipped {snapshotFile.RepoPath} because {exception.Message}"
                    });
                    Debug.LogWarning(failureMessage);
                    continue;
                }

                ModelWriter.Save(runtimeSentisAbsolutePath, conversionResult.Model);
                sentisFileCount++;
                runtimeFiles.Add(new ImportedRuntimeFileMetadata
                {
                    RepoPath = snapshotFile.RepoPath,
                    Kind = "sentis",
                    AssetPath = runtimeSentisAssetPath,
                    RelativePath = ProjectPaths.ToStreamingAssetsRelativePath(runtimeSentisAssetPath),
                    SizeInBytes = new FileInfo(runtimeSentisAbsolutePath).Length,
                    RecipeHash = OnnxImportMetadataUtility.ComputeRecipeHash(conversionResult.AppliedRecipe),
                    ConversionPipelineVersion = conversionResult.ConversionPipelineVersion,
                    InputSignatureHash = OnnxImportMetadataUtility.ComputeInputSignatureHash(conversionResult.InputSignature)
                });

                foreach (var conversionDiagnostic in conversionResult.Diagnostics ?? Array.Empty<OnnxConversionDiagnostic>())
                {
                    if (string.IsNullOrWhiteSpace(conversionDiagnostic?.Message))
                    {
                        continue;
                    }

                    diagnostics.Add(new ImportDiagnostic
                    {
                        Severity = string.IsNullOrWhiteSpace(conversionDiagnostic.Severity) ? "warning" : conversionDiagnostic.Severity,
                        Message = conversionDiagnostic.Message
                    });
                }
            }

            if (onnxFileCount > 0 && sentisFileCount == 0)
            {
                throw new InvalidOperationException(
                    conversionFailures.Count > 0
                        ? conversionFailures[0]
                        : $"Failed to convert ONNX files from repository '{dependency.Repository}'.");
            }

            foreach (var runtimeFile in runtimeFiles)
            {
                AssetDatabase.ImportAsset(
                    runtimeFile.AssetPath,
                    ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            }

            var sharedMetadata = CreateSharedMetadata(
                dependency,
                cachedSnapshot,
                snapshotRootAssetPath,
                runtimeFiles,
                skippedRepoPaths,
                diagnostics,
                sentisVersion,
                recipeSetHash);
            WriteSharedMetadata(sharedMetadataAbsolutePath, sharedMetadata);

            var bindingMetadata = CreateBindingMetadata(
                dependency,
                cachedSnapshot,
                sentisVersion,
                sharedMetadataFilePath,
                recipeSetHash);
            WriteBindingMetadata(bindingMetadataAbsolutePath, bindingMetadata);
            PruneDeselectedRuntimeFiles(dependency.Repository, resolvedCommitHash);

            AssetDatabase.SaveAssets();

            var resolvedMetadata = CreateResolvedMetadata(
                bindingMetadataFilePath,
                bindingMetadata,
                sharedMetadata);
            return Task.FromResult(new ImportedModelArtifact(
                snapshotRootAssetPath,
                bindingMetadataFilePath,
                resolvedMetadata,
                sharedMetadataFilePath));
        }

        internal static bool TryReadMetadata(string metadataAbsolutePath, out ImportedModelMetadata metadata)
        {
            return TryReadResolvedMetadata(metadataAbsolutePath, out metadata);
        }

        internal static bool TryReadResolvedMetadata(string bindingMetadataAbsolutePath, out ImportedModelMetadata metadata)
        {
            metadata = null;
            if (!TryReadBindingMetadata(bindingMetadataAbsolutePath, out var bindingMetadata))
            {
                return false;
            }

            var sharedMetadataAbsolutePath = ProjectPaths.ToProjectAbsolutePath(bindingMetadata.SharedImportMetadataPath);
            if (!TryReadSharedMetadata(sharedMetadataAbsolutePath, out var sharedMetadata))
            {
                return false;
            }

            metadata = CreateResolvedMetadata(
                ProjectPaths.GetImportBindingMetadataFilePath(bindingMetadata.PackageId, bindingMetadata.ModelId),
                bindingMetadata,
                sharedMetadata);
            return metadata != null;
        }

        internal static bool TryReadBindingMetadata(string metadataAbsolutePath, out ImportBindingMetadata metadata)
        {
            metadata = null;
            if (!File.Exists(metadataAbsolutePath))
            {
                return false;
            }

            try
            {
                metadata = JsonConvert.DeserializeObject<ImportBindingMetadata>(File.ReadAllText(metadataAbsolutePath));
                return metadata != null;
            }
            catch
            {
                return false;
            }
        }

        internal static bool TryReadSharedMetadata(string metadataAbsolutePath, out SharedImportMetadata metadata)
        {
            metadata = null;
            if (!File.Exists(metadataAbsolutePath))
            {
                return false;
            }

            try
            {
                metadata = JsonConvert.DeserializeObject<SharedImportMetadata>(File.ReadAllText(metadataAbsolutePath));
                return metadata != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsUpToDate(
            ImportedModelMetadata metadata,
            DiscoveredModelDependency dependency,
            CachedSnapshot cachedSnapshot,
            string snapshotRootAssetPath,
            string sharedMetadataFilePath,
            string recipeSetHash)
        {
            if (metadata == null)
            {
                return false;
            }

            if (!string.Equals(metadata.PackageId, dependency.PackageId, StringComparison.Ordinal) ||
                !string.Equals(metadata.ModelId, dependency.ModelId, StringComparison.Ordinal) ||
                !string.Equals(metadata.ManifestHash, dependency.ManifestHash, StringComparison.Ordinal) ||
                !string.Equals(metadata.Repository, dependency.Repository, StringComparison.Ordinal) ||
                !string.Equals(metadata.RequestedRevision, dependency.Revision, StringComparison.Ordinal) ||
                !string.Equals(metadata.ResolvedCommitHash, cachedSnapshot.Metadata?.ResolvedCommitHash, StringComparison.Ordinal) ||
                !string.Equals(metadata.RawSnapshotRootPath, cachedSnapshot.SnapshotRootPath, StringComparison.Ordinal) ||
                !string.Equals(metadata.SentisVersion, GetSentisVersion(), StringComparison.Ordinal) ||
                !string.Equals(metadata.RecipeSetHash, recipeSetHash, StringComparison.Ordinal) ||
                !string.Equals(metadata.DependencySelectionHash, dependency.DependencySelectionHash, StringComparison.Ordinal) ||
                !string.Equals(metadata.SharedImportMetadataPath, sharedMetadataFilePath, StringComparison.Ordinal))
            {
                return false;
            }

            if (!(metadata.RepositoryFiles ?? Array.Empty<RepositoryFileMetadata>())
                    .Select(file => file.RepoPath)
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .SequenceEqual(
                        (cachedSnapshot.Metadata?.RepositoryFiles ?? Array.Empty<RepositoryFileMetadata>())
                        .Select(file => file.RepoPath)
                        .OrderBy(path => path, StringComparer.Ordinal),
                        StringComparer.Ordinal))
            {
                return false;
            }

            var importedFiles = metadata.RuntimeFiles ?? Array.Empty<ImportedRuntimeFileMetadata>();
            if (importedFiles.Length == 0)
            {
                return false;
            }

            if (!Directory.Exists(ProjectPaths.ToAbsolutePath(snapshotRootAssetPath)))
            {
                return false;
            }

            return importedFiles.All(file => File.Exists(ProjectPaths.ToAbsolutePath(file.AssetPath)));
        }

        private static bool IsSharedUpToDate(
            SharedImportMetadata metadata,
            DiscoveredModelDependency dependency,
            CachedSnapshot cachedSnapshot,
            string snapshotRootAssetPath,
            string sentisVersion,
            string recipeSetHash,
            IReadOnlyList<string> selectedOnnxRepoPaths)
        {
            if (metadata == null)
            {
                return false;
            }

            if (!string.Equals(metadata.Repository, dependency.Repository, StringComparison.Ordinal) ||
                !string.Equals(metadata.ResolvedCommitHash, cachedSnapshot.Metadata?.ResolvedCommitHash, StringComparison.Ordinal) ||
                !string.Equals(metadata.RawSnapshotRootPath, cachedSnapshot.SnapshotRootPath, StringComparison.Ordinal) ||
                !string.Equals(metadata.RawMetadataPath, cachedSnapshot.MetadataPath, StringComparison.Ordinal) ||
                !string.Equals(metadata.SentisVersion, sentisVersion, StringComparison.Ordinal) ||
                !string.Equals(metadata.RecipeSetHash, recipeSetHash, StringComparison.Ordinal) ||
                !string.Equals(metadata.RuntimeSnapshotAssetPath, snapshotRootAssetPath, StringComparison.Ordinal))
            {
                return false;
            }

            var runtimeFiles = metadata.RuntimeFiles ?? Array.Empty<ImportedRuntimeFileMetadata>();
            if (runtimeFiles.Length == 0)
            {
                return false;
            }

            if (!Directory.Exists(ProjectPaths.ToAbsolutePath(snapshotRootAssetPath)))
            {
                return false;
            }

            if (!(metadata.RepositoryFiles ?? Array.Empty<RepositoryFileMetadata>())
                    .Select(file => file.RepoPath)
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .SequenceEqual(
                        (cachedSnapshot.Metadata?.RepositoryFiles ?? Array.Empty<RepositoryFileMetadata>())
                        .Select(file => file.RepoPath)
                        .OrderBy(path => path, StringComparer.Ordinal),
                        StringComparer.Ordinal))
            {
                return false;
            }

            var runtimeRepoPaths = metadata.GetImportedOrSkippedOnnxRepoPaths();
            if (!runtimeRepoPaths.SequenceEqual(
                    (selectedOnnxRepoPaths ?? Array.Empty<string>())
                    .OrderBy(path => path, StringComparer.Ordinal),
                    StringComparer.Ordinal))
            {
                return false;
            }

            return runtimeFiles.All(file =>
                File.Exists(ProjectPaths.ToAbsolutePath(file.AssetPath)) &&
                !string.IsNullOrWhiteSpace(file.RecipeHash) &&
                !string.IsNullOrWhiteSpace(file.ConversionPipelineVersion));
        }

        private static SharedImportMetadata CreateSharedMetadata(
            DiscoveredModelDependency dependency,
            CachedSnapshot cachedSnapshot,
            string snapshotRootAssetPath,
            IReadOnlyList<ImportedRuntimeFileMetadata> runtimeFiles,
            IReadOnlyList<string> skippedRepoPaths,
            IReadOnlyList<ImportDiagnostic> diagnostics,
            string sentisVersion,
            string recipeSetHash)
        {
            return new SharedImportMetadata
            {
                Repository = dependency.Repository,
                ResolvedCommitHash = cachedSnapshot.Metadata?.ResolvedCommitHash,
                RequestedRevisionHint = dependency.Revision,
                RawSnapshotRootPath = cachedSnapshot.SnapshotRootPath,
                RawMetadataPath = cachedSnapshot.MetadataPath,
                RepositoryFiles = cachedSnapshot.Metadata?.RepositoryFiles ?? Array.Empty<RepositoryFileMetadata>(),
                RawCacheSizeInBytes = cachedSnapshot.Metadata?.Files?
                    .Select(file => file.SizeInBytes)
                    .Where(size => size.HasValue)
                    .Sum(size => size.Value),
                SentisVersion = sentisVersion,
                UnityVersion = Application.unityVersion,
                RecipeSetHash = recipeSetHash,
                RuntimeSnapshotAssetPath = snapshotRootAssetPath,
                Diagnostics = diagnostics?.ToArray() ?? Array.Empty<ImportDiagnostic>(),
                RuntimeFiles = runtimeFiles.ToArray(),
                SkippedRepoPaths = skippedRepoPaths?.ToArray() ?? Array.Empty<string>(),
                ImportedAtUtc = DateTime.UtcNow.ToString("O")
            };
        }

        private static ImportBindingMetadata CreateBindingMetadata(
            DiscoveredModelDependency dependency,
            CachedSnapshot cachedSnapshot,
            string sentisVersion,
            string sharedMetadataFilePath,
            string recipeSetHash)
        {
            var runtimeRegistryAssetPath = ProjectPaths.GetRuntimeRegistryAssetPath(dependency.PackageId);
            return new ImportBindingMetadata
            {
                PackageId = dependency.PackageId,
                PackageVersion = dependency.PackageVersion,
                ModelId = dependency.ModelId,
                ManifestHash = dependency.ManifestHash,
                Repository = dependency.Repository,
                RequestedRevision = dependency.Revision,
                ResolvedCommitHash = cachedSnapshot.Metadata?.ResolvedCommitHash,
                SentisVersion = sentisVersion,
                RecipeSetHash = recipeSetHash,
                DependencySelectionHash = dependency.DependencySelectionHash,
                SharedImportMetadataPath = sharedMetadataFilePath,
                RuntimeRegistryAssetPath = runtimeRegistryAssetPath,
                RuntimeRegistryRelativePath = ProjectPaths.ToStreamingAssetsRelativePath(runtimeRegistryAssetPath),
                BoundAtUtc = DateTime.UtcNow.ToString("O")
            };
        }

        private static ImportedModelMetadata CreateResolvedMetadata(
            string bindingMetadataFilePath,
            ImportBindingMetadata bindingMetadata,
            SharedImportMetadata sharedMetadata)
        {
            if (bindingMetadata == null || sharedMetadata == null)
            {
                return null;
            }

            return new ImportedModelMetadata
            {
                PackageId = bindingMetadata.PackageId,
                PackageVersion = bindingMetadata.PackageVersion,
                ModelId = bindingMetadata.ModelId,
                ManifestHash = bindingMetadata.ManifestHash,
                Repository = bindingMetadata.Repository,
                RequestedRevision = bindingMetadata.RequestedRevision,
                ResolvedCommitHash = sharedMetadata.ResolvedCommitHash,
                RawSnapshotRootPath = sharedMetadata.RawSnapshotRootPath,
                RawMetadataPath = sharedMetadata.RawMetadataPath,
                RepositoryFiles = sharedMetadata.RepositoryFiles ?? Array.Empty<RepositoryFileMetadata>(),
                RawCacheSizeInBytes = sharedMetadata.RawCacheSizeInBytes,
                SentisVersion = sharedMetadata.SentisVersion,
                UnityVersion = sharedMetadata.UnityVersion,
                RecipeSetHash = sharedMetadata.RecipeSetHash ?? bindingMetadata.RecipeSetHash,
                DependencySelectionHash = bindingMetadata.DependencySelectionHash,
                RuntimeRegistryAssetPath = bindingMetadata.RuntimeRegistryAssetPath,
                RuntimeRegistryRelativePath = bindingMetadata.RuntimeRegistryRelativePath,
                BindingMetadataPath = bindingMetadataFilePath,
                SharedImportMetadataPath = bindingMetadata.SharedImportMetadataPath,
                Diagnostics = sharedMetadata.Diagnostics ?? Array.Empty<ImportDiagnostic>(),
                RuntimeFiles = sharedMetadata.RuntimeFiles ?? Array.Empty<ImportedRuntimeFileMetadata>(),
                SkippedRepoPaths = sharedMetadata.SkippedRepoPaths ?? Array.Empty<string>(),
                ImportedAtUtc = sharedMetadata.ImportedAtUtc
            };
        }

        private static void WriteBindingMetadata(string metadataAbsolutePath, ImportBindingMetadata metadata)
        {
            WriteJson(metadataAbsolutePath, metadata);
        }

        private static void WriteSharedMetadata(string metadataAbsolutePath, SharedImportMetadata metadata)
        {
            WriteJson(metadataAbsolutePath, metadata);
        }

        private static void WriteJson<TDocument>(string absolutePath, TDocument document)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath) ?? throw new InvalidOperationException("Missing metadata directory."));

            var temporaryPath = absolutePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(document, Formatting.Indented));

            if (File.Exists(absolutePath))
            {
                File.Delete(absolutePath);
            }

            File.Move(temporaryPath, absolutePath);
        }

        private static void EnsureAssetFolderExists(string assetFolderPath)
        {
            if (AssetDatabase.IsValidFolder(assetFolderPath))
            {
                return;
            }

            var parts = assetFolderPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !string.Equals(parts[0], "Assets", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Expected Unity asset folder under Assets, got '{assetFolderPath}'.");
            }

            var currentFolder = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var nextFolder = $"{currentFolder}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(nextFolder))
                {
                    AssetDatabase.CreateFolder(currentFolder, parts[i]);
                }

                currentFolder = nextFolder;
            }
        }

        private static void PruneDeselectedRuntimeFiles(string repository, string resolvedCommitHash)
        {
            var runtimeSnapshotAssetPath = ProjectPaths.GetRuntimeSnapshotFolder(repository, resolvedCommitHash);
            var runtimeSnapshotAbsolutePath = ProjectPaths.ToAbsolutePath(runtimeSnapshotAssetPath);
            if (!Directory.Exists(runtimeSnapshotAbsolutePath))
            {
                return;
            }

            var referencedRuntimeAssetPaths = GetReferencedRuntimeAssetPaths(repository, resolvedCommitHash);
            foreach (var absoluteRuntimeFilePath in Directory.EnumerateFiles(runtimeSnapshotAbsolutePath, "*.sentis", SearchOption.AllDirectories))
            {
                var runtimeAssetPath = ToAssetPath(absoluteRuntimeFilePath);
                if (referencedRuntimeAssetPaths.Contains(runtimeAssetPath))
                {
                    continue;
                }

                AssetDatabase.DeleteAsset(runtimeAssetPath);
            }
        }

        private static HashSet<string> GetReferencedRuntimeAssetPaths(string repository, string resolvedCommitHash)
        {
            var referencedRuntimeAssetPaths = new HashSet<string>(StringComparer.Ordinal);
            var bindingsRootAbsolutePath = ProjectPaths.ToProjectAbsolutePath("Library/Doji/HfHub/imports/bindings");
            if (!Directory.Exists(bindingsRootAbsolutePath))
            {
                return referencedRuntimeAssetPaths;
            }

            foreach (var bindingMetadataAbsolutePath in Directory.EnumerateFiles(
                         bindingsRootAbsolutePath,
                         "hf-hub-binding.json",
                         SearchOption.AllDirectories))
            {
                if (!TryReadBindingMetadata(bindingMetadataAbsolutePath, out var bindingMetadata) ||
                    !string.Equals(bindingMetadata.Repository, repository, StringComparison.Ordinal) ||
                    !string.Equals(bindingMetadata.ResolvedCommitHash, resolvedCommitHash, StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(bindingMetadata.SharedImportMetadataPath))
                {
                    continue;
                }

                var sharedMetadataAbsolutePath = ProjectPaths.ToProjectAbsolutePath(bindingMetadata.SharedImportMetadataPath);
                if (!TryReadSharedMetadata(sharedMetadataAbsolutePath, out var sharedMetadata) ||
                    !string.Equals(sharedMetadata.Repository, repository, StringComparison.Ordinal) ||
                    !string.Equals(sharedMetadata.ResolvedCommitHash, resolvedCommitHash, StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var runtimeFile in sharedMetadata.RuntimeFiles ?? Array.Empty<ImportedRuntimeFileMetadata>())
                {
                    if (string.Equals(runtimeFile.Kind, "sentis", StringComparison.Ordinal) &&
                        !string.IsNullOrWhiteSpace(runtimeFile.AssetPath))
                    {
                        referencedRuntimeAssetPaths.Add(runtimeFile.AssetPath);
                    }
                }
            }

            return referencedRuntimeAssetPaths;
        }

        private static string ToAssetPath(string absolutePath)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var projectRelativePath = Path.GetRelativePath(projectRoot, absolutePath);
            return projectRelativePath.Replace('\\', '/');
        }

        private static string GetSentisVersion()
        {
            return UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ModelAsset).Assembly)?.version ?? "unknown";
        }
    }
}
