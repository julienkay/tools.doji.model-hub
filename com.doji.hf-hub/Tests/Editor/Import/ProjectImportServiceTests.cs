using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Unity.InferenceEngine;
using UnityEditor;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers repo-snapshot import behavior using a local ONNX sample.
    /// </summary>
    internal sealed class ProjectImportServiceTests
    {
        private ProjectImportService _service;

        [SetUp]
        public void SetUp()
        {
            _service = new ProjectImportService();
            CleanupProjectArtifacts();
            CleanupRecipeOverrideAsset();
        }

        [TearDown]
        public void TearDown()
        {
            CleanupProjectArtifacts();
            CleanupRecipeOverrideAsset();
        }

        /// <summary>
        /// Verifies import converts ONNX files into runtime `.sentis` assets and writes repo-level metadata.
        /// </summary>
        [Test]
        public async Task ImportAsync_CreatesGeneratedRuntimeSnapshotAndMetadata()
        {
            var dependency = CreateDependency();
            var cachedSnapshot = CreateCachedSnapshot();

            var artifact = await _service.ImportAsync(dependency, cachedSnapshot, CancellationToken.None);

            Assert.That(artifact.SnapshotRootAssetPath, Is.EqualTo("Assets/StreamingAssets/hf-hub/doji/test-model"));
            Assert.That(File.Exists(ProjectPaths.ToProjectAbsolutePath(artifact.BindingMetadataFilePath)), Is.True);
            Assert.That(File.Exists(ProjectPaths.ToProjectAbsolutePath(artifact.SharedMetadataFilePath)), Is.True);
            Assert.That(artifact.Metadata.PackageId, Is.EqualTo("com.doji.test"));
            Assert.That(artifact.Metadata.ModelId, Is.EqualTo("doji/test-model"));
            Assert.That(artifact.Metadata.ResolvedCommitHash, Is.EqualTo("commit123"));
            Assert.That(artifact.Metadata.BindingMetadataPath, Is.EqualTo(ProjectPaths.GetImportBindingMetadataFilePath("com.doji.test", "doji/test-model")));
            var expectedRecipeSetHash = OnnxImportMetadataUtility.ComputeRecipeSetHash(cachedSnapshot.Metadata.Files);
            Assert.That(artifact.Metadata.SharedImportMetadataPath, Is.EqualTo(ProjectPaths.GetSharedImportMetadataFilePath("doji/test-model", "commit123", StatusService.GetCurrentSentisVersion(), expectedRecipeSetHash)));
            Assert.That(artifact.RuntimeRegistryAssetPath, Is.EqualTo("Assets/StreamingAssets/hf-hub/registries/com.doji.test/hf-hub-runtime.json"));
            Assert.That(artifact.RuntimeFiles.Count, Is.EqualTo(1));
            Assert.That(artifact.Metadata.RecipeSetHash, Is.EqualTo(expectedRecipeSetHash));

            var sentisFile = artifact.RuntimeFiles.Single(file => file.Kind == "sentis");
            Assert.That(sentisFile.AssetPath, Is.EqualTo("Assets/StreamingAssets/hf-hub/doji/test-model/SingleInputSingleOuput.sentis"));
            Assert.That(File.Exists(ProjectPaths.ToAbsolutePath(sentisFile.AssetPath)), Is.True);
            Assert.That(sentisFile.SizeInBytes, Is.GreaterThan(0));
            Assert.That(sentisFile.RecipeHash, Is.EqualTo(OnnxImportMetadataUtility.ComputeRecipeHash(new OnnxConversionRecipe())));
            Assert.That(sentisFile.ConversionPipelineVersion, Is.EqualTo(OnnxImportMetadataUtility.DefaultConversionPipelineVersion));
            Assert.That(ModelLoader.Load(ProjectPaths.ToAbsolutePath(sentisFile.AssetPath)), Is.Not.Null);
        }

        /// <summary>
        /// Verifies import short-circuits when repo metadata and generated files already match.
        /// </summary>
        [Test]
        public async Task ImportAsync_ReusesExistingGeneratedArtifactWhenMetadataMatches()
        {
            var dependency = CreateDependency();
            var cachedSnapshot = CreateCachedSnapshot();

            var firstArtifact = await _service.ImportAsync(dependency, cachedSnapshot, CancellationToken.None);
            var firstTimestamp = firstArtifact.Metadata.ImportedAtUtc;

            var secondArtifact = await _service.ImportAsync(dependency, cachedSnapshot, CancellationToken.None);

            Assert.That(secondArtifact.SnapshotRootAssetPath, Is.EqualTo(firstArtifact.SnapshotRootAssetPath));
            Assert.That(secondArtifact.BindingMetadataFilePath, Is.EqualTo(firstArtifact.BindingMetadataFilePath));
            Assert.That(secondArtifact.SharedMetadataFilePath, Is.EqualTo(firstArtifact.SharedMetadataFilePath));
            Assert.That(secondArtifact.Metadata.ImportedAtUtc, Is.EqualTo(firstTimestamp));
        }

        /// <summary>
        /// Verifies second owner reuses shared import metadata and runtime files instead of reconverting same repo snapshot.
        /// </summary>
        [Test]
        public async Task ImportAsync_ReusesSharedImportAcrossOwnersWithoutReconverting()
        {
            var converter = new CountingOnnxConverter();
            var service = new ProjectImportService(converter);
            var firstDependency = CreateDependency();
            var secondDependency = CreateDependency(packageId: "com.doji.other", manifestHash: "other-manifest-hash");
            var cachedSnapshot = CreateCachedSnapshot();

            var firstArtifact = await service.ImportAsync(firstDependency, cachedSnapshot, CancellationToken.None);
            var secondArtifact = await service.ImportAsync(secondDependency, cachedSnapshot, CancellationToken.None);

            Assert.That(converter.CallCount, Is.EqualTo(1));
            Assert.That(firstArtifact.SharedMetadataFilePath, Is.EqualTo(secondArtifact.SharedMetadataFilePath));
            Assert.That(firstArtifact.BindingMetadataFilePath, Is.Not.EqualTo(secondArtifact.BindingMetadataFilePath));
            Assert.That(secondArtifact.Metadata.PackageId, Is.EqualTo("com.doji.other"));
            Assert.That(secondArtifact.Metadata.ManifestHash, Is.EqualTo("other-manifest-hash"));
            Assert.That(secondArtifact.Metadata.SharedImportMetadataPath, Is.EqualTo(firstArtifact.Metadata.SharedImportMetadataPath));
        }

        /// <summary>
        /// Verifies invalid shared import metadata triggers fresh conversion on next import.
        /// </summary>
        [Test]
        public async Task ImportAsync_ReconvertsWhenSharedMetadataJsonIsInvalid()
        {
            var converter = new CountingOnnxConverter();
            var service = new ProjectImportService(converter);
            var dependency = CreateDependency();
            var cachedSnapshot = CreateCachedSnapshot();

            var firstArtifact = await service.ImportAsync(dependency, cachedSnapshot, CancellationToken.None);
            File.WriteAllText(ProjectPaths.ToProjectAbsolutePath(firstArtifact.SharedMetadataFilePath), "{ invalid json");

            var secondArtifact = await service.ImportAsync(dependency, cachedSnapshot, CancellationToken.None);

            Assert.That(converter.CallCount, Is.EqualTo(2));
            Assert.That(secondArtifact.SharedMetadataFilePath, Is.EqualTo(firstArtifact.SharedMetadataFilePath));
            Assert.That(secondArtifact.Metadata.BindingMetadataPath, Is.EqualTo(firstArtifact.Metadata.BindingMetadataPath));
        }

        /// <summary>
        /// Verifies whole-repo dependency (no explicit file selection) imports all ONNX files from RepositoryFiles.
        /// </summary>
        [Test]
        public async Task ImportAsync_ImportsAllOnnxFiles_WhenNoExplicitFileSelectionAndRepositoryFilesPopulated()
        {
            var dependency = CreateDependency();
            var cachedSnapshot = CreateCachedSnapshotWithTwoOnnxFiles();

            var artifact = await _service.ImportAsync(dependency, cachedSnapshot, CancellationToken.None);

            Assert.That(artifact.RuntimeFiles.Count, Is.EqualTo(2));
            Assert.That(artifact.TryGetRuntimeFile("model.onnx", "sentis", out var firstSentis), Is.True);
            Assert.That(artifact.TryGetRuntimeFile("model_bnb4.onnx", "sentis", out var secondSentis), Is.True);
            Assert.That(File.Exists(ProjectPaths.ToAbsolutePath(firstSentis.AssetPath)), Is.True);
            Assert.That(File.Exists(ProjectPaths.ToAbsolutePath(secondSentis.AssetPath)), Is.True);
        }

        /// <summary>
        /// Verifies explicit file selection imports only the declared subset of ONNX files.
        /// </summary>
        [Test]
        public async Task ImportAsync_ImportsOnlySelectedFiles_WhenExplicitFileSelectionProvided()
        {
            var dependency = new DiscoveredModelDependency(
                "com.doji.test",
                "Test Package",
                "1.0.0",
                "/tmp/com.doji.test",
                "/tmp/com.doji.test/hf-hub-manifest.json",
                "manifesthash",
                new ModelDependencyDefinition(
                    "doji/test-model",
                    "main",
                    new[] { "model.onnx" }));
            var cachedSnapshot = CreateCachedSnapshotWithTwoOnnxFiles();

            var artifact = await _service.ImportAsync(dependency, cachedSnapshot, CancellationToken.None);

            Assert.That(artifact.RuntimeFiles.Count, Is.EqualTo(1));
            Assert.That(artifact.TryGetRuntimeFile("model.onnx", "sentis", out _), Is.True);
            Assert.That(artifact.TryGetRuntimeFile("model_bnb4.onnx", "sentis", out _), Is.False);
        }

        /// <summary>
        /// Verifies optional-file selection shrinks remove no-longer-selected generated Sentis outputs.
        /// </summary>
        [Test]
        public async Task ImportAsync_PrunesDeselectedRuntimeFiles_WhenOptionalSelectionShrinks()
        {
            var initialDependency = new DiscoveredModelDependency(
                "com.doji.test",
                "Test Package",
                "1.0.0",
                "/tmp/com.doji.test",
                "/tmp/com.doji.test/hf-hub-manifest.json",
                "manifesthash",
                new ModelDependencyDefinition(
                    "doji/test-model",
                    "main",
                    files: null,
                    optionalFiles: new[] { "model.onnx", "model_bnb4.onnx" }),
                enabledOptionalFiles: new[] { "model.onnx", "model_bnb4.onnx" });
            var shrunkDependency = new DiscoveredModelDependency(
                "com.doji.test",
                "Test Package",
                "1.0.0",
                "/tmp/com.doji.test",
                "/tmp/com.doji.test/hf-hub-manifest.json",
                "manifesthash",
                new ModelDependencyDefinition(
                    "doji/test-model",
                    "main",
                    files: null,
                    optionalFiles: new[] { "model.onnx", "model_bnb4.onnx" }),
                enabledOptionalFiles: new[] { "model.onnx" });
            var cachedSnapshot = CreateCachedSnapshotWithTwoOnnxFiles();

            var firstArtifact = await _service.ImportAsync(initialDependency, cachedSnapshot, CancellationToken.None);
            var secondArtifact = await _service.ImportAsync(shrunkDependency, cachedSnapshot, CancellationToken.None);

            Assert.That(firstArtifact.TryGetRuntimeFile("model_bnb4.onnx", "sentis", out var firstVariantRuntimeFile), Is.True);
            Assert.That(secondArtifact.TryGetRuntimeFile("model_bnb4.onnx", "sentis", out _), Is.False);
            Assert.That(File.Exists(ProjectPaths.ToAbsolutePath(firstVariantRuntimeFile.AssetPath)), Is.False);
        }

        /// <summary>
        /// Verifies whole-repo dependencies fail with a clear message when repository inventory contains no ONNX file.
        /// </summary>
        [Test]
        public void ImportAsync_ThrowsWhenWholeRepositorySelectionContainsNoOnnxFiles()
        {
            var dependency = CreateDependency();
            var cachedSnapshot = CreateCachedSnapshotWithoutOnnxFiles();

            var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await _service.ImportAsync(dependency, cachedSnapshot, CancellationToken.None));

            Assert.That(exception?.Message, Is.EqualTo("Dependency 'doji/test-model' does not select any ONNX file. Select the whole repository or at least one `.onnx` file."));
        }

        /// <summary>
        /// Verifies import keeps successful ONNX conversions when one variant in the repo is unsupported.
        /// </summary>
        [Test]
        public async Task ImportAsync_SkipsUnsupportedOnnxVariantsWhenAnotherOnnxConverts()
        {
            var dependency = CreateDependency();
            var cachedSnapshot = CreateCachedSnapshotWithTwoOnnxFiles();
            var service = new ProjectImportService(new PartiallyThrowingOnnxConverter("model_bnb4.onnx"));

            var artifact = await service.ImportAsync(dependency, cachedSnapshot, CancellationToken.None);

            Assert.That(artifact.RuntimeFiles.Count, Is.EqualTo(1));
            Assert.That(artifact.RuntimeFiles.Count(file => file.Kind == "sentis"), Is.EqualTo(1));
            Assert.That(artifact.TryGetRuntimeFile("model.onnx", "sentis", out var sentisFile), Is.True);
            Assert.That(artifact.TryGetRuntimeFile("model_bnb4.onnx", "sentis", out _), Is.False);
            Assert.That(File.Exists(ProjectPaths.ToAbsolutePath(sentisFile.AssetPath)), Is.True);
            Assert.That(artifact.Metadata.Diagnostics, Has.Length.EqualTo(1));
            Assert.That(artifact.Metadata.Diagnostics[0].Severity, Is.EqualTo("warning"));
            Assert.That(artifact.Metadata.Diagnostics[0].Message, Does.Contain("model_bnb4.onnx"));
            Assert.That(artifact.Metadata.Diagnostics[0].Message, Does.Contain("unsupported operators or data types"));
        }

        /// <summary>
        /// Verifies partial ONNX conversion warnings survive metadata reuse on later imports.
        /// </summary>
        [Test]
        public async Task ImportAsync_ReusesPersistedPartialConversionWarningsWhenMetadataMatches()
        {
            var dependency = CreateDependency();
            var cachedSnapshot = CreateCachedSnapshotWithTwoOnnxFiles();
            var service = new ProjectImportService(new PartiallyThrowingOnnxConverter("model_bnb4.onnx"));

            var firstArtifact = await service.ImportAsync(dependency, cachedSnapshot, CancellationToken.None);
            var secondArtifact = await _service.ImportAsync(dependency, cachedSnapshot, CancellationToken.None);

            Assert.That(firstArtifact.Metadata.Diagnostics, Has.Length.EqualTo(1));
            Assert.That(secondArtifact.Metadata.Diagnostics, Has.Length.EqualTo(1));
            Assert.That(secondArtifact.Metadata.Diagnostics[0].Severity, Is.EqualTo("warning"));
            Assert.That(secondArtifact.Metadata.Diagnostics[0].Message, Is.EqualTo(firstArtifact.Metadata.Diagnostics[0].Message));
        }

        /// <summary>
        /// Verifies ONNX conversion failures include repo file path and canonical model id for easier diagnostics.
        /// </summary>
        [Test]
        public void ImportAsync_WrapsOnnxConversionFailuresWithRepoFileContext()
        {
            var dependency = CreateDependency();
            var cachedSnapshot = CreateCachedSnapshot();
            var service = new ProjectImportService(new ThrowingOnnxConverter("Model contains unsupported operators or data types."));

            var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await service.ImportAsync(dependency, cachedSnapshot, CancellationToken.None));

            Assert.That(exception?.Message, Does.Contain("SingleInputSingleOuput.onnx"));
            Assert.That(exception?.Message, Does.Contain("doji/test-model"));
            Assert.That(exception?.Message, Does.Contain("unsupported operators or data types"));
        }

        /// <summary>
        /// Verifies path sanitization still replaces invalid characters.
        /// </summary>
        [Test]
        public void SanitizePathSegment_ReplacesInvalidPathCharacters()
        {
            Assert.That(
                ProjectPaths.SanitizePathSegment("com/doji\\bad name"),
                Is.EqualTo("com-doji-bad-name"));
        }

        [Test]
        public void ProjectModelRecipeSource_EnsureProjectOverride_CreatesAssetAndEntry()
        {
            var source = new ProjectModelRecipeSource();

            var assetPath = source.EnsureProjectOverride("doji/test-model", "model.onnx");

            Assert.That(assetPath, Is.EqualTo(ProjectModelRecipeSource.DefaultAssetPath));
            var asset = AssetDatabase.LoadAssetAtPath<ProjectModelConversionOverridesAsset>(assetPath);
            Assert.That(asset, Is.Not.Null);
            Assert.That(asset.Overrides, Has.Length.EqualTo(1));
            Assert.That(asset.Overrides[0].Repository, Is.EqualTo("doji/test-model"));
            Assert.That(asset.Overrides[0].RepoPath, Is.EqualTo("model.onnx"));
            Assert.That(asset.Overrides[0].Recipe, Is.Not.Null);
            Assert.That(asset.Overrides[0].Recipe.QuantizationMode, Is.EqualTo("none"));
        }

        [Test]
        public void ProjectModelRecipeSource_RemoveProjectOverride_RemovesMatchingEntry()
        {
            var source = new ProjectModelRecipeSource();
            source.EnsureProjectOverride("doji/test-model", "model.onnx");

            var removed = source.RemoveProjectOverride("doji/test-model", "model.onnx");

            Assert.That(removed, Is.True);
            var asset = AssetDatabase.LoadAssetAtPath<ProjectModelConversionOverridesAsset>(ProjectModelRecipeSource.DefaultAssetPath);
            Assert.That(asset, Is.Not.Null);
            Assert.That(asset.Overrides, Is.Empty);
        }

        [Test]
        public void ProjectModelRecipeSource_SetProjectOverride_RemovesEntryWhenRecipeMatchesDefault()
        {
            var source = new ProjectModelRecipeSource();
            source.EnsureProjectOverride("doji/test-model", "model.onnx");

            source.SetProjectOverride("doji/test-model", "model.onnx", new OnnxConversionRecipe());

            var asset = AssetDatabase.LoadAssetAtPath<ProjectModelConversionOverridesAsset>(ProjectModelRecipeSource.DefaultAssetPath);
            Assert.That(asset, Is.Not.Null);
            Assert.That(asset.Overrides, Is.Empty);
        }

        [Test]
        public void ProjectModelRecipeSource_SetProjectOverride_UpsertsRecipeValues()
        {
            var source = new ProjectModelRecipeSource();

            source.SetProjectOverride(
                "doji/test-model",
                "model.onnx",
                new OnnxConversionRecipe
                {
                    QuantizationMode = "int8",
                    StaticInputShapeOverrides = new[]
                    {
                        new OnnxStaticInputShapeOverride
                        {
                            InputName = "input_ids",
                            Dimensions = new[] { 1, 128 }
                        }
                    }
                });

            var asset = AssetDatabase.LoadAssetAtPath<ProjectModelConversionOverridesAsset>(ProjectModelRecipeSource.DefaultAssetPath);
            Assert.That(asset, Is.Not.Null);
            Assert.That(asset.Overrides, Has.Length.EqualTo(1));
            Assert.That(asset.Overrides[0].Recipe.QuantizationMode, Is.EqualTo("int8"));
            Assert.That(asset.Overrides[0].Recipe.StaticInputShapeOverrides, Has.Length.EqualTo(1));
            Assert.That(asset.Overrides[0].Recipe.StaticInputShapeOverrides[0].InputName, Is.EqualTo("input_ids"));
            Assert.That(asset.Overrides[0].Recipe.StaticInputShapeOverrides[0].Dimensions, Is.EqualTo(new[] { 1, 128 }));
        }

        /// <summary>
        /// Verifies project optional selection source creates a project-owned asset and owner-scoped entry.
        /// </summary>
        [Test]
        public void ProjectOptionalFileSelectionSource_SetEnabledOptionalFiles_CreatesAssetAndEntry()
        {
            var source = new ProjectOptionalFileSelectionSource();

            source.SetEnabledOptionalFiles(
                "com.doji.test",
                "doji/test-model",
                new[] { "model_q4.onnx" });

            var asset = AssetDatabase.LoadAssetAtPath<ProjectOptionalFileSelectionsAsset>(ProjectOptionalFileSelectionSource.DefaultAssetPath);
            Assert.That(asset, Is.Not.Null);
            Assert.That(asset.Selections, Has.Length.EqualTo(1));
            Assert.That(asset.Selections[0].OwnerId, Is.EqualTo("com.doji.test"));
            Assert.That(asset.Selections[0].ModelId, Is.EqualTo("doji/test-model"));
            Assert.That(asset.Selections[0].EnabledOptionalFiles, Is.EqualTo(new[] { "model_q4.onnx" }));
        }

        /// <summary>
        /// Verifies clearing enabled optional files removes the project-owned entry instead of storing an empty override.
        /// </summary>
        [Test]
        public void ProjectOptionalFileSelectionSource_SetEnabledOptionalFiles_RemovesEntryWhenSelectionCleared()
        {
            var source = new ProjectOptionalFileSelectionSource();
            source.SetEnabledOptionalFiles(
                "com.doji.test",
                "doji/test-model",
                new[] { "model_q4.onnx" });

            source.SetEnabledOptionalFiles(
                "com.doji.test",
                "doji/test-model",
                Array.Empty<string>());

            var asset = AssetDatabase.LoadAssetAtPath<ProjectOptionalFileSelectionsAsset>(ProjectOptionalFileSelectionSource.DefaultAssetPath);
            Assert.That(asset, Is.Not.Null);
            Assert.That(asset.Selections, Is.Empty);
        }

        private static DiscoveredModelDependency CreateDependency(
            string packageId = "com.doji.test",
            string manifestHash = "manifesthash")
        {
            return new DiscoveredModelDependency(
                packageId,
                "Test Package",
                "1.0.0",
                $"/tmp/{packageId}",
                $"/tmp/{packageId}/hf-hub-manifest.json",
                manifestHash,
                new ModelDependencyDefinition(
                    "doji/test-model",
                    "main"));
        }

        private static CachedSnapshot CreateCachedSnapshot()
        {
            var sampleOnnxPath = GetInferenceSampleOnnxPath();
            var snapshotRoot = Path.Combine(Path.GetTempPath(), "Doji.ModelHub.ImportSnapshot", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(snapshotRoot);
            var cachedModelPath = Path.Combine(snapshotRoot, "SingleInputSingleOuput.onnx");
            File.Copy(sampleOnnxPath, cachedModelPath, true);

            var metadata = new RawCacheMetadata
            {
                Repository = "doji/test-model",
                RequestedRevision = "main",
                ResolvedCommitHash = "commit123",
                CachedSnapshotRoot = snapshotRoot,
                RepositoryFiles = new[]
                {
                    new RepositoryFileMetadata { RepoPath = "SingleInputSingleOuput.onnx" }
                },
                Files = new[]
                {
                    new RawCacheFileMetadata
                    {
                        RepoPath = "SingleInputSingleOuput.onnx",
                        CachedFilePath = cachedModelPath
                    }
                },
                DownloadedAtUtc = DateTime.UtcNow.ToString("O")
            };

            return new CachedSnapshot(snapshotRoot, Path.Combine(snapshotRoot, "snapshot.hfhub.json"), metadata);
        }

        private static CachedSnapshot CreateCachedSnapshotWithTwoOnnxFiles()
        {
            var sampleOnnxPath = GetInferenceSampleOnnxPath();
            var snapshotRoot = Path.Combine(Path.GetTempPath(), "Doji.ModelHub.ImportSnapshot", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(snapshotRoot);

            var firstModelPath = Path.Combine(snapshotRoot, "model.onnx");
            var secondModelPath = Path.Combine(snapshotRoot, "model_bnb4.onnx");
            File.Copy(sampleOnnxPath, firstModelPath, true);
            File.Copy(sampleOnnxPath, secondModelPath, true);

            var metadata = new RawCacheMetadata
            {
                Repository = "doji/test-model",
                RequestedRevision = "main",
                ResolvedCommitHash = "commit123",
                CachedSnapshotRoot = snapshotRoot,
                RepositoryFiles = new[]
                {
                    new RepositoryFileMetadata { RepoPath = "model.onnx" },
                    new RepositoryFileMetadata { RepoPath = "model_bnb4.onnx" }
                },
                Files = new[]
                {
                    new RawCacheFileMetadata
                    {
                        RepoPath = "model.onnx",
                        CachedFilePath = firstModelPath
                    },
                    new RawCacheFileMetadata
                    {
                        RepoPath = "model_bnb4.onnx",
                        CachedFilePath = secondModelPath
                    }
                },
                DownloadedAtUtc = DateTime.UtcNow.ToString("O")
            };

            return new CachedSnapshot(snapshotRoot, Path.Combine(snapshotRoot, "snapshot.hfhub.json"), metadata);
        }

        private static CachedSnapshot CreateCachedSnapshotWithoutOnnxFiles()
        {
            var snapshotRoot = Path.Combine(Path.GetTempPath(), "Doji.ModelHub.ImportSnapshot", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(snapshotRoot);

            var tokenizerPath = Path.Combine(snapshotRoot, "tokenizer.json");
            File.WriteAllText(tokenizerPath, "{}");

            var metadata = new RawCacheMetadata
            {
                Repository = "doji/test-model",
                RequestedRevision = "main",
                ResolvedCommitHash = "commit123",
                CachedSnapshotRoot = snapshotRoot,
                RepositoryFiles = new[]
                {
                    new RepositoryFileMetadata { RepoPath = "tokenizer.json" }
                },
                Files = new[]
                {
                    new RawCacheFileMetadata
                    {
                        RepoPath = "tokenizer.json",
                        CachedFilePath = tokenizerPath
                    }
                },
                DownloadedAtUtc = DateTime.UtcNow.ToString("O")
            };

            return new CachedSnapshot(snapshotRoot, Path.Combine(snapshotRoot, "snapshot.hfhub.json"), metadata);
        }

        private static string GetInferenceSampleOnnxPath()
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ModelAsset).Assembly);
            Assert.That(packageInfo, Is.Not.Null, "Expected com.unity.ai.inference package info.");

            var samplePath = Path.Combine(packageInfo.resolvedPath, "Samples~", "Run a model", "SingleInputSingleOuput.onnx");
            Assert.That(File.Exists(samplePath), Is.True, $"Expected local ONNX sample at {samplePath}");
            return samplePath;
        }

        private static void CleanupProjectArtifacts()
        {
            DeleteAssetIfExists("Assets/StreamingAssets/hf-hub");
            DeleteProjectFolderIfExists("Library/Doji/HfHub/imports");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static void CleanupRecipeOverrideAsset()
        {
            DeleteAssetIfExists(ProjectModelRecipeSource.DefaultAssetPath);
            DeleteAssetIfExists(ProjectOptionalFileSelectionSource.DefaultAssetPath);
            DeleteEmptyAssetFolderIfExists("Assets/Doji/HfHub");
            DeleteEmptyAssetFolderIfExists("Assets/Doji");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static void DeleteAssetIfExists(string assetPath)
        {
            if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null || Directory.Exists(ProjectPaths.ToAbsolutePath(assetPath)))
            {
                AssetDatabase.DeleteAsset(assetPath);
            }
        }

        private static void DeleteEmptyAssetFolderIfExists(string assetPath)
        {
            if (!AssetDatabase.IsValidFolder(assetPath))
            {
                return;
            }

            var absolutePath = ProjectPaths.ToAbsolutePath(assetPath);
            if (Directory.Exists(absolutePath) &&
                !Directory.EnumerateFileSystemEntries(absolutePath).Any())
            {
                AssetDatabase.DeleteAsset(assetPath);
            }
        }

        private static void DeleteProjectFolderIfExists(string projectRelativePath)
        {
            var absolutePath = ProjectPaths.ToProjectAbsolutePath(projectRelativePath);
            if (Directory.Exists(absolutePath))
            {
                Directory.Delete(absolutePath, true);
            }
        }

        private sealed class ThrowingOnnxConverter : IOnnxModelConverter
        {
            private readonly string _message;

            public ThrowingOnnxConverter(string message)
            {
                _message = message;
            }

            public OnnxConversionResult Convert(OnnxConversionRequest request)
            {
                throw new InvalidOperationException(_message);
            }
        }

        private sealed class PartiallyThrowingOnnxConverter : IOnnxModelConverter
        {
            private readonly string _failingFileName;
            private readonly EditorOnnxModelConverter _inner = new();

            public PartiallyThrowingOnnxConverter(string failingFileName)
            {
                _failingFileName = failingFileName;
            }

            public OnnxConversionResult Convert(OnnxConversionRequest request)
            {
                if (string.Equals(Path.GetFileName(request.OnnxFilePath), _failingFileName, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Model contains unsupported operators or data types.");
                }

                return _inner.Convert(request);
            }
        }

        private sealed class CountingOnnxConverter : IOnnxModelConverter
        {
            private readonly EditorOnnxModelConverter _inner = new();

            public int CallCount { get; private set; }

            public OnnxConversionResult Convert(OnnxConversionRequest request)
            {
                CallCount++;
                return _inner.Convert(request);
            }
        }
    }
}
