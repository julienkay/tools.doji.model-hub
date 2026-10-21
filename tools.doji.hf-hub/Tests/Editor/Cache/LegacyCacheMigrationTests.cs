using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers moving HF Hub's old private cache (<c>&lt;data-root&gt;/raw</c>) into the shared Hugging Face cache.
    /// </summary>
    internal sealed class LegacyCacheMigrationTests
    {
        private const string Repository = "doji/test-model";
        private const string Commit = "0123456789abcdef0123456789abcdef01234567";

        private TestCacheScope _cacheScope;
        private string _rootDirectory;
        private string _legacyRoot;

        [SetUp]
        public void SetUp()
        {
            _rootDirectory = Path.Combine(Path.GetTempPath(), "Doji.ModelHub.LegacyCacheMigrationTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rootDirectory);
            _cacheScope = new TestCacheScope(Path.Combine(_rootDirectory, "SharedCache"));
            _legacyRoot = LegacyCacheMigration.GetLegacyRoot();
        }

        [TearDown]
        public void TearDown()
        {
            CacheFiles.ForceSymlinkSupport = null;
            _cacheScope?.Dispose();
            _cacheScope = null;
            if (Directory.Exists(_rootDirectory))
            {
                Directory.Delete(_rootDirectory, true);
            }
        }

        /// <summary>
        /// Verifies listed files move into the shared cache with their ref and index, and the old folder is removed.
        /// </summary>
        [Test]
        public void Migrate_MovesListedFiles_AndRemovesLegacyFolder()
        {
            CacheFiles.ForceSymlinkSupport = false;
            CreateLegacySnapshot(
                new LegacyFile("onnx/model.onnx", "model-bytes", "\"etag-model\""),
                new LegacyFile("config.json", "{}", null));

            var result = LegacyCacheMigration.Migrate(_legacyRoot);

            Assert.That(result.Failures, Is.Empty);
            Assert.That(result.Repositories, Is.EqualTo(new[] { Repository }));
            Assert.That(result.Files, Is.EqualTo(2));
            Assert.That(File.ReadAllText(CachePaths.GetSnapshotFilePath(Repository, Commit, "onnx/model.onnx")), Is.EqualTo("model-bytes"));
            Assert.That(File.ReadAllText(CachePaths.GetSnapshotFilePath(Repository, Commit, "config.json")), Is.EqualTo("{}"));
            Assert.That(File.ReadAllText(CachePaths.GetRevisionRefPath(Repository, "main")), Is.EqualTo(Commit));
            Assert.That(Directory.Exists(_legacyRoot), Is.False);

            var snapshot = new RawCacheService().ReadSnapshot(Repository, "main", Commit);
            Assert.That(snapshot.RepositoryFiles.Select(file => file.RepoPath), Is.EquivalentTo(new[] { "config.json", "onnx/model.onnx", "onnx/extra.onnx" }));
            Assert.That(snapshot.Files.Single(file => file.RepoPath == "onnx/model.onnx").EntityTag, Is.EqualTo("etag-model"));
        }

        /// <summary>
        /// Verifies a moved file becomes a blob named by its etag, with a snapshot link, like a download.
        /// </summary>
        [Test]
        public void Migrate_StoresFilesAsBlobs_WhenSymlinksAreSupported()
        {
            Assume.That(CacheFiles.AreSymlinksSupported(_rootDirectory), Is.True, "This machine cannot create symbolic links.");
            CreateLegacySnapshot(new LegacyFile("onnx/model.onnx", "model-bytes", "\"etag-model\""));

            LegacyCacheMigration.Migrate(_legacyRoot);

            var snapshotPath = CachePaths.GetSnapshotFilePath(Repository, Commit, "onnx/model.onnx");
            Assert.That(File.ReadAllText(CachePaths.GetBlobPath(Repository, "etag-model")), Is.EqualTo("model-bytes"));
            Assert.That(CacheFiles.IsSymlink(snapshotPath), Is.True);
            Assert.That(File.ReadAllText(snapshotPath), Is.EqualTo("model-bytes"));
        }

        /// <summary>
        /// Verifies a large file, whose old etag was the CDN's xet hash, is named by the sha256 of its content, the
        /// LFS etag that huggingface_hub uses.
        /// </summary>
        [Test]
        public void Migrate_NamesLargeFileBlobsBySha256()
        {
            CacheFiles.ForceSymlinkSupport = false;
            CreateLegacySnapshot(new LegacyFile("onnx/model.onnx", "model-bytes", new string('a', 64)));
            string expected;
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                expected = string.Concat(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes("model-bytes")).Select(value => value.ToString("x2")));
            }

            LegacyCacheMigration.Migrate(_legacyRoot);

            var index = JsonConvert.DeserializeObject<RawCacheMetadata>(File.ReadAllText(CachePaths.GetSnapshotMetadataPath(Repository, Commit)));
            Assert.That(index.Files.Single().EntityTag, Is.EqualTo(expected));
            Assert.That(File.Exists(CachePaths.GetBlobPath(Repository, new string('a', 64))), Is.False);
        }

        /// <summary>
        /// Verifies partial downloads, unlisted files, and files with the wrong size are not moved.
        /// </summary>
        [Test]
        public void Migrate_DropsPartialUnlistedAndWrongSizeFiles()
        {
            CacheFiles.ForceSymlinkSupport = false;
            var snapshotFolder = CreateLegacySnapshot(
                new LegacyFile("onnx/model.onnx", "model-bytes", "\"etag-model\""),
                new LegacyFile("onnx/broken.onnx", "short", "\"etag-broken\"", sizeInBytes: 999));
            File.WriteAllText(Path.Combine(snapshotFolder, "onnx", "extra.onnx.downloading"), "part");
            File.WriteAllText(Path.Combine(snapshotFolder, "onnx", "extra.onnx"), "unlisted");

            var result = LegacyCacheMigration.Migrate(_legacyRoot);

            Assert.That(result.Files, Is.EqualTo(1));
            var cachedPaths = new RawCacheService().ReadSnapshot(Repository, "main", Commit).Files.Select(file => file.RepoPath);
            Assert.That(cachedPaths, Is.EqualTo(new[] { "onnx/model.onnx" }));
            Assert.That(Directory.Exists(_legacyRoot), Is.False);
        }

        /// <summary>
        /// Verifies the shared cache wins: an existing blob and a newer ref stay as they are.
        /// </summary>
        [Test]
        public void Migrate_KeepsExistingBlobAndRef()
        {
            CacheFiles.ForceSymlinkSupport = false;
            CreateLegacySnapshot(new LegacyFile("onnx/model.onnx", "old-bytes", "\"etag-model\""));
            var blobPath = CachePaths.GetBlobPath(Repository, "etag-model");
            Directory.CreateDirectory(Path.GetDirectoryName(blobPath));
            File.WriteAllText(blobPath, "new-bytes");
            RawCacheService.WriteRevisionRef(Repository, "main", "fedcba9876543210fedcba9876543210fedcba98");

            var result = LegacyCacheMigration.Migrate(_legacyRoot);

            Assert.That(result.MovedBytes, Is.Zero);
            Assert.That(File.ReadAllText(blobPath), Is.EqualTo("new-bytes"));
            Assert.That(File.ReadAllText(CachePaths.GetSnapshotFilePath(Repository, Commit, "onnx/model.onnx")), Is.EqualTo("new-bytes"));
            Assert.That(File.ReadAllText(CachePaths.GetRevisionRefPath(Repository, "main")), Is.EqualTo("fedcba9876543210fedcba9876543210fedcba98"));
        }

        /// <summary>
        /// Verifies a repository whose blob another program is writing stays in the old folder for a later retry.
        /// </summary>
        [Test]
        public void Migrate_KeepsRepositoryInLegacyFolder_WhenBlobIsLocked()
        {
            var snapshotFolder = CreateLegacySnapshot(new LegacyFile("onnx/model.onnx", "model-bytes", "\"etag-model\""));
            Assert.That(BlobLock.TryAcquire(CachePaths.GetBlobLockPath(Repository, "etag-model"), out var heldLock), Is.True);

            LegacyCacheMigration.MigrationResult result;
            using (heldLock)
            {
                result = LegacyCacheMigration.Migrate(_legacyRoot);
            }

            Assert.That(result.Failures, Has.Count.EqualTo(1));
            Assert.That(File.ReadAllText(Path.Combine(snapshotFolder, "onnx", "model.onnx")), Is.EqualTo("model-bytes"));

            var retry = LegacyCacheMigration.Migrate(_legacyRoot);

            Assert.That(retry.Failures, Is.Empty);
            Assert.That(File.ReadAllText(CachePaths.GetSnapshotFilePath(Repository, Commit, "onnx/model.onnx")), Is.EqualTo("model-bytes"));
            Assert.That(Directory.Exists(_legacyRoot), Is.False);
        }

        private string CreateLegacySnapshot(params LegacyFile[] files)
        {
            var repositoryFolder = Path.Combine(_legacyRoot, "doji--test-model");
            var snapshotFolder = Path.Combine(repositoryFolder, "snapshots", Commit);
            Directory.CreateDirectory(Path.Combine(repositoryFolder, "refs"));
            File.WriteAllText(Path.Combine(repositoryFolder, "refs", "main"), Commit);

            var entries = new List<RawCacheFileMetadata>();
            foreach (var file in files)
            {
                var path = Path.Combine(snapshotFolder, file.RepoPath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, file.Content);
                entries.Add(new RawCacheFileMetadata
                {
                    RepoPath = file.RepoPath,
                    CachedFilePath = path,
                    EntityTag = file.EntityTag,
                    SizeInBytes = file.SizeInBytes ?? new FileInfo(path).Length
                });
            }

            var metadata = new RawCacheMetadata
            {
                Repository = Repository,
                RequestedRevision = "main",
                ResolvedCommitHash = Commit,
                CachedSnapshotRoot = snapshotFolder,
                RepositoryFiles = entries
                    .Select(entry => new RepositoryFileMetadata { RepoPath = entry.RepoPath, SizeInBytes = entry.SizeInBytes })
                    .Append(new RepositoryFileMetadata { RepoPath = "onnx/extra.onnx", SizeInBytes = 8 })
                    .ToArray(),
                Files = entries.ToArray()
            };
            File.WriteAllText(Path.Combine(snapshotFolder, "snapshot.hfhub.json"), JsonConvert.SerializeObject(metadata, Formatting.Indented));
            return snapshotFolder;
        }

        private sealed class LegacyFile
        {
            public LegacyFile(string repoPath, string content, string entityTag, long? sizeInBytes = null)
            {
                RepoPath = repoPath;
                Content = content;
                EntityTag = entityTag;
                SizeInBytes = sizeInBytes;
            }

            public string RepoPath { get; }
            public string Content { get; }
            public string EntityTag { get; }
            public long? SizeInBytes { get; }
        }
    }
}
