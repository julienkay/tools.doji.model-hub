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
    internal sealed class HfHubStatusServiceTests
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
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(HfHubDependencyReasonCode.RawCacheRevisionRefMissing));
            Assert.That(report.DependencyStatuses[0].MaintainerMessage, Is.EqualTo("Raw cache revision ref is missing."));
        }

        [Test]
        public void BuildStatusReport_ReturnsMissingWhenImportBindingIsAbsent()
        {
            var dependency = CreateDependency();
            WriteRawCache(dependency);

            var report = BuildStatusReport(dependency);

            Assert.That(report.MissingCount, Is.EqualTo(1));
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(HfHubDependencyReasonCode.ImportBindingMissing));
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
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(HfHubDependencyReasonCode.None));
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
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(HfHubDependencyReasonCode.RawCacheRemovedAfterImport));
        }

        [Test]
        public void BuildStatusReport_ReturnsStaleWhenBindingManifestHashDiffers()
        {
            var dependency = CreateDependency();
            WriteRawCache(dependency);
            WriteGeneratedArtifacts(dependency, bindingManifestHash: "old-manifest-hash");

            var report = BuildStatusReport(dependency);

            Assert.That(report.StaleCount, Is.EqualTo(1));
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(HfHubDependencyReasonCode.ImportBindingStale));
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
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(HfHubDependencyReasonCode.SharedImportMetadataMissing));
            Assert.That(report.DependencyStatuses[0].MaintainerMessage, Is.EqualTo("Shared import metadata file is missing."));
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
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(HfHubDependencyReasonCode.RuntimeFileMissing));
        }

        [Test]
        public void BuildStatusReport_ReturnsFailedWhenBindingMetadataIsInvalid()
        {
            var dependency = CreateDependency();
            WriteRawCache(dependency);
            WriteRuntimeFiles(dependency);

            var bindingMetadataAbsolutePath = HfHubProjectPaths.ToProjectAbsolutePath(
                HfHubProjectPaths.GetImportBindingMetadataFilePath(dependency.PackageId, dependency.ModelId));
            Directory.CreateDirectory(Path.GetDirectoryName(bindingMetadataAbsolutePath) ?? throw new InvalidOperationException());
            File.WriteAllText(bindingMetadataAbsolutePath, "{ invalid json");

            var report = BuildStatusReport(dependency);

            Assert.That(report.FailedCount, Is.EqualTo(1));
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(HfHubDependencyReasonCode.ImportBindingInvalid));
            Assert.That(report.DependencyStatuses[0].MaintainerMessage, Is.EqualTo("Import binding metadata is invalid."));
        }

        private static HfHubStatusReport BuildStatusReport(HfHubDiscoveredModelDependency dependency)
        {
            var discoveryReport = new HfHubDiscoveryReport(
                new[] { dependency },
                Array.Empty<HfHubManifestDiscoveryIssue>());
            return new HfHubStatusService().BuildStatusReport(discoveryReport);
        }

        private static HfHubDiscoveredModelDependency CreateDependency()
        {
            return new HfHubDiscoveredModelDependency(
                "com.doji.test",
                "Test Package",
                "1.0.0",
                "/tmp/com.doji.test",
                "/tmp/com.doji.test/hf-hub-manifest.json",
                "manifesthash",
                new HfHubModelDependencyDefinition(
                    "doji/test-model",
                    "main"));
        }

        private static void WriteRawCache(HfHubDiscoveredModelDependency dependency, string commitHash = "abc123")
        {
            var rawFilePath = HfHubCachePaths.GetSnapshotFilePath(dependency.Repository, commitHash, "models/model.onnx");
            var rawMetadataPath = HfHubCachePaths.GetSnapshotMetadataPath(dependency.Repository, commitHash);
            var revisionRefPath = HfHubCachePaths.GetRevisionRefPath(dependency.Repository, dependency.Revision);

            Directory.CreateDirectory(Path.GetDirectoryName(rawFilePath) ?? throw new InvalidOperationException());
            Directory.CreateDirectory(Path.GetDirectoryName(revisionRefPath) ?? throw new InvalidOperationException());

            File.WriteAllText(rawFilePath, "onnx");
            File.WriteAllText(revisionRefPath, commitHash);
            File.WriteAllText(
                rawMetadataPath,
                JsonConvert.SerializeObject(new HfHubRawCacheMetadata
                {
                    Repository = dependency.Repository,
                    RequestedRevision = dependency.Revision,
                    ResolvedCommitHash = commitHash,
                    CachedSnapshotRoot = HfHubCachePaths.GetSnapshotRoot(dependency.Repository, commitHash),
                    Files = new[]
                    {
                        new HfHubRawCacheFileMetadata
                        {
                            RepoPath = "models/model.onnx",
                            CachedFilePath = rawFilePath
                        }
                    },
                    DownloadedAtUtc = DateTime.UtcNow.ToString("O")
                }, Formatting.Indented));
        }

        private static void WriteGeneratedArtifacts(
            HfHubDiscoveredModelDependency dependency,
            string commitHash = "abc123",
            string bindingManifestHash = null)
        {
            var runtimeFiles = WriteRuntimeFiles(dependency, commitHash);
            var sentisVersion = HfHubStatusService.GetCurrentSentisVersion();
            var sharedMetadataPath = HfHubProjectPaths.GetSharedImportMetadataFilePath(dependency.Repository, commitHash, sentisVersion);
            var sharedMetadataAbsolutePath = HfHubProjectPaths.ToProjectAbsolutePath(sharedMetadataPath);
            var bindingMetadataAbsolutePath = HfHubProjectPaths.ToProjectAbsolutePath(
                HfHubProjectPaths.GetImportBindingMetadataFilePath(dependency.PackageId, dependency.ModelId));

            Directory.CreateDirectory(Path.GetDirectoryName(sharedMetadataAbsolutePath) ?? throw new InvalidOperationException());
            Directory.CreateDirectory(Path.GetDirectoryName(bindingMetadataAbsolutePath) ?? throw new InvalidOperationException());

            File.WriteAllText(
                sharedMetadataAbsolutePath,
                JsonConvert.SerializeObject(new HfHubSharedImportMetadata
                {
                    Repository = dependency.Repository,
                    ResolvedCommitHash = commitHash,
                    RequestedRevisionHint = dependency.Revision,
                    RawSnapshotRootPath = HfHubCachePaths.GetSnapshotRoot(dependency.Repository, commitHash),
                    RawMetadataPath = HfHubCachePaths.GetSnapshotMetadataPath(dependency.Repository, commitHash),
                    RawCacheSizeInBytes = 4,
                    SentisVersion = sentisVersion,
                    UnityVersion = UnityEngine.Application.unityVersion,
                    RuntimeSnapshotAssetPath = HfHubProjectPaths.GetRuntimeSnapshotFolder(dependency.Repository, commitHash),
                    RuntimeFiles = runtimeFiles,
                    ImportedAtUtc = DateTime.UtcNow.ToString("O")
                }, Formatting.Indented));

            File.WriteAllText(
                bindingMetadataAbsolutePath,
                JsonConvert.SerializeObject(new HfHubImportBindingMetadata
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
                    RuntimeRegistryAssetPath = HfHubProjectPaths.GetRuntimeRegistryAssetPath(dependency.PackageId),
                    RuntimeRegistryRelativePath = HfHubProjectPaths.ToStreamingAssetsRelativePath(HfHubProjectPaths.GetRuntimeRegistryAssetPath(dependency.PackageId)),
                    BoundAtUtc = DateTime.UtcNow.ToString("O")
                }, Formatting.Indented));
        }

        private static HfHubImportedRuntimeFileMetadata[] WriteRuntimeFiles(HfHubDiscoveredModelDependency dependency, string commitHash = "abc123")
        {
            var sentisAssetPath = HfHubProjectPaths.GetRuntimeSentisAssetPath(dependency.Repository, commitHash, "models/model.onnx");
            var sentisAbsolutePath = HfHubProjectPaths.ToAbsolutePath(sentisAssetPath);

            Directory.CreateDirectory(Path.GetDirectoryName(sentisAbsolutePath) ?? throw new InvalidOperationException());
            File.WriteAllText(sentisAbsolutePath, "sentis");

            return new[]
            {
                new HfHubImportedRuntimeFileMetadata
                {
                    RepoPath = "models/model.onnx",
                    Kind = "sentis",
                    AssetPath = sentisAssetPath,
                    RelativePath = HfHubProjectPaths.ToStreamingAssetsRelativePath(sentisAssetPath),
                    SizeInBytes = 6
                }
            };
        }

        private static void DeleteRawCache(HfHubDiscoveredModelDependency dependency)
        {
            var repositoryRoot = HfHubCachePaths.GetRepositoryRoot(dependency.Repository);
            if (Directory.Exists(repositoryRoot))
            {
                Directory.Delete(repositoryRoot, true);
            }
        }

        private static void DeleteSharedMetadata(HfHubDiscoveredModelDependency dependency, string commitHash = "abc123")
        {
            var sharedMetadataAbsolutePath = HfHubProjectPaths.ToProjectAbsolutePath(
                HfHubProjectPaths.GetSharedImportMetadataFilePath(
                    dependency.Repository,
                    commitHash,
                    HfHubStatusService.GetCurrentSentisVersion()));
            if (File.Exists(sharedMetadataAbsolutePath))
            {
                File.Delete(sharedMetadataAbsolutePath);
            }
        }

        private static void CleanupProjectArtifacts()
        {
            DeleteAssetIfExists("Assets/StreamingAssets/hf-hub");
            DeleteProjectFolderIfExists("Library/Doji/HfHub/imports");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static void DeleteAssetIfExists(string assetPath)
        {
            if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null || Directory.Exists(HfHubProjectPaths.ToAbsolutePath(assetPath)))
            {
                AssetDatabase.DeleteAsset(assetPath);
            }
        }

        private static void DeleteProjectFolderIfExists(string projectRelativePath)
        {
            var absolutePath = HfHubProjectPaths.ToProjectAbsolutePath(projectRelativePath);
            if (Directory.Exists(absolutePath))
            {
                Directory.Delete(absolutePath, true);
            }
        }

        private static void DeleteGeneratedRuntimeFiles(HfHubDiscoveredModelDependency dependency)
        {
            var sentisAssetPath = HfHubProjectPaths.GetRuntimeSentisAssetPath(dependency.Repository, "abc123", "models/model.onnx");
            var sentisAbsolutePath = HfHubProjectPaths.ToAbsolutePath(sentisAssetPath);
            if (File.Exists(sentisAbsolutePath))
            {
                File.Delete(sentisAbsolutePath);
            }
        }
    }
}
