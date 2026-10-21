using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers cache-only removal behavior for imported repository entries.
    /// </summary>
    internal sealed class ProjectRemovalServiceTests
    {
        private TestCacheScope _cacheScope;
        private TestProjectStateScope _projectScope;
        private string _rootDirectory;

        [SetUp]
        public void SetUp()
        {
            _rootDirectory = Path.Combine(
                Path.GetTempPath(),
                "Doji.ModelHub.ProjectRemovalTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rootDirectory);
            _cacheScope = new TestCacheScope(Path.Combine(_rootDirectory, "SharedCache"));
            _projectScope = new TestProjectStateScope();
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_rootDirectory))
            {
                Directory.Delete(_rootDirectory, true);
            }

            _projectScope?.Dispose();
            _projectScope = null;
            _cacheScope?.Dispose();
            _cacheScope = null;
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

            new ProjectRemovalService().RemoveRawCache(item);

            Assert.That(File.Exists(CachePaths.GetRevisionRefPath(item.Repository, "main")), Is.False);
            Assert.That(Directory.Exists(CachePaths.GetSnapshotRoot(item.Repository, "abc123")), Is.False);
        }

        /// <summary>
        /// Removing a revision deletes the blobs only it links to, keeps blobs another revision still links to,
        /// and leaves other files in blobs/ alone, such as another program's partial download.
        /// </summary>
        [Test]
        public void RemoveRawCache_DeletesOnlyBlobsNoOtherSnapshotLinksTo()
        {
            Assume.That(CacheFiles.AreSymlinksSupported(_rootDirectory), Is.True, "This machine cannot create symbolic links.");
            const string repository = "doji/test-model";
            var sharedBlob = WriteBlob(repository, "shared-etag");
            var ownBlob = WriteBlob(repository, "own-etag");
            var partialDownload = WriteBlob(repository, "other-etag.incomplete");
            CacheFiles.LinkSnapshotFile(sharedBlob, CachePaths.GetSnapshotFilePath(repository, "abc123", "config.json"), false);
            CacheFiles.LinkSnapshotFile(ownBlob, CachePaths.GetSnapshotFilePath(repository, "abc123", "onnx/model.onnx"), false);
            CacheFiles.LinkSnapshotFile(sharedBlob, CachePaths.GetSnapshotFilePath(repository, "def456", "config.json"), false);
            WriteRevisionRef("main", "abc123");
            WriteRevisionRef("v2", "def456");

            new ProjectRemovalService().RemoveRawCache(CreateItem("main", "abc123"));

            Assert.That(Directory.Exists(CachePaths.GetSnapshotRoot(repository, "abc123")), Is.False);
            Assert.That(File.Exists(ownBlob), Is.False);
            Assert.That(File.Exists(sharedBlob), Is.True);
            Assert.That(File.Exists(partialDownload), Is.True);
            Assert.That(File.ReadAllText(CachePaths.GetSnapshotFilePath(repository, "def456", "config.json")), Is.EqualTo("shared-etag"));
        }

        private static string WriteBlob(string repository, string name)
        {
            var path = Path.Combine(CachePaths.GetBlobsRoot(repository), name);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, name);
            return path;
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

            new ProjectRemovalService().RemoveRawCache(item);

            Assert.That(File.Exists(CachePaths.GetRevisionRefPath(item.Repository, "main")), Is.False);
            Assert.That(File.Exists(CachePaths.GetRevisionRefPath(item.Repository, "v1")), Is.True);
            Assert.That(Directory.Exists(CachePaths.GetSnapshotRoot(item.Repository, "abc123")), Is.True);
        }

        /// <summary>
        /// Verifies project removal keeps shared runtime import until last owner binding is removed.
        /// </summary>
        [Test]
        public void Remove_KeepsSharedImportUntilLastBindingIsRemoved()
        {
            var sharedMetadataPath = ProjectPaths.GetSharedImportMetadataFilePath("doji/test-model", "abc123", "sentis-test", "recipe-test");
            WriteBinding("tools.doji.first", "doji/test-model", "manifest-a", sharedMetadataPath);
            WriteBinding("tools.doji.second", "doji/test-model", "manifest-b", sharedMetadataPath);
            WriteSharedImport(sharedMetadataPath);

            var firstItem = CreateProjectRemovalItem("tools.doji.first", "manifest-a", sharedMetadataPath);
            var secondItem = CreateProjectRemovalItem("tools.doji.second", "manifest-b", sharedMetadataPath);

            new ProjectRemovalService().Remove(firstItem);

            Assert.That(File.Exists(ProjectPaths.ToProjectAbsolutePath(ProjectPaths.GetImportBindingMetadataFilePath("tools.doji.first", "doji/test-model"))), Is.False);
            Assert.That(File.Exists(ProjectPaths.ToProjectAbsolutePath(ProjectPaths.GetImportBindingMetadataFilePath("tools.doji.second", "doji/test-model"))), Is.True);
            Assert.That(File.Exists(ProjectPaths.ToProjectAbsolutePath(sharedMetadataPath)), Is.True);
            Assert.That(File.Exists(ProjectPaths.ToAbsolutePath("Assets/StreamingAssets/hf-hub/doji/test-model/models/model.sentis")), Is.True);

            new ProjectRemovalService().Remove(secondItem);

            Assert.That(File.Exists(ProjectPaths.ToProjectAbsolutePath(ProjectPaths.GetImportBindingMetadataFilePath("tools.doji.second", "doji/test-model"))), Is.False);
            Assert.That(File.Exists(ProjectPaths.ToProjectAbsolutePath(sharedMetadataPath)), Is.False);
            Assert.That(AssetDatabase.LoadMainAssetAtPath("Assets/StreamingAssets/hf-hub/doji/test-model") == null, Is.True);
        }

        /// <summary>
        /// Verifies project removal does nothing while repository row is blocked by requested-revision conflict.
        /// </summary>
        [Test]
        public void Remove_DoesNothingWhenItemHasRevisionConflict()
        {
            var sharedMetadataPath = ProjectPaths.GetSharedImportMetadataFilePath("doji/test-model", "abc123", "sentis-test", "recipe-test");
            WriteBinding("tools.doji.first", "doji/test-model", "manifest-a", sharedMetadataPath);
            WriteSharedImport(sharedMetadataPath);
            var conflictItem = CreateProjectRemovalItem(
                "tools.doji.first",
                "manifest-a",
                sharedMetadataPath,
                DependencyState.Failed,
                DependencyReasonCode.RevisionConflict);

            new ProjectRemovalService().Remove(conflictItem);

            Assert.That(File.Exists(ProjectPaths.ToProjectAbsolutePath(ProjectPaths.GetImportBindingMetadataFilePath("tools.doji.first", "doji/test-model"))), Is.True);
            Assert.That(File.Exists(ProjectPaths.ToProjectAbsolutePath(sharedMetadataPath)), Is.True);
            Assert.That(File.Exists(ProjectPaths.ToAbsolutePath("Assets/StreamingAssets/hf-hub/doji/test-model/models/model.sentis")), Is.True);
        }

        /// <summary>
        /// Verifies raw cache removal does nothing while repository row is blocked by requested-revision conflict.
        /// </summary>
        [Test]
        public void RemoveRawCache_DoesNothingWhenItemHasRevisionConflict()
        {
            WriteRevisionRef("main", "abc123");
            WriteSnapshot("abc123");
            var conflictItem = CreateItem(
                "main",
                "abc123",
                DependencyState.Failed,
                DependencyReasonCode.RevisionConflict);

            new ProjectRemovalService().RemoveRawCache(conflictItem);

            Assert.That(File.Exists(CachePaths.GetRevisionRefPath(conflictItem.Repository, "main")), Is.True);
            Assert.That(Directory.Exists(CachePaths.GetSnapshotRoot(conflictItem.Repository, "abc123")), Is.True);
        }

        private static ModelHubItem CreateItem(
            string requestedRevision,
            string resolvedCommitHash,
            DependencyState state = DependencyState.Ready,
            DependencyReasonCode reasonCode = DependencyReasonCode.None)
        {
            return new ModelHubItem(
                "doji/test-model",
                requestedRevision,
                resolvedCommitHash,
                CachePaths.GetSnapshotRoot("doji/test-model", resolvedCommitHash),
                "Assets/StreamingAssets/hf-hub/doji/test-model",
                null,
                null,
                null,
                null,
                state == DependencyState.Failed ? ModelHubItemState.Error : ModelHubItemState.Ready,
                new[]
                {
                    new DependencyStatus(
                        new DiscoveredModelDependency(
                            "tools.doji.test",
                            "Test Package",
                            "1.0.0",
                            "/tmp/tools.doji.test",
                            "/tmp/tools.doji.test/hf-hub-manifest.json",
                            "manifesthash",
                            new ModelDependencyDefinition("doji/test-model", requestedRevision)),
                        state,
                        reasonCode,
                        state == DependencyState.Failed ? "Revision conflict." : "Ready.",
                        CachePaths.GetRevisionRefPath("doji/test-model", requestedRevision),
                        CachePaths.GetSnapshotRoot("doji/test-model", resolvedCommitHash),
                        CachePaths.GetSnapshotMetadataPath("doji/test-model", resolvedCommitHash),
                        null,
                        null,
                        null,
                        null)
                },
                Array.Empty<ModelHubDiagnostic>(),
                Array.Empty<ImportedRuntimeFileMetadata>(),
                Array.Empty<ModelHubFileItem>());
        }

        private static ModelHubItem CreateProjectRemovalItem(
            string packageId,
            string manifestHash,
            string sharedMetadataPath,
            DependencyState state = DependencyState.Ready,
            DependencyReasonCode reasonCode = DependencyReasonCode.None)
        {
            var dependency = new DiscoveredModelDependency(
                packageId,
                packageId,
                "1.0.0",
                $"/tmp/{packageId}",
                $"/tmp/{packageId}/hf-hub-manifest.json",
                manifestHash,
                new ModelDependencyDefinition("doji/test-model", "main"));
            var importedMetadata = new ImportedModelMetadata
            {
                PackageId = packageId,
                PackageVersion = "1.0.0",
                ModelId = "doji/test-model",
                ManifestHash = manifestHash,
                Repository = "doji/test-model",
                RequestedRevision = "main",
                ResolvedCommitHash = "abc123",
                BindingMetadataPath = ProjectPaths.GetImportBindingMetadataFilePath(packageId, "doji/test-model"),
                SharedImportMetadataPath = sharedMetadataPath,
                RuntimeFiles = new[]
                {
                    new ImportedRuntimeFileMetadata
                    {
                        RepoPath = "models/model.onnx",
                        Kind = "sentis",
                        AssetPath = "Assets/StreamingAssets/hf-hub/doji/test-model/models/model.sentis",
                        RelativePath = "hf-hub/doji/test-model/models/model.sentis"
                    }
                }
            };

            return new ModelHubItem(
                "doji/test-model",
                "main",
                "abc123",
                CachePaths.GetSnapshotRoot("doji/test-model", "abc123"),
                "Assets/StreamingAssets/hf-hub/doji/test-model",
                null,
                null,
                null,
                null,
                state == DependencyState.Failed ? ModelHubItemState.Error : ModelHubItemState.Ready,
                new[]
                {
                    new DependencyStatus(
                        dependency,
                        state,
                        reasonCode,
                        state == DependencyState.Failed ? "Revision conflict." : "Ready.",
                        CachePaths.GetRevisionRefPath("doji/test-model", "main"),
                        CachePaths.GetSnapshotRoot("doji/test-model", "abc123"),
                        CachePaths.GetSnapshotMetadataPath("doji/test-model", "abc123"),
                        null,
                        "Assets/StreamingAssets/hf-hub/doji/test-model/models/model.sentis",
                        ProjectPaths.GetImportBindingMetadataFilePath(packageId, "doji/test-model"),
                        importedMetadata)
                },
                Array.Empty<ModelHubDiagnostic>(),
                importedMetadata.RuntimeFiles,
                Array.Empty<ModelHubFileItem>());
        }

        private static void WriteRevisionRef(string revision, string commitHash)
        {
            var refPath = CachePaths.GetRevisionRefPath("doji/test-model", revision);
            Directory.CreateDirectory(Path.GetDirectoryName(refPath) ?? throw new InvalidOperationException());
            File.WriteAllText(refPath, commitHash);
        }

        private static void WriteSnapshot(string commitHash)
        {
            var snapshotRoot = CachePaths.GetSnapshotRoot("doji/test-model", commitHash);
            Directory.CreateDirectory(snapshotRoot);
            File.WriteAllText(Path.Combine(snapshotRoot, "snapshot.hfhub.json"), "{}");
            File.WriteAllText(Path.Combine(snapshotRoot, "model.onnx"), "onnx");
        }

        private static void WriteBinding(string packageId, string modelId, string manifestHash, string sharedMetadataPath, string sentisVersion = "sentis-test")
        {
            var bindingAbsolutePath = ProjectPaths.ToProjectAbsolutePath(ProjectPaths.GetImportBindingMetadataFilePath(packageId, modelId));
            Directory.CreateDirectory(Path.GetDirectoryName(bindingAbsolutePath) ?? throw new InvalidOperationException());
            File.WriteAllText(bindingAbsolutePath, Newtonsoft.Json.JsonConvert.SerializeObject(new ImportBindingMetadata
            {
                PackageId = packageId,
                PackageVersion = "1.0.0",
                ModelId = modelId,
                ManifestHash = manifestHash,
                Repository = modelId,
                RequestedRevision = "main",
                ResolvedCommitHash = "abc123",
                SentisVersion = sentisVersion,
                DependencySelectionHash = ModelDependencyDefinition.ComputeSelectionHash(null),
                SharedImportMetadataPath = sharedMetadataPath,
                RuntimeRegistryAssetPath = ProjectPaths.GetRuntimeRegistryAssetPath(packageId),
                RuntimeRegistryRelativePath = ProjectPaths.ToStreamingAssetsRelativePath(ProjectPaths.GetRuntimeRegistryAssetPath(packageId)),
                BoundAtUtc = DateTime.UtcNow.ToString("O")
            }));
        }

        private static void WriteSharedImport(string sharedMetadataPath, string sentisVersion = "sentis-test")
        {
            var sharedAbsolutePath = ProjectPaths.ToProjectAbsolutePath(sharedMetadataPath);
            Directory.CreateDirectory(Path.GetDirectoryName(sharedAbsolutePath) ?? throw new InvalidOperationException());
            var runtimeAbsolutePath = ProjectPaths.ToAbsolutePath("Assets/StreamingAssets/hf-hub/doji/test-model/models/model.sentis");
            Directory.CreateDirectory(Path.GetDirectoryName(runtimeAbsolutePath) ?? throw new InvalidOperationException());
            File.WriteAllText(runtimeAbsolutePath, "sentis");
            AssetDatabase.ImportAsset("Assets/StreamingAssets/hf-hub/doji/test-model/models/model.sentis", ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            File.WriteAllText(sharedAbsolutePath, Newtonsoft.Json.JsonConvert.SerializeObject(new SharedImportMetadata
            {
                Repository = "doji/test-model",
                ResolvedCommitHash = "abc123",
                RawSnapshotRootPath = "/cache/doji/test-model/abc123",
                RawMetadataPath = "/cache/doji/test-model/abc123/snapshot.hfhub.json",
                RepositoryFiles = new[]
                {
                    new RepositoryFileMetadata
                    {
                        RepoPath = "models/model.onnx"
                    }
                },
                SentisVersion = sentisVersion,
                RuntimeSnapshotAssetPath = "Assets/StreamingAssets/hf-hub/doji/test-model",
                RuntimeFiles = new[]
                {
                    new ImportedRuntimeFileMetadata
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


    }
}
