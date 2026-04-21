using System;
using System.IO;
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
        public void BuildStatusReport_ReturnsMissingWhenImportBindingIsAbsent()
        {
            var dependency = CreateDependency();
            WriteRawCache(dependency);

            var report = BuildStatusReport(dependency);

            Assert.That(report.MissingCount, Is.EqualTo(1));
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(DependencyReasonCode.ImportBindingMissing));
            Assert.That(report.DependencyStatuses[0].MaintainerMessage, Is.EqualTo("Import binding metadata file is missing."));
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

        private static StatusReport BuildStatusReport(DiscoveredModelDependency dependency)
        {
            var discoveryReport = new DiscoveryReport(
                new[] { dependency },
                Array.Empty<ManifestDiscoveryIssue>());
            return new StatusService().BuildStatusReport(discoveryReport);
        }

        private static DiscoveredModelDependency CreateDependency()
        {
            return new DiscoveredModelDependency(
                "com.doji.test",
                "Test Package",
                "1.0.0",
                "/tmp/com.doji.test",
                "/tmp/com.doji.test/hf-hub-manifest.json",
                "manifesthash",
                new ModelDependencyDefinition(
                    "doji/test-model",
                    "main"));
        }

        private static void WriteRawCache(DiscoveredModelDependency dependency, string commitHash = "abc123")
        {
            var rawFilePath = CachePaths.GetSnapshotFilePath(dependency.Repository, commitHash, "models/model.onnx");
            var rawMetadataPath = CachePaths.GetSnapshotMetadataPath(dependency.Repository, commitHash);
            var revisionRefPath = CachePaths.GetRevisionRefPath(dependency.Repository, dependency.Revision);

            Directory.CreateDirectory(Path.GetDirectoryName(rawFilePath) ?? throw new InvalidOperationException());
            Directory.CreateDirectory(Path.GetDirectoryName(revisionRefPath) ?? throw new InvalidOperationException());

            File.WriteAllText(rawFilePath, "onnx");
            File.WriteAllText(revisionRefPath, commitHash);
            File.WriteAllText(
                rawMetadataPath,
                JsonConvert.SerializeObject(new RawCacheMetadata
                {
                    Repository = dependency.Repository,
                    RequestedRevision = dependency.Revision,
                    ResolvedCommitHash = commitHash,
                    CachedSnapshotRoot = CachePaths.GetSnapshotRoot(dependency.Repository, commitHash),
                    Files = new[]
                    {
                        new RawCacheFileMetadata
                        {
                            RepoPath = "models/model.onnx",
                            CachedFilePath = rawFilePath
                        }
                    },
                    DownloadedAtUtc = DateTime.UtcNow.ToString("O")
                }, Formatting.Indented));
        }

        private static void WriteGeneratedArtifacts(
            DiscoveredModelDependency dependency,
            string commitHash = "abc123",
            string bindingManifestHash = null)
        {
            var runtimeFiles = WriteRuntimeFiles(dependency, commitHash);
            var sentisVersion = StatusService.GetCurrentSentisVersion();
            var sharedMetadataPath = ProjectPaths.GetSharedImportMetadataFilePath(dependency.Repository, commitHash, sentisVersion);
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
                    RawCacheSizeInBytes = 4,
                    SentisVersion = sentisVersion,
                    UnityVersion = UnityEngine.Application.unityVersion,
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
                    SizeInBytes = 6
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
                    StatusService.GetCurrentSentisVersion()));
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
                    StatusService.GetCurrentSentisVersion()));
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
