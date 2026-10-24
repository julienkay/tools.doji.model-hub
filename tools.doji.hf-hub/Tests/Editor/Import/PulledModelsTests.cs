using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers copying runtime models into Assets/StreamingAssets/hf-hub/models, where builds and Play mode use them.
    /// </summary>
    internal sealed class PulledModelsTests
    {
        private const string Repository = "doji/pull-test";
        private TestProjectStateScope _projectScope;
        private string _rootDirectory;

        [SetUp]
        public void SetUp()
        {
            _projectScope = new TestProjectStateScope();
            _rootDirectory = Path.Combine(Path.GetTempPath(), "Doji.ModelHub.PulledModelsTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rootDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            _projectScope.Dispose();
            Directory.Delete(_rootDirectory, true);
        }

        /// <summary>
        /// Verifies pulling copies each runtime model to the path a build would use, and removing deletes the copies.
        /// </summary>
        [Test]
        public void Pull_CopiesRuntimeModelsToTheBuildPathAndRemoveDeletesThem()
        {
            var files = new[] { RuntimeFile("onnx/model.onnx", "converted"), RuntimeFile("sentis/model.sentis", "downloaded") };

            var written = PulledModels.Pull(Repository, files);

            Assert.That(written, Is.EqualTo(new[]
            {
                "Assets/StreamingAssets/hf-hub/models/doji/pull-test/onnx/model.sentis",
                "Assets/StreamingAssets/hf-hub/models/doji/pull-test/sentis/model.sentis"
            }));
            Assert.That(File.ReadAllText(ProjectPaths.ToAbsolutePath(written[0])), Is.EqualTo("converted"));
            Assert.That(PulledModels.HasCopies(Repository, files), Is.True);

            Assert.That(PulledModels.Remove(Repository), Is.True);
            Assert.That(PulledModels.HasCopies(Repository, files), Is.False);
            Assert.That(File.Exists(ProjectPaths.ToAbsolutePath(written[0])), Is.False);
        }

        /// <summary>
        /// Verifies pulling a model whose runtime model is missing explains that it must be imported first.
        /// </summary>
        [Test]
        public void Pull_ExplainsAMissingRuntimeModel()
        {
            var missing = new ImportedRuntimeFileMetadata { RepoPath = "a.onnx", FilePath = Path.Combine(_rootDirectory, "missing.sentis") };

            var exception = Assert.Throws<FileNotFoundException>(() => PulledModels.Pull(Repository, new[] { missing }));

            Assert.That(exception.Message, Does.Contain("Import doji/pull-test first"));
        }

        private ImportedRuntimeFileMetadata RuntimeFile(string repoPath, string content)
        {
            var path = Path.Combine(_rootDirectory, Guid.NewGuid().ToString("N") + ".sentis");
            File.WriteAllText(path, content);
            return new ImportedRuntimeFileMetadata { RepoPath = repoPath, Kind = "sentis", FilePath = path };
        }
    }
}
