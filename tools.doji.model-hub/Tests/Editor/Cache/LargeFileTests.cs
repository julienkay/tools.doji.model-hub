using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Doji.Downloads;
using NUnit.Framework;
using Unity.InferenceEngine;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Downloads files larger than 2 GiB from a local fake Hub, through Editor sync and through the player download.
    /// The files are ONNX external weights, the usual way models past the 2 GiB protobuf limit ship. Each test cuts
    /// the connection after the 2 GiB mark and resumes, so offsets and sizes past <see cref="int.MaxValue"/> go
    /// through the Range request, the partial file, the blob, and the snapshot link. Each test writes about 2.1 GiB
    /// to the temp folder and deletes it.
    /// </summary>
    [Category("LargeFiles")]
    internal sealed class LargeFileTests
    {
        private const string Repo = "doji/large-model";
        private const string Commit = "fedcba9876543210fedcba9876543210fedcba98";
        private const string OnnxEtag = "1111111111111111111111111111111111111111";
        private const string WeightsEtag = "2222222222222222222222222222222222222222222222222222222222222222";
        private const long WeightsSize = int.MaxValue + 64L * 1024 * 1024 + 3;
        private const long CutAfter = int.MaxValue + 16L * 1024 * 1024;

        private FakeHubServer _server;
        private TestCacheScope _cacheScope;
        private string _rootDirectory;
        private FakeHubFile _weights;

        private static readonly string TempRoot = Path.Combine(Path.GetTempPath(), "Doji.ModelHub.LargeFileTests");

        /// <summary>
        /// Deletes folders left by runs that stopped before TearDown, for example when the Editor closed mid-download.
        /// Each one holds up to 2.1 GiB and nothing reads it again.
        /// </summary>
        [OneTimeSetUp]
        public void DeleteLeftoverRuns()
        {
            if (!Directory.Exists(TempRoot))
            {
                return;
            }

            foreach (var directory in Directory.GetDirectories(TempRoot))
            {
                TryDeleteDirectory(directory);
            }
        }

        [OneTimeTearDown]
        public void DeleteTempRoot()
        {
            if (Directory.Exists(TempRoot) && !Directory.EnumerateFileSystemEntries(TempRoot).Any())
            {
                TryDeleteDirectory(TempRoot);
            }
        }

        [SetUp]
        public void SetUp()
        {
            _rootDirectory = Path.Combine(TempRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rootDirectory);
            _cacheScope = new TestCacheScope(Path.Combine(_rootDirectory, "SharedCache"));
            _server = FakeHubServer.Start();
            HubEndpoint.Override = _server.Url;
            CacheFiles.ForceSymlinkSupport = false;
            _server.AddFile(Repo, Commit, "onnx/model.onnx", 1024, OnnxEtag, lfs: false);
            _weights = _server.AddFile(Repo, Commit, "onnx/model.onnx_data", WeightsSize, WeightsEtag, lfs: true);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                HubEndpoint.Override = null;
                CacheFiles.ForceSymlinkSupport = null;
                RuntimeModels.RegistryOverride = null;
                RuntimeModels.ResetForTests();
                Hub.RegisterOnnxConverter(null, null);
                _server?.Dispose();
                _cacheScope?.Dispose();
            }
            finally
            {
                // Runs even when a reset above throws, so a failed test never keeps 2.1 GiB on disk.
                TryDeleteDirectory(_rootDirectory);
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (path != null && Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch (IOException)
            {
                // A file is still open. The next run's OneTimeSetUp deletes the folder.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        /// <summary>
        /// Verifies Editor sync downloads the external weights of a selected ONNX file, resumes them after a dropped
        /// connection past 2 GiB, stores them as the etag's blob, and reports the full size.
        /// </summary>
        [Test]
        public async Task Sync_ResumesExternalWeightsLargerThan2GiB()
        {
            var dependency = new DiscoveredModelDependency(
                "tools.doji.large",
                "tools.doji.large",
                "1.0.0",
                "/tmp/tools.doji.large",
                "/tmp/tools.doji.large/model-hub-manifest.json",
                "large-manifest-hash",
                new ModelDependencyDefinition(Repo, "main", new[] { new ModelDependencyFileDefinition("onnx/model.onnx") }));
            var service = new RawCacheService(new HttpClient());
            _weights.CutNextDownloadAfter(CutAfter);

            var interrupted = await CatchAsync(() => service.GetOrDownloadAsync(dependency, null, CancellationToken.None));
            var progress = new LastProgress();
            var snapshot = await service.GetOrDownloadAsync(dependency, progress, CancellationToken.None);

            Assert.That(interrupted, Is.Not.Null, "The dropped connection fails the first sync.");
            Assert.That(snapshot.TryGetFilePath("onnx/model.onnx_data", out var weightsPath), Is.True, "The companion file is part of the selection.");
            AssertResumedPast2GiB(weightsPath);
            Assert.That(CacheFiles.GetLength(CachePaths.GetSnapshotFilePath(Repo, Commit, "onnx/model.onnx")), Is.EqualTo(1024));
            Assert.That(progress.Value?.TotalBytes, Is.EqualTo(WeightsSize));
            Assert.That(progress.Value?.DownloadedBytes, Is.EqualTo(WeightsSize));
        }

        /// <summary>
        /// Verifies a player downloads the external weights of an ONNX model at runtime, resumes them after a dropped
        /// connection past 2 GiB, converts with both files in place, and loads from the device cache afterwards.
        /// </summary>
        [Test]
        public async Task Runtime_ResumesExternalWeightsLargerThan2GiB()
        {
            var converter = new RecordingConverter();
            Hub.RegisterOnnxConverter(converter, "test-converter@1");
            RuntimeModels.RegistryOverride = new RuntimeRegistry
            {
                packages = new[]
                {
                    new RuntimePackageEntry
                    {
                        packageId = "tools.doji.large",
                        packageVersion = "1.0.0",
                        models = new[]
                        {
                            new RuntimeModelEntry
                            {
                                repoId = Repo,
                                revision = "main",
                                commit = Commit,
                                delivery = "download",
                                files = new[]
                                {
                                    new RuntimeModelFile
                                    {
                                        repoPath = "onnx/model.onnx",
                                        format = "onnx",
                                        source = new RuntimeSourceFile { repoPath = "onnx/model.onnx", etag = OnnxEtag, size = 1024 },
                                        companions = new[] { new RuntimeSourceFile { repoPath = "onnx/model.onnx_data", etag = WeightsEtag, size = WeightsSize } },
                                        quantization = "none",
                                        recipeHash = "recipe-1"
                                    }
                                }
                            }
                        }
                    }
                }
            };
            _weights.CutNextDownloadAfter(CutAfter);

            var interrupted = await CatchAsync(() => Hub.LoadModelAsync(Repo, "onnx/model.onnx"));
            var model = await Hub.LoadModelAsync(Repo, "onnx/model.onnx");
            var requestsBeforeReload = _server.Requests.Count;
            var reloaded = await Hub.LoadModelAsync(Repo, "onnx/model.onnx");

            Assert.That(interrupted, Is.Not.Null, "The dropped connection fails the first load.");
            AssertResumedPast2GiB(CachePaths.GetSnapshotFilePath(Repo, Commit, "onnx/model.onnx_data"));
            Assert.That(converter.WeightsSizes, Is.EqualTo(new[] { WeightsSize }), "Converted once, with the complete weights next to the ONNX file.");
            Assert.That(model, Is.Not.Null);
            Assert.That(reloaded, Is.Not.Null);
            Assert.That(_server.Requests.Count, Is.EqualTo(requestsBeforeReload), "A later load needs no network.");
        }

        /// <summary>
        /// Checks the size, that the second download continued past 2 GiB instead of starting over, and the bytes
        /// around the 2 GiB mark, the cut, and the end.
        /// </summary>
        private void AssertResumedPast2GiB(string weightsPath)
        {
            Assert.That(CacheFiles.GetLength(weightsPath), Is.EqualTo(WeightsSize));
            var downloads = _server.DownloadsOf(_weights);
            Assert.That(downloads.Count, Is.EqualTo(2), "One cut download and one resume.");
            Assert.That(downloads[0].Range, Is.Null);
            Assert.That(downloads[1].Range, Does.StartWith("bytes="));
            var resumedFrom = long.Parse(downloads[1].Range.Substring(6).TrimEnd('-'));
            Assert.That(resumedFrom, Is.GreaterThan((long)int.MaxValue).And.LessThanOrEqualTo(CutAfter));
            Assert.That(downloads[1].IfRange, Is.EqualTo($"\"{WeightsEtag}\""));

            using var stream = new FileStream(weightsPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            foreach (var offset in new[] { 0L, int.MaxValue - 2L, resumedFrom - 2, WeightsSize - 4 })
            {
                var actual = new byte[4];
                stream.Position = offset;
                Assert.That(stream.Read(actual, 0, 4), Is.EqualTo(4));
                var expected = Enumerable.Range(0, 4).Select(index => FakeHubServer.ByteAt(offset + index)).ToArray();
                Assert.That(actual, Is.EqualTo(expected), $"Bytes at offset {offset}.");
            }

            var blobs = Directory.GetFiles(CachePaths.GetBlobsRoot(Repo));
            Assert.That(blobs.Where(path => !path.EndsWith(".lock", StringComparison.Ordinal)), Is.Empty, "The copy fallback moves the blob into the snapshot.");
            Assert.That(Directory.GetFiles(CachePaths.GetSnapshotRoot(Repo, Commit), "*.downloading*", SearchOption.AllDirectories), Is.Empty);
        }

        private static async Task<Exception> CatchAsync(Func<Task> action)
        {
            try
            {
                await action();
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private sealed class LastProgress : IProgress<DownloadProgress>
        {
            public DownloadProgress? Value { get; private set; }

            public void Report(DownloadProgress value)
            {
                if (value.TotalBytes == WeightsSize)
                {
                    Value = value;
                }
            }
        }

        /// <summary>
        /// Records the size of the weights next to the ONNX file, then returns a small real model: the fake Hub serves
        /// generated bytes, not a model.
        /// </summary>
        private sealed class RecordingConverter : IOnnxModelConverter
        {
            public List<long> WeightsSizes { get; } = new();

            public OnnxConversionResult Convert(OnnxConversionRequest request)
            {
                WeightsSizes.Add(new FileInfo(request.OnnxFilePath + "_data").Length);
                var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ModelAsset).Assembly);
                var sample = Path.Combine(packageInfo.resolvedPath, "Samples~", "Run a model", "SingleInputSingleOuput.onnx");
                return new EditorOnnxModelConverter().Convert(new OnnxConversionRequest(sample, request.Recipe));
            }
        }
    }
}
