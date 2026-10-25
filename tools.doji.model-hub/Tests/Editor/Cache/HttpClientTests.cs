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
    /// Covers HTTP client repo inventory and selected-file resolution using a fully mocked transport.
    /// </summary>
    internal sealed class HttpClientTests
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
        /// Verifies snapshot resolution lists the resolved commit, falls back to HEAD only for files the listing gives no etag, and downloads from the resolve URL.
        /// </summary>
        [Test]
        public async Task ResolveSnapshotVersionAsync_ResolvesInventoryAndSelectedFiles()
        {
            var transport = new FakeTransport();
            transport.JsonResponses["https://huggingface.co/api/models/doji/test-model/revision/main?expand[]=sha&expand[]=cardData"] =
                CreateJsonResponse(
                    "https://huggingface.co/api/models/doji/test-model/revision/main?expand[]=sha&expand[]=cardData",
                    new JObject
                    {
                        ["sha"] = "abc123",
                        ["cardData"] = new JObject { ["license"] = "apache-2.0", ["base_model"] = "doji/base" }
                    });
            transport.JsonResponses["https://huggingface.co/api/models/doji/test-model/tree/abc123?recursive=true"] =
                CreateJsonResponse(
                    "https://huggingface.co/api/models/doji/test-model/tree/abc123?recursive=true",
                    new JArray
                    {
                        new JObject
                        {
                            ["type"] = "file",
                            ["path"] = "config.json",
                            ["size"] = 10
                        },
                        new JObject
                        {
                            ["type"] = "file",
                            ["path"] = "onnx/model.onnx",
                            ["size"] = 20
                        }
                    });
            transport.HeadResponses["https://huggingface.co/doji/test-model/resolve/abc123/onnx/model.onnx"] =
                CreateHeadResponse("https://cdn.example/model.onnx", new Dictionary<string, string>
                {
                    ["x-linked-etag"] = "\"etag-model\"",
                    ["x-linked-size"] = "20"
                });

            var client = new HttpClient(transport);
            var resolved = await client.ResolveSnapshotVersionAsync(CreateDependency(new[] { "onnx/model.onnx" }), CancellationToken.None);

            Assert.That(resolved.ResolvedCommitHash, Is.EqualTo("abc123"));
            Assert.That(resolved.ModelCard?.License, Is.EqualTo("apache-2.0"), "The license comes with the revision lookup, without another request.");
            Assert.That(resolved.RepositoryFiles.Select(file => file.RepoPath), Is.EqualTo(new[] { "config.json", "onnx/model.onnx" }));
            Assert.That(resolved.Files.Select(file => file.RepoPath), Is.EqualTo(new[] { "onnx/model.onnx" }));
            Assert.That(resolved.Files[0].DownloadUrl, Is.EqualTo("https://huggingface.co/doji/test-model/resolve/abc123/onnx/model.onnx"));
            Assert.That(transport.GetJsonCallCount, Is.EqualTo(2));
            Assert.That(transport.HeadCallCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies the etag comes from the tree listing, like huggingface_hub's X-Linked-Etag: the LFS sha256 for
        /// large files and the git blob id otherwise, without a HEAD request.
        /// </summary>
        [Test]
        public async Task ResolveSnapshotVersionAsync_UsesTreeListingEtags()
        {
            var transport = new FakeTransport();
            transport.JsonResponses["https://huggingface.co/api/models/doji/test-model/revision/main?expand[]=sha&expand[]=cardData"] =
                CreateJsonResponse(
                    "https://huggingface.co/api/models/doji/test-model/revision/main?expand[]=sha&expand[]=cardData",
                    new JObject { ["sha"] = "abc123" });
            transport.JsonResponses["https://huggingface.co/api/models/doji/test-model/tree/abc123?recursive=true"] =
                CreateJsonResponse(
                    "https://huggingface.co/api/models/doji/test-model/tree/abc123?recursive=true",
                    new JArray
                    {
                        new JObject { ["type"] = "file", ["path"] = "config.json", ["size"] = 10, ["oid"] = "git-sha1" },
                        new JObject
                        {
                            ["type"] = "file",
                            ["path"] = "onnx/model.onnx",
                            ["size"] = 20,
                            ["oid"] = "pointer-sha1",
                            ["lfs"] = new JObject { ["oid"] = "lfs-sha256", ["size"] = 20 },
                            ["xetHash"] = "xet-hash"
                        }
                    });
            foreach (var path in new[] { "config.json", "onnx/model.onnx" })
            {
                transport.HeadResponses[$"https://huggingface.co/doji/test-model/resolve/abc123/{path}"] =
                    CreateHeadResponse("https://cdn.example/" + path, new Dictionary<string, string> { ["etag"] = "\"xet-hash\"" });
            }

            var client = new HttpClient(transport);
            var resolved = await client.ResolveSnapshotVersionAsync(CreateDependency(new[] { "config.json", "onnx/model.onnx" }), CancellationToken.None);

            Assert.That(resolved.Files.Single(file => file.RepoPath == "onnx/model.onnx").EntityTag, Is.EqualTo("lfs-sha256"));
            Assert.That(resolved.Files.Single(file => file.RepoPath == "config.json").EntityTag, Is.EqualTo("git-sha1"));
            Assert.That(transport.HeadCallCount, Is.Zero);
        }

        /// <summary>
        /// Verifies the Hub's spelling of the id names the download, as in huggingface_hub, and that an id the Hub
        /// answers with a different name is not adopted.
        /// </summary>
        [TestCase("doji/Test-Model", "doji/Test-Model")]
        [TestCase("other/renamed-model", "doji/test-model")]
        [TestCase(null, "doji/test-model")]
        public async Task ResolveSnapshotVersionAsync_UsesHubSpellingOfTheId(string hubId, string expected)
        {
            var transport = new FakeTransport();
            transport.JsonResponses["https://huggingface.co/api/models/doji/test-model/revision/main?expand[]=sha&expand[]=cardData"] =
                CreateJsonResponse(
                    "https://huggingface.co/api/models/doji/test-model/revision/main?expand[]=sha&expand[]=cardData",
                    new JObject { ["sha"] = "abc123", ["id"] = hubId });
            transport.JsonResponses["https://huggingface.co/api/models/doji/test-model/tree/abc123?recursive=true"] =
                CreateJsonResponse(
                    "https://huggingface.co/api/models/doji/test-model/tree/abc123?recursive=true",
                    new JArray
                    {
                        new JObject { ["type"] = "file", ["path"] = "onnx/model.onnx", ["size"] = 20, ["oid"] = "git-sha1" }
                    });

            var client = new HttpClient(transport);
            var resolved = await client.ResolveSnapshotVersionAsync(CreateDependency(new[] { "onnx/model.onnx" }), CancellationToken.None);

            Assert.That(resolved.Repository, Is.EqualTo(expected));
        }

        /// <summary>
        /// Verifies repository inventory follows paged tree responses by reading the Link header.
        /// </summary>
        [Test]
        public async Task GetRepositoryInventoryAsync_FollowsPagedTreeResponses()
        {
            var transport = new FakeTransport();
            transport.JsonResponses["https://huggingface.co/api/models/doji/test-model/revision/main?expand[]=sha&expand[]=cardData"] =
                CreateJsonResponse(
                    "https://huggingface.co/api/models/doji/test-model/revision/main?expand[]=sha&expand[]=cardData",
                    new JObject
                    {
                        ["sha"] = "abc123"
                    });
            transport.JsonResponses["https://huggingface.co/api/models/doji/test-model/tree/abc123?recursive=true"] =
                CreateJsonResponse(
                    "https://huggingface.co/api/models/doji/test-model/tree/abc123?recursive=true",
                    new JArray
                    {
                        new JObject
                        {
                            ["type"] = "file",
                            ["path"] = "config.json",
                            ["size"] = 10
                        }
                    },
                    new Dictionary<string, string>
                    {
                        ["link"] = "<https://huggingface.co/api/models/doji/test-model/tree/abc123?recursive=true&cursor=page-2>; rel=\"next\""
                    });
            transport.JsonResponses["https://huggingface.co/api/models/doji/test-model/tree/abc123?recursive=true&cursor=page-2"] =
                CreateJsonResponse(
                    "https://huggingface.co/api/models/doji/test-model/tree/abc123?recursive=true&cursor=page-2",
                    new JArray
                    {
                        new JObject
                        {
                            ["type"] = "file",
                            ["path"] = "onnx/model.onnx",
                            ["size"] = 20
                        }
                    });

            var client = new HttpClient(transport);
            var inventory = await client.GetRepositoryInventoryAsync("doji/test-model", "main", CancellationToken.None);

            Assert.That(inventory.ResolvedCommitHash, Is.EqualTo("abc123"));
            Assert.That(inventory.Files.Select(file => file.RepoPath), Is.EqualTo(new[] { "config.json", "onnx/model.onnx" }));
            Assert.That(transport.GetJsonCallCount, Is.EqualTo(3));
        }

        /// <summary>
        /// Verifies file download still delegates to transport for individual snapshot files.
        /// </summary>
        [Test]
        public async Task DownloadFileAsync_DelegatesToTransport()
        {
            var transport = new FakeTransport();
            var client = new HttpClient(transport);
            var targetPath = Path.Combine(_rootDirectory, "model.onnx");
            var progressRecorder = new ProgressRecorder();

            await client.DownloadFileAsync(
                new ResolvedRepoFile(
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

        private static DiscoveredModelDependency CreateDependency(string[] files = null)
        {
            return new DiscoveredModelDependency(
                "tools.doji.test",
                "Test Package",
                "1.0.0",
                "/tmp/tools.doji.test",
                "/tmp/tools.doji.test/model-hub-manifest.json",
                "manifesthash",
                new ModelDependencyDefinition(
                    "doji/test-model",
                    "main",
                    ToRequiredFileEntries(files)));
        }

        private static ModelDependencyFileDefinition[] ToRequiredFileEntries(string[] files)
        {
            return (files ?? Array.Empty<string>())
                .Select(path => new ModelDependencyFileDefinition(path))
                .ToArray();
        }

        private static HeadResponse CreateHeadResponse(string requestUrl, IReadOnlyDictionary<string, string> headers)
        {
            return new HeadResponse(
                requestUrl,
                headerName => headers.TryGetValue(headerName, out var value) ? value : null);
        }

        private static JsonResponse CreateJsonResponse(
            string requestUrl,
            JToken json,
            IReadOnlyDictionary<string, string> headers = null)
        {
            var effectiveHeaders = headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return new JsonResponse(
                requestUrl,
                json,
                headerName => effectiveHeaders.TryGetValue(headerName, out var value) ? value : null);
        }

        private sealed class FakeTransport : IHttpTransport
        {
            public Dictionary<string, HeadResponse> HeadResponses { get; } = new(StringComparer.Ordinal);
            public Dictionary<string, JsonResponse> JsonResponses { get; } = new(StringComparer.Ordinal);
            public int GetJsonCallCount { get; private set; }
            public int HeadCallCount { get; private set; }
            public int DownloadCallCount { get; private set; }

            public Task<HeadResponse> HeadAsync(string url, CancellationToken cancellationToken)
            {
                HeadCallCount++;
                return Task.FromResult(HeadResponses[url]);
            }

            public Task<JsonResponse> GetJsonAsync(string url, CancellationToken cancellationToken)
            {
                GetJsonCallCount++;
                return Task.FromResult(JsonResponses[url]);
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
