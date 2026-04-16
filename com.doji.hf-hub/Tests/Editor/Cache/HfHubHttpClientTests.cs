using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Doji.Downloads;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers HTTP client behavior using a fully mocked transport so tests stay independent from live Hub responses.
    /// The fake transport returns synthetic HEAD headers, JSON payloads, and file downloads to verify request-selection logic only.
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

        [Test]
        public async Task ResolveFileVersionAsync_UsesHeadHeadersWhenPresent()
        {
            // Fake transport simulates successful HEAD response with all metadata already present.
            // Test verifies client does not call JSON fallback and maps headers into resolved file version.
            var transport = new FakeTransport
            {
                HeadResponse = CreateHeadResponse(
                    "https://cdn.example/model.onnx",
                    new Dictionary<string, string>
                    {
                        ["x-repo-commit"] = "abc123",
                        ["x-linked-etag"] = "\"etag-1\"",
                        ["x-linked-size"] = "42"
                    })
            };
            var client = new HfHubHttpClient(transport);

            var resolved = await client.ResolveFileVersionAsync(CreateDependency(), CancellationToken.None);

            Assert.That(resolved.ResolvedCommitHash, Is.EqualTo("abc123"));
            Assert.That(resolved.EntityTag, Is.EqualTo("etag-1"));
            Assert.That(resolved.SizeInBytes, Is.EqualTo(42));
            Assert.That(resolved.DownloadUrl, Is.EqualTo("https://cdn.example/model.onnx"));
            Assert.That(transport.GetJsonCallCount, Is.EqualTo(0));
        }

        [Test]
        public async Task ResolveFileVersionAsync_FallsBackToModelRevisionApiWhenHeadMissesCommit()
        {
            // Fake transport omits commit header from HEAD response on purpose.
            // Test verifies client falls back to model revision API and still keeps etag and size from HEAD response.
            var transport = new FakeTransport
            {
                HeadResponse = CreateHeadResponse(
                    "https://huggingface.co/doji/test-model/resolve/main/model.onnx",
                    new Dictionary<string, string>
                    {
                        ["etag"] = "\"etag-2\"",
                        ["content-length"] = "99"
                    }),
                JsonResponse = new JObject
                {
                    ["sha"] = "fallbacksha"
                }
            };
            var client = new HfHubHttpClient(transport);

            var resolved = await client.ResolveFileVersionAsync(CreateDependency(), CancellationToken.None);

            Assert.That(resolved.ResolvedCommitHash, Is.EqualTo("fallbacksha"));
            Assert.That(resolved.EntityTag, Is.EqualTo("etag-2"));
            Assert.That(resolved.SizeInBytes, Is.EqualTo(99));
            Assert.That(transport.GetJsonCallCount, Is.EqualTo(1));
            StringAssert.Contains("/api/models/doji/test-model/revision/main", transport.LastJsonUrl);
        }

        [Test]
        public async Task DownloadFileAsync_DelegatesToTransport()
        {
            // Fake transport writes fixed file contents to target path.
            // Test verifies client delegates download work instead of reimplementing transport-side file write logic.
            var transport = new FakeTransport();
            var client = new HfHubHttpClient(transport);
            var targetPath = Path.Combine(_rootDirectory, "model.onnx");
            var progressRecorder = new ProgressRecorder();

            await client.DownloadFileAsync(
                new HfHubResolvedFileVersion(
                    "doji/test-model",
                    "main",
                    "abc123",
                    "model.onnx",
                    "https://huggingface.co/doji/test-model/resolve/main/model.onnx",
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
                    "test-model",
                    "Test Model",
                    "doji/test-model",
                    "main",
                    "model.onnx"));
        }

        private static HfHubHeadResponse CreateHeadResponse(string requestUrl, IReadOnlyDictionary<string, string> headers)
        {
            return new HfHubHeadResponse(
                requestUrl,
                headerName => headers.TryGetValue(headerName, out var value) ? value : null);
        }

        /// <summary>
        /// Minimal fake transport used by HTTP client tests.
        /// `HeadResponse` controls metadata resolution, `JsonResponse` controls API fallback, and `DownloadFileAsync`
        /// writes a deterministic file so assertions can check delegation and file output.
        /// </summary>
        private sealed class FakeTransport : IHfHubHttpTransport
        {
            public HfHubHeadResponse HeadResponse { get; set; }
            public JObject JsonResponse { get; set; }
            public int GetJsonCallCount { get; private set; }
            public int DownloadCallCount { get; private set; }
            public string LastJsonUrl { get; private set; }

            public Task<HfHubHeadResponse> HeadAsync(string url, CancellationToken cancellationToken)
            {
                return Task.FromResult(HeadResponse);
            }

            public Task<JObject> GetJsonAsync(string url, CancellationToken cancellationToken)
            {
                GetJsonCallCount++;
                LastJsonUrl = url;
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
