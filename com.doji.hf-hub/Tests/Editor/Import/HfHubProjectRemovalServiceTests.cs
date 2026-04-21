using System;
using System.IO;
using NUnit.Framework;

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
    }
}
