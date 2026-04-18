using System;
using System.IO;
using Newtonsoft.Json;
using NUnit.Framework;
using Unity.InferenceEngine;
using UnityEditor;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers local dependency state classification.
    /// Tests write synthetic raw-cache and generated-artifact files directly so status evaluation stays deterministic
    /// and does not depend on live Hub traffic or Unity's ONNX importer.
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
            Assert.That(report.DependencyStatuses[0].State, Is.EqualTo(HfHubDependencyState.Missing));
            Assert.That(report.DependencyStatuses[0].Message, Is.EqualTo("Raw cache revision ref is missing."));
        }

        [Test]
        public void BuildStatusReport_ReturnsMissingWhenGeneratedArtifactIsAbsent()
        {
            var dependency = CreateDependency();
            WriteRawCache(dependency);

            var report = BuildStatusReport(dependency);

            Assert.That(report.MissingCount, Is.EqualTo(1));
            Assert.That(report.DependencyStatuses[0].State, Is.EqualTo(HfHubDependencyState.Missing));
            Assert.That(report.DependencyStatuses[0].Message, Is.EqualTo("Runtime .sentis asset is missing."));
        }

        [Test]
        public void BuildStatusReport_ReturnsReadyWhenRawAndImportArtifactsMatch()
        {
            var dependency = CreateDependency();
            WriteRawCache(dependency);
            WriteGeneratedArtifacts(dependency);

            var report = BuildStatusReport(dependency);

            Assert.That(report.ReadyCount, Is.EqualTo(1));
            Assert.That(report.DependencyStatuses[0].State, Is.EqualTo(HfHubDependencyState.Ready));
            Assert.That(report.DependencyStatuses[0].Message, Is.EqualTo("Raw cache and runtime .sentis are current."));
        }

        [Test]
        public void BuildStatusReport_ReturnsStaleWhenImportMetadataManifestHashDiffers()
        {
            var dependency = CreateDependency();
            WriteRawCache(dependency);
            WriteGeneratedArtifacts(dependency, importedManifestHash: "old-manifest-hash");

            var report = BuildStatusReport(dependency);

            Assert.That(report.StaleCount, Is.EqualTo(1));
            Assert.That(report.DependencyStatuses[0].State, Is.EqualTo(HfHubDependencyState.Stale));
            Assert.That(report.DependencyStatuses[0].Message, Is.EqualTo("Import metadata manifest hash differs from current manifest."));
        }

        [Test]
        public void BuildStatusReport_ReturnsFailedWhenImportMetadataIsInvalid()
        {
            var dependency = CreateDependency();
            WriteRawCache(dependency);
            WriteSentisFile(dependency);

            var importMetadataAbsolutePath = HfHubProjectPaths.ToProjectAbsolutePath(
                HfHubProjectPaths.GetImportMetadataFilePath(dependency.PackageId, dependency.ModelId));
            Directory.CreateDirectory(Path.GetDirectoryName(importMetadataAbsolutePath) ?? throw new InvalidOperationException());
            File.WriteAllText(importMetadataAbsolutePath, "{ invalid json");

            var report = BuildStatusReport(dependency);

            Assert.That(report.FailedCount, Is.EqualTo(1));
            Assert.That(report.DependencyStatuses[0].State, Is.EqualTo(HfHubDependencyState.Failed));
            Assert.That(report.DependencyStatuses[0].Message, Is.EqualTo("Import metadata is invalid."));
        }

        [Test]
        public void ShouldPrompt_ReturnsTrueWhenAttentionIsNeeded()
        {
            var report = new HfHubStatusReport(
                Array.Empty<HfHubManifestDiscoveryIssue>(),
                new[]
                {
                    new HfHubDependencyStatus(
                        CreateDependency(),
                        HfHubDependencyState.Missing,
                        "Raw cache revision ref is missing.",
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null)
                });

            Assert.That(HfHubAutoPrompt.ShouldPrompt(report), Is.True);
        }

        [Test]
        public void ShouldPrompt_ReturnsFalseWhenEverythingIsReady()
        {
            var dependency = CreateDependency();
            var report = new HfHubStatusReport(
                Array.Empty<HfHubManifestDiscoveryIssue>(),
                new[]
                {
                    new HfHubDependencyStatus(
                        dependency,
                        HfHubDependencyState.Ready,
                        "Raw cache and runtime .sentis are current.",
                        "ref",
                        "raw",
                        "rawmeta",
                        new HfHubRawCacheMetadata(),
                        "Assets/StreamingAssets/hf-hub/models--doji--test-model/snapshots/abc123/model.sentis",
                        "Library/Doji/HfHub/import-state/com.doji.test/test-model/hf-hub-import.json",
                        new HfHubImportedModelMetadata())
                });

            Assert.That(HfHubAutoPrompt.ShouldPrompt(report), Is.False);
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
                    "test-model",
                    "Test Model",
                    "doji/test-model",
                    "main",
                    "models/model.onnx"));
        }

        private static void WriteRawCache(HfHubDiscoveredModelDependency dependency, string commitHash = "abc123")
        {
            var rawFilePath = HfHubCachePaths.GetSnapshotFilePath(dependency.Repository, commitHash, dependency.SourceFile);
            var rawMetadataPath = HfHubCachePaths.GetSnapshotMetadataPath(dependency.Repository, commitHash, dependency.SourceFile);
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
                    SourceFile = dependency.SourceFile,
                    CachedFilePath = rawFilePath,
                    DownloadedAtUtc = DateTime.UtcNow.ToString("O")
                }, Formatting.Indented));
        }

        private static void WriteGeneratedArtifacts(
            HfHubDiscoveredModelDependency dependency,
            string commitHash = "abc123",
            string importedManifestHash = null)
        {
            WriteSentisFile(dependency);

            var sentisAssetPath = HfHubProjectPaths.GetRuntimeSentisAssetPath(
                dependency.Repository,
                commitHash,
                dependency.SourceFile);
            var importMetadataAbsolutePath = HfHubProjectPaths.ToProjectAbsolutePath(
                HfHubProjectPaths.GetImportMetadataFilePath(dependency.PackageId, dependency.ModelId));
            var rawFilePath = HfHubCachePaths.GetSnapshotFilePath(dependency.Repository, commitHash, dependency.SourceFile);
            var rawMetadataPath = HfHubCachePaths.GetSnapshotMetadataPath(dependency.Repository, commitHash, dependency.SourceFile);

            Directory.CreateDirectory(Path.GetDirectoryName(importMetadataAbsolutePath) ?? throw new InvalidOperationException());
            File.WriteAllText(
                importMetadataAbsolutePath,
                JsonConvert.SerializeObject(new HfHubImportedModelMetadata
                {
                    PackageId = dependency.PackageId,
                    PackageVersion = dependency.PackageVersion,
                    ModelId = dependency.ModelId,
                    ModelDisplayName = dependency.ModelDisplayName,
                    ManifestHash = importedManifestHash ?? dependency.ManifestHash,
                    Repository = dependency.Repository,
                    RequestedRevision = dependency.Revision,
                    ResolvedCommitHash = commitHash,
                    SourceFile = dependency.SourceFile,
                    RawFilePath = rawFilePath,
                    RawMetadataPath = rawMetadataPath,
                    SentisVersion = HfHubStatusService.GetCurrentSentisVersion(),
                    UnityVersion = UnityEngine.Application.unityVersion,
                    SentisAssetPath = sentisAssetPath,
                    ImportedAtUtc = DateTime.UtcNow.ToString("O")
                }, Formatting.Indented));
        }

        private static void WriteSentisFile(HfHubDiscoveredModelDependency dependency)
        {
            var sentisAbsolutePath = HfHubProjectPaths.ToAbsolutePath(
                HfHubProjectPaths.GetRuntimeSentisAssetPath(dependency.Repository, "abc123", dependency.SourceFile));
            Directory.CreateDirectory(Path.GetDirectoryName(sentisAbsolutePath) ?? throw new InvalidOperationException());
            File.WriteAllText(sentisAbsolutePath, "sentis");
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
    }
}
