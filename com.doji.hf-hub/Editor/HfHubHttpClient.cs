using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Contract for resolving and downloading public Hub files.
    /// </summary>
    internal interface IHfHubHttpClient
    {
        Task<HfHubResolvedFileVersion> ResolveFileVersionAsync(
            HfHubDiscoveredModelDependency dependency,
            CancellationToken cancellationToken);

        Task DownloadFileAsync(
            HfHubResolvedFileVersion resolvedFile,
            string targetPath,
            CancellationToken cancellationToken);
    }

    internal sealed class HfHubHeadResponse
    {
        public HfHubHeadResponse(string requestUrl, Func<string, string> getHeader)
        {
            RequestUrl = requestUrl;
            GetHeader = getHeader;
        }

        public string RequestUrl { get; }
        public Func<string, string> GetHeader { get; }
    }

    internal interface IHfHubHttpTransport
    {
        Task<HfHubHeadResponse> HeadAsync(string url, CancellationToken cancellationToken);
        Task<JObject> GetJsonAsync(string url, CancellationToken cancellationToken);
        Task DownloadFileAsync(string url, string targetPath, CancellationToken cancellationToken);
    }

    internal sealed class UnityWebRequestHfHubTransport : IHfHubHttpTransport
    {
        public async Task<HfHubHeadResponse> HeadAsync(string url, CancellationToken cancellationToken)
        {
            using var request = UnityWebRequest.Head(url);
            request.SetRequestHeader("Accept-Encoding", "identity");
            await HfHubHttpClient.SendAsync(request, cancellationToken);

            var headers = request.GetResponseHeaders() ?? new Dictionary<string, string>();

            return new HfHubHeadResponse(
                request.url,
                headerName => headers.TryGetValue(headerName, out var value) ? value : null);
        }

        public async Task<JObject> GetJsonAsync(string url, CancellationToken cancellationToken)
        {
            using var request = UnityWebRequest.Get(url);
            await HfHubHttpClient.SendAsync(request, cancellationToken);
            return JObject.Parse(request.downloadHandler.text);
        }

        public async Task DownloadFileAsync(string url, string targetPath, CancellationToken cancellationToken)
        {
            var targetDirectory = System.IO.Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetDirectory))
            {
                System.IO.Directory.CreateDirectory(targetDirectory);
            }

            var temporaryPath = targetPath + ".downloading";
            if (System.IO.File.Exists(temporaryPath))
            {
                System.IO.File.Delete(temporaryPath);
            }

            using var request = UnityWebRequest.Get(url);
            request.downloadHandler = new DownloadHandlerFile(temporaryPath)
            {
                removeFileOnAbort = true
            };

            await HfHubHttpClient.SendAsync(request, cancellationToken);

            if (System.IO.File.Exists(targetPath))
            {
                System.IO.File.Delete(targetPath);
            }

            System.IO.File.Move(temporaryPath, targetPath);
        }
    }

    /// <summary>
    /// Minimal editor-only HTTP client for public HuggingFace file resolution and download.
    /// </summary>
    internal sealed class HfHubHttpClient : IHfHubHttpClient
    {
        private const string HubEndpoint = "https://huggingface.co";
        private const string CommitHeaderName = "x-repo-commit";
        private const string ETagHeaderName = "etag";
        private const string LinkedETagHeaderName = "x-linked-etag";
        private const string ContentLengthHeaderName = "content-length";
        private const string LinkedSizeHeaderName = "x-linked-size";
        private readonly IHfHubHttpTransport _transport;

        public HfHubHttpClient(IHfHubHttpTransport transport = null)
        {
            _transport = transport ?? new UnityWebRequestHfHubTransport();
        }

        /// <summary>
        /// Resolves requested file version to concrete commit and download metadata.
        /// </summary>
        public async Task<HfHubResolvedFileVersion> ResolveFileVersionAsync(
            HfHubDiscoveredModelDependency dependency,
            CancellationToken cancellationToken)
        {
            var fileUrl = BuildResolveUrl(dependency.Repository, dependency.Revision, dependency.SourceFile);
            var response = await _transport.HeadAsync(fileUrl, cancellationToken);

            var resolvedCommit = response.GetHeader(CommitHeaderName);
            if (string.IsNullOrWhiteSpace(resolvedCommit))
            {
                resolvedCommit = await ResolveCommitFromModelRevisionApiAsync(
                    dependency.Repository,
                    dependency.Revision,
                    cancellationToken);
            }

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

            return new HfHubResolvedFileVersion(
                dependency.Repository,
                dependency.Revision,
                resolvedCommit,
                dependency.SourceFile,
                response.RequestUrl,
                entityTag,
                sizeInBytes);
        }

        /// <summary>
        /// Downloads resolved raw file to target path atomically through temporary file.
        /// </summary>
        public async Task DownloadFileAsync(
            HfHubResolvedFileVersion resolvedFile,
            string targetPath,
            CancellationToken cancellationToken)
        {
            await _transport.DownloadFileAsync(resolvedFile.DownloadUrl, targetPath, cancellationToken);
        }

        private static string BuildResolveUrl(string repository, string revision, string sourceFile)
        {
            var escapedRevision = UnityWebRequest.EscapeURL(revision);
            var escapedSourceFile = string.Join(
                "/",
                sourceFile.Split('/')
                    .Select(segment => UnityWebRequest.EscapeURL(segment).Replace("+", "%20", StringComparison.Ordinal)));
            return $"{HubEndpoint}/{repository}/resolve/{escapedRevision}/{escapedSourceFile}";
        }

        private async Task<string> ResolveCommitFromModelRevisionApiAsync(
            string repository,
            string revision,
            CancellationToken cancellationToken)
        {
            var escapedRevision = UnityWebRequest.EscapeURL(revision);
            var apiUrl = $"{HubEndpoint}/api/models/{repository}/revision/{escapedRevision}?expand=sha";
            var response = await _transport.GetJsonAsync(apiUrl, cancellationToken);
            var sha = response.Value<string>("sha");
            if (string.IsNullOrWhiteSpace(sha))
            {
                throw new InvalidOperationException($"Missing `sha` in model revision response for `{repository}@{revision}`.");
            }

            return sha;
        }

        internal static async Task SendAsync(UnityWebRequest request, CancellationToken cancellationToken)
        {
            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (request.result is UnityWebRequest.Result.ConnectionError or UnityWebRequest.Result.ProtocolError or UnityWebRequest.Result.DataProcessingError)
            {
                throw new InvalidOperationException($"HuggingFace request failed: {request.responseCode} {request.error}");
            }
        }

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
