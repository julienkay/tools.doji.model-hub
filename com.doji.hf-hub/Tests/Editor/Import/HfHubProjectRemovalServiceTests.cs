using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers cache-only removal behavior for imported repository entries.
    /// </summary>
    internal sealed class HfHubProjectRemovalServiceTests
    {
        private string _rootDirectory;

        [SetUp]
        public void SetUp()
        {
            _rootDirectory = Path.Combine(
                Path.GetTempPath(),
                "Doji.ModelHub.ProjectRemovalTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rootDirectory);
            Environment.SetEnvironmentVariable("DOJI_HF_HUB_CACHE_ROOT", Path.Combine(_rootDirectory, "SharedCache"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_rootDirectory))
            {
                Directory.Delete(_rootDirectory, true);
            }

            DeleteAssetIfExists("Assets/StreamingAssets/hf-hub");
            DeleteProjectFolderIfExists("Library/Doji/HfHub/imports");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Environment.SetEnvironmentVariable("DOJI_HF_HUB_CACHE_ROOT", null);
        }

        /// <summary>
        /// Verifies cache removal deletes both selected revision ref and snapshot when no other ref uses that commit.
        /// </summary>
        [Test]
        public void RemoveRawCache_RemovesSelectedRevisionAndSnapshotWhenCommitIsUnshared()
        {
            WriteRevisionRef("main", "abc123");
            WriteSnapshot("abc123");
            var item = CreateItem("main", "abc123");

            new HfHubProjectRemovalService().RemoveRawCache(item);

            Assert.That(File.Exists(HfHubCachePaths.GetRevisionRefPath(item.Repository, "main")), Is.False);
            Assert.That(Directory.Exists(HfHubCachePaths.GetSnapshotRoot(item.Repository, "abc123")), Is.False);
        }

        /// <summary>
        /// Verifies cache removal keeps shared snapshot when another revision still points at that commit.
        /// </summary>
        [Test]
        public void RemoveRawCache_KeepsSnapshotWhenAnotherRevisionStillUsesCommit()
        {
            WriteRevisionRef("main", "abc123");
            WriteRevisionRef("v1", "abc123");
            WriteSnapshot("abc123");
            var item = CreateItem("main", "abc123");

            new HfHubProjectRemovalService().RemoveRawCache(item);

            Assert.That(File.Exists(HfHubCachePaths.GetRevisionRefPath(item.Repository, "main")), Is.False);
            Assert.That(File.Exists(HfHubCachePaths.GetRevisionRefPath(item.Repository, "v1")), Is.True);
            Assert.That(Directory.Exists(HfHubCachePaths.GetSnapshotRoot(item.Repository, "abc123")), Is.True);
        }

        /// <summary>
        /// Verifies project removal keeps shared runtime import until last owner binding is removed.
        /// </summary>
        [Test]
        public void Remove_KeepsSharedImportUntilLastBindingIsRemoved()
        {
            var sharedMetadataPath = HfHubProjectPaths.GetSharedImportMetadataFilePath("doji/test-model", "abc123", "sentis-test");
            WriteBinding("com.doji.first", "doji/test-model", "manifest-a", sharedMetadataPath);
            WriteBinding("com.doji.second", "doji/test-model", "manifest-b", sharedMetadataPath);
            WriteSharedImport(sharedMetadataPath);

            var firstItem = CreateProjectRemovalItem("com.doji.first", "manifest-a", sharedMetadataPath);
            var secondItem = CreateProjectRemovalItem("com.doji.second", "manifest-b", sharedMetadataPath);

            new HfHubProjectRemovalService().Remove(firstItem);

            Assert.That(File.Exists(HfHubProjectPaths.ToProjectAbsolutePath(HfHubProjectPaths.GetImportBindingMetadataFilePath("com.doji.first", "doji/test-model"))), Is.False);
            Assert.That(File.Exists(HfHubProjectPaths.ToProjectAbsolutePath(HfHubProjectPaths.GetImportBindingMetadataFilePath("com.doji.second", "doji/test-model"))), Is.True);
            Assert.That(File.Exists(HfHubProjectPaths.ToProjectAbsolutePath(sharedMetadataPath)), Is.True);
            Assert.That(File.Exists(HfHubProjectPaths.ToAbsolutePath("Assets/StreamingAssets/hf-hub/doji/test-model/models/model.sentis")), Is.True);

            new HfHubProjectRemovalService().Remove(secondItem);

            Assert.That(File.Exists(HfHubProjectPaths.ToProjectAbsolutePath(HfHubProjectPaths.GetImportBindingMetadataFilePath("com.doji.second", "doji/test-model"))), Is.False);
            Assert.That(File.Exists(HfHubProjectPaths.ToProjectAbsolutePath(sharedMetadataPath)), Is.False);
            Assert.That(AssetDatabase.LoadMainAssetAtPath("Assets/StreamingAssets/hf-hub/doji/test-model") == null, Is.True);
        }

        private static HfHubModelHubItem CreateItem(string requestedRevision, string resolvedCommitHash)
        {
            return new HfHubModelHubItem(
                "doji/test-model",
                requestedRevision,
                resolvedCommitHash,
                HfHubCachePaths.GetSnapshotRoot("doji/test-model", resolvedCommitHash),
                "Assets/StreamingAssets/hf-hub/doji/test-model",
                null,
                null,
                null,
                null,
                HfHubModelHubItemState.Ready,
                Array.Empty<HfHubDependencyStatus>(),
                Array.Empty<HfHubModelHubDiagnostic>(),
                Array.Empty<HfHubImportedRuntimeFileMetadata>());
        }

        private static HfHubModelHubItem CreateProjectRemovalItem(string packageId, string manifestHash, string sharedMetadataPath)
        {
            var dependency = new HfHubDiscoveredModelDependency(
                packageId,
                packageId,
                "1.0.0",
                $"/tmp/{packageId}",
                $"/tmp/{packageId}/hf-hub-manifest.json",
                manifestHash,
                new HfHubModelDependencyDefinition("doji/test-model", "main"));
            var importedMetadata = new HfHubImportedModelMetadata
            {
                PackageId = packageId,
                PackageVersion = "1.0.0",
                ModelId = "doji/test-model",
                ManifestHash = manifestHash,
                Repository = "doji/test-model",
                RequestedRevision = "main",
                ResolvedCommitHash = "abc123",
                BindingMetadataPath = HfHubProjectPaths.GetImportBindingMetadataFilePath(packageId, "doji/test-model"),
                SharedImportMetadataPath = sharedMetadataPath,
                RuntimeFiles = new[]
                {
                    new HfHubImportedRuntimeFileMetadata
                    {
                        RepoPath = "models/model.onnx",
                        Kind = "sentis",
                        AssetPath = "Assets/StreamingAssets/hf-hub/doji/test-model/models/model.sentis",
                        RelativePath = "hf-hub/doji/test-model/models/model.sentis"
                    }
                }
            };

            return new HfHubModelHubItem(
                "doji/test-model",
                "main",
                "abc123",
                HfHubCachePaths.GetSnapshotRoot("doji/test-model", "abc123"),
                "Assets/StreamingAssets/hf-hub/doji/test-model",
                null,
                null,
                null,
                null,
                HfHubModelHubItemState.Ready,
                new[]
                {
                    new HfHubDependencyStatus(
                        dependency,
                        HfHubDependencyState.Ready,
                        HfHubDependencyReasonCode.None,
                        "Ready.",
                        HfHubCachePaths.GetRevisionRefPath("doji/test-model", "main"),
                        HfHubCachePaths.GetSnapshotRoot("doji/test-model", "abc123"),
                        HfHubCachePaths.GetSnapshotMetadataPath("doji/test-model", "abc123"),
                        null,
                        "Assets/StreamingAssets/hf-hub/doji/test-model/models/model.sentis",
                        HfHubProjectPaths.GetImportBindingMetadataFilePath(packageId, "doji/test-model"),
                        importedMetadata)
                },
                Array.Empty<HfHubModelHubDiagnostic>(),
                importedMetadata.RuntimeFiles);
        }

        private static void WriteRevisionRef(string revision, string commitHash)
        {
            var refPath = HfHubCachePaths.GetRevisionRefPath("doji/test-model", revision);
            Directory.CreateDirectory(Path.GetDirectoryName(refPath) ?? throw new InvalidOperationException());
            File.WriteAllText(refPath, commitHash);
        }

        private static void WriteSnapshot(string commitHash)
        {
            var snapshotRoot = HfHubCachePaths.GetSnapshotRoot("doji/test-model", commitHash);
            Directory.CreateDirectory(snapshotRoot);
            File.WriteAllText(Path.Combine(snapshotRoot, "snapshot.hfhub.json"), "{}");
            File.WriteAllText(Path.Combine(snapshotRoot, "model.onnx"), "onnx");
        }

        private static void WriteBinding(string packageId, string modelId, string manifestHash, string sharedMetadataPath, string sentisVersion = "sentis-test")
        {
            var bindingAbsolutePath = HfHubProjectPaths.ToProjectAbsolutePath(HfHubProjectPaths.GetImportBindingMetadataFilePath(packageId, modelId));
            Directory.CreateDirectory(Path.GetDirectoryName(bindingAbsolutePath) ?? throw new InvalidOperationException());
            File.WriteAllText(bindingAbsolutePath, Newtonsoft.Json.JsonConvert.SerializeObject(new HfHubImportBindingMetadata
            {
                PackageId = packageId,
                PackageVersion = "1.0.0",
                ModelId = modelId,
                ManifestHash = manifestHash,
                Repository = modelId,
                RequestedRevision = "main",
                ResolvedCommitHash = "abc123",
                SentisVersion = sentisVersion,
                SharedImportMetadataPath = sharedMetadataPath,
                RuntimeRegistryAssetPath = HfHubProjectPaths.GetRuntimeRegistryAssetPath(packageId),
                RuntimeRegistryRelativePath = HfHubProjectPaths.ToStreamingAssetsRelativePath(HfHubProjectPaths.GetRuntimeRegistryAssetPath(packageId)),
                BoundAtUtc = DateTime.UtcNow.ToString("O")
            }));
        }

        private static void WriteSharedImport(string sharedMetadataPath, string sentisVersion = "sentis-test")
        {
            var sharedAbsolutePath = HfHubProjectPaths.ToProjectAbsolutePath(sharedMetadataPath);
            Directory.CreateDirectory(Path.GetDirectoryName(sharedAbsolutePath) ?? throw new InvalidOperationException());
            var runtimeAbsolutePath = HfHubProjectPaths.ToAbsolutePath("Assets/StreamingAssets/hf-hub/doji/test-model/models/model.sentis");
            Directory.CreateDirectory(Path.GetDirectoryName(runtimeAbsolutePath) ?? throw new InvalidOperationException());
            File.WriteAllText(runtimeAbsolutePath, "sentis");
            AssetDatabase.ImportAsset("Assets/StreamingAssets/hf-hub/doji/test-model/models/model.sentis", ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            File.WriteAllText(sharedAbsolutePath, Newtonsoft.Json.JsonConvert.SerializeObject(new HfHubSharedImportMetadata
            {
                Repository = "doji/test-model",
                ResolvedCommitHash = "abc123",
                RawSnapshotRootPath = "/cache/doji/test-model/abc123",
                RawMetadataPath = "/cache/doji/test-model/abc123/snapshot.hfhub.json",
                SentisVersion = sentisVersion,
                RuntimeSnapshotAssetPath = "Assets/StreamingAssets/hf-hub/doji/test-model",
                RuntimeFiles = new[]
                {
                    new HfHubImportedRuntimeFileMetadata
                    {
                        RepoPath = "models/model.onnx",
                        Kind = "sentis",
                        AssetPath = "Assets/StreamingAssets/hf-hub/doji/test-model/models/model.sentis",
                        RelativePath = "hf-hub/doji/test-model/models/model.sentis"
                    }
                },
                ImportedAtUtc = DateTime.UtcNow.ToString("O")
            }));
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
