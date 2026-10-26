using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Doji.Downloads;
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
        /// Resolves one repository revision to a concrete commit and full file inventory.
        /// </summary>
        Task<ResolvedRepositoryInventory> GetRepositoryInventoryAsync(
            string repository,
            string revision,
            CancellationToken cancellationToken);

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
    /// Captures response payload plus headers without keeping a live request object around.
    /// </summary>
    internal class HttpResponseInfo
    {
        public HttpResponseInfo(string requestUrl, Func<string, string> getHeader)
        {
            RequestUrl = requestUrl;
            GetHeader = getHeader;
        }

        public string RequestUrl { get; }
        public Func<string, string> GetHeader { get; }
    }

    /// <summary>
    /// Captures one JSON response body plus headers for paged Hub API calls.
    /// </summary>
    internal sealed class JsonResponse : HttpResponseInfo
    {
        public JsonResponse(string requestUrl, JToken json, Func<string, string> getHeader)
            : base(requestUrl, getHeader)
        {
            Json = json;
        }

        public JToken Json { get; }
    }

    /// <summary>
    /// Captures the minimum HEAD response data needed to resolve a Hub file version without keeping a request object alive.
    /// </summary>
    internal sealed class HeadResponse : HttpResponseInfo
    {
        public HeadResponse(string requestUrl, Func<string, string> getHeader)
            : base(requestUrl, getHeader)
        {
        }
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
        /// Reads JSON API responses together with response headers used for pagination.
        /// </summary>
        Task<JsonResponse> GetJsonAsync(string url, CancellationToken cancellationToken);

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
        private readonly ResumableDownloader _downloader = new();

        /// <summary>
        /// Reads response headers for a Hub file without downloading the payload.
        /// </summary>
        public async Task<HeadResponse> HeadAsync(string url, CancellationToken cancellationToken)
        {
            using var request = UnityWebRequest.Head(url);
            request.SetRequestHeader("Accept-Encoding", "identity");
            await request.SendAsync(cancellationToken);

            var headers = request.GetResponseHeaders() ?? new Dictionary<string, string>();

            return new HeadResponse(
                request.url,
                headerName => headers.TryGetValue(headerName, out var value) ? value : null);
        }

        /// <summary>
        /// Fetches JSON API data used by revision lookup and paged repository inventory scans.
        /// </summary>
        public async Task<JsonResponse> GetJsonAsync(string url, CancellationToken cancellationToken)
        {
            using var request = UnityWebRequest.Get(url);
            await request.SendAsync(cancellationToken);
            var headers = request.GetResponseHeaders() ?? new Dictionary<string, string>();
            return new JsonResponse(
                request.url,
                JToken.Parse(request.downloadHandler.text),
                headerName => headers.TryGetValue(headerName, out var value) ? value : null);
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
        private const string ETagHeaderName = "etag";
        private const string LinkedETagHeaderName = "x-linked-etag";
        private const string ContentLengthHeaderName = "content-length";
        private const string LinkedSizeHeaderName = "x-linked-size";
        private const string LinkHeaderName = "link";
        private readonly IHttpTransport _transport;

        public HttpClient(IHttpTransport transport = null)
        {
            _transport = transport ?? new UnityWebRequestTransport();
        }

        public async Task<ResolvedRepositoryInventory> GetRepositoryInventoryAsync(
            string repository,
            string revision,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(repository))
            {
                throw new ArgumentException("Repository is required.", nameof(repository));
            }

            if (string.IsNullOrWhiteSpace(revision))
            {
                throw new ArgumentException("Revision is required.", nameof(revision));
            }

            var apiResponse = await GetModelRevisionApiResponseAsync(repository, revision, cancellationToken);
            var resolvedCommit = apiResponse.Value<string>("sha");
            if (string.IsNullOrWhiteSpace(resolvedCommit))
            {
                throw new InvalidOperationException($"Missing `sha` in model revision response for `{repository}@{revision}`.");
            }

            // List the resolved commit, not the branch, so file etags always belong to that commit.
            var repositoryFiles = await ListRepositoryFilesAsync(repository, resolvedCommit, cancellationToken);

            // The Hub matches ids without letter case and returns its own spelling, which names the cache folder,
            // as in huggingface_hub. "julienkay/sentis-midas" is stored as "models--julienkay--sentis-MiDaS".
            var canonicalRepository = apiResponse.Value<string>("id");
            return new ResolvedRepositoryInventory(
                string.Equals(canonicalRepository, repository, StringComparison.OrdinalIgnoreCase) ? canonicalRepository : repository,
                revision,
                resolvedCommit,
                repositoryFiles,
                ModelCardMetadata.FromCardData(apiResponse["cardData"]));
        }

        /// <summary>
        /// Resolves requested repository snapshot to concrete commit and file list metadata.
        /// </summary>
        public async Task<ResolvedSnapshotVersion> ResolveSnapshotVersionAsync(
            DiscoveredModelDependency dependency,
            CancellationToken cancellationToken)
        {
            var repositoryInventory = await GetRepositoryInventoryAsync(
                dependency.Repository,
                dependency.Revision,
                cancellationToken);
            var requestedRepoPaths = dependency.GetSelectedRepoPaths(
                repositoryInventory.Files.Select(file => file.RepoPath));
            var inventoryByRepoPath = repositoryInventory.Files
                .ToDictionary(file => file.RepoPath, StringComparer.Ordinal);
            var missingSelectedFiles = requestedRepoPaths
                .Where(repoPath => !inventoryByRepoPath.ContainsKey(repoPath))
                .OrderBy(repoPath => repoPath, StringComparer.Ordinal)
                .ToArray();
            if (missingSelectedFiles.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Model `{dependency.Repository}@{dependency.Revision}` does not contain selected file(s): {string.Join(", ", missingSelectedFiles)}.");
            }

            var files = new List<ResolvedRepoFile>();
            foreach (var repoPath in requestedRepoPaths)
            {
                // Downloads use the resolve URL at the commit, which never expires; the downloader follows the
                // redirect to the CDN on every attempt, so an interrupted download resumes with a fresh signed URL.
                var fileUrl = BuildResolveUrl(dependency.Repository, repositoryInventory.ResolvedCommitHash, repoPath);
                inventoryByRepoPath.TryGetValue(repoPath, out var listedFile);
                var entityTag = NormalizeEntityTag(listedFile?.EntityTag);
                var sizeInBytes = listedFile?.SizeInBytes;
                if (string.IsNullOrWhiteSpace(entityTag) || !sizeInBytes.HasValue)
                {
                    // The tree listing gives the same etag and size huggingface_hub reads from X-Linked-Etag and
                    // X-Linked-Size, so HEAD is only a fallback. HEAD follows the redirect to the CDN, whose ETag
                    // (a xet hash for large files) is not the blob name.
                    var response = await _transport.HeadAsync(fileUrl, cancellationToken);
                    entityTag ??= NormalizeEntityTag(response.GetHeader(LinkedETagHeaderName) ?? response.GetHeader(ETagHeaderName));
                    var contentLengthHeader = response.GetHeader(LinkedSizeHeaderName) ?? response.GetHeader(ContentLengthHeaderName);
                    if (!sizeInBytes.HasValue &&
                        long.TryParse(contentLengthHeader, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSize))
                    {
                        sizeInBytes = parsedSize;
                    }
                }

                files.Add(new ResolvedRepoFile(repoPath, fileUrl, entityTag, sizeInBytes));
            }

            return new ResolvedSnapshotVersion(
                repositoryInventory.Repository,
                dependency.Revision,
                repositoryInventory.ResolvedCommitHash,
                repositoryInventory.Files,
                files.OrderBy(file => file.RepoPath, StringComparer.Ordinal).ToArray(),
                repositoryInventory.ModelCard);
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
            var escapedRevision = EscapePathSegment(revision);
            var escapedSourceFile = string.Join(
                "/",
                sourceFile.Split('/')
                    .Select(EscapePathSegment));
            return $"{HubEndpoint.Url}/{repository}/resolve/{escapedRevision}/{escapedSourceFile}";
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
            var escapedRevision = EscapePathSegment(revision);
            var apiUrl = $"{HubEndpoint.Url}/api/models/{repository}/revision/{escapedRevision}?expand[]=sha&expand[]=cardData";
            JsonResponse response;
            try
            {
                response = await _transport.GetJsonAsync(apiUrl, cancellationToken);
            }
            catch (WebRequestException exception) when (HubAccess.IsDenied(exception.ResponseCode) || exception.ResponseCode == 404)
            {
                throw HubAccess.RepositoryDenied(repository, revision, exception);
            }
            if (response.Json is JObject jsonObject)
            {
                return jsonObject;
            }

            throw new InvalidOperationException(
                $"Expected JSON object from model revision response for `{repository}@{revision}`.");
        }

        private async Task<IReadOnlyList<RepositoryFileMetadata>> ListRepositoryFilesAsync(
            string repository,
            string revision,
            CancellationToken cancellationToken)
        {
            var escapedRevision = EscapePathSegment(revision);
            var nextPageUrl = $"{HubEndpoint.Url}/api/models/{repository}/tree/{escapedRevision}?recursive=true";
            var repositoryFiles = new List<RepositoryFileMetadata>();

            while (!string.IsNullOrWhiteSpace(nextPageUrl))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var response = await _transport.GetJsonAsync(nextPageUrl, cancellationToken);
                if (response.Json is not JArray page)
                {
                    throw new InvalidOperationException(
                        $"Expected JSON array from repository tree response for `{repository}@{revision}`.");
                }

                foreach (var entry in page.OfType<JObject>())
                {
                    if (!string.Equals(entry.Value<string>("type"), "file", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var repoPath = ModelDependencyDefinition.NormalizeRepoPath(
                        entry.Value<string>("path") ??
                        entry.Value<string>("rfilename"));
                    if (string.IsNullOrWhiteSpace(repoPath))
                    {
                        continue;
                    }

                    repositoryFiles.Add(new RepositoryFileMetadata
                    {
                        RepoPath = repoPath,
                        SizeInBytes = entry.Value<long?>("size"),
                        EntityTag = (entry["lfs"] as JObject)?.Value<string>("oid") ?? entry.Value<string>("oid")
                    });
                }

                nextPageUrl = TryGetNextPageUrl(response.GetHeader(LinkHeaderName));
            }

            return repositoryFiles
                .GroupBy(file => file.RepoPath, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(file => file.RepoPath, StringComparer.Ordinal)
                .ToArray();
        }

        private static string TryGetNextPageUrl(string linkHeader)
        {
            if (string.IsNullOrWhiteSpace(linkHeader))
            {
                return null;
            }

            foreach (var segment in linkHeader.Split(','))
            {
                var trimmedSegment = segment?.Trim();
                if (string.IsNullOrWhiteSpace(trimmedSegment) ||
                    trimmedSegment.IndexOf("rel=\"next\"", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var startIndex = trimmedSegment.IndexOf('<');
                var endIndex = trimmedSegment.IndexOf('>', startIndex + 1);
                if (startIndex < 0 || endIndex <= startIndex)
                {
                    continue;
                }

                return trimmedSegment.Substring(startIndex + 1, endIndex - startIndex - 1);
            }

            return null;
        }

        private static string EscapePathSegment(string value)
        {
            return UnityWebRequest.EscapeURL(value ?? string.Empty)
                .Replace("+", "%20", StringComparison.Ordinal);
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
