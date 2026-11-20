using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.InferenceEngine;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Prepares the runtime model of every selected model file and records them for this project under
    /// <c>Library</c>. A <c>.sentis</c> file is used as downloaded. An <c>.onnx</c> file is converted once into the
    /// shared converted folder (<see cref="ConvertedModelStore"/>), with this project's conversion settings applied.
    /// Nothing is written into <c>Assets</c>: builds take the runtime models from where they are.
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
        /// Converter id used in conversion keys: the Editor pipeline plus the installed Inference Engine version.
        /// </summary>
        internal static string GetEditorConverterId()
        {
            return $"{OnnxImportMetadataUtility.DefaultConversionPipelineVersion}@{SentisCompatibility.GetGeneration(GetSentisVersion())}";
        }

        /// <summary>
        /// Key of the converted model of one cached ONNX file with this project's conversion settings.
        /// </summary>
        internal static string GetConversionKey(
            string repository,
            RawCacheFileMetadata file,
            IProjectModelRecipeSource recipeSource,
            string converterId = null)
        {
            var recipe = recipeSource.GetEffectiveRecipe(repository, file.RepoPath).Recipe;
            return ConvertedModelStore.ComputeKey(
                SourceFileIdentity.GetEtag(file),
                converterId ?? GetEditorConverterId(),
                OnnxImportMetadataUtility.ComputeRecipeHash(recipe));
        }

        /// <summary>
        /// Selected ONNX files whose converted model is not in the shared converted store, so importing them means
        /// converting. Files that are not cached are left out: they need a download first.
        /// </summary>
        internal static string[] GetPendingConversions(
            string repository,
            IEnumerable<RawCacheFileMetadata> cachedFiles,
            IEnumerable<string> selectedOnnxRepoPaths,
            IProjectModelRecipeSource recipeSource)
        {
            var selected = new HashSet<string>(selectedOnnxRepoPaths ?? Array.Empty<string>(), StringComparer.Ordinal);
            if (selected.Count == 0)
            {
                return Array.Empty<string>();
            }

            var converterId = GetEditorConverterId();
            return (cachedFiles ?? Array.Empty<RawCacheFileMetadata>())
                .Where(file => file != null && selected.Contains(file.RepoPath))
                .Where(file => !ConvertedModelStore.TryGet(repository, GetConversionKey(repository, file, recipeSource, converterId), out _))
                .Select(file => file.RepoPath)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Space the converted models of <paramref name="pendingConversions"/> need in the converted store: about the
        /// size of each ONNX file and its external weights. Quantized models need less, so this is an upper bound.
        /// </summary>
        internal static DiskSpaceRequirement BuildConversionRequirement(
            IReadOnlyCollection<string> pendingConversions,
            IEnumerable<RawCacheFileMetadata> cachedFiles)
        {
            var files = (cachedFiles ?? Array.Empty<RawCacheFileMetadata>())
                .Where(file => !string.IsNullOrWhiteSpace(file?.RepoPath))
                .GroupBy(file => file.RepoPath, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().SizeInBytes.GetValueOrDefault(), StringComparer.Ordinal);
            var bytes = (pendingConversions ?? Array.Empty<string>())
                .SelectMany(path => new[] { path }.Concat(ModelFiles.GetCompanionFiles(path, files.Keys)))
                .Distinct(StringComparer.Ordinal)
                .Sum(path => files.TryGetValue(path, out var size) ? size : 0);
            return new DiskSpaceRequirement(CachePaths.GetConvertedRoot(), bytes, "converted models");
        }

        /// <summary>
        /// Returns the recorded import when it is current, otherwise prepares the runtime models of the selected files.
        /// With <paramref name="allowConversion"/> false, an import that would convert an ONNX file throws
        /// <see cref="ConversionRequiredException"/> before it writes anything.
        /// </summary>
        public Task<ImportedModelArtifact> ImportAsync(
            DiscoveredModelDependency dependency,
            CachedSnapshot cachedSnapshot,
            CancellationToken cancellationToken,
            bool allowConversion = true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sentisVersion = GetSentisVersion();
            var resolvedCommitHash = cachedSnapshot.Metadata?.ResolvedCommitHash
                ?? throw new InvalidOperationException($"The cache entry of '{dependency.Repository}' does not name its commit. Download it again.");
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
            var selectionProblem = dependency.GetSelectionProblem(
                repositoryFiles.Length > 0 ? repositoryFiles : null,
                fallbackCachedFiles);
            if (selectionProblem != null)
            {
                throw new InvalidOperationException(selectionProblem);
            }

            var selectedModelRepoPaths = dependency.GetSelectedModelRepoPaths(
                repositoryFiles.Length > 0 ? repositoryFiles : null,
                fallbackCachedFiles);
            var recipeSetHash = _recipeSource.ComputeRecipeSetHash(
                dependency.Repository,
                selectedModelRepoPaths.Where(path => ModelFiles.GetFormat(path) == ModelFileFormat.Onnx));
            var sharedMetadataFilePath = ProjectPaths.GetSharedImportMetadataFilePath(
                dependency.Repository,
                resolvedCommitHash,
                sentisVersion,
                recipeSetHash);
            var sharedMetadataAbsolutePath = ProjectPaths.ToProjectAbsolutePath(sharedMetadataFilePath);

            if (TryReadSharedMetadata(sharedMetadataAbsolutePath, out var existingSharedMetadata) &&
                DescribeStaleShared(existingSharedMetadata, cachedSnapshot.Metadata, cachedSnapshot.SnapshotRootPath, cachedSnapshot.MetadataPath, selectedModelRepoPaths) == null &&
                HasRuntimeModels(existingSharedMetadata.RuntimeFiles))
            {
                if (!TryReadBindingMetadata(bindingMetadataAbsolutePath, out var existingBindingMetadata) ||
                    DescribeStaleBinding(existingBindingMetadata, dependency, resolvedCommitHash, recipeSetHash) != null)
                {
                    existingBindingMetadata = CreateBindingMetadata(dependency, cachedSnapshot, sentisVersion, sharedMetadataFilePath, recipeSetHash);
                    WriteJson(bindingMetadataAbsolutePath, existingBindingMetadata);
                }

                return Task.FromResult(new ImportedModelArtifact(bindingMetadataFilePath, sharedMetadataFilePath, existingSharedMetadata));
            }

            var pendingConversions = GetPendingConversions(
                dependency.Repository,
                cachedSnapshot.Metadata?.Files,
                selectedModelRepoPaths.Where(path => ModelFiles.GetFormat(path) == ModelFileFormat.Onnx),
                _recipeSource);
            if (!allowConversion && pendingConversions.Length > 0)
            {
                throw new ConversionRequiredException(dependency.Repository, pendingConversions);
            }

            DiskSpaceCheck.EnsureFits(
                $"convert {dependency.Repository}",
                new[] { BuildConversionRequirement(pendingConversions, cachedSnapshot.Metadata?.Files) });

            var runtimeFiles = new List<ImportedRuntimeFileMetadata>();
            var onnxFileCount = 0;
            var conversionFailures = new List<string>();
            var skippedRepoPaths = new List<string>();
            var diagnostics = new List<ImportDiagnostic>();
            var converterId = GetEditorConverterId();
            foreach (var snapshotFile in cachedSnapshot.Metadata?.Files ?? Array.Empty<RawCacheFileMetadata>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!selectedModelRepoPaths.Contains(snapshotFile.RepoPath, StringComparer.Ordinal))
                {
                    continue;
                }

                var sourceSize = CacheFiles.GetLength(snapshotFile.CachedFilePath);
                if (ModelFiles.GetFormat(snapshotFile.RepoPath) == ModelFileFormat.Sentis)
                {
                    runtimeFiles.Add(new ImportedRuntimeFileMetadata
                    {
                        RepoPath = snapshotFile.RepoPath,
                        Kind = "sentis",
                        Format = "sentis",
                        FilePath = snapshotFile.CachedFilePath,
                        SourceEtag = SourceFileIdentity.GetEtag(snapshotFile),
                        SourceSizeInBytes = sourceSize,
                        SizeInBytes = sourceSize
                    });
                    continue;
                }

                onnxFileCount++;
                var recipe = _recipeSource.GetEffectiveRecipe(dependency.Repository, snapshotFile.RepoPath).Recipe;
                var recipeHash = OnnxImportMetadataUtility.ComputeRecipeHash(recipe);
                var sourceEtag = SourceFileIdentity.GetEtag(snapshotFile);
                var conversionKey = GetConversionKey(dependency.Repository, snapshotFile, _recipeSource, converterId);
                long? importedWeightBytes = null;
                long? convertedWeightBytes = null;
                if (!ConvertedModelStore.TryGet(dependency.Repository, conversionKey, out var convertedPath))
                {
                    try
                    {
                        var unsupported = OnnxModelInspector.Inspect(snapshotFile.CachedFilePath)?.DescribeUnsupportedOperators();
                        if (unsupported != null)
                        {
                            throw new InvalidOperationException(unsupported);
                        }

                        var conversionResult = _onnxModelConverter.Convert(new OnnxConversionRequest(snapshotFile.CachedFilePath, recipe));
                        var settingWarnings = new List<string>();
                        importedWeightBytes = OnnxConversionSettings.GetWeightBytes(conversionResult.Model);
                        var convertedModel = OnnxConversionSettings.Apply(conversionResult.Model, recipe, settingWarnings);
                        convertedWeightBytes = OnnxConversionSettings.GetWeightBytes(convertedModel);
                        foreach (var warning in settingWarnings)
                        {
                            diagnostics.Add(new ImportDiagnostic { Severity = "warning", Message = $"{snapshotFile.RepoPath}: {warning}" });
                        }

                        foreach (var conversionDiagnostic in conversionResult.Diagnostics ?? Array.Empty<OnnxConversionDiagnostic>())
                        {
                            if (!string.IsNullOrWhiteSpace(conversionDiagnostic?.Message))
                            {
                                diagnostics.Add(new ImportDiagnostic
                                {
                                    Severity = string.IsNullOrWhiteSpace(conversionDiagnostic.Severity) ? "warning" : conversionDiagnostic.Severity,
                                    Message = conversionDiagnostic.Message
                                });
                            }
                        }

                        convertedPath = ConvertedModelStore.Save(
                            dependency.Repository,
                            conversionKey,
                            convertedModel,
                            new ConvertedModelRecord
                            {
                                repository = dependency.Repository,
                                repoPath = snapshotFile.RepoPath,
                                sourceEtag = sourceEtag,
                                converterId = converterId,
                                formatGeneration = SentisCompatibility.GetGeneration(sentisVersion),
                                recipeHash = recipeHash,
                                importedWeightBytes = importedWeightBytes ?? 0,
                                convertedWeightBytes = convertedWeightBytes ?? 0
                            });
                    }
                    catch (Exception exception)
                    {
                        var failureMessage = $"Failed to convert ONNX file '{snapshotFile.RepoPath}' of model '{dependency.Repository}'. {exception.Message}";
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
                }
                else
                {
                    // Another project or an earlier run converted it: report the recorded effect again.
                    var record = ConvertedModelStore.TryReadRecord(dependency.Repository, conversionKey);
                    if (record != null)
                    {
                        importedWeightBytes = record.importedWeightBytes;
                        convertedWeightBytes = record.convertedWeightBytes;
                        var warning = OnnxConversionSettings.DescribeQuantizationEffect(
                            recipe.QuantizationMode,
                            record.importedWeightBytes,
                            record.convertedWeightBytes);
                        if (warning != null)
                        {
                            diagnostics.Add(new ImportDiagnostic { Severity = "warning", Message = $"{snapshotFile.RepoPath}: {warning}" });
                        }
                    }
                }

                runtimeFiles.Add(new ImportedRuntimeFileMetadata
                {
                    RepoPath = snapshotFile.RepoPath,
                    Kind = "sentis",
                    Format = "onnx",
                    FilePath = convertedPath,
                    SourceEtag = sourceEtag,
                    SourceSizeInBytes = sourceSize,
                    Companions = GetCompanions(cachedSnapshot, snapshotFile.RepoPath),
                    ConversionKey = conversionKey,
                    SizeInBytes = new FileInfo(convertedPath).Length,
                    RecipeHash = recipeHash,
                    ConversionPipelineVersion = converterId,
                    ImportedWeightBytes = importedWeightBytes,
                    ConvertedWeightBytes = convertedWeightBytes
                });
            }

            if (onnxFileCount > 0 && runtimeFiles.Count == 0)
            {
                throw new InvalidOperationException(
                    conversionFailures.Count > 0
                        ? conversionFailures[0]
                        : $"Failed to convert the ONNX files of model '{dependency.Repository}'.");
            }

            var sharedMetadata = CreateSharedMetadata(dependency, cachedSnapshot, runtimeFiles, skippedRepoPaths, diagnostics, sentisVersion, recipeSetHash);
            WriteJson(sharedMetadataAbsolutePath, sharedMetadata);

            var bindingMetadata = CreateBindingMetadata(dependency, cachedSnapshot, sentisVersion, sharedMetadataFilePath, recipeSetHash);
            WriteJson(bindingMetadataAbsolutePath, bindingMetadata);

            return Task.FromResult(new ImportedModelArtifact(bindingMetadataFilePath, sharedMetadataFilePath, sharedMetadata));
        }

        private static ImportedSourceFileMetadata[] GetCompanions(CachedSnapshot cachedSnapshot, string onnxRepoPath)
        {
            var files = cachedSnapshot.Metadata?.Files ?? Array.Empty<RawCacheFileMetadata>();
            var companionPaths = ModelFiles.GetCompanionFiles(onnxRepoPath, files.Select(file => file.RepoPath));
            return files
                .Where(file => companionPaths.Contains(file.RepoPath, StringComparer.Ordinal))
                .Select(file => new ImportedSourceFileMetadata
                {
                    RepoPath = file.RepoPath,
                    Etag = SourceFileIdentity.GetEtag(file),
                    SizeInBytes = CacheFiles.GetLength(file.CachedFilePath)
                })
                .OrderBy(file => file.RepoPath, StringComparer.Ordinal)
                .ToArray();
        }

        internal static bool TryReadBindingMetadata(string metadataAbsolutePath, out ImportBindingMetadata metadata)
        {
            return TryReadJson(metadataAbsolutePath, out metadata);
        }

        internal static bool TryReadSharedMetadata(string metadataAbsolutePath, out SharedImportMetadata metadata)
        {
            return TryReadJson(metadataAbsolutePath, out metadata);
        }

        private static bool TryReadJson<TDocument>(string absolutePath, out TDocument document)
            where TDocument : class
        {
            document = null;
            if (!File.Exists(absolutePath))
            {
                return false;
            }

            try
            {
                document = JsonConvert.DeserializeObject<TDocument>(File.ReadAllText(absolutePath));
                return document != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Whether every runtime model of an import record is on disk.
        /// </summary>
        internal static bool HasRuntimeModels(IEnumerable<ImportedRuntimeFileMetadata> runtimeFiles)
        {
            var files = (runtimeFiles ?? Array.Empty<ImportedRuntimeFileMetadata>()).ToArray();
            return files.Length > 0 && files.All(file => !string.IsNullOrWhiteSpace(file?.FilePath) && File.Exists(file.FilePath));
        }

        /// <summary>
        /// Says why a binding no longer fits the dependency, or returns null when it is current. The shared record path
        /// holds the repository, the resolved commit, the Inference Engine format generation and the recipe set, so a
        /// binding is current when it points at the expected record and has the same requested revision and selection.
        /// The recorded commit, Sentis version and recipe set only explain a path that differs.
        /// </summary>
        internal static string DescribeStaleBinding(
            ImportBindingMetadata binding,
            DiscoveredModelDependency dependency,
            string resolvedCommitHash,
            string recipeSetHash)
        {
            var sentisVersion = GetSentisVersion();
            var expectedSharedMetadataPath = ProjectPaths.GetSharedImportMetadataFilePath(
                dependency.Repository,
                resolvedCommitHash,
                sentisVersion,
                recipeSetHash);
            var mismatches = new List<string>();
            AddMismatch(mismatches, "requested revision", binding.RequestedRevision, dependency.Revision);
            AddMismatch(mismatches, "selection hash", binding.DependencySelectionHash, dependency.DependencySelectionHash);
            if (!string.Equals(binding.SharedImportMetadataPath, expectedSharedMetadataPath, StringComparison.Ordinal))
            {
                var count = mismatches.Count;
                AddMismatch(mismatches, "resolved commit", binding.ResolvedCommitHash, resolvedCommitHash);
                AddMismatch(mismatches, "Sentis format generation", SentisCompatibility.GetGeneration(binding.SentisVersion), SentisCompatibility.GetGeneration(sentisVersion));
                AddMismatch(mismatches, "recipe set hash", binding.RecipeSetHash, recipeSetHash);
                if (mismatches.Count == count)
                {
                    AddMismatch(mismatches, "shared metadata path", binding.SharedImportMetadataPath, expectedSharedMetadataPath);
                }
            }

            return mismatches.Count == 0 ? null : $"The project's setup is out of date: {string.Join("; ", mismatches)}.";
        }

        /// <summary>
        /// Says why a shared record no longer fits the cached snapshot and the selected model files, or returns null when
        /// it is current. Its path already holds the repository, commit, format generation and recipe set.
        /// </summary>
        internal static string DescribeStaleShared(
            SharedImportMetadata shared,
            RawCacheMetadata rawMetadata,
            string snapshotRoot,
            string rawMetadataPath,
            IEnumerable<string> selectedModelRepoPaths)
        {
            var mismatches = new List<string>();
            AddMismatch(mismatches, "raw snapshot root", shared.RawSnapshotRootPath, snapshotRoot);
            AddMismatch(mismatches, "raw metadata path", shared.RawMetadataPath, rawMetadataPath);
            AddMismatch(mismatches, "repository inventory", JoinRepoPaths(shared.RepositoryFiles), JoinRepoPaths(rawMetadata?.RepositoryFiles));
            AddMismatch(
                mismatches,
                "runtime file set",
                string.Join("|", shared.GetImportedOrSkippedModelRepoPaths()),
                string.Join("|", (selectedModelRepoPaths ?? Array.Empty<string>()).Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal)));
            if ((shared.RuntimeFiles ?? Array.Empty<ImportedRuntimeFileMetadata>()).Any(file =>
                    file == null ||
                    string.IsNullOrWhiteSpace(file.FilePath) ||
                    (string.Equals(file.Format, "onnx", StringComparison.Ordinal) &&
                     (string.IsNullOrWhiteSpace(file.RecipeHash) || string.IsNullOrWhiteSpace(file.ConversionPipelineVersion)))))
            {
                mismatches.Add("a runtime file record is incomplete");
            }

            return mismatches.Count == 0 ? null : $"The converted model is out of date: {string.Join("; ", mismatches)}.";
        }

        private static string JoinRepoPaths(IEnumerable<RepositoryFileMetadata> files)
        {
            return string.Join("|", (files ?? Array.Empty<RepositoryFileMetadata>())
                .Select(file => file.RepoPath)
                .OrderBy(path => path, StringComparer.Ordinal));
        }

        private static void AddMismatch(List<string> mismatches, string fieldName, string actualValue, string expectedValue)
        {
            if (!string.Equals(actualValue, expectedValue, StringComparison.Ordinal))
            {
                mismatches.Add($"{fieldName} expected `{expectedValue}` but found `{actualValue}`");
            }
        }

        private static SharedImportMetadata CreateSharedMetadata(
            DiscoveredModelDependency dependency,
            CachedSnapshot cachedSnapshot,
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
            return new ImportBindingMetadata
            {
                PackageId = dependency.PackageId,
                PackageVersion = dependency.PackageVersion,
                ModelId = dependency.ModelId,
                Repository = dependency.Repository,
                RequestedRevision = dependency.Revision,
                ResolvedCommitHash = cachedSnapshot.Metadata?.ResolvedCommitHash,
                SentisVersion = sentisVersion,
                RecipeSetHash = recipeSetHash,
                DependencySelectionHash = dependency.DependencySelectionHash,
                SharedImportMetadataPath = sharedMetadataFilePath,
                BoundAtUtc = DateTime.UtcNow.ToString("O")
            };
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

        internal static string GetSentisVersion()
        {
            return UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ModelAsset).Assembly)?.version ?? "unknown";
        }
    }
}
