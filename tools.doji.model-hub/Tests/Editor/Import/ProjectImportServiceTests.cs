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
    /// Covers preparing runtime models: Sentis files used as downloaded, ONNX files converted once into the shared
    /// converted folder with this project's settings, and nothing written into Assets.
    /// </summary>
    internal sealed class ProjectImportServiceTests
    {
        private ProjectImportService _service;
        private TestProjectStateScope _projectScope;
        private IDisposable _projectOverridesScope;
        private TestCacheScope _cacheScope;
        private string _rootDirectory;

        [SetUp]
        public void SetUp()
        {
            _rootDirectory = Path.Combine(Path.GetTempPath(), "Doji.ModelHub.ImportTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rootDirectory);
            _cacheScope = new TestCacheScope(Path.Combine(_rootDirectory, "SharedCache"));
            _service = new ProjectImportService();
            _projectScope = new TestProjectStateScope();
            _projectOverridesScope = ProjectSettings.PushEmptyProjectOverrides();
        }

        [TearDown]
        public void TearDown()
        {
            _projectOverridesScope?.Dispose();
            _projectOverridesScope = null;
            _projectScope?.Dispose();
            _projectScope = null;
            _cacheScope?.Dispose();
            _cacheScope = null;
            if (Directory.Exists(_rootDirectory))
            {
                Directory.Delete(_rootDirectory, true);
            }
        }

        /// <summary>
        /// Verifies an ONNX file is converted into the shared converted folder, keyed by etag, converter, and
        /// settings, and that the project gets only its import records.
        /// </summary>
        [Test]
        public async Task ImportAsync_ConvertsOnnxIntoTheSharedConvertedFolder()
        {
            var dependency = CreateDependency();
            var cachedSnapshot = CreateCachedSnapshot();

            var artifact = await _service.ImportAsync(dependency, cachedSnapshot, CancellationToken.None);

            Assert.That(File.Exists(ProjectPaths.ToProjectAbsolutePath(artifact.BindingMetadataFilePath)), Is.True);
            Assert.That(File.Exists(ProjectPaths.ToProjectAbsolutePath(artifact.SharedMetadataFilePath)), Is.True);
            Assert.That(artifact.Shared.ResolvedCommitHash, Is.EqualTo("commit123"));
            var runtimeFile = artifact.RuntimeFiles.Single();
            var recipeHash = OnnxImportMetadataUtility.ComputeRecipeHash(new OnnxConversionRecipe());
            var expectedKey = ConvertedModelStore.ComputeKey("etag-single", ProjectImportService.GetEditorConverterId(), recipeHash);
            Assert.That(runtimeFile.Format, Is.EqualTo("onnx"));
            Assert.That(runtimeFile.SourceEtag, Is.EqualTo("etag-single"));
            Assert.That(runtimeFile.ConversionKey, Is.EqualTo(expectedKey));
            Assert.That(runtimeFile.FilePath, Is.EqualTo(ConvertedModelStore.GetModelPath("doji/test-model", expectedKey)));
            Assert.That(runtimeFile.FilePath, Does.StartWith(_cacheScope.DataRoot));
            Assert.That(runtimeFile.RecipeHash, Is.EqualTo(recipeHash));
            Assert.That(ModelLoader.Load(runtimeFile.FilePath), Is.Not.Null);
            Assert.That(File.Exists(ConvertedModelStore.GetRecordPath("doji/test-model", expectedKey)), Is.True);
            Assert.That(Directory.Exists(ProjectPaths.ToAbsolutePath("Assets/StreamingAssets/model-hub/doji")), Is.False);
        }

        /// <summary>
        /// Verifies a Sentis file is used as downloaded: no conversion, and the runtime model is the cached file.
        /// </summary>
        [Test]
        public async Task ImportAsync_UsesSentisFilesAsDownloaded()
        {
            var converter = new CountingOnnxConverter();
            var service = new ProjectImportService(converter);
            var cachedSnapshot = CreateCachedSnapshotWithFormats();
            var dependency = CreateDependency(files: new[] { "sentis/model.sentis" });

            var artifact = await service.ImportAsync(dependency, cachedSnapshot, CancellationToken.None);

            var runtimeFile = artifact.RuntimeFiles.Single();
            Assert.That(converter.CallCount, Is.Zero);
            Assert.That(runtimeFile.Format, Is.EqualTo("sentis"));
            Assert.That(runtimeFile.FilePath, Is.EqualTo(cachedSnapshot.Metadata.Files.Single(file => file.RepoPath == "sentis/model.sentis").CachedFilePath));
            Assert.That(runtimeFile.SourceEtag, Is.EqualTo("etag-sentis"));
            Assert.That(runtimeFile.ConversionKey, Is.Null);
            Assert.That(ModelLoader.Load(runtimeFile.FilePath), Is.Not.Null);
        }

        /// <summary>
        /// Verifies AI Model Hub never picks a format for a whole-repository dependency on a repository with both formats.
        /// </summary>
        [Test]
        public void ImportAsync_RejectsWholeRepositoryOfMixedFormats()
        {
            var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await _service.ImportAsync(CreateDependency(), CreateCachedSnapshotWithFormats(), CancellationToken.None));

            Assert.That(exception?.Message, Does.Contain("both ONNX and Sentis files"));
        }

        /// <summary>
        /// Verifies the project's conversion settings change the converted model and its key.
        /// </summary>
        [Test]
        public async Task ImportAsync_AppliesConversionSettings()
        {
            var cachedSnapshot = CreateCachedSnapshot();
            var sampleInput = ModelLoader.Load(await ConvertSampleAsync()).inputs[0];
            var fixedDimensions = Enumerable.Repeat(7, sampleInput.shape.rank).ToArray();
            var recipe = new OnnxConversionRecipe
            {
                QuantizationMode = "float16",
                StaticInputShapeOverrides = new[] { new OnnxStaticInputShapeOverride { InputName = sampleInput.name, Dimensions = fixedDimensions } }
            };
            var service = new ProjectImportService(recipeSource: new FixedRecipeSource(recipe));

            var artifact = await service.ImportAsync(CreateDependency(), cachedSnapshot, CancellationToken.None);

            var runtimeFile = artifact.RuntimeFiles.Single();
            Assert.That(runtimeFile.RecipeHash, Is.EqualTo(OnnxImportMetadataUtility.ComputeRecipeHash(recipe)));
            var model = ModelLoader.Load(runtimeFile.FilePath);
            var shape = model.inputs[0].shape;
            Assert.That(shape.IsStatic(), Is.True, "Every input dimension is fixed by the setting.");
            Assert.That(shape.ToTensorShape().ToArray(), Is.EqualTo(fixedDimensions));
        }

        /// <summary>
        /// Verifies a second owner, and a project whose import records broke, reuse the converted model without
        /// converting again.
        /// </summary>
        [Test]
        public async Task ImportAsync_ReusesConvertedModelsAcrossOwnersAndBrokenRecords()
        {
            var converter = new CountingOnnxConverter();
            var service = new ProjectImportService(converter);
            var cachedSnapshot = CreateCachedSnapshot();

            var first = await service.ImportAsync(CreateDependency(), cachedSnapshot, CancellationToken.None);
            var second = await service.ImportAsync(CreateDependency(packageId: "tools.doji.other"), cachedSnapshot, CancellationToken.None);
            File.WriteAllText(ProjectPaths.ToProjectAbsolutePath(first.SharedMetadataFilePath), "{ invalid json");
            var third = await service.ImportAsync(CreateDependency(), cachedSnapshot, CancellationToken.None);

            Assert.That(converter.CallCount, Is.EqualTo(1));
            Assert.That(second.RuntimeFiles.Single().FilePath, Is.EqualTo(first.RuntimeFiles.Single().FilePath));
            Assert.That(third.RuntimeFiles.Single().FilePath, Is.EqualTo(first.RuntimeFiles.Single().FilePath));
            Assert.That(second.BindingMetadataFilePath, Is.Not.EqualTo(first.BindingMetadataFilePath));
        }

        /// <summary>
        /// Verifies a current import record short-circuits the import.
        /// </summary>
        [Test]
        public async Task ImportAsync_ReusesCurrentImportRecord()
        {
            var cachedSnapshot = CreateCachedSnapshot();

            var first = await _service.ImportAsync(CreateDependency(), cachedSnapshot, CancellationToken.None);
            var second = await _service.ImportAsync(CreateDependency(), cachedSnapshot, CancellationToken.None);

            Assert.That(second.Shared.ImportedAtUtc, Is.EqualTo(first.Shared.ImportedAtUtc));
        }

        /// <summary>
        /// Verifies an import recorded with another Inference Engine version of the same format generation stays
        /// current, so an update does not convert again.
        /// </summary>
        [Test]
        public async Task ImportAsync_ReusesTheImportAfterAnUpdateWithinTheFormatGeneration()
        {
            var converter = new CountingOnnxConverter();
            var service = new ProjectImportService(converter);
            var cachedSnapshot = CreateCachedSnapshot();
            var first = await service.ImportAsync(CreateDependency(), cachedSnapshot, CancellationToken.None);
            foreach (var path in new[] { first.BindingMetadataFilePath, first.SharedMetadataFilePath })
            {
                var absolutePath = ProjectPaths.ToProjectAbsolutePath(path);
                var json = File.ReadAllText(absolutePath);
                Assert.That(json, Does.Contain(ProjectImportService.GetSentisVersion()));
                File.WriteAllText(absolutePath, System.Text.RegularExpressions.Regex.Replace(json, @"""SentisVersion"":\s*""[^""]*""", "\"SentisVersion\": \"2.2.0\""));
            }

            var second = await service.ImportAsync(CreateDependency(), cachedSnapshot, CancellationToken.None);

            Assert.That(converter.CallCount, Is.EqualTo(1));
            Assert.That(second.Shared.ImportedAtUtc, Is.EqualTo(first.Shared.ImportedAtUtc));
        }

        /// <summary>
        /// Verifies a deleted converted model is converted again instead of being reported as imported.
        /// </summary>
        [Test]
        public async Task ImportAsync_ConvertsAgainWhenTheConvertedModelWasDeleted()
        {
            var converter = new CountingOnnxConverter();
            var service = new ProjectImportService(converter);
            var cachedSnapshot = CreateCachedSnapshot();
            var first = await service.ImportAsync(CreateDependency(), cachedSnapshot, CancellationToken.None);
            File.Delete(first.RuntimeFiles.Single().FilePath);

            var second = await service.ImportAsync(CreateDependency(), cachedSnapshot, CancellationToken.None);

            Assert.That(converter.CallCount, Is.EqualTo(2));
            Assert.That(File.Exists(second.RuntimeFiles.Single().FilePath), Is.True);
        }

        /// <summary>
        /// Verifies an ONNX file's external weights are recorded, so a player can download them with the model.
        /// </summary>
        [Test]
        public async Task ImportAsync_RecordsCompanionFiles()
        {
            var cachedSnapshot = CreateCachedSnapshot(companion: true);

            var artifact = await _service.ImportAsync(CreateDependency(), cachedSnapshot, CancellationToken.None);

            var companion = artifact.RuntimeFiles.Single().Companions.Single();
            Assert.That(companion.RepoPath, Is.EqualTo("SingleInputSingleOuput.onnx_data"));
            Assert.That(companion.Etag, Is.EqualTo("etag-data"));
            Assert.That(companion.SizeInBytes, Is.EqualTo(4));
        }

        /// <summary>
        /// Verifies a whole-repository dependency imports every ONNX file, and a listed file only itself.
        /// </summary>
        [Test]
        public async Task ImportAsync_FollowsTheSelection()
        {
            var cachedSnapshot = CreateCachedSnapshotWithTwoOnnxFiles();

            var whole = await _service.ImportAsync(CreateDependency(), cachedSnapshot, CancellationToken.None);
            var listed = await _service.ImportAsync(CreateDependency(packageId: "tools.doji.listed", files: new[] { "model.onnx" }), cachedSnapshot, CancellationToken.None);

            Assert.That(whole.RuntimeFiles.Select(file => file.RepoPath), Is.EquivalentTo(new[] { "model.onnx", "model_bnb4.onnx" }));
            Assert.That(listed.RuntimeFiles.Select(file => file.RepoPath), Is.EqualTo(new[] { "model.onnx" }));
        }

        /// <summary>
        /// Verifies selections without model files fail with a clear message.
        /// </summary>
        [Test]
        public void ImportAsync_ThrowsWhenSelectionHasNoModelFiles()
        {
            var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await _service.ImportAsync(CreateDependency(), CreateCachedSnapshotWithoutOnnxFiles(), CancellationToken.None));

            Assert.That(exception?.Message, Does.Contain("does not select any model file"));
        }

        /// <summary>
        /// Verifies import keeps successful conversions when one ONNX file is unsupported, and the warning survives
        /// a later import.
        /// </summary>
        [Test]
        public async Task ImportAsync_SkipsUnsupportedOnnxFilesWhenAnotherConverts()
        {
            var cachedSnapshot = CreateCachedSnapshotWithTwoOnnxFiles();
            var service = new ProjectImportService(new PartiallyThrowingOnnxConverter("model_bnb4.onnx"));

            var artifact = await service.ImportAsync(CreateDependency(), cachedSnapshot, CancellationToken.None);
            var again = await _service.ImportAsync(CreateDependency(), cachedSnapshot, CancellationToken.None);

            Assert.That(artifact.RuntimeFiles.Select(file => file.RepoPath), Is.EqualTo(new[] { "model.onnx" }));
            Assert.That(artifact.Shared.SkippedRepoPaths, Is.EqualTo(new[] { "model_bnb4.onnx" }));
            Assert.That(artifact.Shared.Diagnostics.Single().Message, Does.Contain("model_bnb4.onnx"));
            Assert.That(again.Shared.Diagnostics.Single().Message, Is.EqualTo(artifact.Shared.Diagnostics.Single().Message));
        }

        /// <summary>
        /// Verifies a source with quantized ONNX operators is skipped before conversion, with a message that names
        /// the operator.
        /// </summary>
        [Test]
        public async Task ImportAsync_SkipsSourcesWithUnsupportedQuantizationOperators()
        {
            var cachedSnapshot = CreateSnapshot(
                ("model.onnx", "etag-model", _ => File.ReadAllBytes(GetInferenceSampleOnnxPath())),
                ("model_q4.onnx", "etag-q4", _ => OnnxModelInspectorTests.BuildQuantizedOnnxModel()));

            var artifact = await _service.ImportAsync(CreateDependency(), cachedSnapshot, CancellationToken.None);

            Assert.That(artifact.RuntimeFiles.Select(file => file.RepoPath), Is.EqualTo(new[] { "model.onnx" }));
            Assert.That(artifact.Shared.SkippedRepoPaths, Is.EqualTo(new[] { "model_q4.onnx" }));
            Assert.That(artifact.Shared.Diagnostics.Single().Message, Does.Contain("`MatMulNBits`"));
        }

        /// <summary>
        /// Verifies the measured weight bytes reach the runtime file, and a reused conversion reports the same
        /// totals and warnings as the first.
        /// </summary>
        [Test]
        public async Task ImportAsync_RecordsWeightBytesAndRepeatsThemOnReuse()
        {
            var cachedSnapshot = CreateCachedSnapshot();
            var service = new ProjectImportService(recipeSource: new FixedRecipeSource(new OnnxConversionRecipe { QuantizationMode = "uint8" }));

            var first = await service.ImportAsync(CreateDependency(), cachedSnapshot, CancellationToken.None);
            var again = await service.ImportAsync(CreateDependency(packageId: "tools.doji.other"), cachedSnapshot, CancellationToken.None);

            var firstFile = first.RuntimeFiles.Single();
            var againFile = again.RuntimeFiles.Single();
            Assert.That(firstFile.ImportedWeightBytes, Is.Not.Null);
            Assert.That(firstFile.ConvertedWeightBytes, Is.Not.Null);
            Assert.That(firstFile.ConvertedWeightBytes, Is.LessThanOrEqualTo(firstFile.ImportedWeightBytes));
            Assert.That(againFile.ImportedWeightBytes, Is.EqualTo(firstFile.ImportedWeightBytes));
            Assert.That(againFile.ConvertedWeightBytes, Is.EqualTo(firstFile.ConvertedWeightBytes));
            Assert.That(
                again.Shared.Diagnostics.Select(diagnostic => diagnostic.Message),
                Is.EqualTo(first.Shared.Diagnostics.Select(diagnostic => diagnostic.Message)));
        }

        /// <summary>
        /// Verifies a conversion failure names the file and the repository.
        /// </summary>
        [Test]
        public void ImportAsync_WrapsOnnxConversionFailuresWithRepoFileContext()
        {
            var service = new ProjectImportService(new ThrowingOnnxConverter("Model contains unsupported operators or data types."));

            var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await service.ImportAsync(CreateDependency(), CreateCachedSnapshot(), CancellationToken.None));

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
                Hub.SanitizePathSegment("tools/doji\\bad name"),
                Is.EqualTo("tools-doji-bad-name"));
        }

        /// <summary>
        /// Verifies a project override is stored in Project Settings and removed again on request.
        /// </summary>
        [Test]
        public void ProjectModelRecipeSource_RemoveProjectOverride_RemovesMatchingEntry()
        {
            var source = new ProjectModelRecipeSource();
            source.SetProjectOverride("doji/test-model", "model.onnx", new OnnxConversionRecipe { QuantizationMode = "uint8" });
            Assert.That(ProjectSettings.instance.GetConversionOverride("doji/test-model", "model.onnx")?.QuantizationMode, Is.EqualTo("uint8"));

            var removed = source.RemoveProjectOverride("doji/test-model", "model.onnx");

            Assert.That(removed, Is.True);
            Assert.That(ProjectSettings.instance.GetConversionOverride("doji/test-model", "model.onnx"), Is.Null);
            Assert.That(source.RemoveProjectOverride("doji/test-model", "model.onnx"), Is.False);
        }

        /// <summary>
        /// Verifies setting the default recipe clears the override instead of storing a copy of the default.
        /// </summary>
        [Test]
        public void ProjectModelRecipeSource_SetProjectOverride_RemovesEntryWhenRecipeMatchesDefault()
        {
            var source = new ProjectModelRecipeSource();
            source.SetProjectOverride("doji/test-model", "model.onnx", new OnnxConversionRecipe { QuantizationMode = "uint8" });

            source.SetProjectOverride("doji/test-model", "model.onnx", new OnnxConversionRecipe());

            Assert.That(ProjectSettings.instance.GetConversionOverride("doji/test-model", "model.onnx"), Is.Null);
        }

        [Test]
        public void ProjectModelRecipeSource_UsesManifestDefaultWithoutOverride()
        {
            var source = new ProjectModelRecipeSource(new[]
            {
                CreateManifestDependency("com.example.a", "model.onnx", "float16"),
                CreateManifestDependency("com.example.b", "model.onnx", null)
            });

            var recipe = source.GetEffectiveRecipe("doji/test-model", "model.onnx");

            Assert.That(recipe.Recipe.QuantizationMode, Is.EqualTo("float16"));
            Assert.That(recipe.SourceLabel, Is.EqualTo("Manifest: com.example.a"));
            Assert.That(recipe.HasProjectOverride, Is.False);
            Assert.That(source.GetEffectiveRecipe("doji/test-model", "other.onnx").SourceLabel, Is.EqualTo("Built-in Default"));
        }

        [Test]
        public void ProjectModelRecipeSource_UsesBuiltInDefaultWhenManifestsDisagree()
        {
            var source = new ProjectModelRecipeSource(new[]
            {
                CreateManifestDependency("com.example.a", "model.onnx", "float16"),
                CreateManifestDependency("com.example.b", "model.onnx", "uint8")
            });

            var recipe = source.GetEffectiveRecipe("doji/test-model", "model.onnx");

            Assert.That(recipe.Recipe.QuantizationMode, Is.EqualTo("none"));
            Assert.That(recipe.SourceLabel, Is.EqualTo("Built-in Default (manifests disagree: com.example.a, com.example.b)"));
        }

        [Test]
        public void ProjectModelRecipeSource_ProjectOverrideReplacesManifestDefault()
        {
            var source = new ProjectModelRecipeSource(new[] { CreateManifestDependency("com.example.a", "model.onnx", "float16") });

            source.SetProjectOverride("doji/test-model", "model.onnx", new OnnxConversionRecipe { QuantizationMode = "none" });

            var recipe = source.GetEffectiveRecipe("doji/test-model", "model.onnx");
            Assert.That(recipe.HasProjectOverride, Is.True, "Going back to `none` differs from the manifest default, so it stays an override.");
            Assert.That(recipe.Recipe.QuantizationMode, Is.EqualTo("none"));

            source.SetProjectOverride("doji/test-model", "model.onnx", new OnnxConversionRecipe { QuantizationMode = "float16" });

            Assert.That(source.GetEffectiveRecipe("doji/test-model", "model.onnx").HasProjectOverride, Is.False, "An override equal to the manifest default is removed.");
        }

        private static DiscoveredModelDependency CreateManifestDependency(string packageId, string repoPath, string quantization)
        {
            return new DiscoveredModelDependency(
                packageId,
                packageId,
                "1.0.0",
                null,
                null,
                new ModelDependencyDefinition(
                    "doji/test-model",
                    "main",
                    new[]
                    {
                        new ModelDependencyFileDefinition(
                            repoPath,
                            conversion: quantization == null ? null : new OnnxConversionRecipe { QuantizationMode = quantization })
                    }));
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

            var stored = ProjectSettings.instance.GetConversionOverride("doji/test-model", "model.onnx");
            Assert.That(stored, Is.Not.Null);
            Assert.That(stored.QuantizationMode, Is.EqualTo("int8"));
            Assert.That(stored.StaticInputShapeOverrides, Has.Length.EqualTo(1));
            Assert.That(stored.StaticInputShapeOverrides[0].InputName, Is.EqualTo("input_ids"));
            Assert.That(stored.StaticInputShapeOverrides[0].Dimensions, Is.EqualTo(new[] { 1, 128 }));
        }

        /// <summary>
        /// Verifies a conversion override survives serialization, so it is still there after a domain reload and in
        /// the settings file other team members check out.
        /// </summary>
        [Test]
        public void ProjectSettings_ConversionOverride_SurvivesSerialization()
        {
            ProjectSettings.instance.SetConversionOverride(
                "doji/test-model",
                "model.onnx",
                new OnnxConversionRecipe
                {
                    QuantizationMode = "float16",
                    StaticInputShapeOverrides = new[] { new OnnxStaticInputShapeOverride { InputName = "pixel_values", Dimensions = new[] { 1, 3, 224, 224 } } }
                });

            // A second ProjectSettings instance would log a ScriptableSingleton error, so check the serialized form.
            var json = EditorJsonUtility.ToJson(ProjectSettings.instance);

            Assert.That(json, Does.Contain("\"repoPath\":\"model.onnx\""));
            Assert.That(json, Does.Contain("\"quantization\":\"float16\""));
            Assert.That(json, Does.Contain("\"input\":\"pixel_values\""));
            Assert.That(json, Does.Contain("\"dimensions\":[1,3,224,224]"));
        }

        /// <summary>
        /// Verifies project optional selection source stores an owner-scoped entry in Project Settings and returns only
        /// files the manifest still declares.
        /// </summary>
        [Test]
        public void ProjectOptionalFileSelectionSource_SetEnabledOptionalFiles_StoresEntry()
        {
            var source = new ProjectOptionalFileSelectionSource();

            source.SetEnabledOptionalFiles(
                "tools.doji.test",
                "doji/test-model",
                new[] { "model_q4.onnx" });

            Assert.That(
                ProjectSettings.instance.GetEnabledOptionalFiles("tools.doji.test", "doji/test-model"),
                Is.EqualTo(new[] { "model_q4.onnx" }));
            Assert.That(
                source.GetEnabledOptionalFiles("tools.doji.test", "doji/test-model", new[] { "model_q4.onnx", "model_fp16.onnx" }),
                Is.EqualTo(new[] { "model_q4.onnx" }));
            Assert.That(
                source.GetEnabledOptionalFiles("tools.doji.test", "doji/test-model", new[] { "model_fp16.onnx" }),
                Is.Empty);
        }

        /// <summary>
        /// Verifies clearing enabled optional files removes the project-owned entry instead of storing an empty override.
        /// </summary>
        [Test]
        public void ProjectOptionalFileSelectionSource_SetEnabledOptionalFiles_RemovesEntryWhenSelectionCleared()
        {
            var source = new ProjectOptionalFileSelectionSource();
            source.SetEnabledOptionalFiles(
                "tools.doji.test",
                "doji/test-model",
                new[] { "model_q4.onnx" });

            source.SetEnabledOptionalFiles(
                "tools.doji.test",
                "doji/test-model",
                Array.Empty<string>());

            Assert.That(ProjectSettings.instance.GetEnabledOptionalFiles("tools.doji.test", "doji/test-model"), Is.Empty);
        }

        /// <summary>
        /// Verifies pending conversions list the selected ONNX files without a converted model in the store, and that
        /// one conversion serves every later lookup with the same etag, converter, and settings.
        /// </summary>
        [Test]
        public async Task GetPendingConversions_ListsSelectedOnnxFilesWithoutAStoreHit()
        {
            var cachedSnapshot = CreateCachedSnapshotWithTwoOnnxFiles();
            var recipes = new ProjectModelRecipeSource();
            var both = new[] { "model.onnx", "model_bnb4.onnx" };

            var before = ProjectImportService.GetPendingConversions("doji/test-model", cachedSnapshot.Metadata.Files, both, recipes);
            await _service.ImportAsync(CreateDependency(files: new[] { "model.onnx" }), cachedSnapshot, CancellationToken.None);
            var after = ProjectImportService.GetPendingConversions("doji/test-model", cachedSnapshot.Metadata.Files, both, recipes);
            var unselected = ProjectImportService.GetPendingConversions("doji/test-model", cachedSnapshot.Metadata.Files, Array.Empty<string>(), recipes);

            Assert.That(before, Is.EqualTo(both));
            Assert.That(after, Is.EqualTo(new[] { "model_bnb4.onnx" }));
            Assert.That(unselected, Is.Empty);
        }

        /// <summary>
        /// Verifies changed conversion settings miss the store, because the settings are part of the key.
        /// </summary>
        [Test]
        public async Task GetPendingConversions_MissesTheStoreAfterASettingsChange()
        {
            var cachedSnapshot = CreateCachedSnapshot();
            await _service.ImportAsync(CreateDependency(), cachedSnapshot, CancellationToken.None);
            var changed = new FixedRecipeSource(new OnnxConversionRecipe { QuantizationMode = "float16" });

            var pending = ProjectImportService.GetPendingConversions(
                "doji/test-model",
                cachedSnapshot.Metadata.Files,
                new[] { "SingleInputSingleOuput.onnx" },
                changed);

            Assert.That(pending, Is.EqualTo(new[] { "SingleInputSingleOuput.onnx" }));
        }

        /// <summary>
        /// Verifies an import that may not convert stops before it writes records when an ONNX file needs converting.
        /// </summary>
        [Test]
        public void ImportAsync_WithoutConversion_ThrowsBeforeWritingAnything()
        {
            var converter = new CountingOnnxConverter();
            var service = new ProjectImportService(converter);
            var dependency = CreateDependency();

            var exception = Assert.ThrowsAsync<ConversionRequiredException>(async () =>
                await service.ImportAsync(dependency, CreateCachedSnapshot(), CancellationToken.None, allowConversion: false));

            Assert.That(exception.RepoPaths, Is.EqualTo(new[] { "SingleInputSingleOuput.onnx" }));
            Assert.That(converter.CallCount, Is.Zero);
            Assert.That(File.Exists(ProjectPaths.ToProjectAbsolutePath(ProjectPaths.GetImportBindingMetadataFilePath(dependency.PackageId, dependency.ModelId))), Is.False);
        }

        /// <summary>
        /// Verifies an import that may not convert still finishes when the store has every converted model, for
        /// example for a second project or owner on the same machine.
        /// </summary>
        [Test]
        public async Task ImportAsync_WithoutConversion_ReusesStoreHits()
        {
            var converter = new CountingOnnxConverter();
            var service = new ProjectImportService(converter);
            var cachedSnapshot = CreateCachedSnapshot();
            var first = await service.ImportAsync(CreateDependency(), cachedSnapshot, CancellationToken.None);

            var second = await service.ImportAsync(
                CreateDependency(packageId: "tools.doji.other"),
                cachedSnapshot,
                CancellationToken.None,
                allowConversion: false);

            Assert.That(converter.CallCount, Is.EqualTo(1));
            Assert.That(second.RuntimeFiles.Single().FilePath, Is.EqualTo(first.RuntimeFiles.Single().FilePath));
            Assert.That(File.Exists(ProjectPaths.ToProjectAbsolutePath(second.BindingMetadataFilePath)), Is.True);
        }

        /// <summary>
        /// Verifies Sentis files never count as conversions, so their import is always cheap.
        /// </summary>
        [Test]
        public async Task ImportAsync_WithoutConversion_UsesSentisFiles()
        {
            var converter = new CountingOnnxConverter();
            var service = new ProjectImportService(converter);

            var artifact = await service.ImportAsync(
                CreateDependency(files: new[] { "sentis/model.sentis" }),
                CreateCachedSnapshotWithFormats(),
                CancellationToken.None,
                allowConversion: false);

            Assert.That(converter.CallCount, Is.Zero);
            Assert.That(artifact.RuntimeFiles.Single().Format, Is.EqualTo("sentis"));
        }

        private static DiscoveredModelDependency CreateDependency(
            string packageId = "tools.doji.test",
            string[] files = null)
        {
            return new DiscoveredModelDependency(
                packageId,
                "Test Package",
                "1.0.0",
                $"/tmp/{packageId}",
                $"/tmp/{packageId}/model-hub-manifest.json",
                new ModelDependencyDefinition(
                    "doji/test-model",
                    "main",
                    (files ?? Array.Empty<string>()).Select(file => new ModelDependencyFileDefinition(file)).ToArray()));
        }

        private CachedSnapshot CreateSnapshot(params (string RepoPath, string Etag, Func<string, byte[]> Content)[] files)
        {
            var snapshotRoot = Path.Combine(_rootDirectory, "Snapshot", Guid.NewGuid().ToString("N"));
            var entries = files.Select(file =>
            {
                var path = Path.Combine(snapshotRoot, file.RepoPath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, file.Content(path));
                return new RawCacheFileMetadata { RepoPath = file.RepoPath, CachedFilePath = path, EntityTag = file.Etag, SizeInBytes = new FileInfo(path).Length };
            }).ToArray();

            var metadata = new RawCacheMetadata
            {
                Repository = "doji/test-model",
                RequestedRevision = "main",
                ResolvedCommitHash = "commit123",
                CachedSnapshotRoot = snapshotRoot,
                RepositoryFiles = entries.Select(entry => new RepositoryFileMetadata { RepoPath = entry.RepoPath, SizeInBytes = entry.SizeInBytes }).ToArray(),
                Files = entries,
                DownloadedAtUtc = DateTime.UtcNow.ToString("O")
            };

            return new CachedSnapshot(snapshotRoot, Path.Combine(snapshotRoot, "snapshot.modelhub.json"), metadata);
        }

        private CachedSnapshot CreateCachedSnapshot(bool companion = false)
        {
            var files = new[] { ("SingleInputSingleOuput.onnx", "etag-single", (Func<string, byte[]>)(_ => File.ReadAllBytes(GetInferenceSampleOnnxPath()))) };
            if (companion)
            {
                files = files.Append(("SingleInputSingleOuput.onnx_data", "etag-data", _ => new byte[4])).ToArray();
            }

            return CreateSnapshot(files);
        }

        private CachedSnapshot CreateCachedSnapshotWithTwoOnnxFiles()
        {
            return CreateSnapshot(
                ("model.onnx", "etag-model", _ => File.ReadAllBytes(GetInferenceSampleOnnxPath())),
                ("model_bnb4.onnx", "etag-bnb4", _ => File.ReadAllBytes(GetInferenceSampleOnnxPath())));
        }

        private CachedSnapshot CreateCachedSnapshotWithFormats()
        {
            var sentisBytes = File.ReadAllBytes(ConvertSampleAsync().GetAwaiter().GetResult());
            return CreateSnapshot(
                ("onnx/model.onnx", "etag-onnx", _ => File.ReadAllBytes(GetInferenceSampleOnnxPath())),
                ("sentis/model.sentis", "etag-sentis", _ => sentisBytes));
        }

        private CachedSnapshot CreateCachedSnapshotWithoutOnnxFiles()
        {
            return CreateSnapshot(("tokenizer.json", "etag-tokenizer", _ => new byte[] { (byte)'{', (byte)'}' }));
        }

        /// <summary>
        /// Converts the Inference Engine sample to a Sentis file in the test folder.
        /// </summary>
        private Task<string> ConvertSampleAsync()
        {
            var path = Path.Combine(_rootDirectory, "sample.sentis");
            if (!File.Exists(path))
            {
                ModelWriter.Save(path, new EditorOnnxModelConverter().Convert(new OnnxConversionRequest(GetInferenceSampleOnnxPath())).Model);
            }

            return Task.FromResult(path);
        }

        private static string GetInferenceSampleOnnxPath()
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ModelAsset).Assembly);
            Assert.That(packageInfo, Is.Not.Null, "Expected com.unity.ai.inference package info.");

            var samplePath = Path.Combine(packageInfo.resolvedPath, "Samples~", "Run a model", "SingleInputSingleOuput.onnx");
            Assert.That(File.Exists(samplePath), Is.True, $"Expected local ONNX sample at {samplePath}");
            return samplePath;
        }

        private sealed class FixedRecipeSource : IProjectModelRecipeSource
        {
            private readonly OnnxConversionRecipe _recipe;
            private readonly ProjectModelRecipeSource _inner = new();

            public FixedRecipeSource(OnnxConversionRecipe recipe)
            {
                _recipe = recipe;
            }

            public EffectiveOnnxConversionRecipe GetEffectiveRecipe(string repository, string repoPath)
            {
                return new EffectiveOnnxConversionRecipe(_recipe, "Test", true);
            }

            public string ComputeRecipeSetHash(string repository, System.Collections.Generic.IEnumerable<string> repoPaths)
            {
                return OnnxImportMetadataUtility.ComputeRecipeHash(_recipe) + string.Join("|", repoPaths);
            }

            public void SetProjectOverride(string repository, string repoPath, OnnxConversionRecipe recipe) => _inner.SetProjectOverride(repository, repoPath, recipe);
            public bool RemoveProjectOverride(string repository, string repoPath) => _inner.RemoveProjectOverride(repository, repoPath);
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
