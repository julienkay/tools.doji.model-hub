using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Shares cached Hugging Face repository-existence checks across editor surfaces.
    /// </summary>
    internal static class RepositoryValidationCacheService
    {
        private const string CacheProjectPath = "Library/Doji/HfHub/model-manifest-validation/hf-hub-repo-validation.json";
        private static readonly TimeSpan CacheTtl = TimeSpan.FromDays(1);
        private static readonly object InFlightValidationGate = new();
        private static readonly System.Collections.Generic.Dictionary<string, Task<bool>> InFlightValidationByRepository =
            new(StringComparer.Ordinal);
        private static readonly System.Collections.Generic.HashSet<string> SessionValidationRequestedByRepository =
            new(StringComparer.Ordinal);
        private static int _cacheRevision;

        internal static int CacheRevision => _cacheRevision;

        [Serializable]
        private sealed class CacheDocument
        {
            public CacheEntry[] entries;
        }

        [Serializable]
        private sealed class CacheEntry
        {
            public string repository;
            public bool exists;
            public string checkedAtUtc;
        }

        internal static bool TryGetCachedRepositoryExists(string repository, out bool exists)
        {
            exists = false;
            if (string.IsNullOrWhiteSpace(repository))
            {
                return false;
            }

            var cachePath = ProjectPaths.ToProjectAbsolutePath(CacheProjectPath);
            if (!File.Exists(cachePath))
            {
                return false;
            }

            try
            {
                var document = ReadCache(cachePath);
                var entry = (document?.entries ?? Array.Empty<CacheEntry>())
                    .FirstOrDefault(candidate => string.Equals(candidate.repository, repository, StringComparison.Ordinal));
                if (entry == null ||
                    string.IsNullOrWhiteSpace(entry.checkedAtUtc) ||
                    !DateTime.TryParse(entry.checkedAtUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var checkedAtUtc))
                {
                    return false;
                }

                if ((DateTime.UtcNow - checkedAtUtc) > CacheTtl)
                {
                    return false;
                }

                exists = entry.exists;
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static void WriteCachedRepositoryExists(string repository, bool exists)
        {
            if (string.IsNullOrWhiteSpace(repository))
            {
                return;
            }

            var cachePath = ProjectPaths.ToProjectAbsolutePath(CacheProjectPath);
            var directory = Path.GetDirectoryName(cachePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var document = ReadCache(cachePath) ?? new CacheDocument
            {
                entries = Array.Empty<CacheEntry>()
            };
            var entries = (document.entries ?? Array.Empty<CacheEntry>()).ToList();
            var existingEntry = entries.FirstOrDefault(entry => string.Equals(entry.repository, repository, StringComparison.Ordinal));
            if (existingEntry == null)
            {
                existingEntry = new CacheEntry
                {
                    repository = repository
                };
                entries.Add(existingEntry);
            }

            existingEntry.exists = exists;
            existingEntry.checkedAtUtc = DateTime.UtcNow.ToString("O");
            document.entries = entries
                .OrderBy(entry => entry.repository, StringComparer.Ordinal)
                .ToArray();

            File.WriteAllText(cachePath, UnityEngine.JsonUtility.ToJson(document, true));
            _cacheRevision++;
        }

        internal static Task<bool> GetOrStartRepositoryValidationAsync(string repository, Func<Task<bool>> factory)
        {
            if (string.IsNullOrWhiteSpace(repository))
            {
                return Task.FromResult(false);
            }

            lock (InFlightValidationGate)
            {
                if (InFlightValidationByRepository.TryGetValue(repository, out var existingTask))
                {
                    return existingTask;
                }

                var task = RunValidationAsync(repository, factory);
                InFlightValidationByRepository[repository] = task;
                return task;
            }
        }

        internal static bool TryMarkSessionValidationRequested(string repository)
        {
            if (string.IsNullOrWhiteSpace(repository))
            {
                return false;
            }

            lock (InFlightValidationGate)
            {
                return SessionValidationRequestedByRepository.Add(repository);
            }
        }

        private static CacheDocument ReadCache(string cachePath)
        {
            if (string.IsNullOrWhiteSpace(cachePath) || !File.Exists(cachePath))
            {
                return null;
            }

            try
            {
                return UnityEngine.JsonUtility.FromJson<CacheDocument>(File.ReadAllText(cachePath));
            }
            catch
            {
                return null;
            }
        }

        private static async Task<bool> RunValidationAsync(string repository, Func<Task<bool>> factory)
        {
            try
            {
                return await factory();
            }
            finally
            {
                lock (InFlightValidationGate)
                {
                    InFlightValidationByRepository.Remove(repository);
                }
            }
        }
    }
}
