using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// A local stand-in for the Hugging Face Hub: the revision and tree APIs and <c>/resolve</c> downloads with
    /// <c>Range</c> and <c>If-Range</c>. File content is generated from the byte offset, so files of any size cost no
    /// memory, and <see cref="ByteAt"/> tells a test which byte belongs at an offset. Point HF Hub at it with
    /// <see cref="HubEndpoint.Override"/>.
    /// </summary>
    internal sealed class FakeHubServer : IDisposable
    {
        /// <summary>
        /// Length of the repeating content pattern. A prime, so offsets 2 GiB and 4 GiB apart get different bytes.
        /// </summary>
        private const int PatternLength = 65521;
        private const int ChunkSize = 1024 * 1024;
        private static readonly byte[] Pattern = CreatePattern();

        private readonly HttpListener _listener;
        private readonly Task _serverTask;
        private readonly List<FakeHubFile> _files = new();
        private readonly List<FakeHubRequest> _requests = new();

        private FakeHubServer(HttpListener listener, string url)
        {
            _listener = listener;
            Url = url;
            _serverTask = Task.Run(RunAsync);
        }

        /// <summary>
        /// Base URL without a trailing slash, for <see cref="HubEndpoint.Override"/>.
        /// </summary>
        public string Url { get; }

        public IReadOnlyList<FakeHubRequest> Requests
        {
            get
            {
                lock (_requests)
                {
                    return _requests.ToArray();
                }
            }
        }

        public static FakeHubServer Start()
        {
            var url = $"http://127.0.0.1:{GetFreePort()}";
            var listener = new HttpListener();
            listener.Prefixes.Add(url + "/");
            listener.Start();
            return new FakeHubServer(listener, url);
        }

        /// <summary>
        /// The byte the server sends at <paramref name="offset"/> of every file.
        /// </summary>
        public static byte ByteAt(long offset)
        {
            return Pattern[offset % PatternLength];
        }

        /// <summary>
        /// Adds one file to a repository at a commit. An LFS file is listed with its sha256-style etag in <c>lfs.oid</c>,
        /// like large files on the Hub.
        /// </summary>
        public FakeHubFile AddFile(string repository, string commit, string repoPath, long size, string etag, bool lfs)
        {
            var file = new FakeHubFile(repository, commit, repoPath, size, etag, lfs);
            lock (_files)
            {
                _files.Add(file);
            }

            return file;
        }

        /// <summary>
        /// Requests for one file's content, in order.
        /// </summary>
        public IReadOnlyList<FakeHubRequest> DownloadsOf(FakeHubFile file)
        {
            return Requests.Where(request => request.Method == "GET" && request.Path == file.ResolvePath).ToArray();
        }

        public void Dispose()
        {
            _listener.Close();
            try
            {
                _serverTask.GetAwaiter().GetResult();
            }
            catch
            {
                // Closing the listener interrupts the pending accept.
            }
        }

        private async Task RunAsync()
        {
            while (true)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch
                {
                    return;
                }

                // Serve requests in parallel, like the Hub, so a client that opens a second connection is not blocked.
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await RespondAsync(context);
                    }
                    catch
                    {
                        // The client may abort a transfer; the server keeps running.
                    }
                });
            }
        }

        private async Task RespondAsync(HttpListenerContext context)
        {
            var request = context.Request;
            var path = Uri.UnescapeDataString(request.Url.AbsolutePath);
            lock (_requests)
            {
                _requests.Add(new FakeHubRequest(request.HttpMethod, path, request.Headers["Range"], request.Headers["If-Range"]));
            }

            var response = context.Response;
            FakeHubFile[] files;
            lock (_files)
            {
                files = _files.ToArray();
            }

            var revision = files.FirstOrDefault(file => path.StartsWith($"/api/models/{file.Repository}/revision/", StringComparison.OrdinalIgnoreCase));
            if (revision != null)
            {
                await WriteJsonAsync(response, new JObject { ["id"] = revision.Repository, ["sha"] = revision.Commit });
                return;
            }

            var tree = files.Where(file => path.Equals($"/api/models/{file.Repository}/tree/{file.Commit}", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (tree.Length > 0)
            {
                await WriteJsonAsync(response, new JArray(tree.Select(file => file.ToTreeEntry())));
                return;
            }

            var target = files.FirstOrDefault(file => string.Equals(path, file.ResolvePath, StringComparison.OrdinalIgnoreCase));
            if (target == null)
            {
                response.StatusCode = 404;
                response.Close();
                return;
            }

            if (target.Gated)
            {
                // The Hub answers a request without an access token like this for gated and private files.
                response.StatusCode = 401;
                response.Close();
                return;
            }

            await WriteFileAsync(request, response, target);
        }

        private static async Task WriteFileAsync(HttpListenerRequest request, HttpListenerResponse response, FakeHubFile file)
        {
            var quotedEtag = $"\"{file.ETag}\"";
            response.AddHeader("ETag", quotedEtag);
            response.AddHeader("X-Repo-Commit", file.Commit);
            response.AddHeader("Accept-Ranges", "bytes");
            if (file.Lfs)
            {
                response.AddHeader("X-Linked-Etag", quotedEtag);
                response.AddHeader("X-Linked-Size", file.Size.ToString(CultureInfo.InvariantCulture));
            }

            var start = 0L;
            var range = request.Headers["Range"];
            var ifRange = request.Headers["If-Range"];
            var usesRange = range != null &&
                            range.StartsWith("bytes=", StringComparison.Ordinal) &&
                            range.EndsWith("-", StringComparison.Ordinal) &&
                            long.TryParse(range.Substring(6, range.Length - 7), NumberStyles.Integer, CultureInfo.InvariantCulture, out start) &&
                            (ifRange == null || ifRange == quotedEtag);
            if (!usesRange)
            {
                start = 0;
            }
            else if (start >= file.Size)
            {
                response.StatusCode = 416;
                response.AddHeader("Content-Range", $"bytes */{file.Size}");
                response.Close();
                return;
            }

            response.StatusCode = usesRange ? 206 : 200;
            response.ContentLength64 = file.Size - start;
            if (usesRange)
            {
                response.AddHeader("Content-Range", $"bytes {start}-{file.Size - 1}/{file.Size}");
            }

            if (request.HttpMethod == "HEAD")
            {
                response.Close();
                return;
            }

            var cut = file.TakeCut();
            var end = cut.HasValue ? Math.Min(file.Size, start + cut.Value) : file.Size;
            var output = response.OutputStream;
            for (var offset = start; offset < end;)
            {
                var count = (int)Math.Min(ChunkSize, end - offset);
                await output.WriteAsync(Pattern, (int)(offset % PatternLength), count);
                offset += count;
            }

            if (cut.HasValue)
            {
                // A dropped connection: the client has the bytes so far but not the announced length.
                await output.FlushAsync();
                response.Abort();
                return;
            }

            response.Close();
        }

        private static async Task WriteJsonAsync(HttpListenerResponse response, JToken json)
        {
            var body = Encoding.UTF8.GetBytes(json.ToString());
            response.ContentType = "application/json";
            response.ContentLength64 = body.Length;
            await response.OutputStream.WriteAsync(body, 0, body.Length);
            response.Close();
        }

        /// <summary>
        /// The pattern repeated so that any chunk, starting anywhere in the pattern, is one contiguous slice.
        /// </summary>
        private static byte[] CreatePattern()
        {
            var unique = new byte[PatternLength];
            new Random(7).NextBytes(unique);
            var repeated = new byte[PatternLength + ChunkSize];
            for (var index = 0; index < repeated.Length; index += PatternLength)
            {
                Buffer.BlockCopy(unique, 0, repeated, index, Math.Min(PatternLength, repeated.Length - index));
            }

            return repeated;
        }

        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            try
            {
                listener.Start();
                return ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            finally
            {
                listener.Stop();
            }
        }
    }

    internal sealed class FakeHubFile
    {
        private long? _cutFirstResponseAfterBytes;

        public FakeHubFile(string repository, string commit, string repoPath, long size, string etag, bool lfs)
        {
            Repository = repository;
            Commit = commit;
            RepoPath = repoPath;
            Size = size;
            ETag = etag;
            Lfs = lfs;
        }

        public string Repository { get; }
        public string Commit { get; }
        public string RepoPath { get; }
        public long Size { get; }
        public string ETag { get; }
        public bool Lfs { get; }

        /// <summary>
        /// Answers downloads with 401, like a gated or private file requested without an access token.
        /// </summary>
        public bool Gated { get; set; }
        public string ResolvePath => $"/{Repository}/resolve/{Commit}/{RepoPath}";

        /// <summary>
        /// Drops the connection of the next download after this many bytes, once.
        /// </summary>
        public void CutNextDownloadAfter(long bytes)
        {
            lock (this)
            {
                _cutFirstResponseAfterBytes = bytes;
            }
        }

        public long? TakeCut()
        {
            lock (this)
            {
                var cut = _cutFirstResponseAfterBytes;
                _cutFirstResponseAfterBytes = null;
                return cut;
            }
        }

        public JObject ToTreeEntry()
        {
            var entry = new JObject
            {
                ["type"] = "file",
                ["path"] = RepoPath,
                ["size"] = Size,
                ["oid"] = Lfs ? new string('0', 40) : ETag
            };
            if (Lfs)
            {
                entry["lfs"] = new JObject { ["oid"] = ETag, ["size"] = Size };
            }

            return entry;
        }
    }

    internal readonly struct FakeHubRequest
    {
        public FakeHubRequest(string method, string path, string range, string ifRange)
        {
            Method = method;
            Path = path;
            Range = range;
            IfRange = ifRange;
        }

        public string Method { get; }
        public string Path { get; }
        public string Range { get; }
        public string IfRange { get; }
    }
}
