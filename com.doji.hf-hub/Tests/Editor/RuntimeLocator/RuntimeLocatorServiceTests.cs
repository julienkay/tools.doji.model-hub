using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using Unity.InferenceEngine;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers generated runtime locator registry write and repo-level lookup behavior.
    /// </summary>
    internal sealed class RuntimeLocatorServiceTests
    {
        private RuntimeLocatorRegistryService _registryService;

        [SetUp]
        public void SetUp()
        {
            _registryService = new RuntimeLocatorRegistryService();
            CleanupRuntimeArtifacts();
        }

        [TearDown]
        public void TearDown()
        {
            CleanupRuntimeArtifacts();
        }

        /// <summary>
        /// Verifies registry JSON writes canonical model id and runtime file entries.
        /// </summary>
        [Test]
        public void WriteRegistries_WritesPackageScopedRegistryJson()
        {
            var result = CreateSuccessResult("tiny-model");

            _registryService.WriteRegistries(new[] { result });

            var registryPath = ProjectPaths.GetRuntimeRegistryAssetPath("com.doji.test");
            var registryAbsolutePath = ProjectPaths.ToAbsolutePath(registryPath);

            Assert.That(File.Exists(registryAbsolutePath), Is.True);
            Assert.That(File.ReadAllText(registryAbsolutePath), Does.Contain("\"_repoId\": \"doji/test-model\""));
            Assert.That(File.ReadAllText(registryAbsolutePath), Does.Contain("\"_kind\": \"sentis\""));
        }

        /// <summary>
        /// Verifies owner-specific repo lookup returns generated Sentis entries.
        /// </summary>
        [Test]
        public void TryResolveRuntimeModel_ReturnsResolvedAbsolutePathsFromGeneratedRegistry()
        {
            var result = CreateSuccessResult("tiny-model");
            WriteRuntimeFiles(result.ImportedArtifact.Metadata);
            _registryService.WriteRegistries(new[] { result });

            var found = Hub.TryResolveRuntimeModel("com.doji.test", "doji/test-model", out var model);

            Assert.That(found, Is.True);
            Assert.That(model, Is.Not.Null);
            Assert.That(model.PackageId, Is.EqualTo("com.doji.test"));
            Assert.That(model.RepoId, Is.EqualTo("doji/test-model"));
            Assert.That(model.Files.Count, Is.EqualTo(1));
            Assert.That(model.TryGetSentisPath("models/model.onnx", out var sentisPath), Is.True);
            Assert.That(NormalizePath(sentisPath), Does.EndWith("/models/model.sentis"));
        }

        /// <summary>
        /// Verifies single-string repo lookup succeeds when only one owner publishes that repo.
        /// </summary>
        [Test]
        public void TryResolveRuntimeModel_ByModelId_ReturnsResolvedModel()
        {
            var result = CreateSuccessResult("tiny-model");
            WriteRuntimeFiles(result.ImportedArtifact.Metadata);
            _registryService.WriteRegistries(new[] { result });

            var found = Hub.TryResolveRuntimeModel("doji/test-model", out var model);

            Assert.That(found, Is.True);
            Assert.That(model, Is.Not.Null);
            Assert.That(model.RepoId, Is.EqualTo("doji/test-model"));
        }

        /// <summary>
        /// Verifies `from_pretrained(...)` style wrapper resolves Sentis payloads by repo path.
        /// </summary>
        [Test]
        public void FromPretrained_ReturnsRepositoryWrapperWithSentisAccess()
        {
            var result = CreateSuccessResult("tiny-model");
            WriteRuntimeFiles(result.ImportedArtifact.Metadata);
            _registryService.WriteRegistries(new[] { result });

            var repository = Hub.FromPretrained("doji/test-model");

            Assert.That(repository.PackageId, Is.EqualTo("com.doji.test"));
            Assert.That(repository.RepoId, Is.EqualTo("doji/test-model"));
            Assert.That(repository.TryGetSentisPath("models/model.onnx", out var sentisPath), Is.True);
            Assert.That(NormalizePath(sentisPath), Does.EndWith("/models/model.sentis"));
        }

        /// <summary>
        /// Verifies owner-specific `from_pretrained(...)` style wrapper stays deterministic when repo id is ambiguous globally.
        /// </summary>
        [Test]
        public void FromPretrained_ByOwnerAndModelId_ResolvesChosenOwnerWhenAmbiguous()
        {
            var first = CreateSuccessResult("tiny-model", "com.doji.first");
            var second = CreateSuccessResult("tiny-model", "com.doji.second");
            WriteRuntimeFiles(first.ImportedArtifact.Metadata);
            WriteRuntimeFiles(second.ImportedArtifact.Metadata);
            _registryService.WriteRegistries(new[] { first, second });

            var repository = Hub.FromPretrained("com.doji.second", "doji/test-model");

            Assert.That(repository, Is.Not.Null);
            Assert.That(repository.PackageId, Is.EqualTo("com.doji.second"));
            Assert.That(repository.RepoId, Is.EqualTo("doji/test-model"));
        }

        /// <summary>
        /// Verifies `from_pretrained(...)` wrapper can load generated Sentis payload by repo-relative ONNX path.
        /// </summary>
        [Test]
        public async System.Threading.Tasks.Task FromPretrained_LoadModel_LoadsGeneratedSentisPayload()
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
                    "main"));

            var cachedSnapshot = CreateCachedSnapshot();
            var importService = new ProjectImportService();
            var importedArtifact = await importService.ImportAsync(
                dependency,
                cachedSnapshot,
                System.Threading.CancellationToken.None);

            _registryService.WriteRegistries(new[]
            {
                new ModelSyncResult(SyncOperationMode.Full, dependency, cachedSnapshot, importedArtifact, null)
            });

            var repository = Hub.FromPretrained("doji/test-model");
            var model = repository.LoadModel("SingleInputSingleOuput.onnx");

            Assert.That(model, Is.Not.Null);
        }

        /// <summary>
        /// Verifies package-specific repo lookup returns false when registry is missing.
        /// </summary>
        [Test]
        public void TryResolveRuntimeModel_ReturnsFalseWhenRegistryMissing()
        {
            var found = Hub.TryResolveRuntimeModel("com.doji.test", "missing-model", out var model);

            Assert.That(found, Is.False);
            Assert.That(model, Is.Null);
        }

        /// <summary>
        /// Verifies public runtime load resolves sentis by repo-relative ONNX path.
        /// </summary>
        [Test]
        public async System.Threading.Tasks.Task LoadRuntimeModel_LoadsGeneratedSentisPayloadFromPublicApi()
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
                    "main"));

            var cachedSnapshot = CreateCachedSnapshot();
            var importService = new ProjectImportService();
            var importedArtifact = await importService.ImportAsync(
                dependency,
                cachedSnapshot,
                System.Threading.CancellationToken.None);

            _registryService.WriteRegistries(new[]
            {
                new ModelSyncResult(SyncOperationMode.Full, dependency, cachedSnapshot, importedArtifact, null)
            });

            var model = Hub.LoadRuntimeModel("doji/test-model", "SingleInputSingleOuput.onnx");

            Assert.That(model, Is.Not.Null);
        }

        /// <summary>
        /// Verifies single-string repo lookup throws when multiple owners publish same repo.
        /// </summary>
        [Test]
        public void TryResolveRuntimeModel_ByModelId_ThrowsWhenAmbiguousAcrossOwners()
        {
            var first = CreateSuccessResult("tiny-model", "com.doji.first");
            var second = CreateSuccessResult("tiny-model", "com.doji.second");
            WriteRuntimeFiles(first.ImportedArtifact.Metadata);
            WriteRuntimeFiles(second.ImportedArtifact.Metadata);
            _registryService.WriteRegistries(new[] { first, second });

            var exception = Assert.Throws<InvalidOperationException>(() =>
                Hub.TryResolveRuntimeModel("doji/test-model", out _));

            Assert.That(exception?.Message, Does.Contain("ambiguous"));
        }

        /// <summary>
        /// Verifies single-string `from_pretrained(...)` lookup throws when multiple owners publish same repo.
        /// </summary>
        [Test]
        public void FromPretrained_ByModelId_ThrowsWhenAmbiguousAcrossOwners()
        {
            var first = CreateSuccessResult("tiny-model", "com.doji.first");
            var second = CreateSuccessResult("tiny-model", "com.doji.second");
            WriteRuntimeFiles(first.ImportedArtifact.Metadata);
            WriteRuntimeFiles(second.ImportedArtifact.Metadata);
            _registryService.WriteRegistries(new[] { first, second });

            var exception = Assert.Throws<InvalidOperationException>(() =>
                Hub.FromPretrained("doji/test-model"));

            Assert.That(exception?.Message, Does.Contain("ambiguous"));
        }

        /// <summary>
        /// Verifies owner-specific repo lookup remains deterministic when repo id is ambiguous globally.
        /// </summary>
        [Test]
        public void TryResolveRuntimeModel_ByOwnerAndModelId_ResolvesChosenOwnerWhenAmbiguous()
        {
            var first = CreateSuccessResult("tiny-model", "com.doji.first");
            var second = CreateSuccessResult("tiny-model", "com.doji.second");
            WriteRuntimeFiles(first.ImportedArtifact.Metadata);
            WriteRuntimeFiles(second.ImportedArtifact.Metadata);
            _registryService.WriteRegistries(new[] { first, second });

            var found = Hub.TryResolveRuntimeModel("com.doji.second", "doji/test-model", out var model);

            Assert.That(found, Is.True);
            Assert.That(model, Is.Not.Null);
            Assert.That(model.PackageId, Is.EqualTo("com.doji.second"));
            Assert.That(model.RepoId, Is.EqualTo("doji/test-model"));
        }

        private static ModelSyncResult CreateSuccessResult(string modelId, string packageId = "com.doji.test")
        {
            var dependency = new DiscoveredModelDependency(
                packageId,
                "Test Package",
                "1.0.0",
                $"/tmp/{packageId}",
                $"/tmp/{packageId}/hf-hub-manifest.json",
                "manifesthash",
                new ModelDependencyDefinition(
                    "doji/test-model",
                    "main"));

            var metadata = new ImportedModelMetadata
            {
                PackageId = dependency.PackageId,
                PackageVersion = dependency.PackageVersion,
                ModelId = dependency.ModelId,
                ManifestHash = dependency.ManifestHash,
                Repository = dependency.Repository,
                RequestedRevision = dependency.Revision,
                ResolvedCommitHash = "abc123",
                RawSnapshotRootPath = "/cache/doji/test-model/abc123",
                RawMetadataPath = "/cache/doji/test-model/abc123/snapshot.hfhub.json",
                SentisVersion = "2.6.1",
                UnityVersion = "6000.3.10f1",
                RuntimeRegistryAssetPath = ProjectPaths.GetRuntimeRegistryAssetPath(dependency.PackageId),
                RuntimeRegistryRelativePath = ProjectPaths.ToStreamingAssetsRelativePath(ProjectPaths.GetRuntimeRegistryAssetPath(dependency.PackageId)),
                RuntimeFiles = new[]
                {
                    new ImportedRuntimeFileMetadata
                    {
                        RepoPath = "models/model.onnx",
                        Kind = "sentis",
                        AssetPath = ProjectPaths.GetRuntimeSentisAssetPath(dependency.Repository, "abc123", "models/model.onnx"),
                        RelativePath = ProjectPaths.ToStreamingAssetsRelativePath(ProjectPaths.GetRuntimeSentisAssetPath(dependency.Repository, "abc123", "models/model.onnx"))
                    }
                },
                ImportedAtUtc = DateTime.UtcNow.ToString("O")
            };

            return new ModelSyncResult(
                SyncOperationMode.Full,
                dependency,
                new CachedSnapshot("/cache/doji/test-model/abc123", "/cache/doji/test-model/abc123/snapshot.hfhub.json", new RawCacheMetadata()),
                new ImportedModelArtifact(
                    ProjectPaths.GetRuntimeSnapshotFolder(dependency.Repository, "abc123"),
                    ProjectPaths.GetImportBindingMetadataFilePath(dependency.PackageId, dependency.ModelId),
                    metadata,
                    ProjectPaths.GetSharedImportMetadataFilePath(dependency.Repository, "abc123", "2.6.1", OnnxImportMetadataUtility.ComputeRecipeSetHash(new[] { "SingleInputSingleOuput.onnx" }))),
                null);
        }

        private static CachedSnapshot CreateCachedSnapshot()
        {
            var sampleOnnxPath = GetInferenceSampleOnnxPath();
            var snapshotRoot = Path.Combine(Path.GetTempPath(), "Doji.ModelHub.RuntimeLocatorSnapshot", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(snapshotRoot);
            var cachedModelPath = Path.Combine(snapshotRoot, "SingleInputSingleOuput.onnx");
            File.Copy(sampleOnnxPath, cachedModelPath, true);

            return new CachedSnapshot(
                snapshotRoot,
                Path.Combine(snapshotRoot, "snapshot.hfhub.json"),
                new RawCacheMetadata
                {
                    Repository = "doji/test-model",
                    RequestedRevision = "main",
                    ResolvedCommitHash = "abc123",
                    CachedSnapshotRoot = snapshotRoot,
                    Files = new[]
                    {
                        new RawCacheFileMetadata
                        {
                            RepoPath = "SingleInputSingleOuput.onnx",
                            CachedFilePath = cachedModelPath
                        }
                    }
                });
        }

        private static void WriteRuntimeFiles(ImportedModelMetadata metadata)
        {
            foreach (var runtimeFile in metadata.RuntimeFiles)
            {
                var absolutePath = ProjectPaths.ToAbsolutePath(runtimeFile.AssetPath);
                Directory.CreateDirectory(Path.GetDirectoryName(absolutePath) ?? throw new InvalidOperationException());
                File.WriteAllText(absolutePath, runtimeFile.Kind);
                AssetDatabase.ImportAsset(runtimeFile.AssetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            }
        }

        private static void CleanupRuntimeArtifacts()
        {
            DeleteAssetIfExists("Assets/StreamingAssets/hf-hub");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static string GetInferenceSampleOnnxPath()
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ModelAsset).Assembly);
            Assert.That(packageInfo, Is.Not.Null, "Expected com.unity.ai.inference package info.");

            var samplePath = Path.Combine(packageInfo.resolvedPath, "Samples~", "Run a model", "SingleInputSingleOuput.onnx");
            Assert.That(File.Exists(samplePath), Is.True, $"Expected local ONNX sample at {samplePath}");
            return samplePath;
        }

        private static void DeleteAssetIfExists(string assetPath)
        {
            if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null || Directory.Exists(ProjectPaths.ToAbsolutePath(assetPath)))
            {
                AssetDatabase.DeleteAsset(assetPath);
            }
        }

        private static string NormalizePath(string path)
        {
            return path.Replace('\\', '/');
        }
    }
}
