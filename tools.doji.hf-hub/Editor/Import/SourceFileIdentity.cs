using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Finds the etag that identifies a cached file's content, for keys of converted models.
    /// </summary>
    internal static class SourceFileIdentity
    {
        private static readonly Dictionary<string, string> ContentHashes = new(StringComparer.Ordinal);

        /// <summary>
        /// Returns the etag from HF Hub's index, else the name of the blob a snapshot link points to (the cache names
        /// blobs by etag), else the sha256 of the content. The content hash is computed once per file version.
        /// </summary>
        public static string GetEtag(RawCacheFileMetadata file)
        {
            var etag = CachePaths.NormalizeEtag(file?.EntityTag);
            if (!string.IsNullOrWhiteSpace(etag))
            {
                return etag;
            }

            var path = file?.CachedFilePath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            var linkTarget = CacheFiles.TryGetLinkTarget(path);
            if (!string.IsNullOrWhiteSpace(linkTarget))
            {
                return Path.GetFileName(linkTarget);
            }

            var info = new FileInfo(path);
            var cacheKey = $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
            lock (ContentHashes)
            {
                if (ContentHashes.TryGetValue(cacheKey, out var hash))
                {
                    return hash;
                }
            }

            string computed;
            using (var stream = File.OpenRead(path))
            using (var sha256 = SHA256.Create())
            {
                computed = string.Concat(sha256.ComputeHash(stream).Select(value => value.ToString("x2")));
            }

            lock (ContentHashes)
            {
                ContentHashes[cacheKey] = computed;
            }

            return computed;
        }
    }
}
