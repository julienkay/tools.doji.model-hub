using System;
using System.IO;
using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers moving a cache folder: a rename on the same drive, a checked copy across drives that keeps the source
    /// until it is deleted on request, the Hugging Face layout with its relative snapshot links, and the free-space
    /// check before a copy.
    /// </summary>
    internal sealed class CacheFolderMoverTests
    {
        private const string Repository = "models--doji--test-model";
        private const string Commit = "abc123";
        private const string Etag = "5d41402abc";

        private string _root;
        private string _source;
        private string _destination;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "Doji.ModelHub.CacheFolderMoverTests", Guid.NewGuid().ToString("N"));
            _source = Path.Combine(_root, "a", "hub");
            _destination = Path.Combine(_root, "b", "hub");
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        /// <summary>
        /// On the same drive the content is renamed: the source is gone, refs and blobs are in the destination, and
        /// the snapshot file still reads the blob's content.
        /// </summary>
        [Test]
        public void Move_SameDrive_RenamesAndKeepsLayout()
        {
            var sourceIsLink = WriteRepository(_source);
            var disk = new FakeDiskSpace().Add(_root, "T:\\", 1000);

            var plan = CacheFolderMover.Plan(_source, _destination, disk);
            var result = CacheFolderMover.Move(plan, diskSpace: disk);

            Assert.That(plan.Method, Is.EqualTo(CacheMoveMethod.Rename));
            Assert.That(Directory.Exists(_source), Is.False);
            Assert.That(result.KeptInDestination, Is.Empty);
            AssertRepository(_destination, sourceIsLink);
        }

        /// <summary>
        /// A rename into a folder that already has content merges into it. A file the destination already has is
        /// kept and reported.
        /// </summary>
        [Test]
        public void Move_SameDrive_MergesIntoExistingFolder()
        {
            WriteRepository(_source);
            var existingRef = Path.Combine(_destination, Repository, "refs", "main");
            Directory.CreateDirectory(Path.GetDirectoryName(existingRef));
            File.WriteAllText(existingRef, "def456");
            var disk = new FakeDiskSpace().Add(_root, "T:\\", 1000);

            var result = CacheFolderMover.Move(CacheFolderMover.Plan(_source, _destination, disk), diskSpace: disk);

            Assert.That(result.KeptInDestination, Is.EqualTo(new[] { Path.Combine(Repository, "refs", "main") }));
            Assert.That(File.ReadAllText(existingRef), Is.EqualTo("def456"));
            Assert.That(File.ReadAllText(Path.Combine(_destination, Repository, "blobs", Etag)), Is.EqualTo("hello"));
        }

        /// <summary>
        /// Across drives the content is copied and checked, and the source stays until DeleteSource. Snapshot links
        /// are relative links to the copied blobs, not to the old folder.
        /// </summary>
        [Test]
        public void Move_OtherDrive_CopiesChecksAndKeepsSourceUntilDeleted()
        {
            var sourceIsLink = WriteRepository(_source);
            var disk = new FakeDiskSpace()
                .Add(Path.Combine(_root, "a"), "C:\\", 1000)
                .Add(Path.Combine(_root, "b"), "G:\\", 1000);

            var plan = CacheFolderMover.Plan(_source, _destination, disk);
            var result = CacheFolderMover.Move(plan, diskSpace: disk);

            Assert.That(plan.Method, Is.EqualTo(CacheMoveMethod.Copy));
            Assert.That(Directory.Exists(_source), Is.True, "The source stays until the user confirms.");
            AssertRepository(_destination, sourceIsLink);
            if (sourceIsLink && CacheFiles.AreSymlinksSupported(_destination))
            {
                Assert.That(result.LinksCreated, Is.EqualTo(1));
            }

            CacheFolderMover.DeleteSource(result);
            Assert.That(Directory.Exists(_source), Is.False);
            AssertRepository(_destination, sourceIsLink);
        }

        /// <summary>
        /// A copy that does not fit on the new drive stops before it copies anything, with the needed and free size.
        /// </summary>
        [Test]
        public void Move_OtherDrive_StopsWhenTheDriveIsFull()
        {
            WriteRepository(_source);
            var disk = new FakeDiskSpace()
                .Add(Path.Combine(_root, "a"), "C:\\", 1000)
                .Add(Path.Combine(_root, "b"), "G:\\", 3);

            var plan = CacheFolderMover.Plan(_source, _destination, disk);
            var exception = Assert.Throws<InsufficientDiskSpaceException>(() => CacheFolderMover.Move(plan, diskSpace: disk));

            Assert.That(exception.Shortfalls[0].DriveRoot, Is.EqualTo("G:\\"));
            Assert.That(exception.Shortfalls[0].AvailableBytes, Is.EqualTo(3));
            Assert.That(exception.Shortfalls[0].NeededBytes, Is.GreaterThanOrEqualTo(plan.FileBytes));
            Assert.That(Directory.Exists(Path.Combine(_destination, Repository)), Is.False);
            Assert.That(Directory.Exists(_source), Is.True);
        }

        /// <summary>
        /// A destination inside the source, or the source itself, is refused before anything moves.
        /// </summary>
        [Test]
        public void Plan_RefusesNestedOrSameFolder()
        {
            Directory.CreateDirectory(_source);

            Assert.Throws<InvalidOperationException>(() => CacheFolderMover.Plan(_source, Path.Combine(_source, "inner")));
            Assert.Throws<InvalidOperationException>(() => CacheFolderMover.Plan(_source, _source + Path.DirectorySeparatorChar));
        }

        /// <summary>
        /// Writes one repository in the Hugging Face layout: a ref, a blob, and a snapshot entry that links to the blob
        /// when the file system allows it. Returns whether the snapshot entry is a link.
        /// </summary>
        private static bool WriteRepository(string cacheRoot)
        {
            var repositoryRoot = Path.Combine(cacheRoot, Repository);
            Directory.CreateDirectory(Path.Combine(repositoryRoot, "refs"));
            File.WriteAllText(Path.Combine(repositoryRoot, "refs", "main"), Commit);
            var blob = Path.Combine(repositoryRoot, "blobs", Etag);
            Directory.CreateDirectory(Path.GetDirectoryName(blob));
            File.WriteAllText(blob, "hello");
            var snapshot = Path.Combine(repositoryRoot, "snapshots", Commit, "onnx", "model.onnx");
            CacheFiles.LinkSnapshotFile(blob, snapshot, isNewBlob: false);
            Directory.CreateDirectory(Path.Combine(cacheRoot, ".locks", Repository));
            return CacheFiles.IsSymlink(snapshot);
        }

        private static void AssertRepository(string cacheRoot, bool expectLink)
        {
            var repositoryRoot = Path.Combine(cacheRoot, Repository);
            var blob = Path.Combine(repositoryRoot, "blobs", Etag);
            var snapshot = Path.Combine(repositoryRoot, "snapshots", Commit, "onnx", "model.onnx");
            Assert.That(File.ReadAllText(Path.Combine(repositoryRoot, "refs", "main")), Is.EqualTo(Commit));
            Assert.That(File.ReadAllText(blob), Is.EqualTo("hello"));
            Assert.That(File.ReadAllText(snapshot), Is.EqualTo("hello"));
            if (expectLink && CacheFiles.AreSymlinksSupported(cacheRoot))
            {
                Assert.That(CacheFiles.IsSymlink(snapshot), Is.True);
                Assert.That(
                    Path.GetFullPath(CacheFiles.TryGetLinkTarget(snapshot)),
                    Is.EqualTo(Path.GetFullPath(blob)).IgnoreCase);
            }
        }
    }
}
