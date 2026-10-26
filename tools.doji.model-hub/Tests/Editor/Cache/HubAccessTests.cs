using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Verifies gated, private, and missing repositories fail with a message that says so, in Editor sync and in
    /// player downloads, against a local fake Hub.
    /// </summary>
    internal sealed class HubAccessTests
    {
        private const string Repo = "doji/gated-model";
        private const string Commit = "0123456789abcdef0123456789abcdef01234567";
        private const string Etag = "3333333333333333333333333333333333333333";

        private FakeHubServer _server;
        private TestCacheScope _cacheScope;
        private string _rootDirectory;
        private FakeHubFile _model;

        [SetUp]
        public void SetUp()
        {
            _rootDirectory = Path.Combine(Path.GetTempPath(), "Doji.ModelHub.HubAccessTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rootDirectory);
            _cacheScope = new TestCacheScope(Path.Combine(_rootDirectory, "SharedCache"));
            _server = FakeHubServer.Start();
            HubEndpoint.Override = _server.Url;
            _model = _server.AddFile(Repo, Commit, "model.sentis", 256, Etag, lfs: false);
            _model.Gated = true;
        }

        [TearDown]
        public void TearDown()
        {
            HubEndpoint.Override = null;
            RuntimeModels.RegistryOverride = null;
            RuntimeModels.ResetForTests();
            _server?.Dispose();
            _cacheScope?.Dispose();
            if (Directory.Exists(_rootDirectory))
            {
                Directory.Delete(_rootDirectory, true);
            }
        }

        /// <summary>
        /// Verifies Editor sync of a gated file names the repository and file and says tokens are not supported.
        /// </summary>
        [Test]
        public async Task Sync_GatedFile_SaysModelIsGatedOrPrivate()
        {
            var service = new RawCacheService(new HttpClient());

            var exception = await CatchAsync(() => service.GetOrDownloadAsync(CreateDependency(Repo), null, CancellationToken.None));

            Assert.That(exception.Message, Does.Contain("'model.sentis' from 'doji/gated-model'"));
            Assert.That(exception.Message, Does.Contain("gated or private"));
            Assert.That(exception.Message, Does.Contain("access tokens are not supported"));
        }

        /// <summary>
        /// Verifies a repository the Hub does not list publicly fails the lookup with the id and revision.
        /// </summary>
        [Test]
        public async Task Sync_UnknownRepository_SaysNoPublicRepository()
        {
            var service = new RawCacheService(new HttpClient());

            var exception = await CatchAsync(() => service.GetOrDownloadAsync(CreateDependency("doji/missing-model"), null, CancellationToken.None));

            Assert.That(exception.Message, Does.Contain("no public model 'doji/missing-model' at revision 'main'"));
        }

        /// <summary>
        /// Verifies a player download of a gated file fails with the same message and leaves no partial file.
        /// </summary>
        [Test]
        public async Task Runtime_GatedFile_SaysModelIsGatedOrPrivate()
        {
            var exception = await CatchAsync(() => RuntimeDownloader.EnsureFileAsync(
                Repo,
                "main",
                Commit,
                new RuntimeSourceFile { repoPath = "model.sentis", etag = Etag, size = 256 },
                null,
                CancellationToken.None));

            Assert.That(exception.Message, Does.Contain("gated or private"));
            Assert.That(File.Exists(CachePaths.GetSnapshotFilePath(Repo, Commit, "model.sentis")), Is.False);
        }

        /// <summary>
        /// Awaits instead of blocking: UnityWebRequest completes on the main thread, which a blocking assert holds.
        /// </summary>
        private static async Task<IOException> CatchAsync(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (IOException exception)
            {
                return exception;
            }

            Assert.Fail("Expected an IOException.");
            return null;
        }

        private static DiscoveredModelDependency CreateDependency(string repository)
        {
            return new DiscoveredModelDependency(
                "tools.doji.gated",
                "tools.doji.gated",
                "1.0.0",
                "/tmp/tools.doji.gated",
                "/tmp/tools.doji.gated/model-hub-manifest.json",
                "gated-manifest-hash",
                new ModelDependencyDefinition(repository, "main", new[] { new ModelDependencyFileDefinition("model.sentis") }));
        }
    }
}
