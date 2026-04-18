using System;
using System.Collections.Generic;
using System.Linq;

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// Describes one generated runtime locator registry for a package's synchronized model files.
    /// </summary>
    [Serializable]
    public sealed class HfHubRuntimeLocatorManifest
    {
        private readonly HfHubRuntimeLocatorEntry[] _models;

        /// <summary>
        /// Creates immutable runtime locator data for one package.
        /// </summary>
        public HfHubRuntimeLocatorManifest(
            int version,
            string packageId,
            string packageVersion,
            IReadOnlyList<HfHubRuntimeLocatorEntry> models)
        {
            Version = version;
            PackageId = packageId ?? throw new ArgumentNullException(nameof(packageId));
            PackageVersion = packageVersion ?? throw new ArgumentNullException(nameof(packageVersion));
            _models = models?.ToArray() ?? Array.Empty<HfHubRuntimeLocatorEntry>();
        }

        /// <summary>
        /// Locator schema version used to evolve runtime registry format safely.
        /// </summary>
        public int Version { get; }

        /// <summary>
        /// Package id that owns these generated runtime files.
        /// </summary>
        public string PackageId { get; }

        /// <summary>
        /// Package version associated with generated runtime files.
        /// </summary>
        public string PackageVersion { get; }

        /// <summary>
        /// Runtime-loadable model entries declared for package.
        /// </summary>
        public IReadOnlyList<HfHubRuntimeLocatorEntry> Models => _models;
    }

    /// <summary>
    /// Maps one declared model dependency to runtime-loadable files under shared StreamingAssets root.
    /// </summary>
    [Serializable]
    public sealed class HfHubRuntimeLocatorEntry
    {
        private readonly HfHubRuntimeLocatorFile[] _files;

        /// <summary>
        /// Creates immutable runtime locator entry for one generated model dependency.
        /// </summary>
        public HfHubRuntimeLocatorEntry(
            string modelId,
            string repository,
            string displayName,
            string primarySentisRelativePath,
            IReadOnlyList<HfHubRuntimeLocatorFile> files)
        {
            ModelId = modelId ?? throw new ArgumentNullException(nameof(modelId));
            Repository = repository ?? throw new ArgumentNullException(nameof(repository));
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
            PrimarySentisRelativePath = primarySentisRelativePath ?? throw new ArgumentNullException(nameof(primarySentisRelativePath));
            _files = files?.ToArray() ?? Array.Empty<HfHubRuntimeLocatorFile>();
        }

        /// <summary>
        /// Stable package-local model id used at runtime lookup.
        /// </summary>
        public string ModelId { get; }

        /// <summary>
        /// Canonical HuggingFace repository id used by `from_pretrained(...)` style calls.
        /// </summary>
        public string Repository { get; }

        /// <summary>
        /// User-facing model name for diagnostics and tooling.
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// Primary `.sentis` file path relative to `Application.streamingAssetsPath`.
        /// </summary>
        public string PrimarySentisRelativePath { get; }

        /// <summary>
        /// All runtime files generated for model under shared StreamingAssets root.
        /// </summary>
        public IReadOnlyList<HfHubRuntimeLocatorFile> Files => _files;
    }

    /// <summary>
    /// Describes one runtime file emitted for a synchronized model dependency.
    /// </summary>
    [Serializable]
    public sealed class HfHubRuntimeLocatorFile
    {
        /// <summary>
        /// Creates immutable runtime file locator metadata.
        /// </summary>
        public HfHubRuntimeLocatorFile(string relativePath, string sourceFile, string kind)
        {
            RelativePath = relativePath ?? throw new ArgumentNullException(nameof(relativePath));
            SourceFile = sourceFile ?? throw new ArgumentNullException(nameof(sourceFile));
            Kind = kind ?? throw new ArgumentNullException(nameof(kind));
        }

        /// <summary>
        /// Path relative to `Application.streamingAssetsPath`.
        /// </summary>
        public string RelativePath { get; }

        /// <summary>
        /// Original repository-relative file path represented by this generated runtime file.
        /// </summary>
        public string SourceFile { get; }

        /// <summary>
        /// File role such as `sentis` or `source`.
        /// </summary>
        public string Kind { get; }
    }
}
