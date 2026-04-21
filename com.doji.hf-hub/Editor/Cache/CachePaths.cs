using System;
using System.IO;
using System.Text;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Computes shared raw-cache paths used by editor-only Hub downloads.
    /// </summary>
    internal static class CachePaths
    {
        private const string CompanyFolderName = "Doji";
        private const string ProductFolderName = "HfHub";
        private const string CacheRootEnvironmentVariable = "DOJI_HF_HUB_CACHE_ROOT";

        /// <summary>
        /// Gets shared per-user cache root for this package.
        /// </summary>
        public static string GetCacheRoot()
        {
            var overrideRoot = Environment.GetEnvironmentVariable(CacheRootEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(overrideRoot))
            {
                return overrideRoot;
            }

            return ProjectSettings.instance.GetEffectiveCacheLocation();
        }

        /// <summary>
        /// Gets platform default shared per-user cache root for this package.
        /// </summary>
        public static string GetDefaultCacheRoot()
        {
            if (UnityEngine.Application.platform == UnityEngine.RuntimePlatform.WindowsEditor)
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    CompanyFolderName,
                    ProductFolderName);
            }

            if (UnityEngine.Application.platform == UnityEngine.RuntimePlatform.OSXEditor)
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Personal),
                    "Library",
                    "Application Support",
                    CompanyFolderName,
                    ProductFolderName);
            }

            var xdgCacheHome = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            if (!string.IsNullOrWhiteSpace(xdgCacheHome))
            {
                return Path.Combine(xdgCacheHome, "doji", "hf-hub");
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Personal),
                ".cache",
                "doji",
                "hf-hub");
        }

        /// <summary>
        /// Serializes repo id into one safe folder name.
        /// </summary>
        public static string GetSerializedRepositoryFolderName(string repository)
        {
            if (string.IsNullOrWhiteSpace(repository))
            {
                throw new ArgumentException("Repository cannot be null or whitespace.", nameof(repository));
            }

            return repository.Replace("/", "--", StringComparison.Ordinal);
        }

        /// <summary>
        /// Gets folder that stores refs, snapshots, and metadata for one repository.
        /// </summary>
        public static string GetRepositoryRoot(string repository)
        {
            return Path.Combine(GetCacheRoot(), "raw", GetSerializedRepositoryFolderName(repository));
        }

        /// <summary>
        /// Gets ref file that maps requested revision to resolved commit.
        /// </summary>
        public static string GetRevisionRefPath(string repository, string revision)
        {
            return Path.Combine(GetRepositoryRoot(repository), "refs", SanitizeRelativePathSegment(revision));
        }

        /// <summary>
        /// Gets cached snapshot root for one resolved commit.
        /// </summary>
        public static string GetSnapshotRoot(string repository, string commitHash)
        {
            return Path.Combine(
                GetRepositoryRoot(repository),
                "snapshots",
                SanitizeRelativePathSegment(commitHash));
        }

        /// <summary>
        /// Gets cached snapshot file path for one resolved commit and repository-relative file.
        /// </summary>
        public static string GetSnapshotFilePath(string repository, string commitHash, string sourceFile)
        {
            return Path.Combine(
                GetSnapshotRoot(repository, commitHash),
                ToPlatformRelativePath(sourceFile));
        }

        /// <summary>
        /// Gets metadata path for one cached snapshot.
        /// </summary>
        public static string GetSnapshotMetadataPath(string repository, string commitHash)
        {
            return Path.Combine(GetSnapshotRoot(repository, commitHash), "snapshot.hfhub.json");
        }

        /// <summary>
        /// Gets lock file path for one cached snapshot.
        /// </summary>
        public static string GetSnapshotLockPath(string repository, string commitHash)
        {
            return GetSnapshotMetadataPath(repository, commitHash) + ".lock";
        }

        private static string ToPlatformRelativePath(string sourceFile)
        {
            var builder = new StringBuilder(sourceFile.Length);
            foreach (var character in sourceFile)
            {
                builder.Append(character == '/' ? Path.DirectorySeparatorChar : character);
            }

            return builder.ToString();
        }

        private static string SanitizeRelativePathSegment(string value)
        {
            var sanitized = value.Trim().Replace("/", "_", StringComparison.Ordinal);
            foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
            {
                sanitized = sanitized.Replace(invalidCharacter, '_');
            }

            return sanitized;
        }
    }
}
