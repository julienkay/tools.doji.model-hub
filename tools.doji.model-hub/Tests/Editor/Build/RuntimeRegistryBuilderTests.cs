using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers the build plan: which runtime models a build bundles, what the registry says for downloaded models, and
    /// which models stop a build.
    /// </summary>
    internal sealed class RuntimeRegistryBuilderTests
    {
        private string _rootDirectory;
        private string _projectRoot;
        private string _convertedPath;
        private string _sentisPath;

        [SetUp]
        public void SetUp()
        {
            _rootDirectory = Path.Combine(Path.GetTempPath(), "Doji.ModelHub.RegistryBuilderTests", Guid.NewGuid().ToString("N"));
            _projectRoot = Path.Combine(_rootDirectory, "Project");
            Directory.CreateDirectory(_projectRoot);
            _convertedPath = WriteFile("converted/key.sentis");
            _sentisPath = WriteFile("cache/model.sentis");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_rootDirectory))
            {
                Directory.Delete(_rootDirectory, true);
            }
        }

        /// <summary>
        /// Verifies a bundled model adds its runtime model at the same path in StreamingAssets that the player looks up.
        /// </summary>
        [Test]
        public void Build_BundlesRuntimeModelsAtTheirLookupPath()
        {
            var plan = Plan(ModelDelivery.Bundle, Ready("tools.doji.a", OnnxFile("onnx/model.onnx")));

            Assert.That(plan.Problems, Is.Empty);
            var (sourcePath, relativePath) = plan.BundledFiles.Single();
            Assert.That(sourcePath, Is.EqualTo(_convertedPath));
            Assert.That(relativePath, Is.EqualTo("model-hub/models/doji/test-model/onnx/model.sentis"));
            var file = plan.Registry.packages.Single().models.Single().files.Single();
            Assert.That(file.relativePath, Is.EqualTo(relativePath));
            Assert.That(file.localPath, Is.Null, "Builds never carry paths of this machine.");
            Assert.That(plan.Registry.packages.Single().models.Single().commit, Is.EqualTo("abc123"));
        }

        /// <summary>
        /// Verifies two packages that use the same runtime model bundle it once.
        /// </summary>
        [Test]
        public void Build_BundlesASharedModelOnce()
        {
            var plan = Plan(ModelDelivery.Bundle, Ready("tools.doji.a", OnnxFile("onnx/model.onnx")), Ready("tools.doji.b", OnnxFile("onnx/model.onnx")));

            Assert.That(plan.BundledFiles, Has.Count.EqualTo(1));
            Assert.That(plan.Registry.packages.Select(package => package.packageId), Is.EqualTo(new[] { "tools.doji.a", "tools.doji.b" }));
        }

        /// <summary>
        /// Verifies a model pulled into the project is not added again; Unity already ships StreamingAssets files.
        /// </summary>
        [Test]
        public void Build_SkipsModelsPulledIntoTheProject()
        {
            var pulled = Path.Combine(_projectRoot, "Assets", "StreamingAssets", "model-hub", "models", "doji", "test-model", "onnx", "model.sentis");
            Directory.CreateDirectory(Path.GetDirectoryName(pulled));
            File.WriteAllText(pulled, "pulled");

            var plan = Plan(ModelDelivery.Bundle, Ready("tools.doji.a", OnnxFile("onnx/model.onnx")));
            var editor = PlanForEditor(ModelDelivery.Bundle, Ready("tools.doji.a", OnnxFile("onnx/model.onnx")));

            Assert.That(plan.BundledFiles, Is.Empty);
            Assert.That(plan.Registry.packages.Single().models.Single().files.Single().relativePath, Does.EndWith("onnx/model.sentis"));
            Assert.That(editor.Registry.packages.Single().models.Single().files.Single().localPath, Is.EqualTo(pulled), "Play mode uses the pulled copy.");
        }

        /// <summary>
        /// Verifies a downloaded model ships no file, but the registry carries the pinned source, companions, and
        /// conversion settings a player needs.
        /// </summary>
        [Test]
        public void Build_DescribesDownloadedModels()
        {
            var onnx = OnnxFile("onnx/model.onnx");
            onnx.Companions = new[] { new ImportedSourceFileMetadata { RepoPath = "onnx/model.onnx_data", Etag = "etag-data", SizeInBytes = 9 } };
            var recipe = new OnnxConversionRecipe
            {
                QuantizationMode = "int8",
                StaticInputShapeOverrides = new[] { new OnnxStaticInputShapeOverride { InputName = "x", Dimensions = new[] { 1, 3 } } }
            };

            var plan = PlanWith(ModelDelivery.Download, Ready("tools.doji.a", onnx), recipe, "loader@1");

            Assert.That(plan.Problems, Is.Empty);
            Assert.That(plan.BundledFiles, Is.Empty);
            Assert.That(plan.Registry.onnxConverterId, Is.EqualTo("loader@1"));
            var model = plan.Registry.packages.Single().models.Single();
            var file = model.files.Single();
            Assert.That(model.Delivery, Is.EqualTo(ModelDelivery.Download));
            Assert.That(model.revision, Is.EqualTo("main"));
            Assert.That(file.source.etag, Is.EqualTo("etag-onnx"));
            Assert.That(file.source.size, Is.EqualTo(10));
            Assert.That(file.companions.Single().etag, Is.EqualTo("etag-data"));
            Assert.That(file.quantization, Is.EqualTo("uint8"));
            Assert.That(file.inputShapes.Single().dimensions, Is.EqualTo(new[] { 1, 3 }));
            Assert.That(file.recipeHash, Is.EqualTo("recipe-hash"));
        }

        /// <summary>
        /// Verifies a build stops for models it cannot deliver: not downloaded, not set up, a missing runtime model, a downloaded
        /// ONNX model without a player converter, or a source without an etag.
        /// </summary>
        [Test]
        public void Build_ReportsModelsItCannotDeliver()
        {
            var missing = OnnxFile("onnx/missing.onnx");
            missing.FilePath = Path.Combine(_rootDirectory, "gone.sentis");
            var noEtag = SentisFile("sentis/model.sentis");
            noEtag.SourceEtag = null;

            var notImported = Plan(ModelDelivery.Bundle, NotImported("tools.doji.a"));
            var notDownloaded = Plan(ModelDelivery.Bundle, NotImported("tools.doji.a", DependencyReasonCode.RawCacheRevisionRefMissing));
            var missingFile = Plan(ModelDelivery.Bundle, Ready("tools.doji.a", missing));
            var noConverter = Plan(ModelDelivery.Download, Ready("tools.doji.a", OnnxFile("onnx/model.onnx")));
            var withoutEtag = Plan(ModelDelivery.Download, Ready("tools.doji.a", noEtag));
            var sentisDownload = Plan(ModelDelivery.Download, Ready("tools.doji.a", SentisFile("sentis/model.sentis")));

            Assert.That(notImported.Problems.Single(), Does.Contain("could not be set up from the downloaded files"));
            Assert.That(notDownloaded.Problems.Single(), Does.Contain("not downloaded. Download it in Window > AI Model Hub"));
            Assert.That(missingFile.Problems.Single(), Does.Contain("missing"));
            Assert.That(noConverter.Problems.Single(), Does.Contain("tools.doji.onnx-model-loader"));
            Assert.That(withoutEtag.Problems.Single(), Does.Contain("etag"));
            Assert.That(sentisDownload.Problems, Is.Empty, "A downloaded Sentis model needs no converter.");
        }

        /// <summary>
        /// Verifies the Editor catalog skips models that are not imported instead of failing, and points at local files.
        /// </summary>
        [Test]
        public void Build_ForEditorUsesLocalFilesAndSkipsMissingModels()
        {
            var plan = PlanForEditor(ModelDelivery.Download, Ready("tools.doji.a", SentisFile("sentis/model.sentis")), NotImported("tools.doji.b"));

            Assert.That(plan.Problems, Is.Empty);
            Assert.That(plan.BundledFiles, Is.Empty);
            Assert.That(plan.Registry.packages.Single(package => package.packageId == "tools.doji.a").models.Single().files.Single().localPath, Is.EqualTo(_sentisPath));
            Assert.That(plan.Registry.packages.Single(package => package.packageId == "tools.doji.b").models, Is.Empty);
        }

        /// <summary>
        /// Verifies the delivery setting is read per repository.
        /// </summary>
        [Test]
        public void Build_ReadsDeliveryPerRepository()
        {
            var report = new StatusReport(Array.Empty<ManifestDiscoveryIssue>(), new[]
            {
                Ready("tools.doji.a", SentisFile("sentis/model.sentis")),
                Ready("tools.doji.a", SentisFile("sentis/model.sentis"), repository: "doji/large-model")
            });

            var plan = RuntimeRegistryBuilder.Build(
                report,
                repository => repository == "doji/large-model" ? ModelDelivery.Download : ModelDelivery.Bundle,
                null,
                forEditor: false,
                playerOnnxConverterId: null,
                _projectRoot);

            var models = plan.Registry.packages.Single().models;
            Assert.That(models.Single(model => model.repoId == "doji/test-model").Delivery, Is.EqualTo(ModelDelivery.Bundle));
            Assert.That(models.Single(model => model.repoId == "doji/large-model").Delivery, Is.EqualTo(ModelDelivery.Download));
            Assert.That(plan.BundledFiles, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Verifies the registry records the repository id as the cache spells it, so a player downloads into the
        /// folder Python uses even when a manifest spells the id differently.
        /// </summary>
        [Test]
        public void Build_RecordsTheCacheSpellingOfTheRepositoryId()
        {
            using var cacheScope = new TestCacheScope(Path.Combine(_rootDirectory, "Cache"));
            Directory.CreateDirectory(Path.Combine(cacheScope.CacheRoot, "models--doji--Test-Model"));

            var model = Plan(ModelDelivery.Download, Ready("tools.doji.a", SentisFile("sentis/model.sentis"))).Registry.packages.Single().models.Single();

            Assert.That(model.repoId, Is.EqualTo("doji/test-model"));
            Assert.That(model.cacheRepoId, Is.EqualTo("doji/Test-Model"));
        }

        /// <summary>
        /// Verifies the label a manifest gives a file reaches the registry, so players can show it in pickers.
        /// </summary>
        [Test]
        public void Build_CopiesFileLabelsFromTheManifest()
        {
            var dependency = new DiscoveredModelDependency(
                "tools.doji.a",
                "tools.doji.a",
                "1.0.0",
                "/tmp/tools.doji.a",
                "/tmp/tools.doji.a/model-hub-manifest.json",
                new ModelDependencyDefinition("doji/test-model", "main", new[]
                {
                    new ModelDependencyFileDefinition("onnx/model.onnx", label: "Small"),
                    new ModelDependencyFileDefinition("sentis/model.sentis")
                }));
            var ready = Ready("tools.doji.a", OnnxFile("onnx/model.onnx"));
            ready.Shared.RuntimeFiles = new[] { OnnxFile("onnx/model.onnx"), SentisFile("sentis/model.sentis") };
            var status = new DependencyStatus(
                dependency, DependencyState.Ready, DependencyReasonCode.None, "Ready.", null, null, null, null, _convertedPath, null, null, ready.Shared);

            var files = Plan(ModelDelivery.Bundle, status).Registry.packages.Single().models.Single().files;

            Assert.That(files.Select(file => file.label), Is.EqualTo(new[] { "Small", null }));
        }

        /// <summary>
        /// Verifies selected files that are not models reach the registry: a build bundles them from the cache, Play mode
        /// reads them from the cache, and ONNX companions stay with their model.
        /// </summary>
        [Test]
        public void Build_BundlesDataFilesFromTheCache()
        {
            var snapshot = Path.Combine(_rootDirectory, "snapshot");
            var configPath = Path.Combine(snapshot, "unet", "config.json");
            Directory.CreateDirectory(Path.GetDirectoryName(configPath));
            File.WriteAllText(configPath, "{}");
            var status = ReadyWithDataFiles(snapshot);

            var plan = Plan(ModelDelivery.Bundle, status);
            var editor = PlanForEditor(ModelDelivery.Bundle, status);
            var download = Plan(ModelDelivery.Download, status);

            Assert.That(plan.Problems, Is.Empty);
            var dataFile = plan.Registry.packages.Single().models.Single().dataFiles.Single();
            Assert.That(dataFile.repoPath, Is.EqualTo("unet/config.json"));
            Assert.That(dataFile.relativePath, Is.EqualTo("model-hub/files/doji/test-model/unet/config.json"));
            Assert.That(dataFile.localPath, Is.Null, "Builds never carry paths of this machine.");
            Assert.That(plan.BundledFiles, Does.Contain((configPath, dataFile.relativePath)));
            Assert.That(editor.Registry.packages.Single().models.Single().dataFiles.Single().localPath, Is.EqualTo(configPath));
            var downloaded = download.Registry.packages.Single().models.Single().dataFiles.Single();
            Assert.That(downloaded.source.etag, Is.EqualTo("etag-config"));
            Assert.That(downloaded.source.size, Is.EqualTo(2));
            Assert.That(download.BundledFiles, Is.Empty);
        }

        /// <summary>
        /// Verifies a build stops when a bundled data file is not in the cache.
        /// </summary>
        [Test]
        public void Build_ReportsMissingDataFiles()
        {
            var plan = Plan(ModelDelivery.Bundle, ReadyWithDataFiles(Path.Combine(_rootDirectory, "empty-snapshot")));

            Assert.That(plan.Problems.Single(), Does.Contain("unet/config.json"));
        }

        private DependencyStatus ReadyWithDataFiles(string snapshot)
        {
            var dependency = new DiscoveredModelDependency(
                "tools.doji.a",
                "tools.doji.a",
                "1.0.0",
                "/tmp/tools.doji.a",
                "/tmp/tools.doji.a/model-hub-manifest.json",
                new ModelDependencyDefinition("doji/test-model", "main", new[]
                {
                    new ModelDependencyFileDefinition("unet/model.onnx"),
                    new ModelDependencyFileDefinition("unet/config.json")
                }));
            var metadata = new SharedImportMetadata
{
                Repository = "doji/test-model",
                ResolvedCommitHash = "abc123",
                RawSnapshotRootPath = snapshot,
                RepositoryFiles = new[]
                {
                    new RepositoryFileMetadata { RepoPath = "unet/model.onnx", SizeInBytes = 10, EntityTag = "etag-onnx" },
                    new RepositoryFileMetadata { RepoPath = "unet/model.onnx_data", SizeInBytes = 9, EntityTag = "etag-data" },
                    new RepositoryFileMetadata { RepoPath = "unet/config.json", SizeInBytes = 2, EntityTag = "\"etag-config\"" },
                    new RepositoryFileMetadata { RepoPath = "README.md", SizeInBytes = 1, EntityTag = "etag-readme" }
                },
                RuntimeFiles = new[] { OnnxFile("unet/model.onnx") }
            };
            return new DependencyStatus(
                dependency, DependencyState.Ready, DependencyReasonCode.None, "Ready.", null, null, null, null, _convertedPath, null, null, metadata);
        }

        private RuntimeRegistryBuilder.Plan Plan(ModelDelivery delivery, params DependencyStatus[] statuses)
        {
            return Run(delivery, statuses, false, null, null);
        }

        private RuntimeRegistryBuilder.Plan PlanForEditor(ModelDelivery delivery, params DependencyStatus[] statuses)
        {
            return Run(delivery, statuses, true, null, null);
        }

        private RuntimeRegistryBuilder.Plan PlanWith(ModelDelivery delivery, DependencyStatus status, OnnxConversionRecipe recipe, string converterId)
        {
            return Run(delivery, new[] { status }, false, recipe, converterId);
        }

        private RuntimeRegistryBuilder.Plan Run(
            ModelDelivery delivery,
            DependencyStatus[] statuses,
            bool forEditor,
            OnnxConversionRecipe recipe,
            string converterId)
        {
            return RuntimeRegistryBuilder.Build(
                new StatusReport(Array.Empty<ManifestDiscoveryIssue>(), statuses),
                _ => delivery,
                (_, _) => recipe ?? new OnnxConversionRecipe(),
                forEditor,
                converterId,
                _projectRoot);
        }

        private ImportedRuntimeFileMetadata OnnxFile(string repoPath)
        {
            return new ImportedRuntimeFileMetadata
            {
                RepoPath = repoPath,
                Kind = "sentis",
                Format = "onnx",
                FilePath = _convertedPath,
                SourceEtag = "etag-onnx",
                SourceSizeInBytes = 10,
                ConversionKey = "key",
                RecipeHash = "recipe-hash",
                ConversionPipelineVersion = "test"
            };
        }

        private ImportedRuntimeFileMetadata SentisFile(string repoPath)
        {
            return new ImportedRuntimeFileMetadata
            {
                RepoPath = repoPath,
                Kind = "sentis",
                Format = "sentis",
                FilePath = _sentisPath,
                SourceEtag = "etag-sentis",
                SourceSizeInBytes = 6
            };
        }

        private static DependencyStatus Ready(string packageId, ImportedRuntimeFileMetadata file, string repository = "doji/test-model")
        {
            var dependency = CreateDependency(packageId, repository);
            return new DependencyStatus(
                dependency,
                DependencyState.Ready,
                DependencyReasonCode.None,
                "Ready.",
                null,
                null,
                null,
                null,
                file.FilePath,
                null,
                null,
                new SharedImportMetadata
{
                    Repository = repository,
                    ResolvedCommitHash = "abc123",
                    RuntimeFiles = new[] { file }
                });
        }

        private static DependencyStatus NotImported(string packageId, DependencyReasonCode reasonCode = DependencyReasonCode.ImportBindingMissing)
        {
            return new DependencyStatus(
                CreateDependency(packageId, "doji/test-model"),
                DependencyState.Missing,
                reasonCode,
                "Not set up in this project yet.",
                null,
                null,
                null,
                null,
                null,
                null,
                null);
        }

        private static DiscoveredModelDependency CreateDependency(string packageId, string repository)
        {
            return new DiscoveredModelDependency(
                packageId,
                packageId,
                "1.0.0",
                $"/tmp/{packageId}",
                $"/tmp/{packageId}/model-hub-manifest.json",
                new ModelDependencyDefinition(repository, "main"));
        }

        private string WriteFile(string relativePath)
        {
            var path = Path.Combine(_rootDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "model");
            return path;
        }
    }
}
