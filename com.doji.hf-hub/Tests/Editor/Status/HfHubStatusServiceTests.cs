using System;
using System.IO;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEditor;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers repo-level local dependency state classification.
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

        /// <summary>
        /// Verifies missing raw revision ref reports missing state.
        /// </summary>
        [Test]
        public void BuildStatusReport_ReturnsMissingWhenRawCacheIsAbsent()
        {
            var dependency = CreateDependency();
            var report = BuildStatusReport(dependency);

            Assert.That(report.MissingCount, Is.EqualTo(1));
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(HfHubDependencyReasonCode.RawCacheRevisionRefMissing));
            Assert.That(report.DependencyStatuses[0].MaintainerMessage, Is.EqualTo("Raw cache revision ref is missing."));
        }

        /// <summary>
        /// Verifies missing import metadata reports missing state after raw snapshot exists.
        /// </summary>
        [Test]
        public void BuildStatusReport_ReturnsMissingWhenImportMetadataIsAbsent()
        {
            var dependency = CreateDependency();
            WriteRawCache(dependency);

            var report = BuildStatusReport(dependency);

            Assert.That(report.MissingCount, Is.EqualTo(1));
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(HfHubDependencyReasonCode.ImportMetadataMissing));
            Assert.That(report.DependencyStatuses[0].MaintainerMessage, Is.EqualTo("Import metadata file is missing."));
        }

        /// <summary>
        /// Verifies ready state when repo snapshot and import metadata match.
        /// </summary>
        [Test]
        public void BuildStatusReport_ReturnsReadyWhenRawAndImportArtifactsMatch()
        {
            var dependency = CreateDependency();
            WriteRawCache(dependency);
            WriteGeneratedArtifacts(dependency);

            var report = BuildStatusReport(dependency);

            Assert.That(report.ReadyCount, Is.EqualTo(1));
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(HfHubDependencyReasonCode.None));
            Assert.That(report.DependencyStatuses[0].MaintainerMessage, Is.EqualTo("Raw repo snapshot and generated runtime files are current."));
        }

        /// <summary>
        /// Verifies imported runtime files stay ready after raw cache is removed from this machine.
        /// </summary>
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

        /// <summary>
        /// Verifies stale state when import manifest hash differs.
        /// </summary>
        [Test]
        public void BuildStatusReport_ReturnsStaleWhenImportMetadataManifestHashDiffers()
        {
            var dependency = CreateDependency();
            WriteRawCache(dependency);
            WriteGeneratedArtifacts(dependency, importedManifestHash: "old-manifest-hash");

            var report = BuildStatusReport(dependency);

            Assert.That(report.StaleCount, Is.EqualTo(1));
            Assert.That(
                report.DependencyStatuses[0].MaintainerMessage,
                Is.EqualTo("Import metadata is stale: manifest hash expected `manifesthash` but found `old-manifest-hash`."));
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(HfHubDependencyReasonCode.ImportMetadataStale));
        }

        /// <summary>
        /// Verifies missing runtime file reports exact generated asset path.
        /// </summary>
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

        /// <summary>
        /// Verifies failed state when import metadata cannot be parsed.
        /// </summary>
        [Test]
        public void BuildStatusReport_ReturnsFailedWhenImportMetadataIsInvalid()
        {
            var dependency = CreateDependency();
            WriteRawCache(dependency);
            WriteRuntimeFiles(dependency);

            var importMetadataAbsolutePath = HfHubProjectPaths.ToProjectAbsolutePath(
                HfHubProjectPaths.GetImportMetadataFilePath(dependency.PackageId, dependency.ModelId));
            Directory.CreateDirectory(Path.GetDirectoryName(importMetadataAbsolutePath) ?? throw new InvalidOperationException());
            File.WriteAllText(importMetadataAbsolutePath, "{ invalid json");

            var report = BuildStatusReport(dependency);

            Assert.That(report.FailedCount, Is.EqualTo(1));
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(HfHubDependencyReasonCode.ImportMetadataInvalid));
            Assert.That(report.DependencyStatuses[0].MaintainerMessage, Is.EqualTo("Import metadata is invalid."));
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
            string importedManifestHash = null)
        {
            var runtimeFiles = WriteRuntimeFiles(dependency, commitHash);
            var importMetadataAbsolutePath = HfHubProjectPaths.ToProjectAbsolutePath(
                HfHubProjectPaths.GetImportMetadataFilePath(dependency.PackageId, dependency.ModelId));

            Directory.CreateDirectory(Path.GetDirectoryName(importMetadataAbsolutePath) ?? throw new InvalidOperationException());
            File.WriteAllText(
                importMetadataAbsolutePath,
                JsonConvert.SerializeObject(new HfHubImportedModelMetadata
                {
                    PackageId = dependency.PackageId,
                    PackageVersion = dependency.PackageVersion,
                    ModelId = dependency.ModelId,
                    ManifestHash = importedManifestHash ?? dependency.ManifestHash,
                    Repository = dependency.Repository,
                    RequestedRevision = dependency.Revision,
                    ResolvedCommitHash = commitHash,
                    RawSnapshotRootPath = HfHubCachePaths.GetSnapshotRoot(dependency.Repository, commitHash),
                    RawMetadataPath = HfHubCachePaths.GetSnapshotMetadataPath(dependency.Repository, commitHash),
                    RawCacheSizeInBytes = 4,
                    SentisVersion = HfHubStatusService.GetCurrentSentisVersion(),
                    UnityVersion = UnityEngine.Application.unityVersion,
                    RuntimeFiles = runtimeFiles,
                    ImportedAtUtc = DateTime.UtcNow.ToString("O")
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

        private static void CleanupProjectArtifacts()
        {
            DeleteAssetIfExists("Assets/StreamingAssets/hf-hub");
            DeleteProjectFolderIfExists("Library/Doji/HfHub/import-state");
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
