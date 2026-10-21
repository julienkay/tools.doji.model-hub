using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using UnityEditor;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Moves downloads from HF Hub's old private cache (<c>&lt;data-root&gt;/raw</c>) into the shared Hugging Face
    /// cache, so models are not downloaded again. Runs once per editor session while the old folder exists.
    /// </summary>
    /// <remarks>
    /// Each old snapshot folder holds a <c>snapshot.hfhub.json</c> that lists every complete file with its etag.
    /// Those files move into <c>blobs/&lt;etag&gt;</c> and are linked into the snapshot, exactly as a download
    /// would store them. Files the old metadata does not list, partial downloads, and files with the wrong size are
    /// dropped; Model Hub downloads them again when needed. A repository that cannot be moved, for example because
    /// another program holds a blob lock, stays in the old folder and is retried in the next editor session.
    /// </remarks>
    [InitializeOnLoad]
    internal static class LegacyCacheMigration
    {
        private const string SessionKey = "Doji.AI.ModelHub.LegacyCacheMigrationChecked";
        private const string LegacyFolderName = "raw";
        private const string LegacyMetadataFileName = "snapshot.hfhub.json";

        static LegacyCacheMigration()
        {
            EditorCacheRoot.Register();
            if (SessionState.GetBool(SessionKey, false))
            {
                return;
            }

            SessionState.SetBool(SessionKey, true);
            RunIfNeeded();
        }

        /// <summary>
        /// The old cache folder: <c>raw</c> in HF Hub's data root.
        /// </summary>
        internal static string GetLegacyRoot()
        {
            return Path.Combine(CachePaths.GetDataRoot(), LegacyFolderName);
        }

        internal static void RunIfNeeded()
        {
            var legacyRoot = GetLegacyRoot();
            if (!Directory.Exists(legacyRoot))
            {
                return;
            }

            try
            {
                EditorUtility.DisplayProgressBar("HF Hub", "Moving downloaded models into the shared Hugging Face cache…", 0f);
                var result = Migrate(legacyRoot);
                Log(result, legacyRoot);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogWarning($"HF Hub could not move models from its old cache folder {legacyRoot}: {exception.Message}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>
        /// Moves every repository in <paramref name="legacyRoot"/> into the shared cache and deletes what it moved.
        /// </summary>
        internal static MigrationResult Migrate(string legacyRoot)
        {
            var result = new MigrationResult();
            foreach (var repositoryFolder in Directory.GetDirectories(legacyRoot))
            {
                try
                {
                    MigrateRepository(repositoryFolder, result);
                    Directory.Delete(repositoryFolder, true);
                }
                catch (Exception exception)
                {
                    result.Failures.Add($"{Path.GetFileName(repositoryFolder)}: {exception.Message}");
                }
            }

            foreach (var file in Directory.GetFiles(legacyRoot))
            {
                File.Delete(file);
            }

            if (!Directory.EnumerateFileSystemEntries(legacyRoot).Any())
            {
                Directory.Delete(legacyRoot);
            }

            return result;
        }

        private static void MigrateRepository(string repositoryFolder, MigrationResult result)
        {
            var snapshotsRoot = Path.Combine(repositoryFolder, "snapshots");
            var migrated = new List<RawCacheMetadata>();
            if (Directory.Exists(snapshotsRoot))
            {
                foreach (var snapshotFolder in Directory.GetDirectories(snapshotsRoot))
                {
                    if (!TryReadLegacyMetadata(snapshotFolder, out var metadata))
                    {
                        continue;
                    }

                    MigrateSnapshot(snapshotFolder, metadata, result);
                    migrated.Add(metadata);
                }
            }

            MigrateRefs(repositoryFolder, migrated);
            if (migrated.Count > 0)
            {
                result.Repositories.Add(migrated[0].Repository);
            }
        }

        private static void MigrateSnapshot(string snapshotFolder, RawCacheMetadata legacy, MigrationResult result)
        {
            var repository = legacy.Repository;
            var commit = legacy.ResolvedCommitHash;
            var movedFiles = new List<RawCacheFileMetadata>();
            foreach (var file in legacy.Files ?? Array.Empty<RawCacheFileMetadata>())
            {
                if (string.IsNullOrWhiteSpace(file?.RepoPath))
                {
                    continue;
                }

                var legacyPath = Path.Combine(snapshotFolder, file.RepoPath.Replace('/', Path.DirectorySeparatorChar));
                var length = File.Exists(legacyPath) ? new FileInfo(legacyPath).Length : (long?)null;
                if (!length.HasValue || (file.SizeInBytes.HasValue && file.SizeInBytes.Value != length.Value))
                {
                    continue;
                }

                var snapshotPath = CachePaths.GetSnapshotFilePath(repository, commit, file.RepoPath);
                var etag = GetEntityTag(legacyPath, file.EntityTag);
                if (CacheFiles.GetLength(snapshotPath) != length)
                {
                    result.MovedBytes += MoveIntoCache(repository, legacyPath, snapshotPath, etag, length.Value);
                }

                result.Files++;
                movedFiles.Add(new RawCacheFileMetadata
                {
                    RepoPath = file.RepoPath,
                    CachedFilePath = snapshotPath,
                    EntityTag = etag,
                    SizeInBytes = length
                });
            }

            WriteIndex(legacy, movedFiles);
        }

        /// <summary>
        /// Stores one file like a download does: in <c>blobs/&lt;etag&gt;</c> with a snapshot link, or straight in the
        /// snapshot when the file has no etag. Returns the number of bytes moved; 0 when the blob was already there.
        /// </summary>
        private static long MoveIntoCache(string repository, string legacyPath, string snapshotPath, string entityTag, long length)
        {
            var etag = CachePaths.NormalizeEtag(entityTag);
            if (string.IsNullOrWhiteSpace(etag))
            {
                MoveFile(legacyPath, snapshotPath);
                return length;
            }

            if (!BlobLock.TryAcquire(CachePaths.GetBlobLockPath(repository, etag), out var blobLock))
            {
                throw new IOException("another program is downloading this model");
            }

            using (blobLock)
            {
                var blobPath = CachePaths.GetBlobPath(repository, etag);
                var isNewBlob = CacheFiles.GetLength(blobPath) != length;
                if (isNewBlob)
                {
                    MoveFile(legacyPath, blobPath);
                }

                CacheFiles.LinkSnapshotFile(blobPath, snapshotPath, isNewBlob);
                return isNewBlob ? length : 0;
            }
        }

        /// <summary>
        /// The etag <c>huggingface_hub</c> would name this file's blob by. Old metadata recorded the CDN's xet hash
        /// for large files, while <c>huggingface_hub</c> uses the LFS sha256, which is the sha256 of the content. A
        /// 40-character etag is the git blob id, which both use.
        /// </summary>
        private static string GetEntityTag(string legacyPath, string legacyEtag)

        {
            var etag = CachePaths.NormalizeEtag(legacyEtag);
            if (etag.Length != 64)
            {
                return string.IsNullOrWhiteSpace(etag) ? null : etag;
            }

            using var stream = File.OpenRead(legacyPath);

            using var sha256 = SHA256.Create();
            return string.Concat(sha256.ComputeHash(stream).Select(value => value.ToString("x2")));
        }

        /// <summary>
        /// Moves through a temporary name, so an interrupted move across drives never leaves a file that looks
        /// complete.
        /// </summary>
        private static void MoveFile(string source, string destination)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? string.Empty);
            var temporaryPath = destination + ".incomplete";
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            File.Move(source, temporaryPath);
            if (File.Exists(destination))
            {
                File.Delete(destination);
            }

            File.Move(temporaryPath, destination);
        }

        /// <summary>
        /// Writes HF Hub's index entry for a moved snapshot. An existing entry keeps its repository file list; moved
        /// files are added to its file list.
        /// </summary>
        private static void WriteIndex(RawCacheMetadata legacy, IReadOnlyList<RawCacheFileMetadata> movedFiles)
        {
            var indexPath = CachePaths.GetSnapshotMetadataPath(legacy.Repository, legacy.ResolvedCommitHash);
            var existing = TryReadJson(indexPath, out RawCacheMetadata current) &&
                string.Equals(current.Repository, legacy.Repository, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(current.ResolvedCommitHash, legacy.ResolvedCommitHash, StringComparison.Ordinal)
                ? current
                : null;

            var files = new Dictionary<string, RawCacheFileMetadata>(StringComparer.Ordinal);
            foreach (var file in movedFiles)
            {
                files[file.RepoPath] = file;
            }

            foreach (var file in existing?.Files ?? Array.Empty<RawCacheFileMetadata>())
            {
                if (!string.IsNullOrWhiteSpace(file?.RepoPath))
                {
                    files[file.RepoPath] = file;
                }
            }

            var repositoryFiles = existing?.RepositoryFiles is { Length: > 0 }
                ? existing.RepositoryFiles
                : legacy.RepositoryFiles ?? Array.Empty<RepositoryFileMetadata>();

            RawCacheService.WriteMetadata(indexPath, new RawCacheMetadata
            {
                Repository = legacy.Repository,
                RequestedRevision = existing?.RequestedRevision ?? legacy.RequestedRevision,
                ResolvedCommitHash = legacy.ResolvedCommitHash,
                CachedSnapshotRoot = CachePaths.GetSnapshotRoot(legacy.Repository, legacy.ResolvedCommitHash),
                RepositoryFiles = repositoryFiles,
                Files = files.Values.OrderBy(file => file.RepoPath, StringComparer.Ordinal).ToArray(),
                DownloadedAtUtc = existing?.DownloadedAtUtc ?? legacy.DownloadedAtUtc
            });
        }

        /// <summary>
        /// Copies old refs that point at a moved snapshot. A ref the shared cache already has is never changed, so
        /// a newer download wins.
        /// </summary>
        private static void MigrateRefs(string repositoryFolder, IReadOnlyList<RawCacheMetadata> migrated)
        {
            var refsRoot = Path.Combine(repositoryFolder, "refs");
            if (migrated.Count == 0 || !Directory.Exists(refsRoot))
            {
                return;
            }

            var repository = migrated[0].Repository;
            foreach (var refFile in Directory.GetFiles(refsRoot, "*", SearchOption.AllDirectories))
            {
                var commit = File.ReadAllText(refFile).Trim();
                var snapshot = migrated.FirstOrDefault(item => string.Equals(item.ResolvedCommitHash, commit, StringComparison.Ordinal));
                if (snapshot == null)
                {
                    continue;
                }

                var revision = !string.IsNullOrWhiteSpace(snapshot.RequestedRevision)
                    ? snapshot.RequestedRevision
                    : Path.GetRelativePath(refsRoot, refFile).Replace(Path.DirectorySeparatorChar, '/');
                if (!File.Exists(CachePaths.GetRevisionRefPath(repository, revision)))
                {
                    RawCacheService.WriteRevisionRef(repository, revision, commit);
                }
            }
        }

        private static bool TryReadLegacyMetadata(string snapshotFolder, out RawCacheMetadata metadata)
        {
            return TryReadJson(Path.Combine(snapshotFolder, LegacyMetadataFileName), out metadata) &&
                !string.IsNullOrWhiteSpace(metadata.Repository) &&
                string.Equals(metadata.ResolvedCommitHash, Path.GetFileName(snapshotFolder), StringComparison.Ordinal);
        }

        private static bool TryReadJson<T>(string path, out T value) where T : class
        {
            value = null;
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                value = JsonConvert.DeserializeObject<T>(File.ReadAllText(path));
                return value != null;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static void Log(MigrationResult result, string legacyRoot)
        {
            if (result.Repositories.Count > 0)
            {
                UnityEngine.Debug.Log(
                    $"HF Hub moved {result.Files} downloaded files of {result.Repositories.Count} models from its old cache folder " +
                    $"into the shared Hugging Face cache at {CachePaths.GetCacheRoot()}: {string.Join(", ", result.Repositories)}.");
            }

            if (result.Failures.Count > 0)
            {
                UnityEngine.Debug.LogWarning(
                    $"HF Hub could not move some models from its old cache folder {legacyRoot}. " +
                    $"It tries again in the next editor session.\n{string.Join("\n", result.Failures)}");
            }
        }

        internal sealed class MigrationResult
        {
            public List<string> Repositories { get; } = new();
            public List<string> Failures { get; } = new();
            public int Files { get; set; }
            public long MovedBytes { get; set; }
        }
    }
}
