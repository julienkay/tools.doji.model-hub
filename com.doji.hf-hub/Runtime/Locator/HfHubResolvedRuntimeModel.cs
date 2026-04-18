using System;
using System.Collections.Generic;
using System.Linq;

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// Represents one runtime-loadable repository snapshot resolved from generated HF Hub locator registry data.
    /// </summary>
    public sealed class HfHubResolvedRuntimeModel
    {
        private readonly HfHubResolvedRuntimeFile[] _files;

        public HfHubResolvedRuntimeModel(
            string packageId,
            string packageVersion,
            string repoId,
            string displayName,
            IReadOnlyList<HfHubResolvedRuntimeFile> files)
        {
            PackageId = packageId ?? throw new ArgumentNullException(nameof(packageId));
            PackageVersion = packageVersion ?? throw new ArgumentNullException(nameof(packageVersion));
            RepoId = repoId ?? throw new ArgumentNullException(nameof(repoId));
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
            _files = files?.ToArray() ?? Array.Empty<HfHubResolvedRuntimeFile>();
        }

        public string PackageId { get; }
        public string PackageVersion { get; }
        public string RepoId { get; }
        public string DisplayName { get; }
        public IReadOnlyList<HfHubResolvedRuntimeFile> Files => _files;

        public bool TryGetSentisPath(string repoPath, out string filePath)
        {
            var match = _files.FirstOrDefault(file =>
                string.Equals(file.RepoPath, repoPath, StringComparison.Ordinal) &&
                string.Equals(file.Kind, "sentis", StringComparison.Ordinal));
            if (match == null)
            {
                filePath = null;
                return false;
            }

            filePath = match.FilePath;
            return true;
        }
    }

    /// <summary>
    /// Represents one runtime file resolved from generated locator registry data.
    /// </summary>
    public sealed class HfHubResolvedRuntimeFile
    {
        public HfHubResolvedRuntimeFile(string filePath, string repoPath, string kind)
        {
            FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            RepoPath = repoPath ?? throw new ArgumentNullException(nameof(repoPath));
            Kind = kind ?? throw new ArgumentNullException(nameof(kind));
        }

        public string FilePath { get; }
        public string RepoPath { get; }
        public string Kind { get; }
    }
}
