using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// One file that takes space in the cache: a blob, a plain snapshot file, or a partial download.
    /// </summary>
    internal sealed class CacheFileEntry
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public long SizeInBytes { get; set; }
        public bool IsPartial { get; set; }
    }

    /// <summary>
    /// One repository folder in the shared Hugging Face cache, whoever downloaded it.
    /// </summary>
    internal sealed class CacheRepositoryEntry
    {
        public string RepoType { get; set; }
        public string Repository { get; set; }
        public string FolderName { get; set; }
        public string FolderPath { get; set; }
        public IReadOnlyList<string> Refs { get; set; } = Array.Empty<string>();
        public int SnapshotCount { get; set; }
        public IReadOnlyList<CacheFileEntry> Files { get; set; } = Array.Empty<CacheFileEntry>();

        public long SizeInBytes => Files.Where(file => !file.IsPartial).Sum(file => file.SizeInBytes);
        public long PartialSizeInBytes => Files.Where(file => file.IsPartial).Sum(file => file.SizeInBytes);
        public long TotalSizeInBytes => Files.Sum(file => file.SizeInBytes);
        public bool HasPartialDownloads => Files.Any(file => file.IsPartial);
        public bool IsModel => string.Equals(RepoType, "model", StringComparison.Ordinal);
    }

    /// <summary>
    /// Everything in the shared Hugging Face cache, for the Model Hub cache overview.
    /// </summary>
    internal sealed class CacheOverview
    {
        public string CacheRoot { get; set; }
        public IReadOnlyList<CacheRepositoryEntry> Repositories { get; set; } = Array.Empty<CacheRepositoryEntry>();
        public long TotalSizeInBytes => Repositories.Sum(repository => repository.TotalSizeInBytes);
        public long PartialSizeInBytes => Repositories.Sum(repository => repository.PartialSizeInBytes);
    }

    /// <summary>
    /// Reads the whole shared cache from disk, like <c>scan_cache_dir</c> in <c>huggingface_hub</c>, and removes
    /// repositories or partial downloads from it.
    /// </summary>
    internal static class CacheOverviewService
    {
        private static readonly string[] RepoTypePrefixes = { "models", "datasets", "spaces" };
        private static readonly string[] PartialSuffixes = { ".incomplete", ".downloading", ".downloading.segment" };
        private const string PartialStateSuffix = ".downloading.json";

        /// <summary>
        /// Lists every repository in the cache with the files that take space. Sizes count each blob once; plain
        /// snapshot files (stored without symbolic links) count separately.
        /// </summary>
        public static CacheOverview Scan(string cacheRoot = null)
        {
            cacheRoot ??= CachePaths.GetCacheRoot();
            var repositories = new List<CacheRepositoryEntry>();
            if (Directory.Exists(cacheRoot))
            {
                foreach (var folder in Directory.GetDirectories(cacheRoot))
                {
                    if (TryParseFolderName(Path.GetFileName(folder), out var repoType, out var repository))
                    {
                        repositories.Add(ScanRepository(folder, repoType, repository));
                    }
                }
            }

            return new CacheOverview
            {
                CacheRoot = cacheRoot,
                Repositories = repositories
                    .Where(repository => repository.Files.Count > 0 || repository.SnapshotCount > 0)
                    .OrderByDescending(repository => repository.TotalSizeInBytes)
                    .ToArray()
            };
        }

        /// <summary>
        /// Lists the repository paths of one cached revision: a ref such as <c>main</c>, or a commit hash. Returns an
        /// empty list when the revision is not in the cache.
        /// </summary>
        public static IReadOnlyList<string> GetSnapshotFiles(string repositoryFolder, string revision)
        {
            if (string.IsNullOrWhiteSpace(repositoryFolder) || string.IsNullOrWhiteSpace(revision))
            {
                return Array.Empty<string>();
            }

            var commit = revision.Trim();
            var refPath = Path.Combine(repositoryFolder, "refs", commit.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(refPath))
            {
                commit = File.ReadAllText(refPath).Trim();
            }

            var snapshot = Path.Combine(repositoryFolder, "snapshots", commit);
            if (commit.Length == 0 || !Directory.Exists(snapshot))
            {
                return Array.Empty<string>();
            }

            var prefix = Path.GetFullPath(snapshot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Directory.EnumerateFiles(snapshot, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetFullPath(file).Substring(prefix.Length).Replace(Path.DirectorySeparatorChar, '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// The model files of a cached revision, for adding the model to a manifest: every cached <c>.onnx</c> and
        /// <c>.sentis</c> file. ONNX external weights are selected with their <c>.onnx</c> file, so they are not listed.
        /// </summary>
        public static IReadOnlyList<string> GetCachedModelFiles(IEnumerable<string> snapshotFiles)
        {
            return (snapshotFiles ?? Array.Empty<string>())
                .Where(ModelFiles.IsModelFile)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Parses <c>models--owner--name</c> into its type and id, as <c>huggingface_hub</c> does.
        /// </summary>
        internal static bool TryParseFolderName(string folderName, out string repoType, out string repository)
        {
            repoType = null;
            repository = null;
            var separator = folderName?.IndexOf("--", StringComparison.Ordinal) ?? -1;
            if (separator <= 0)
            {
                return false;
            }

            var prefix = folderName.Substring(0, separator);
            if (!RepoTypePrefixes.Contains(prefix, StringComparer.Ordinal))
            {
                return false;
            }

            repoType = prefix.TrimEnd('s');
            repository = folderName.Substring(separator + 2).Replace("--", "/", StringComparison.Ordinal);
            return !string.IsNullOrWhiteSpace(repository);
        }

        private static CacheRepositoryEntry ScanRepository(string folder, string repoType, string repository)
        {
            var folderName = Path.GetFileName(folder);
            var repoPathsByBlob = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
            var files = new List<CacheFileEntry>();
            var snapshotsRoot = Path.Combine(folder, "snapshots");
            var snapshotCount = 0;
            if (Directory.Exists(snapshotsRoot))
            {
                foreach (var snapshot in Directory.GetDirectories(snapshotsRoot))
                {
                    snapshotCount++;
                    var prefix = Path.GetFullPath(snapshot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    foreach (var file in Directory.EnumerateFiles(snapshot, "*", SearchOption.AllDirectories))
                    {
                        var repoPath = Path.GetFullPath(file).Substring(prefix.Length).Replace(Path.DirectorySeparatorChar, '/');
                        if (CacheFiles.IsSymlink(file))
                        {
                            var blobName = Path.GetFileName(CacheFiles.TryGetLinkTarget(file) ?? string.Empty);
                            if (!string.IsNullOrEmpty(blobName))
                            {
                                if (!repoPathsByBlob.TryGetValue(blobName, out var paths))
                                {
                                    repoPathsByBlob[blobName] = paths = new SortedSet<string>(StringComparer.Ordinal);
                                }

                                paths.Add(repoPath);
                            }

                            continue;
                        }

                        if (repoPath.EndsWith(PartialStateSuffix, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        var partialSuffix = GetPartialSuffix(repoPath);
                        files.Add(new CacheFileEntry
                        {
                            Name = partialSuffix == null ? repoPath : repoPath.Substring(0, repoPath.Length - partialSuffix.Length),
                            Path = file,
                            SizeInBytes = new FileInfo(file).Length,
                            IsPartial = partialSuffix != null
                        });
                    }
                }
            }

            var repoPathsByEtag = ReadIndexedRepoPaths(folderName);
            var blobsRoot = Path.Combine(folder, "blobs");
            if (Directory.Exists(blobsRoot))
            {
                foreach (var blob in Directory.GetFiles(blobsRoot))
                {
                    var blobName = Path.GetFileName(blob);
                    if (blobName.EndsWith(PartialStateSuffix, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var partialSuffix = GetPartialSuffix(blobName);
                    var etag = partialSuffix == null ? blobName : blobName.Substring(0, blobName.Length - partialSuffix.Length);
                    string name;
                    if (repoPathsByBlob.TryGetValue(etag, out var linkedPaths))
                    {
                        name = linkedPaths.First();
                    }
                    else if (!repoPathsByEtag.TryGetValue(etag, out name))
                    {
                        name = partialSuffix == null ? "blobs/" + etag : "Partial download " + etag.Substring(0, Math.Min(12, etag.Length));
                    }

                    files.Add(new CacheFileEntry
                    {
                        Name = name,
                        Path = blob,
                        SizeInBytes = new FileInfo(blob).Length,
                        IsPartial = partialSuffix != null
                    });
                }
            }

            var refsRoot = Path.Combine(folder, "refs");
            var refs = Directory.Exists(refsRoot)
                ? Directory.GetFiles(refsRoot, "*", SearchOption.AllDirectories)
                    .Select(file => Path.GetRelativePath(refsRoot, file).Replace(Path.DirectorySeparatorChar, '/'))
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray()
                : Array.Empty<string>();

            return new CacheRepositoryEntry
            {
                RepoType = repoType,
                Repository = repository,
                FolderName = folderName,
                FolderPath = folder,
                Refs = refs,
                SnapshotCount = snapshotCount,
                Files = files.OrderByDescending(file => file.SizeInBytes).ToArray()
            };
        }

        /// <summary>
        /// Maps etags to repository paths from AI Model Hub's index, to name partial downloads and unlinked blobs.
        /// </summary>
        private static Dictionary<string, string> ReadIndexedRepoPaths(string folderName)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            var indexFolder = Path.Combine(CachePaths.GetDataRoot(), "index", folderName);
            if (!Directory.Exists(indexFolder))
            {
                return result;
            }

            foreach (var indexFile in Directory.GetFiles(indexFolder, "*.json"))
            {
                try
                {
                    var metadata = JsonConvert.DeserializeObject<RawCacheMetadata>(File.ReadAllText(indexFile));
                    foreach (var file in metadata?.Files ?? Array.Empty<RawCacheFileMetadata>())
                    {
                        var etag = CachePaths.NormalizeEtag(file?.EntityTag);
                        if (!string.IsNullOrEmpty(etag) && !string.IsNullOrEmpty(file.RepoPath))
                        {
                            result[etag] = file.RepoPath;
                        }
                    }
                }
                catch (Exception exception) when (exception is IOException or JsonException)
                {
                    // The index is only a hint.
                }
            }

            return result;
        }

        private static string GetPartialSuffix(string name)
        {
            return PartialSuffixes.FirstOrDefault(suffix => name.EndsWith(suffix, StringComparison.Ordinal));
        }

        /// <summary>
        /// Deletes one repository from the cache with all revisions and partial downloads, plus AI Model Hub's index for
        /// it. Throws <see cref="IOException"/> without deleting anything when another program is downloading one of
        /// its files.
        /// </summary>
        public static void RemoveRepository(CacheRepositoryEntry repository, string cacheRoot = null)
        {
            cacheRoot ??= CachePaths.GetCacheRoot();
            var locks = new List<BlobLock>();
            try
            {
                foreach (var etag in GetBlobEtags(repository))
                {
                    if (!BlobLock.TryAcquire(GetLockPath(cacheRoot, repository.FolderName, etag), out var blobLock))
                    {
                        throw new IOException($"Another program is downloading files of {repository.Repository}. Try again when it is done.");
                    }

                    locks.Add(blobLock);
                }

                if (Directory.Exists(repository.FolderPath))
                {
                    Directory.Delete(repository.FolderPath, true);
                }
            }
            finally
            {
                foreach (var blobLock in locks)
                {
                    blobLock.Dispose();
                }
            }

            DeleteFolderIfExists(Path.Combine(cacheRoot, ".locks", repository.FolderName));
            DeleteFolderIfExists(Path.Combine(CachePaths.GetDataRoot(), "index", repository.FolderName));
        }

        /// <summary>
        /// Deletes one partial download, its resume state, and the body of an interrupted request. Throws <see cref="IOException"/> when another program is
        /// still downloading it.
        /// </summary>
        public static void DeletePartialFile(CacheRepositoryEntry repository, CacheFileEntry file, string cacheRoot = null)
        {
            if (file == null || !file.IsPartial)
            {
                return;
            }

            cacheRoot ??= CachePaths.GetCacheRoot();
            var fileName = Path.GetFileName(file.Path);
            var etag = fileName.Substring(0, fileName.Length - (GetPartialSuffix(fileName)?.Length ?? 0));
            if (!BlobLock.TryAcquire(GetLockPath(cacheRoot, repository.FolderName, etag), out var blobLock))
            {
                throw new IOException($"Another program is still downloading {file.Name}.");
            }

            using (blobLock)
            {
                DeleteFileIfExists(file.Path);
                DeleteFileIfExists(file.Path + ".json");
                DeleteFileIfExists(file.Path + ".segment");
            }
        }

        private static IEnumerable<string> GetBlobEtags(CacheRepositoryEntry repository)
        {
            var blobsRoot = Path.Combine(repository.FolderPath, "blobs");
            return repository.Files
                .Where(file => string.Equals(Path.GetDirectoryName(file.Path), blobsRoot, StringComparison.OrdinalIgnoreCase))
                .Select(file => Path.GetFileName(file.Path))
                .Select(name => name.Substring(0, name.Length - (GetPartialSuffix(name)?.Length ?? 0)))
                .Distinct(StringComparer.Ordinal);
        }

        private static string GetLockPath(string cacheRoot, string folderName, string etag)
        {
            return Path.Combine(cacheRoot, ".locks", folderName, etag + ".lock");
        }

        private static void DeleteFolderIfExists(string folder)
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }

        private static void DeleteFileIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
