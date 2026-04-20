using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Doji.Downloads;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers HTTP client repo-snapshot resolution using a fully mocked transport.
    /// </summary>
    internal sealed class HfHubHttpClientTests
    {
        private string _rootDirectory;

        [SetUp]
        public void SetUp()
        {
            _rootDirectory = Path.Combine(
                Path.GetTempPath(),
                "Doji.ModelHub.HttpClientTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rootDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_rootDirectory))
            {
                Directory.Delete(_rootDirectory, true);
            }
        }

        /// <summary>
        /// Verifies snapshot resolution reads repo commit and sibling file list, then resolves each file URL through HEAD.
        /// </summary>
        [Test]
        public async Task ResolveSnapshotVersionAsync_ResolvesCommitAndAllSiblingFiles()
        {
            var transport = new FakeTransport
            {
                JsonResponse = new JObject
                {
                    ["sha"] = "abc123",
                    ["siblings"] = new JArray
                    {
                        new JObject { ["rfilename"] = "config.json" },
                        new JObject { ["rfilename"] = "onnx/model.onnx" }
                    }
                }
            };
            transport.HeadResponses["https://huggingface.co/doji/test-model/resolve/main/config.json"] =
                CreateHeadResponse("https://cdn.example/config.json", new Dictionary<string, string>
                {
                    ["x-linked-etag"] = "\"etag-config\"",
                    ["x-linked-size"] = "10"
                });
            transport.HeadResponses["https://huggingface.co/doji/test-model/resolve/main/onnx/model.onnx"] =
                CreateHeadResponse("https://cdn.example/model.onnx", new Dictionary<string, string>
                {
                    ["x-linked-etag"] = "\"etag-model\"",
                    ["x-linked-size"] = "20"
                });

            var client = new HfHubHttpClient(transport);
            var resolved = await client.ResolveSnapshotVersionAsync(CreateDependency(), CancellationToken.None);

            Assert.That(resolved.ResolvedCommitHash, Is.EqualTo("abc123"));
            Assert.That(resolved.Files.Select(file => file.RepoPath), Is.EqualTo(new[] { "config.json", "onnx/model.onnx" }));
            Assert.That(resolved.Files.First(file => file.RepoPath == "onnx/model.onnx").DownloadUrl, Is.EqualTo("https://cdn.example/model.onnx"));
            Assert.That(transport.GetJsonCallCount, Is.EqualTo(1));
            Assert.That(transport.HeadCallCount, Is.EqualTo(2));
        }

        /// <summary>
        /// Verifies file download still delegates to transport for individual snapshot files.
        /// </summary>
        [Test]
        public async Task DownloadFileAsync_DelegatesToTransport()
        {
            var transport = new FakeTransport();
            var client = new HfHubHttpClient(transport);
            var targetPath = Path.Combine(_rootDirectory, "model.onnx");
            var progressRecorder = new ProgressRecorder();

            await client.DownloadFileAsync(
                new HfHubResolvedRepoFile(
                    "onnx/model.onnx",
                    "https://huggingface.co/doji/test-model/resolve/main/onnx/model.onnx",
                    "etag",
                    12),
                targetPath,
                progressRecorder,
                CancellationToken.None);

            Assert.That(transport.DownloadCallCount, Is.EqualTo(1));
            Assert.That(File.Exists(targetPath), Is.True);
            Assert.That(File.ReadAllText(targetPath), Is.EqualTo("downloaded"));
            Assert.That(progressRecorder.LastProgress.HasValue, Is.True);
            Assert.That(progressRecorder.LastProgress.Value.DownloadedBytes, Is.EqualTo(10));
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

        private static HfHubHeadResponse CreateHeadResponse(string requestUrl, IReadOnlyDictionary<string, string> headers)
        {
            return new HfHubHeadResponse(
                requestUrl,
                headerName => headers.TryGetValue(headerName, out var value) ? value : null);
        }

        private sealed class FakeTransport : IHfHubHttpTransport
        {
            public Dictionary<string, HfHubHeadResponse> HeadResponses { get; } = new(StringComparer.Ordinal);
            public JObject JsonResponse { get; set; }
            public int GetJsonCallCount { get; private set; }
            public int HeadCallCount { get; private set; }
            public int DownloadCallCount { get; private set; }

            public Task<HfHubHeadResponse> HeadAsync(string url, CancellationToken cancellationToken)
            {
                HeadCallCount++;
                return Task.FromResult(HeadResponses[url]);
            }

            public Task<JObject> GetJsonAsync(string url, CancellationToken cancellationToken)
            {
                GetJsonCallCount++;
                return Task.FromResult(JsonResponse);
            }

            public Task DownloadFileAsync(
                string url,
                string targetPath,
                long? expectedSizeInBytes,
                IProgress<DownloadProgress> progress,
                CancellationToken cancellationToken)
            {
                DownloadCallCount++;
                var directory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(targetPath, "downloaded");
                progress?.Report(new DownloadProgress(10, expectedSizeInBytes));
                return Task.CompletedTask;
            }
        }

        private sealed class ProgressRecorder : IProgress<DownloadProgress>
        {
            public DownloadProgress? LastProgress { get; private set; }

            public void Report(DownloadProgress value)
            {
                LastProgress = value;
            }
        }
    }
}
