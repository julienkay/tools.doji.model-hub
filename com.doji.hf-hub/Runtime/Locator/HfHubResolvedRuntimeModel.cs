using System;
using System.Collections.Generic;
using System.Linq;

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// Represents one runtime-loadable model resolved from generated HF Hub locator registry data.
    /// </summary>
    public sealed class HfHubResolvedRuntimeModel
    {
        private readonly HfHubResolvedRuntimeFile[] _files;

        /// <summary>
        /// Creates immutable runtime model data backed by generated locator registry entries.
        /// </summary>
        public HfHubResolvedRuntimeModel(
            string packageId,
            string packageVersion,
            string modelId,
            string displayName,
            string primarySentisPath,
            IReadOnlyList<HfHubResolvedRuntimeFile> files)
        {
            PackageId = packageId ?? throw new ArgumentNullException(nameof(packageId));
            PackageVersion = packageVersion ?? throw new ArgumentNullException(nameof(packageVersion));
            ModelId = modelId ?? throw new ArgumentNullException(nameof(modelId));
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
            PrimarySentisPath = primarySentisPath ?? throw new ArgumentNullException(nameof(primarySentisPath));
            _files = files?.ToArray() ?? Array.Empty<HfHubResolvedRuntimeFile>();
        }

        /// <summary>
        /// Package id that owns synchronized runtime files.
        /// </summary>
        public string PackageId { get; }

        /// <summary>
        /// Package version associated with generated runtime files.
        /// </summary>
        public string PackageVersion { get; }

        /// <summary>
        /// Stable package-local model id used at runtime lookup.
        /// </summary>
        public string ModelId { get; }

        /// <summary>
        /// User-facing model name for diagnostics and tooling.
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// Absolute file path for primary `.sentis` payload under `Application.streamingAssetsPath`.
        /// </summary>
        public string PrimarySentisPath { get; }

        /// <summary>
        /// All resolved runtime files emitted for this model.
        /// </summary>
        public IReadOnlyList<HfHubResolvedRuntimeFile> Files => _files;

        /// <summary>
        /// Returns absolute file path for one original repository-relative source file when it exists in registry.
        /// </summary>
        public bool TryGetFilePath(string sourceFile, out string filePath)
        {
            var match = _files.FirstOrDefault(file => string.Equals(file.SourceFile, sourceFile, StringComparison.Ordinal));
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
        /// <summary>
        /// Creates immutable runtime file data.
        /// </summary>
        public HfHubResolvedRuntimeFile(string filePath, string sourceFile, string kind)
        {
            FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            SourceFile = sourceFile ?? throw new ArgumentNullException(nameof(sourceFile));
            Kind = kind ?? throw new ArgumentNullException(nameof(kind));
        }

        /// <summary>
        /// Absolute runtime file path.
        /// </summary>
        public string FilePath { get; }

        /// <summary>
        /// Original repository-relative file path represented by runtime file.
        /// </summary>
        public string SourceFile { get; }

        /// <summary>
        /// File role such as `sentis` or `source`.
        /// </summary>
        public string Kind { get; }
    }
}
