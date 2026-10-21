using System.IO;
using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers the cache layout shared with the huggingface_hub Python library.
    /// </summary>
    internal sealed class CachePathsTests
    {
        private TestCacheScope _cacheScope;
        private string _cacheRoot;

        [SetUp]
        public void SetUp()
        {
            _cacheRoot = Path.Combine(Path.GetTempPath(), "hfhub-cachepaths-tests", "hub");
            _cacheScope = new TestCacheScope(_cacheRoot);
        }

        [TearDown]
        public void TearDown()
        {
            _cacheScope.Dispose();
        }

        /// <summary>
        /// Repository folders use the models--owner--name naming of huggingface_hub.
        /// </summary>
        [Test]
        public void GetRepositoryRoot_UsesHuggingFaceFolderName()
        {
            Assert.That(CachePaths.GetRepositoryFolderName("julienkay/sentis-MiDaS"), Is.EqualTo("models--julienkay--sentis-MiDaS"));
            Assert.That(CachePaths.GetRepositoryRoot("julienkay/sentis-MiDaS"), Is.EqualTo(Path.Combine(_cacheRoot, "models--julienkay--sentis-MiDaS")));
        }

        /// <summary>
        /// A manifest id that differs only in letter case finds the existing repository folder, with its locks and
        /// index, instead of starting a second copy.
        /// </summary>
        [Test]
        public void GetRepositoryRoot_ReusesFolderThatDiffersInCase()
        {
            var existing = Path.Combine(_cacheRoot, "models--julienkay--sentis-MiDaS");
            Directory.CreateDirectory(existing);

            var root = CachePaths.GetRepositoryRoot("julienkay/sentis-midas");

            Assert.That(root, Is.EqualTo(existing));
            Assert.That(
                Path.GetFileName(Path.GetDirectoryName(CachePaths.GetBlobLockPath("julienkay/sentis-midas", "abc"))),
                Is.EqualTo("models--julienkay--sentis-MiDaS"));
            Assert.That(
                Path.GetFileName(Path.GetDirectoryName(CachePaths.GetSnapshotMetadataPath("julienkay/sentis-midas", "abc123"))),
                Is.EqualTo("models--julienkay--sentis-MiDaS"));
        }

        /// <summary>
        /// Refs, snapshots, and blobs sit inside the repository folder as in huggingface_hub; a revision
        /// with slashes becomes nested folders.
        /// </summary>
        [Test]
        public void RepositoryPaths_MatchHuggingFaceLayout()
        {
            var repoRoot = Path.Combine(_cacheRoot, "models--owner--model");

            Assert.That(CachePaths.GetRevisionRefPath("owner/model", "main"), Is.EqualTo(Path.Combine(repoRoot, "refs", "main")));
            Assert.That(CachePaths.GetRevisionRefPath("owner/model", "refs/pr/1"), Is.EqualTo(Path.Combine(repoRoot, "refs", "refs", "pr", "1")));
            Assert.That(CachePaths.GetSnapshotFilePath("owner/model", "abc123", "onnx/model.onnx"), Is.EqualTo(Path.Combine(repoRoot, "snapshots", "abc123", "onnx", "model.onnx")));
            Assert.That(CachePaths.GetBlobPath("owner/model", "\"5d41402abc\""), Is.EqualTo(Path.Combine(repoRoot, "blobs", "5d41402abc")));
            Assert.That(CachePaths.GetBlobLockPath("owner/model", "5d41402abc"), Is.EqualTo(Path.Combine(_cacheRoot, ".locks", "models--owner--model", "5d41402abc.lock")));
        }

        /// <summary>
        /// HF Hub's own index stays outside the Hugging Face cache, so Python tools never see it.
        /// </summary>
        [Test]
        public void SnapshotMetadata_LivesOutsideTheCache()
        {
            var metadataPath = CachePaths.GetSnapshotMetadataPath("owner/model", "abc123");

            Assert.That(metadataPath, Does.StartWith(_cacheScope.DataRoot));
            Assert.That(metadataPath, Does.Not.StartWith(_cacheRoot + Path.DirectorySeparatorChar));
            Assert.That(CachePaths.GetSnapshotLockPath("owner/model", "abc123"), Does.StartWith(_cacheScope.DataRoot));
        }

        /// <summary>
        /// Paths never escape the repository folder through "." or ".." segments.
        /// </summary>
        [Test]
        public void GetSnapshotFilePath_DropsDotSegments()
        {
            var path = CachePaths.GetSnapshotFilePath("owner/model", "abc123", "../../outside.txt");

            Assert.That(path, Is.EqualTo(Path.Combine(_cacheRoot, "models--owner--model", "snapshots", "abc123", "outside.txt")));
        }
    }
}
