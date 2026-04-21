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
    /// Covers repo-snapshot import behavior using a local ONNX sample.
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

        /// <summary>
        /// Verifies import converts ONNX files into runtime `.sentis` assets and writes repo-level metadata.
        /// </summary>
        [Test]
        public async Task ImportAsync_CreatesGeneratedRuntimeSnapshotAndMetadata()
        {
            var dependency = CreateDependency();
            var cachedSnapshot = CreateCachedSnapshot();

            var artifact = await _service.ImportAsync(dependency, cachedSnapshot, CancellationToken.None);

            Assert.That(artifact.SnapshotRootAssetPath, Is.EqualTo("Assets/StreamingAssets/hf-hub/doji/test-model"));
            Assert.That(File.Exists(HfHubProjectPaths.ToProjectAbsolutePath(artifact.MetadataFilePath)), Is.True);
            Assert.That(artifact.Metadata.PackageId, Is.EqualTo("com.doji.test"));
            Assert.That(artifact.Metadata.ModelId, Is.EqualTo("doji/test-model"));
            Assert.That(artifact.Metadata.ResolvedCommitHash, Is.EqualTo("commit123"));
            Assert.That(artifact.RuntimeRegistryAssetPath, Is.EqualTo("Assets/StreamingAssets/hf-hub/registries/com.doji.test/hf-hub-runtime.json"));
            Assert.That(artifact.RuntimeFiles.Count, Is.EqualTo(1));

            var sentisFile = artifact.RuntimeFiles.Single(file => file.Kind == "sentis");
            Assert.That(sentisFile.AssetPath, Is.EqualTo("Assets/StreamingAssets/hf-hub/doji/test-model/SingleInputSingleOuput.sentis"));
            Assert.That(File.Exists(HfHubProjectPaths.ToAbsolutePath(sentisFile.AssetPath)), Is.True);
            Assert.That(sentisFile.SizeInBytes, Is.GreaterThan(0));
            Assert.That(ModelLoader.Load(HfHubProjectPaths.ToAbsolutePath(sentisFile.AssetPath)), Is.Not.Null);
        }

        /// <summary>
        /// Verifies import short-circuits when repo metadata and generated files already match.
        /// </summary>
        [Test]
        public async Task ImportAsync_ReusesExistingGeneratedArtifactWhenMetadataMatches()
        {
            var dependency = CreateDependency();
            var cachedSnapshot = CreateCachedSnapshot();

            var firstArtifact = await _service.ImportAsync(dependency, cachedSnapshot, CancellationToken.None);
            var firstTimestamp = firstArtifact.Metadata.ImportedAtUtc;

            var secondArtifact = await _service.ImportAsync(dependency, cachedSnapshot, CancellationToken.None);

            Assert.That(secondArtifact.SnapshotRootAssetPath, Is.EqualTo(firstArtifact.SnapshotRootAssetPath));
            Assert.That(secondArtifact.MetadataFilePath, Is.EqualTo(firstArtifact.MetadataFilePath));
            Assert.That(secondArtifact.Metadata.ImportedAtUtc, Is.EqualTo(firstTimestamp));
        }

        /// <summary>
        /// Verifies import keeps successful ONNX conversions when one variant in the repo is unsupported.
        /// </summary>
        [Test]
        public async Task ImportAsync_SkipsUnsupportedOnnxVariantsWhenAnotherOnnxConverts()
        {
            var dependency = CreateDependency();
            var cachedSnapshot = CreateCachedSnapshotWithTwoOnnxFiles();
            var service = new HfHubProjectImportService(new PartiallyThrowingOnnxConverter("model_bnb4.onnx"));

            var artifact = await service.ImportAsync(dependency, cachedSnapshot, CancellationToken.None);

            Assert.That(artifact.RuntimeFiles.Count, Is.EqualTo(1));
            Assert.That(artifact.RuntimeFiles.Count(file => file.Kind == "sentis"), Is.EqualTo(1));
            Assert.That(artifact.TryGetRuntimeFile("model.onnx", "sentis", out var sentisFile), Is.True);
            Assert.That(artifact.TryGetRuntimeFile("model_bnb4.onnx", "sentis", out _), Is.False);
            Assert.That(File.Exists(HfHubProjectPaths.ToAbsolutePath(sentisFile.AssetPath)), Is.True);
        }

        /// <summary>
        /// Verifies ONNX conversion failures include repo file path and canonical model id for easier diagnostics.
        /// </summary>
        [Test]
        public void ImportAsync_WrapsOnnxConversionFailuresWithRepoFileContext()
        {
            var dependency = CreateDependency();
            var cachedSnapshot = CreateCachedSnapshot();
            var service = new HfHubProjectImportService(new ThrowingOnnxConverter("Model contains unsupported operators or data types."));

            var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await service.ImportAsync(dependency, cachedSnapshot, CancellationToken.None));

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
                    "doji/test-model",
                    "main"));
        }

        private static HfHubCachedSnapshot CreateCachedSnapshot()
        {
            var sampleOnnxPath = GetInferenceSampleOnnxPath();
            var snapshotRoot = Path.Combine(Path.GetTempPath(), "Doji.ModelHub.ImportSnapshot", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(snapshotRoot);
            var cachedModelPath = Path.Combine(snapshotRoot, "SingleInputSingleOuput.onnx");
            File.Copy(sampleOnnxPath, cachedModelPath, true);

            var metadata = new HfHubRawCacheMetadata
            {
                Repository = "doji/test-model",
                RequestedRevision = "main",
                ResolvedCommitHash = "commit123",
                CachedSnapshotRoot = snapshotRoot,
                Files = new[]
                {
                    new HfHubRawCacheFileMetadata
                    {
                        RepoPath = "SingleInputSingleOuput.onnx",
                        CachedFilePath = cachedModelPath
                    }
                },
                DownloadedAtUtc = DateTime.UtcNow.ToString("O")
            };

            return new HfHubCachedSnapshot(snapshotRoot, Path.Combine(snapshotRoot, "snapshot.hfhub.json"), metadata);
        }

        private static HfHubCachedSnapshot CreateCachedSnapshotWithTwoOnnxFiles()
        {
            var sampleOnnxPath = GetInferenceSampleOnnxPath();
            var snapshotRoot = Path.Combine(Path.GetTempPath(), "Doji.ModelHub.ImportSnapshot", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(snapshotRoot);

            var firstModelPath = Path.Combine(snapshotRoot, "model.onnx");
            var secondModelPath = Path.Combine(snapshotRoot, "model_bnb4.onnx");
            File.Copy(sampleOnnxPath, firstModelPath, true);
            File.Copy(sampleOnnxPath, secondModelPath, true);

            var metadata = new HfHubRawCacheMetadata
            {
                Repository = "doji/test-model",
                RequestedRevision = "main",
                ResolvedCommitHash = "commit123",
                CachedSnapshotRoot = snapshotRoot,
                Files = new[]
                {
                    new HfHubRawCacheFileMetadata
                    {
                        RepoPath = "model.onnx",
                        CachedFilePath = firstModelPath
                    },
                    new HfHubRawCacheFileMetadata
                    {
                        RepoPath = "model_bnb4.onnx",
                        CachedFilePath = secondModelPath
                    }
                },
                DownloadedAtUtc = DateTime.UtcNow.ToString("O")
            };

            return new HfHubCachedSnapshot(snapshotRoot, Path.Combine(snapshotRoot, "snapshot.hfhub.json"), metadata);
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

        private sealed class ThrowingOnnxConverter : IHfHubOnnxModelConverter
        {
            private readonly string _message;

            public ThrowingOnnxConverter(string message)
            {
                _message = message;
            }

            public Model ConvertFromFile(string onnxFilePath)
            {
                throw new InvalidOperationException(_message);
            }
        }

        private sealed class PartiallyThrowingOnnxConverter : IHfHubOnnxModelConverter
        {
            private readonly string _failingFileName;
            private readonly HfHubEditorOnnxModelConverter _inner = new();

            public PartiallyThrowingOnnxConverter(string failingFileName)
            {
                _failingFileName = failingFileName;
            }

            public Model ConvertFromFile(string onnxFilePath)
            {
                if (string.Equals(Path.GetFileName(onnxFilePath), _failingFileName, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Model contains unsupported operators or data types.");
                }

                return _inner.ConvertFromFile(onnxFilePath);
            }
        }
    }
}
