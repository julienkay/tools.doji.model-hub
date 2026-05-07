using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEditor;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers repo-level local dependency state classification across raw cache, owner bindings, and shared imports.
    /// </summary>
    internal sealed class StatusServiceTests
    {
        private string _rootDirectory;

        [SetUp]
        public void SetUp()
        {
            _rootDirectory = Path.Combine(
                Path.GetTempPath(),
                "Doji.ModelHub.StatusTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rootDirectory);
            Environment.SetEnvironmentVariable("DOJI_HF_HUB_CACHE_ROOT", Path.Combine(_rootDirectory, "SharedCache"));
            CleanupProjectArtifacts();
        }

        [TearDown]
        public void TearDown()
        {
            CleanupProjectArtifacts();

            if (Directory.Exists(_rootDirectory))
            {
                Directory.Delete(_rootDirectory, true);
            }

            Environment.SetEnvironmentVariable("DOJI_HF_HUB_CACHE_ROOT", null);
        }

        [Test]
        public void BuildStatusReport_ReturnsMissingWhenRawCacheIsAbsent()
        {
            var dependency = CreateDependency();
            var report = BuildStatusReport(dependency);

            Assert.That(report.MissingCount, Is.EqualTo(1));
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(DependencyReasonCode.RawCacheRevisionRefMissing));
            Assert.That(report.DependencyStatuses[0].MaintainerMessage, Is.EqualTo("Raw cache revision ref is missing."));
        }

        [Test]
        public void BuildStatusReport_ReturnsFailedWhenRepositoryIsKnownInvalid()
        {
            var dependency = CreateDependency(repository: $"doji/invalid-{Guid.NewGuid():N}");
            RepositoryValidationCacheService.WriteCachedRepositoryExists(dependency.Repository, false);

            var report = BuildStatusReport(dependency);

            Assert.That(report.FailedCount, Is.EqualTo(1));
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(DependencyReasonCode.InvalidRepository));
            Assert.That(report.DependencyStatuses[0].MaintainerMessage, Is.EqualTo("Repository was not found on Hugging Face."));
        }

        [Test]
        public void BuildStatusReport_ReturnsMissingWhenImportBindingIsAbsent()
        {
            var dependency = CreateDependency();
            WriteRawCache(dependency);

            var report = BuildStatusReport(dependency);

            Assert.That(report.MissingCount, Is.EqualTo(1));
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(DependencyReasonCode.ImportBindingMissing));
            Assert.That(report.DependencyStatuses[0].MaintainerMessage, Is.EqualTo("Import binding metadata file is missing."));
        }

        /// <summary>
        /// Verifies selected-file dependencies become missing when the raw cache lacks one selected file.
        /// </summary>
        [Test]
        public void BuildStatusReport_ReturnsMissingWhenSelectedRawCacheFileIsAbsent()
        {
            var dependency = CreateDependency(selectedFiles: new[] { "models/model.onnx", "tokenizer.json" });
            WriteRawCache(dependency, includeTokenizer: false);

            var report = BuildStatusReport(dependency);

            Assert.That(report.MissingCount, Is.EqualTo(1));
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(DependencyReasonCode.RawCacheFilesMissing));
            Assert.That(report.DependencyStatuses[0].MaintainerMessage, Does.Contain("tokenizer.json"));
        }

        /// <summary>
        /// Verifies whole-repo dependencies fail when raw inventory has files but none of them are ONNX.
        /// </summary>
        [Test]
        public void BuildStatusReport_ReturnsFailedWhenWholeRepositorySelectionContainsNoOnnxFiles()
        {
            var dependency = CreateDependency();
            WriteRawCacheWithoutOnnx(dependency);

            var report = BuildStatusReport(dependency);

            Assert.That(report.FailedCount, Is.EqualTo(1));
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(DependencyReasonCode.NoSelectedOnnxFiles));
            Assert.That(report.DependencyStatuses[0].MaintainerMessage, Is.EqualTo("Dependency selection does not include any ONNX file."));
        }

        /// <summary>
        /// Verifies optional-only ONNX dependencies prompt for selection before any cache download is required.
        /// </summary>
        [Test]
        public void BuildStatusReport_ReturnsSelectionRequiredWhenAllOnnxFilesAreOptionalAndNoneAreEnabled()
        {
            var dependency = CreateDependency(
                selectedFiles: Array.Empty<string>(),
                optionalFiles: new[] { "models/model.onnx", "models/model_q4.onnx" });

            var report = BuildStatusReport(dependency);

            Assert.That(report.MissingCount, Is.EqualTo(1));
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(DependencyReasonCode.SelectionRequired));
            Assert.That(report.DependencyStatuses[0].MaintainerMessage, Is.EqualTo("This dependency has optional ONNX files, but none are currently enabled for this project owner."));
        }

        [Test]
        public void BuildStatusReport_ReturnsReadyWhenRawBindingAndSharedArtifactsMatch()
        {
            var dependency = CreateDependency();
            WriteRawCache(dependency);
            WriteGeneratedArtifacts(dependency);

            var report = BuildStatusReport(dependency);

            Assert.That(report.ReadyCount, Is.EqualTo(1));
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(DependencyReasonCode.None));
            Assert.That(report.DependencyStatuses[0].MaintainerMessage, Is.EqualTo("Raw repo snapshot, import binding, and generated runtime files are current."));
        }

        /// <summary>
        /// Verifies adding a broader same-repository dependency later leaves existing owner ready but marks the new owner as cache-update-needed.
        /// </summary>
        [Test]
        public void BuildStatusReport_ReturnsReadyAndMissingForExistingAndNewSameRepositoryOwnersWhenSelectionGrows()
        {
            var existingDependency = CreateDependency(packageId: "com.doji.existing", selectedFiles: new[] { "models/model.onnx" });
            var newDependency = CreateDependency(packageId: "com.doji.new", selectedFiles: new[] { "models/model.onnx", "models/model_bnb4.onnx" });
            WriteRawCacheWithMissingAdditionalOnnx(existingDependency);
            WriteGeneratedArtifactsForExpandedInventory(existingDependency);

            var report = BuildStatusReport(existingDependency, newDependency);

            Assert.That(report.DependencyStatuses, Has.Length.EqualTo(2));

            var existingStatus = report.DependencyStatuses.Single(status => status.Dependency.PackageId == "com.doji.existing");
            Assert.That(existingStatus.State, Is.EqualTo(DependencyState.Ready));
            Assert.That(existingStatus.ReasonCode, Is.EqualTo(DependencyReasonCode.None));

            var newStatus = report.DependencyStatuses.Single(status => status.Dependency.PackageId == "com.doji.new");
            Assert.That(newStatus.State, Is.EqualTo(DependencyState.Missing));
            Assert.That(newStatus.ReasonCode, Is.EqualTo(DependencyReasonCode.RawCacheFilesMissing));
            Assert.That(newStatus.MaintainerMessage, Does.Contain("models/model_bnb4.onnx"));
        }

        /// <summary>
        /// Verifies same repository declared at multiple revisions blocks both dependencies before cache or import checks run.
        /// </summary>
        [Test]
        public void BuildStatusReport_ReturnsFailedWhenRepositoryHasMultipleRequestedRevisions()
        {
            var mainDependency = CreateDependency(packageId: "com.doji.main", revision: "main");
            var releaseDependency = CreateDependency(packageId: "com.doji.release", revision: "refs/tags/v1");

            var report = BuildStatusReport(mainDependency, releaseDependency);

            Assert.That(report.FailedCount, Is.EqualTo(2));
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(DependencyReasonCode.RevisionConflict));
            Assert.That(report.DependencyStatuses[1].ReasonCode, Is.EqualTo(DependencyReasonCode.RevisionConflict));
            Assert.That(report.DependencyStatuses[0].MaintainerMessage, Does.Contain("multiple requested revisions"));
            Assert.That(report.DependencyStatuses[0].MaintainerMessage, Does.Contain("main"));
            Assert.That(report.DependencyStatuses[0].MaintainerMessage, Does.Contain("refs/tags/v1"));
        }

        [Test]
        public void BuildStatusReport_ReturnsReadyWhenRawCacheWasRemovedButImportedFilesRemain()
        {
            var dependency = CreateDependency();
            WriteRawCache(dependency);
            WriteGeneratedArtifacts(dependency);
            DeleteRawCache(dependency);

            var report = BuildStatusReport(dependency);

            Assert.That(report.ReadyCount, Is.EqualTo(1));
            Assert.That(
                report.DependencyStatuses[0].MaintainerMessage,
                Is.EqualTo("Generated runtime files are present. Raw cache has been removed from this machine."));
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(DependencyReasonCode.RawCacheRemovedAfterImport));
        }

        [Test]
        public void BuildStatusReport_ReturnsStaleWhenBindingManifestHashDiffers()
        {
            var dependency = CreateDependency();
            WriteRawCache(dependency);
            WriteGeneratedArtifacts(dependency, bindingManifestHash: "old-manifest-hash");

            var report = BuildStatusReport(dependency);

            Assert.That(report.StaleCount, Is.EqualTo(1));
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(DependencyReasonCode.ImportBindingStale));
            Assert.That(
                report.DependencyStatuses[0].MaintainerMessage,
                Is.EqualTo("Import binding metadata is stale: manifest hash expected `manifesthash` but found `old-manifest-hash`."));
        }

        [Test]
        public void BuildStatusReport_ReturnsMissingWhenSharedImportMetadataIsAbsent()
        {
            var dependency = CreateDependency();
            WriteRawCache(dependency);
            WriteGeneratedArtifacts(dependency);
            DeleteSharedMetadata(dependency);

            var report = BuildStatusReport(dependency);

            Assert.That(report.MissingCount, Is.EqualTo(1));
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(DependencyReasonCode.SharedImportMetadataMissing));
            Assert.That(report.DependencyStatuses[0].MaintainerMessage, Is.EqualTo("Shared import metadata file is missing."));
        }

        [Test]
        public void BuildStatusReport_ReturnsFailedWhenSharedImportMetadataIsInvalid()
        {
            var dependency = CreateDependency();
            WriteRawCache(dependency);
            WriteGeneratedArtifacts(dependency);
            WriteInvalidSharedMetadata(dependency);

            var report = BuildStatusReport(dependency);

            Assert.That(report.FailedCount, Is.EqualTo(1));
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(DependencyReasonCode.SharedImportMetadataInvalid));
            Assert.That(report.DependencyStatuses[0].MaintainerMessage, Is.EqualTo("Shared import metadata is invalid."));
        }

        [Test]
        public void BuildStatusReport_ReturnsMissingWhenGeneratedRuntimeFileIsAbsent()
        {
            var dependency = CreateDependency();
            WriteRawCache(dependency);
            WriteGeneratedArtifacts(dependency);
            DeleteGeneratedRuntimeFiles(dependency);

            var report = BuildStatusReport(dependency);

            Assert.That(report.MissingCount, Is.EqualTo(1));
            Assert.That(
                report.DependencyStatuses[0].MaintainerMessage,
                Is.EqualTo("Generated runtime file is missing: hf-hub/doji/test-model/models/model.sentis."));
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(DependencyReasonCode.RuntimeFileMissing));
        }

        [Test]
        public void BuildStatusReport_ReturnsFailedWhenBindingMetadataIsInvalid()
        {
            var dependency = CreateDependency();
            WriteRawCache(dependency);
            WriteRuntimeFiles(dependency);

            var bindingMetadataAbsolutePath = ProjectPaths.ToProjectAbsolutePath(
                ProjectPaths.GetImportBindingMetadataFilePath(dependency.PackageId, dependency.ModelId));
            Directory.CreateDirectory(Path.GetDirectoryName(bindingMetadataAbsolutePath) ?? throw new InvalidOperationException());
            File.WriteAllText(bindingMetadataAbsolutePath, "{ invalid json");

            var report = BuildStatusReport(dependency);

            Assert.That(report.FailedCount, Is.EqualTo(1));
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(DependencyReasonCode.ImportBindingInvalid));
            Assert.That(report.DependencyStatuses[0].MaintainerMessage, Is.EqualTo("Import binding metadata is invalid."));
        }

        [Test]
        public void BuildStatusReport_ReturnsStaleWhenRecipeSetHashDiffers()
        {
            var dependency = CreateDependency();
            WriteRawCache(dependency);
            WriteGeneratedArtifacts(dependency, bindingRecipeSetHash: "old-recipe-hash");

            var report = BuildStatusReport(dependency);

            Assert.That(report.StaleCount, Is.EqualTo(1));
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(DependencyReasonCode.ImportBindingStale));
            Assert.That(report.DependencyStatuses[0].MaintainerMessage, Does.Contain("recipe set hash"));
        }

        /// <summary>
        /// Verifies import binding becomes stale when owner file selection changed even if raw cache still contains the files.
        /// </summary>
        [Test]
        public void BuildStatusReport_ReturnsStaleWhenBindingSelectionHashDiffers()
        {
            var dependency = CreateDependency(selectedFiles: new[] { "models/model.onnx" });
            WriteRawCache(dependency);
            WriteGeneratedArtifacts(dependency);

            var bindingMetadataAbsolutePath = ProjectPaths.ToProjectAbsolutePath(
                ProjectPaths.GetImportBindingMetadataFilePath(dependency.PackageId, dependency.ModelId));
            var bindingMetadata = JsonConvert.DeserializeObject<ImportBindingMetadata>(File.ReadAllText(bindingMetadataAbsolutePath));
            bindingMetadata.DependencySelectionHash = "old-selection-hash";
            File.WriteAllText(bindingMetadataAbsolutePath, JsonConvert.SerializeObject(bindingMetadata, Formatting.Indented));

            var report = BuildStatusReport(dependency);

            Assert.That(report.StaleCount, Is.EqualTo(1));
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(DependencyReasonCode.ImportBindingStale));
            Assert.That(report.DependencyStatuses[0].MaintainerMessage, Does.Contain("selection hash"));
        }

        private static StatusReport BuildStatusReport(params DiscoveredModelDependency[] dependencies)
        {
            var discoveryReport = new DiscoveryReport(
                dependencies,
                Array.Empty<ManifestDiscoveryIssue>());
            return new StatusService().BuildStatusReport(discoveryReport);
        }

        private static DiscoveredModelDependency CreateDependency(
            string packageId = "com.doji.test",
            string revision = "main",
            string repository = "doji/test-model",
            string[] selectedFiles = null,
            string[] optionalFiles = null,
            string[] enabledOptionalFiles = null)
        {
            return new DiscoveredModelDependency(
                packageId,
                "Test Package",
                "1.0.0",
                $"/tmp/{packageId}",
                $"/tmp/{packageId}/hf-hub-manifest.json",
                "manifesthash",
                new ModelDependencyDefinition(
                    repository,
                    revision,
                    selectedFiles,
                    optionalFiles),
                enabledOptionalFiles);
        }

        private static void WriteRawCache(DiscoveredModelDependency dependency, string commitHash = "abc123", bool includeTokenizer = true)
        {
            var rawFilePath = CachePaths.GetSnapshotFilePath(dependency.Repository, commitHash, "models/model.onnx");
            var rawMetadataPath = CachePaths.GetSnapshotMetadataPath(dependency.Repository, commitHash);
            var revisionRefPath = CachePaths.GetRevisionRefPath(dependency.Repository, dependency.Revision);
            var tokenizerPath = CachePaths.GetSnapshotFilePath(dependency.Repository, commitHash, "tokenizer.json");

            Directory.CreateDirectory(Path.GetDirectoryName(rawFilePath) ?? throw new InvalidOperationException());
            Directory.CreateDirectory(Path.GetDirectoryName(revisionRefPath) ?? throw new InvalidOperationException());

            File.WriteAllText(rawFilePath, "onnx");
            if (includeTokenizer)
            {
                File.WriteAllText(tokenizerPath, "tokenizer");
            }

            File.WriteAllText(revisionRefPath, commitHash);
            File.WriteAllText(
                rawMetadataPath,
                JsonConvert.SerializeObject(new RawCacheMetadata
                {
                    Repository = dependency.Repository,
                    RequestedRevision = dependency.Revision,
                    ResolvedCommitHash = commitHash,
                    CachedSnapshotRoot = CachePaths.GetSnapshotRoot(dependency.Repository, commitHash),
                    RepositoryFiles = new[]
                    {
                        new RepositoryFileMetadata
                        {
                            RepoPath = "models/model.onnx",
                            SizeInBytes = 4
                        },
                        new RepositoryFileMetadata
                        {
                            RepoPath = "tokenizer.json",
                            SizeInBytes = 9
                        }
                    },
                    Files = new[]
                    {
                        new RawCacheFileMetadata
                        {
                            RepoPath = "models/model.onnx",
                            CachedFilePath = rawFilePath
                        },
                        new RawCacheFileMetadata
                        {
                            RepoPath = "tokenizer.json",
                            CachedFilePath = tokenizerPath
                        }
                    },
                    DownloadedAtUtc = DateTime.UtcNow.ToString("O")
                }, Formatting.Indented));
        }

        private static void WriteGeneratedArtifacts(
            DiscoveredModelDependency dependency,
            string commitHash = "abc123",
            string bindingManifestHash = null,
            string bindingRecipeSetHash = null)
        {
            var runtimeFiles = WriteRuntimeFiles(dependency, commitHash);
            var sentisVersion = StatusService.GetCurrentSentisVersion();
            var recipeSetHash = bindingRecipeSetHash ?? OnnxImportMetadataUtility.ComputeRecipeSetHash(new[] { "models/model.onnx" });
            var sharedMetadataPath = ProjectPaths.GetSharedImportMetadataFilePath(dependency.Repository, commitHash, sentisVersion, recipeSetHash);
            var sharedMetadataAbsolutePath = ProjectPaths.ToProjectAbsolutePath(sharedMetadataPath);
            var bindingMetadataAbsolutePath = ProjectPaths.ToProjectAbsolutePath(
                ProjectPaths.GetImportBindingMetadataFilePath(dependency.PackageId, dependency.ModelId));

            Directory.CreateDirectory(Path.GetDirectoryName(sharedMetadataAbsolutePath) ?? throw new InvalidOperationException());
            Directory.CreateDirectory(Path.GetDirectoryName(bindingMetadataAbsolutePath) ?? throw new InvalidOperationException());

            File.WriteAllText(
                sharedMetadataAbsolutePath,
                JsonConvert.SerializeObject(new SharedImportMetadata
                {
                    Repository = dependency.Repository,
                    ResolvedCommitHash = commitHash,
                    RequestedRevisionHint = dependency.Revision,
                    RawSnapshotRootPath = CachePaths.GetSnapshotRoot(dependency.Repository, commitHash),
                    RawMetadataPath = CachePaths.GetSnapshotMetadataPath(dependency.Repository, commitHash),
                    RepositoryFiles = new[]
                    {
                        new RepositoryFileMetadata
                        {
                            RepoPath = "models/model.onnx",
                            SizeInBytes = 4
                        },
                        new RepositoryFileMetadata
                        {
                            RepoPath = "tokenizer.json",
                            SizeInBytes = 9
                        }
                    },
                    RawCacheSizeInBytes = 4,
                    SentisVersion = sentisVersion,
                    UnityVersion = UnityEngine.Application.unityVersion,
                    RecipeSetHash = recipeSetHash,
                    RuntimeSnapshotAssetPath = ProjectPaths.GetRuntimeSnapshotFolder(dependency.Repository, commitHash),
                    RuntimeFiles = runtimeFiles,
                    ImportedAtUtc = DateTime.UtcNow.ToString("O")
                }, Formatting.Indented));

            File.WriteAllText(
                bindingMetadataAbsolutePath,
                JsonConvert.SerializeObject(new ImportBindingMetadata
                {
                    PackageId = dependency.PackageId,
                    PackageVersion = dependency.PackageVersion,
                    ModelId = dependency.ModelId,
                    ManifestHash = bindingManifestHash ?? dependency.ManifestHash,
                    Repository = dependency.Repository,
                    RequestedRevision = dependency.Revision,
                    ResolvedCommitHash = commitHash,
                    SentisVersion = sentisVersion,
                    RecipeSetHash = recipeSetHash,
                    DependencySelectionHash = dependency.DependencySelectionHash,
                    SharedImportMetadataPath = sharedMetadataPath,
                    RuntimeRegistryAssetPath = ProjectPaths.GetRuntimeRegistryAssetPath(dependency.PackageId),
                    RuntimeRegistryRelativePath = ProjectPaths.ToStreamingAssetsRelativePath(ProjectPaths.GetRuntimeRegistryAssetPath(dependency.PackageId)),
                    BoundAtUtc = DateTime.UtcNow.ToString("O")
                }, Formatting.Indented));
        }

        private static void WriteRawCacheWithoutOnnx(DiscoveredModelDependency dependency, string commitHash = "abc123")
        {
            var rawMetadataPath = CachePaths.GetSnapshotMetadataPath(dependency.Repository, commitHash);
            var revisionRefPath = CachePaths.GetRevisionRefPath(dependency.Repository, dependency.Revision);
            var tokenizerPath = CachePaths.GetSnapshotFilePath(dependency.Repository, commitHash, "tokenizer.json");

            Directory.CreateDirectory(Path.GetDirectoryName(revisionRefPath) ?? throw new InvalidOperationException());
            Directory.CreateDirectory(Path.GetDirectoryName(tokenizerPath) ?? throw new InvalidOperationException());

            File.WriteAllText(tokenizerPath, "tokenizer");
            File.WriteAllText(revisionRefPath, commitHash);
            File.WriteAllText(
                rawMetadataPath,
                JsonConvert.SerializeObject(new RawCacheMetadata
                {
                    Repository = dependency.Repository,
                    RequestedRevision = dependency.Revision,
                    ResolvedCommitHash = commitHash,
                    CachedSnapshotRoot = CachePaths.GetSnapshotRoot(dependency.Repository, commitHash),
                    RepositoryFiles = new[]
                    {
                        new RepositoryFileMetadata
                        {
                            RepoPath = "tokenizer.json",
                            SizeInBytes = 9
                        }
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
                }, Formatting.Indented));
        }

        private static void WriteRawCacheWithMissingAdditionalOnnx(DiscoveredModelDependency dependency, string commitHash = "abc123")
        {
            var rawModelPath = CachePaths.GetSnapshotFilePath(dependency.Repository, commitHash, "models/model.onnx");
            var rawMetadataPath = CachePaths.GetSnapshotMetadataPath(dependency.Repository, commitHash);
            var revisionRefPath = CachePaths.GetRevisionRefPath(dependency.Repository, dependency.Revision);

            Directory.CreateDirectory(Path.GetDirectoryName(rawModelPath) ?? throw new InvalidOperationException());
            Directory.CreateDirectory(Path.GetDirectoryName(revisionRefPath) ?? throw new InvalidOperationException());

            File.WriteAllText(rawModelPath, "onnx");
            File.WriteAllText(revisionRefPath, commitHash);
            File.WriteAllText(
                rawMetadataPath,
                JsonConvert.SerializeObject(new RawCacheMetadata
                {
                    Repository = dependency.Repository,
                    RequestedRevision = dependency.Revision,
                    ResolvedCommitHash = commitHash,
                    CachedSnapshotRoot = CachePaths.GetSnapshotRoot(dependency.Repository, commitHash),
                    RepositoryFiles = new[]
                    {
                        new RepositoryFileMetadata
                        {
                            RepoPath = "models/model.onnx",
                            SizeInBytes = 4
                        },
                        new RepositoryFileMetadata
                        {
                            RepoPath = "models/model_bnb4.onnx",
                            SizeInBytes = 8
                        }
                    },
                    Files = new[]
                    {
                        new RawCacheFileMetadata
                        {
                            RepoPath = "models/model.onnx",
                            CachedFilePath = rawModelPath
                        }
                    },
                    DownloadedAtUtc = DateTime.UtcNow.ToString("O")
                }, Formatting.Indented));
        }

        private static void WriteGeneratedArtifactsForExpandedInventory(DiscoveredModelDependency dependency, string commitHash = "abc123")
        {
            var runtimeFiles = WriteRuntimeFiles(dependency, commitHash);
            var sentisVersion = StatusService.GetCurrentSentisVersion();
            var recipeSetHash = OnnxImportMetadataUtility.ComputeRecipeSetHash(new[] { "models/model.onnx" });
            var sharedMetadataPath = ProjectPaths.GetSharedImportMetadataFilePath(dependency.Repository, commitHash, sentisVersion, recipeSetHash);
            var sharedMetadataAbsolutePath = ProjectPaths.ToProjectAbsolutePath(sharedMetadataPath);
            var bindingMetadataAbsolutePath = ProjectPaths.ToProjectAbsolutePath(
                ProjectPaths.GetImportBindingMetadataFilePath(dependency.PackageId, dependency.ModelId));

            Directory.CreateDirectory(Path.GetDirectoryName(sharedMetadataAbsolutePath) ?? throw new InvalidOperationException());
            Directory.CreateDirectory(Path.GetDirectoryName(bindingMetadataAbsolutePath) ?? throw new InvalidOperationException());

            File.WriteAllText(
                sharedMetadataAbsolutePath,
                JsonConvert.SerializeObject(new SharedImportMetadata
                {
                    Repository = dependency.Repository,
                    ResolvedCommitHash = commitHash,
                    RequestedRevisionHint = dependency.Revision,
                    RawSnapshotRootPath = CachePaths.GetSnapshotRoot(dependency.Repository, commitHash),
                    RawMetadataPath = CachePaths.GetSnapshotMetadataPath(dependency.Repository, commitHash),
                    RepositoryFiles = new[]
                    {
                        new RepositoryFileMetadata
                        {
                            RepoPath = "models/model.onnx",
                            SizeInBytes = 4
                        },
                        new RepositoryFileMetadata
                        {
                            RepoPath = "models/model_bnb4.onnx",
                            SizeInBytes = 8
                        }
                    },
                    RawCacheSizeInBytes = 4,
                    SentisVersion = sentisVersion,
                    UnityVersion = UnityEngine.Application.unityVersion,
                    RecipeSetHash = recipeSetHash,
                    RuntimeSnapshotAssetPath = ProjectPaths.GetRuntimeSnapshotFolder(dependency.Repository, commitHash),
                    RuntimeFiles = runtimeFiles,
                    ImportedAtUtc = DateTime.UtcNow.ToString("O")
                }, Formatting.Indented));

            File.WriteAllText(
                bindingMetadataAbsolutePath,
                JsonConvert.SerializeObject(new ImportBindingMetadata
                {
                    PackageId = dependency.PackageId,
                    PackageVersion = dependency.PackageVersion,
                    ModelId = dependency.ModelId,
                    ManifestHash = dependency.ManifestHash,
                    Repository = dependency.Repository,
                    RequestedRevision = dependency.Revision,
                    ResolvedCommitHash = commitHash,
                    SentisVersion = sentisVersion,
                    RecipeSetHash = recipeSetHash,
                    DependencySelectionHash = dependency.DependencySelectionHash,
                    SharedImportMetadataPath = sharedMetadataPath,
                    RuntimeRegistryAssetPath = ProjectPaths.GetRuntimeRegistryAssetPath(dependency.PackageId),
                    RuntimeRegistryRelativePath = ProjectPaths.ToStreamingAssetsRelativePath(ProjectPaths.GetRuntimeRegistryAssetPath(dependency.PackageId)),
                    BoundAtUtc = DateTime.UtcNow.ToString("O")
                }, Formatting.Indented));
        }

        private static ImportedRuntimeFileMetadata[] WriteRuntimeFiles(DiscoveredModelDependency dependency, string commitHash = "abc123")
        {
            var sentisAssetPath = ProjectPaths.GetRuntimeSentisAssetPath(dependency.Repository, commitHash, "models/model.onnx");
            var sentisAbsolutePath = ProjectPaths.ToAbsolutePath(sentisAssetPath);

            Directory.CreateDirectory(Path.GetDirectoryName(sentisAbsolutePath) ?? throw new InvalidOperationException());
            File.WriteAllText(sentisAbsolutePath, "sentis");

            return new[]
            {
                new ImportedRuntimeFileMetadata
                {
                    RepoPath = "models/model.onnx",
                    Kind = "sentis",
                    AssetPath = sentisAssetPath,
                    RelativePath = ProjectPaths.ToStreamingAssetsRelativePath(sentisAssetPath),
                    SizeInBytes = 6,
                    RecipeHash = OnnxImportMetadataUtility.ComputeRecipeHash(new OnnxConversionRecipe()),
                    ConversionPipelineVersion = OnnxImportMetadataUtility.DefaultConversionPipelineVersion
                }
            };
        }

        private static void DeleteRawCache(DiscoveredModelDependency dependency)
        {
            var repositoryRoot = CachePaths.GetRepositoryRoot(dependency.Repository);
            if (Directory.Exists(repositoryRoot))
            {
                Directory.Delete(repositoryRoot, true);
            }
        }

        private static void DeleteSharedMetadata(DiscoveredModelDependency dependency, string commitHash = "abc123")
        {
            var sharedMetadataAbsolutePath = ProjectPaths.ToProjectAbsolutePath(
                ProjectPaths.GetSharedImportMetadataFilePath(
                    dependency.Repository,
                    commitHash,
                    StatusService.GetCurrentSentisVersion(),
                    OnnxImportMetadataUtility.ComputeRecipeSetHash(new[] { "models/model.onnx" })));
            if (File.Exists(sharedMetadataAbsolutePath))
            {
                File.Delete(sharedMetadataAbsolutePath);
            }
        }

        private static void WriteInvalidSharedMetadata(DiscoveredModelDependency dependency, string commitHash = "abc123")
        {
            var sharedMetadataAbsolutePath = ProjectPaths.ToProjectAbsolutePath(
                ProjectPaths.GetSharedImportMetadataFilePath(
                    dependency.Repository,
                    commitHash,
                    StatusService.GetCurrentSentisVersion(),
                    OnnxImportMetadataUtility.ComputeRecipeSetHash(new[] { "models/model.onnx" })));
            File.WriteAllText(sharedMetadataAbsolutePath, "{ invalid json");
        }

        private static void CleanupProjectArtifacts()
        {
            DeleteAssetIfExists("Assets/StreamingAssets/hf-hub");
            DeleteProjectFolderIfExists("Library/Doji/HfHub/imports");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static void DeleteAssetIfExists(string assetPath)
        {
            if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null || Directory.Exists(ProjectPaths.ToAbsolutePath(assetPath)))
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

        private static void DeleteGeneratedRuntimeFiles(DiscoveredModelDependency dependency)
        {
            var sentisAssetPath = ProjectPaths.GetRuntimeSentisAssetPath(dependency.Repository, "abc123", "models/model.onnx");
            var sentisAbsolutePath = ProjectPaths.ToAbsolutePath(sentisAssetPath);
            if (File.Exists(sentisAbsolutePath))
            {
                File.Delete(sentisAbsolutePath);
            }
        }
    }
}
