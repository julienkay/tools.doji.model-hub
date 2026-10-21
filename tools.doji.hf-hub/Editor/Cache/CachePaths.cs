using System;
using System.IO;
using System.Text;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Computes paths in the shared Hugging Face cache and in HF Hub's own index.
    /// The cache uses the same location and layout as the <c>huggingface_hub</c> Python library,
    /// so Unity and Python tools reuse each other's downloads.
    /// </summary>
    internal static class CachePaths
    {
        /// <summary>
        /// Environment variable that sets the Hugging Face cache folder, as in <c>huggingface_hub</c>.
        /// </summary>
        internal const string HubCacheEnvironmentVariable = "HF_HUB_CACHE";

        /// <summary>
        /// Older name of <see cref="HubCacheEnvironmentVariable"/> that <c>huggingface_hub</c> still reads.
        /// </summary>
        internal const string LegacyHubCacheEnvironmentVariable = "HUGGINGFACE_HUB_CACHE";

        /// <summary>
        /// Environment variable for the Hugging Face home folder. The cache is its <c>hub</c> subfolder.
        /// </summary>
        internal const string HomeEnvironmentVariable = "HF_HOME";

        /// <summary>
        /// Maintainer and test override for <see cref="GetDataRoot"/>, so tests never touch the real index.
        /// </summary>
        internal const string DataRootEnvironmentVariable = "DOJI_HF_HUB_DATA_ROOT";

        private const string RepositoryTypePrefix = "models";
        private const string RepositoryIdSeparator = "--";
        private const string CompanyFolderName = "Doji";
        private const string ProductFolderName = "HfHub";

        /// <summary>
        /// Gets the cache folder in use: the Project Settings custom folder, or the Hugging Face default.
        /// </summary>
        public static string GetCacheRoot()
        {
            return ProjectSettings.instance.GetEffectiveCacheLocation();
        }

        /// <summary>
        /// Gets the default Hugging Face cache folder, resolved like <c>huggingface_hub</c>:
        /// <c>HF_HUB_CACHE</c>, then <c>HUGGINGFACE_HUB_CACHE</c>, then <c>HF_HOME/hub</c>,
        /// then <c>$XDG_CACHE_HOME/huggingface/hub</c>, then <c>~/.cache/huggingface/hub</c>.
        /// </summary>
        public static string GetDefaultCacheRoot()
        {
            var hubCache = ReadPathVariable(HubCacheEnvironmentVariable) ?? ReadPathVariable(LegacyHubCacheEnvironmentVariable);
            if (hubCache != null)
            {
                return hubCache;
            }

            var home = ReadPathVariable(HomeEnvironmentVariable);
            if (home == null)
            {
                var cacheHome = ReadPathVariable("XDG_CACHE_HOME") ?? Path.Combine(GetUserHome(), ".cache");
                home = Path.Combine(cacheHome, "huggingface");
            }

            return Path.Combine(home, "hub");
        }

        /// <summary>
        /// Gets the environment variable that decides the default cache folder, or null when none is set.
        /// </summary>
        internal static string GetDefaultCacheRootSource()
        {
            foreach (var name in new[] { HubCacheEnvironmentVariable, LegacyHubCacheEnvironmentVariable, HomeEnvironmentVariable })
            {
                if (ReadPathVariable(name) != null)
                {
                    return name;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets HF Hub's per-user folder. It holds HF Hub's own index and never Hugging Face cache content.
        /// </summary>
        public static string GetDataRoot()
        {
            var dataRootOverride = ReadPathVariable(DataRootEnvironmentVariable);
            if (dataRootOverride != null)
            {
                return dataRootOverride;
            }

            if (UnityEngine.Application.platform == UnityEngine.RuntimePlatform.WindowsEditor)
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    CompanyFolderName,
                    ProductFolderName);
            }

            if (UnityEngine.Application.platform == UnityEngine.RuntimePlatform.OSXEditor)
            {
                return Path.Combine(GetUserHome(), "Library", "Application Support", CompanyFolderName, ProductFolderName);
            }

            var cacheHome = ReadPathVariable("XDG_CACHE_HOME") ?? Path.Combine(GetUserHome(), ".cache");
            return Path.Combine(cacheHome, "doji", "hf-hub");
        }

        /// <summary>
        /// Serializes a repository id into its cache folder name, for example
        /// <c>julienkay/sentis-MiDaS</c> to <c>models--julienkay--sentis-MiDaS</c>.
        /// </summary>
        public static string GetRepositoryFolderName(string repository)
        {
            if (string.IsNullOrWhiteSpace(repository))
            {
                throw new ArgumentException("Repository cannot be null or whitespace.", nameof(repository));
            }

            return RepositoryTypePrefix + RepositoryIdSeparator + repository.Trim().Replace("/", RepositoryIdSeparator, StringComparison.Ordinal);
        }

        /// <summary>
        /// Gets the cache folder of one repository. It contains <c>refs</c>, <c>blobs</c>, and <c>snapshots</c>.
        /// </summary>
        public static string GetRepositoryRoot(string repository)
        {
            return Path.Combine(GetCacheRoot(), ResolveRepositoryFolderName(repository));
        }

        /// <summary>
        /// Gets the cache folder name of a repository, reusing an existing folder whose name differs only in letter
        /// case. Hub ids are not case-sensitive, so a manifest that writes <c>julienkay/sentis-midas</c> finds the
        /// <c>models--julienkay--sentis-MiDaS</c> folder that a download or Python created, also on file systems that
        /// are case-sensitive.
        /// </summary>
        internal static string ResolveRepositoryFolderName(string repository)
        {
            var folderName = GetRepositoryFolderName(repository);
            var cacheRoot = GetCacheRoot();
            if (Directory.Exists(Path.Combine(cacheRoot, folderName)) || !Directory.Exists(cacheRoot))
            {
                return folderName;
            }

            try
            {
                foreach (var folder in Directory.EnumerateDirectories(cacheRoot, RepositoryTypePrefix + RepositoryIdSeparator + "*"))
                {
                    var name = Path.GetFileName(folder);
                    if (string.Equals(name, folderName, StringComparison.OrdinalIgnoreCase))
                    {
                        return name;
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Fall back to the requested spelling.
            }

            return folderName;
        }

        /// <summary>
        /// Gets the ref file that maps a branch or tag to a commit. Revisions with <c>/</c>, such as
        /// <c>refs/pr/1</c>, become nested folders, as in <c>huggingface_hub</c>.
        /// </summary>
        public static string GetRevisionRefPath(string repository, string revision)
        {
            return Path.Combine(GetRepositoryRoot(repository), "refs", ToSafeRelativePath(revision));
        }

        /// <summary>
        /// Gets the snapshot folder of one commit.
        /// </summary>
        public static string GetSnapshotRoot(string repository, string commitHash)
        {
            return Path.Combine(GetRepositoryRoot(repository), "snapshots", SanitizePathSegment(commitHash));
        }

        /// <summary>
        /// Gets the snapshot path of one repository file at one commit.
        /// </summary>
        public static string GetSnapshotFilePath(string repository, string commitHash, string sourceFile)
        {
            return Path.Combine(GetSnapshotRoot(repository, commitHash), ToSafeRelativePath(sourceFile));
        }

        /// <summary>
        /// Gets the blob folder of one repository. Blob files are named by their etag.
        /// </summary>
        public static string GetBlobsRoot(string repository)
        {
            return Path.Combine(GetRepositoryRoot(repository), "blobs");
        }

        /// <summary>
        /// Gets the blob path for one file content, named by its etag.
        /// </summary>
        public static string GetBlobPath(string repository, string etag)
        {
            return Path.Combine(GetBlobsRoot(repository), SanitizePathSegment(NormalizeEtag(etag)));
        }

        /// <summary>
        /// Gets the lock file for one blob, under the cache's shared <c>.locks</c> folder.
        /// </summary>
        public static string GetBlobLockPath(string repository, string etag)
        {
            return Path.Combine(
                GetCacheRoot(),
                ".locks",
                ResolveRepositoryFolderName(repository),
                SanitizePathSegment(NormalizeEtag(etag)) + ".lock");
        }

        /// <summary>
        /// Gets HF Hub's index entry for one cached snapshot. It lives in <see cref="GetDataRoot"/>,
        /// outside the Hugging Face cache, so other tools never see it.
        /// </summary>
        public static string GetSnapshotMetadataPath(string repository, string commitHash)
        {
            return Path.Combine(
                GetDataRoot(),
                "index",
                ResolveRepositoryFolderName(repository),
                SanitizePathSegment(commitHash) + ".json");
        }

        /// <summary>
        /// Gets the lock file that guards one index entry and its snapshot writes.
        /// </summary>
        public static string GetSnapshotLockPath(string repository, string commitHash)
        {
            return GetSnapshotMetadataPath(repository, commitHash) + ".lock";
        }

        /// <summary>
        /// Removes the quotes and weak marker that HTTP etag headers carry.
        /// </summary>
        internal static string NormalizeEtag(string etag)
        {
            var value = etag?.Trim() ?? string.Empty;
            if (value.StartsWith("W/", StringComparison.Ordinal))
            {
                value = value.Substring(2);
            }

            return value.Trim('"');
        }

        private static string ReadPathVariable(string name)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            value = Environment.ExpandEnvironmentVariables(value.Trim());
            if (value == "~" || value.StartsWith("~/", StringComparison.Ordinal) || value.StartsWith("~\\", StringComparison.Ordinal))
            {
                value = GetUserHome() + value.Substring(1);
            }

            return value;
        }

        private static string GetUserHome()
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        private static string ToSafeRelativePath(string relativePath)
        {
            var segments = (relativePath ?? string.Empty).Trim().Split('/', '\\');
            var builder = new StringBuilder(relativePath?.Length ?? 0);
            foreach (var segment in segments)
            {
                if (segment.Length == 0 || segment == "." || segment == "..")
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(Path.DirectorySeparatorChar);
                }

                builder.Append(SanitizePathSegment(segment));
            }

            return builder.ToString();
        }

        private static string SanitizePathSegment(string value)
        {
            var sanitized = (value ?? string.Empty).Trim().Replace("/", "_", StringComparison.Ordinal);
            foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
            {
                sanitized = sanitized.Replace(invalidCharacter, '_');
            }

            return sanitized;
        }
    }
}
