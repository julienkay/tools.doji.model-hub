using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Doji.Downloads;
using Doji.Downloads.Editor;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Contract for resolving and downloading public Hub files.
    /// </summary>
    internal interface IHttpClient
    {
        /// <summary>
        /// Resolves a package-declared dependency to a concrete remote repository snapshot.
        /// </summary>
        Task<ResolvedSnapshotVersion> ResolveSnapshotVersionAsync(
            DiscoveredModelDependency dependency,
            CancellationToken cancellationToken);

        /// <summary>
        /// Downloads one resolved file into local cache, reporting byte progress while the transfer is active.
        /// </summary>
        Task DownloadFileAsync(
            ResolvedRepoFile resolvedFile,
            string targetPath,
            IProgress<DownloadProgress> progress,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Captures the minimum HEAD response data needed to resolve a Hub file version without keeping a request object alive.
    /// </summary>
    internal sealed class HeadResponse
    {
        public HeadResponse(string requestUrl, Func<string, string> getHeader)
        {
            RequestUrl = requestUrl;
            GetHeader = getHeader;
        }

        public string RequestUrl { get; }
        public Func<string, string> GetHeader { get; }
    }

    /// <summary>
    /// Transport abstraction that keeps raw UnityWebRequest usage out of cache and sync orchestration code.
    /// </summary>
    internal interface IHttpTransport
    {
        /// <summary>
        /// Performs lightweight metadata lookup for a Hub file.
        /// </summary>
        Task<HeadResponse> HeadAsync(string url, CancellationToken cancellationToken);

        /// <summary>
        /// Reads JSON API responses used when HEAD metadata is insufficient.
        /// </summary>
        Task<JObject> GetJsonAsync(string url, CancellationToken cancellationToken);

        /// <summary>
        /// Streams a file download to disk, optionally resuming from an existing partial payload.
        /// </summary>
        Task DownloadFileAsync(string url, string targetPath, long? expectedSizeInBytes, IProgress<DownloadProgress> progress, CancellationToken cancellationToken);
    }

    /// <summary>
    /// UnityWebRequest-backed transport used by editor sync.
    /// It handles resumable partial downloads by keeping a sibling `.downloading` file and issuing `Range` requests.
    /// </summary>
    internal sealed class UnityWebRequestTransport : IHttpTransport
    {
        private readonly UnityWebRequestResumableDownloader _downloader = new();

        /// <summary>
        /// Reads response headers for a Hub file without downloading the payload.
        /// </summary>
        public async Task<HeadResponse> HeadAsync(string url, CancellationToken cancellationToken)
        {
            using var request = UnityWebRequest.Head(url);
            request.SetRequestHeader("Accept-Encoding", "identity");
            await UnityWebRequestResumableDownloader.SendAsync(request, cancellationToken);

            var headers = request.GetResponseHeaders() ?? new Dictionary<string, string>();

            return new HeadResponse(
                request.url,
                headerName => headers.TryGetValue(headerName, out var value) ? value : null);
        }

        /// <summary>
        /// Fetches JSON API data used as fallback when HEAD metadata is incomplete.
        /// </summary>
        public async Task<JObject> GetJsonAsync(string url, CancellationToken cancellationToken)
        {
            using var request = UnityWebRequest.Get(url);
            await UnityWebRequestResumableDownloader.SendAsync(request, cancellationToken);
            return JObject.Parse(request.downloadHandler.text);
        }

        /// <summary>
        /// Streams a file to a temporary path, resuming from an existing partial file when possible.
        /// If the server ignores `Range`, the transfer restarts once from zero to avoid appending duplicate content.
        /// </summary>
        public async Task DownloadFileAsync(
            string url,
            string targetPath,
            long? expectedSizeInBytes,
            IProgress<DownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            await _downloader.DownloadFileAsync(url, targetPath, expectedSizeInBytes, progress, cancellationToken);
        }
    }

    /// <summary>
    /// Minimal editor-only HTTP client for public HuggingFace file resolution and download.
    /// </summary>
    internal sealed class HttpClient : IHttpClient
    {
        private const string HubEndpoint = "https://huggingface.co";
        private const string CommitHeaderName = "x-repo-commit";
        private const string ETagHeaderName = "etag";
        private const string LinkedETagHeaderName = "x-linked-etag";
        private const string ContentLengthHeaderName = "content-length";
        private const string LinkedSizeHeaderName = "x-linked-size";
        private readonly IHttpTransport _transport;

        public HttpClient(IHttpTransport transport = null)
        {
            _transport = transport ?? new UnityWebRequestTransport();
        }

        /// <summary>
        /// Resolves requested repository snapshot to concrete commit and file list metadata.
        /// </summary>
        public async Task<ResolvedSnapshotVersion> ResolveSnapshotVersionAsync(
            DiscoveredModelDependency dependency,
            CancellationToken cancellationToken)
        {
            var apiResponse = await GetModelRevisionApiResponseAsync(
                dependency.Repository,
                dependency.Revision,
                cancellationToken);
            var resolvedCommit = apiResponse.Value<string>("sha");
            if (string.IsNullOrWhiteSpace(resolvedCommit))
            {
                throw new InvalidOperationException($"Missing `sha` in model revision response for `{dependency.Repository}@{dependency.Revision}`.");
            }

            var siblings = apiResponse["siblings"] as JArray;
            if (siblings == null)
            {
                throw new InvalidOperationException($"Missing `siblings` in model revision response for `{dependency.Repository}@{dependency.Revision}`.");
            }

            var files = new List<ResolvedRepoFile>();
            foreach (var sibling in siblings.OfType<JObject>())
            {
                var repoPath = sibling.Value<string>("rfilename")?.Trim();
                if (string.IsNullOrWhiteSpace(repoPath))
                {
                    continue;
                }

                var fileUrl = BuildResolveUrl(dependency.Repository, dependency.Revision, repoPath);
                var response = await _transport.HeadAsync(fileUrl, cancellationToken);
                var entityTag = NormalizeEntityTag(
                    response.GetHeader(LinkedETagHeaderName) ??
                    response.GetHeader(ETagHeaderName));
                var contentLengthHeader = response.GetHeader(LinkedSizeHeaderName) ?? response.GetHeader(ContentLengthHeaderName);
                long? sizeInBytes = null;
                if (!string.IsNullOrWhiteSpace(contentLengthHeader) &&
                    long.TryParse(contentLengthHeader, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSize))
                {
                    sizeInBytes = parsedSize;
                }

                files.Add(new ResolvedRepoFile(repoPath, response.RequestUrl, entityTag, sizeInBytes));
            }

            return new ResolvedSnapshotVersion(
                dependency.Repository,
                dependency.Revision,
                resolvedCommit,
                files.OrderBy(file => file.RepoPath, StringComparer.Ordinal).ToArray());
        }

        /// <summary>
        /// Downloads resolved raw file to target path atomically through temporary file.
        /// Progress reporting is optional but intended to feed editor background task UI.
        /// </summary>
        public async Task DownloadFileAsync(
            ResolvedRepoFile resolvedFile,
            string targetPath,
            IProgress<DownloadProgress> progress,
            CancellationToken cancellationToken)
        {
            await _transport.DownloadFileAsync(resolvedFile.DownloadUrl, targetPath, resolvedFile.SizeInBytes, progress, cancellationToken);
        }

        /// <summary>
        /// Builds the public HuggingFace `resolve` URL used for both HEAD metadata lookup and file download.
        /// Path segments are escaped individually so nested repo paths remain stable.
        /// </summary>
        private static string BuildResolveUrl(string repository, string revision, string sourceFile)
        {
            var escapedRevision = UnityWebRequest.EscapeURL(revision);
            var escapedSourceFile = string.Join(
                "/",
                sourceFile.Split('/')
                    .Select(segment => UnityWebRequest.EscapeURL(segment).Replace("+", "%20", StringComparison.Ordinal)));
            return $"{HubEndpoint}/{repository}/resolve/{escapedRevision}/{escapedSourceFile}";
        }

        /// <summary>
        /// Falls back to the model revision API when HEAD metadata does not include a resolved commit hash.
        /// This keeps the cache keyed by concrete commit even when the CDN response is sparse.
        /// </summary>
        private async Task<JObject> GetModelRevisionApiResponseAsync(
            string repository,
            string revision,
            CancellationToken cancellationToken)
        {
            var escapedRevision = UnityWebRequest.EscapeURL(revision);
            var apiUrl = $"{HubEndpoint}/api/models/{repository}/revision/{escapedRevision}?expand[]=sha&expand[]=siblings";
            return await _transport.GetJsonAsync(apiUrl, cancellationToken);
        }

        /// <summary>
        /// Removes weak formatting noise from ETag values so cache metadata stays stable across response variants.
        /// </summary>
        private static string NormalizeEntityTag(string entityTag)
        {
            if (string.IsNullOrWhiteSpace(entityTag))
            {
                return null;
            }

            return entityTag.Trim().Trim('"');
        }
    }
}
