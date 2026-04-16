using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Unity.InferenceEngine;
using UnityEditor;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers real project import behavior.
    /// Raw input ONNX stays local by reusing a tiny sample model shipped inside `com.unity.ai.inference`,
    /// while Unity's real ONNX importer, `.sentis` importer, and `AssetDatabase` pipeline stay active.
    /// These tests verify generated path shape, metadata write, temp asset cleanup, and up-to-date reuse.
    /// </summary>
    internal sealed class HfHubProjectImportServiceTests
    {
        private HfHubProjectImportService _service;

        [SetUp]
        public void SetUp()
        {
            _service = new HfHubProjectImportService();
            CleanupProjectArtifacts();
        }

        [TearDown]
        public void TearDown()
        {
            CleanupProjectArtifacts();
        }

        [Test]
        public async Task ImportAsync_CreatesGeneratedSentisArtifactAndMetadata()
        {
            // Uses a small local ONNX sample from the installed Unity Inference package.
            // Test keeps real Unity import/serialization behavior and checks only deterministic project outputs.
            var dependency = CreateDependency();
            var cachedRawFile = CreateCachedRawFile();

            var artifact = await _service.ImportAsync(dependency, cachedRawFile, CancellationToken.None);

            Assert.That(artifact.SentisAssetPath, Is.EqualTo("Assets/Doji/HfHub/Generated/com.doji.test/tiny-sample/SingleInputSingleOuput.sentis"));
            Assert.That(File.Exists(HfHubProjectPaths.ToAbsolutePath(artifact.SentisAssetPath)), Is.True);
            Assert.That(File.Exists(HfHubProjectPaths.ToAbsolutePath(artifact.MetadataAssetPath)), Is.True);
            Assert.That(AssetDatabase.LoadAssetAtPath<ModelAsset>(artifact.SentisAssetPath), Is.Not.Null);
            Assert.That(artifact.Metadata.PackageId, Is.EqualTo("com.doji.test"));
            Assert.That(artifact.Metadata.ModelId, Is.EqualTo("tiny-sample"));
            Assert.That(artifact.Metadata.ResolvedCommitHash, Is.EqualTo("commit123"));
            Assert.That(artifact.Metadata.SentisAssetPath, Is.EqualTo(artifact.SentisAssetPath));
            Assert.That(artifact.RuntimeRegistryAssetPath, Is.EqualTo("Assets/StreamingAssets/Doji/HfHub/Registries/com.doji.test/hf-hub-runtime.json"));
            Assert.That(artifact.RuntimeRegistryRelativePath, Is.EqualTo("Doji/HfHub/Registries/com.doji.test/hf-hub-runtime.json"));
            Assert.That(artifact.RuntimeSentisAssetPath, Is.EqualTo("Assets/StreamingAssets/Doji/HfHub/Models/com.doji.test/tiny-sample/SingleInputSingleOuput.sentis"));
            Assert.That(artifact.RuntimeSentisRelativePath, Is.EqualTo("Doji/HfHub/Models/com.doji.test/tiny-sample/SingleInputSingleOuput.sentis"));
            Assert.That(AssetDatabase.LoadMainAssetAtPath("Assets/Doji/HfHub/TempImport/com.doji.test/tiny-sample/SingleInputSingleOuput.onnx"), Is.Null);
        }

        [Test]
        public async Task ImportAsync_ReusesExistingGeneratedArtifactWhenMetadataMatches()
        {
            // First import creates generated `.sentis` output and metadata.
            // Second import should short-circuit on the metadata contract instead of running Unity ONNX import again.
            var dependency = CreateDependency();
            var cachedRawFile = CreateCachedRawFile();

            var firstArtifact = await _service.ImportAsync(dependency, cachedRawFile, CancellationToken.None);
            var firstTimestamp = firstArtifact.Metadata.ImportedAtUtc;

            var secondArtifact = await _service.ImportAsync(dependency, cachedRawFile, CancellationToken.None);

            Assert.That(secondArtifact.SentisAssetPath, Is.EqualTo(firstArtifact.SentisAssetPath));
            Assert.That(secondArtifact.MetadataAssetPath, Is.EqualTo(firstArtifact.MetadataAssetPath));
            Assert.That(secondArtifact.Metadata.ImportedAtUtc, Is.EqualTo(firstTimestamp));
        }

        [Test]
        public void SanitizePathSegment_ReplacesInvalidPathCharacters()
        {
            Assert.That(
                HfHubProjectPaths.SanitizePathSegment("com/doji\\bad name"),
                Is.EqualTo("com-doji-bad-name"));
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
                    "tiny-sample",
                    "Tiny Sample",
                    "doji/test-model",
                    "main",
                    "SingleInputSingleOuput.onnx"));
        }

        private static HfHubCachedRawFile CreateCachedRawFile()
        {
            var sampleOnnxPath = GetInferenceSampleOnnxPath();
            var metadata = new HfHubRawCacheMetadata
            {
                Repository = "doji/test-model",
                RequestedRevision = "main",
                ResolvedCommitHash = "commit123",
                SourceFile = "SingleInputSingleOuput.onnx",
                CachedFilePath = sampleOnnxPath,
                DownloadedAtUtc = DateTime.UtcNow.ToString("O")
            };

            return new HfHubCachedRawFile(sampleOnnxPath, sampleOnnxPath + ".hfhub.json", metadata);
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
            DeleteAssetIfExists("Assets/Doji/HfHub/Generated");
            DeleteAssetIfExists("Assets/Doji/HfHub/TempImport");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static void DeleteAssetIfExists(string assetPath)
        {
            if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null || Directory.Exists(HfHubProjectPaths.ToAbsolutePath(assetPath)))
            {
                AssetDatabase.DeleteAsset(assetPath);
            }
        }
    }
}
