using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers generated runtime locator registry write and lookup behavior.
    /// </summary>
    internal sealed class HfHubRuntimeLocatorServiceTests
    {
        private HfHubRuntimeLocatorRegistryService _registryService;

        [SetUp]
        public void SetUp()
        {
            _registryService = new HfHubRuntimeLocatorRegistryService();
            CleanupRuntimeArtifacts();
        }

        [TearDown]
        public void TearDown()
        {
            CleanupRuntimeArtifacts();
        }

        /// <summary>
        /// Writes one successful import result and verifies generated registry JSON lands under package-scoped StreamingAssets path.
        /// </summary>
        [Test]
        public void WriteRegistries_WritesPackageScopedRegistryJson()
        {
            var result = CreateSuccessResult("tiny-model");

            _registryService.WriteRegistries(new[] { result });

            var registryPath = HfHubProjectPaths.GetRuntimeRegistryAssetPath("com.doji.test");
            var registryAbsolutePath = HfHubProjectPaths.ToAbsolutePath(registryPath);

            Assert.That(File.Exists(registryAbsolutePath), Is.True);
            Assert.That(File.ReadAllText(registryAbsolutePath), Does.Contain("\"_modelId\": \"tiny-model\""));
            Assert.That(File.ReadAllText(registryAbsolutePath), Does.Contain("\"_primarySentisRelativePath\": \"hf-hub/models--doji--test-model/snapshots/abc123/model.sentis\""));
        }

        /// <summary>
        /// Writes one runtime registry file, then resolves model through public API without manual StreamingAssets path composition.
        /// </summary>
        [Test]
        public void TryResolveRuntimeModel_ReturnsResolvedAbsolutePathsFromGeneratedRegistry()
        {
            var result = CreateSuccessResult("tiny-model");
            WriteRuntimeSentis(result.ImportedArtifact.Metadata.RuntimeSentisAssetPath);
            _registryService.WriteRegistries(new[] { result });

            var found = ModelHub.TryResolveRuntimeModel("com.doji.test", "tiny-model", out var model);

            Assert.That(found, Is.True);
            Assert.That(model, Is.Not.Null);
            Assert.That(model.PackageId, Is.EqualTo("com.doji.test"));
            Assert.That(model.ModelId, Is.EqualTo("tiny-model"));
            Assert.That(model.PrimarySentisPath, Is.EqualTo(
                Path.Combine(UnityEngine.Application.streamingAssetsPath, "hf-hub/models--doji--test-model/snapshots/abc123/model.sentis")));
            Assert.That(model.Files.Count, Is.EqualTo(1));
            Assert.That(model.Files[0].Kind, Is.EqualTo("sentis"));
            Assert.That(model.Files[0].FilePath, Is.EqualTo(model.PrimarySentisPath));
            Assert.That(model.TryGetFilePath("models/model.onnx", out var filePath), Is.True);
            Assert.That(filePath, Is.EqualTo(model.PrimarySentisPath));
        }

        /// <summary>
        /// Verifies public runtime lookup fails cleanly when package registry has not been generated yet.
        /// </summary>
        [Test]
        public void TryResolveRuntimeModel_ReturnsFalseWhenRegistryMissing()
        {
            var found = ModelHub.TryResolveRuntimeModel("com.doji.test", "missing-model", out var model);

            Assert.That(found, Is.False);
            Assert.That(model, Is.Null);
        }

        private static HfHubModelSyncResult CreateSuccessResult(string modelId)
        {
            var dependency = new HfHubDiscoveredModelDependency(
                "com.doji.test",
                "Test Package",
                "1.0.0",
                "/tmp/com.doji.test",
                "/tmp/com.doji.test/hf-hub-manifest.json",
                "manifesthash",
                new HfHubModelDependencyDefinition(
                    modelId,
                    "Tiny Model",
                    "doji/test-model",
                    "main",
                    "models/model.onnx"));

            var metadata = new HfHubImportedModelMetadata
            {
                PackageId = dependency.PackageId,
                PackageVersion = dependency.PackageVersion,
                ModelId = dependency.ModelId,
                ModelDisplayName = dependency.ModelDisplayName,
                ManifestHash = dependency.ManifestHash,
                Repository = dependency.Repository,
                RequestedRevision = dependency.Revision,
                ResolvedCommitHash = "abc123",
                SourceFile = dependency.SourceFile,
                RawFilePath = "/cache/model.onnx",
                RawMetadataPath = "/cache/model.onnx.hfhub.json",
                SentisVersion = "2.6.1",
                UnityVersion = "6000.3.10f1",
                SentisAssetPath = "Assets/StreamingAssets/hf-hub/models--doji--test-model/snapshots/abc123/model.sentis",
                RuntimeRegistryAssetPath = HfHubProjectPaths.GetRuntimeRegistryAssetPath(dependency.PackageId),
                RuntimeRegistryRelativePath = HfHubProjectPaths.ToStreamingAssetsRelativePath(HfHubProjectPaths.GetRuntimeRegistryAssetPath(dependency.PackageId)),
                RuntimeSentisAssetPath = HfHubProjectPaths.GetRuntimeSentisAssetPath(dependency.Repository, "abc123", dependency.SourceFile),
                RuntimeSentisRelativePath = HfHubProjectPaths.ToStreamingAssetsRelativePath(HfHubProjectPaths.GetRuntimeSentisAssetPath(dependency.Repository, "abc123", dependency.SourceFile)),
                ImportedAtUtc = DateTime.UtcNow.ToString("O")
            };

            return new HfHubModelSyncResult(
                dependency,
                new HfHubCachedRawFile("/cache/model.onnx", "/cache/model.onnx.hfhub.json", new HfHubRawCacheMetadata()),
                new HfHubImportedModelArtifact(metadata.SentisAssetPath, $"Library/Doji/HfHub/import-state/{dependency.PackageId}/{dependency.ModelId}/hf-hub-import.json", metadata),
                null);
        }

        private static void WriteRuntimeSentis(string runtimeSentisAssetPath)
        {
            var runtimeSentisAbsolutePath = HfHubProjectPaths.ToAbsolutePath(runtimeSentisAssetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(runtimeSentisAbsolutePath) ?? throw new InvalidOperationException());
            File.WriteAllText(runtimeSentisAbsolutePath, "sentis");
            AssetDatabase.ImportAsset(runtimeSentisAssetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        }

        private static void CleanupRuntimeArtifacts()
        {
            DeleteAssetIfExists("Assets/StreamingAssets/hf-hub");
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
