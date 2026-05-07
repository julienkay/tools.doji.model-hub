using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// Describes one canonical HuggingFace model id that a Unity package requires from the Hub.
    /// </summary>
    [Serializable]
    public sealed class ModelDependencyDefinition
    {
        private readonly string[] _files;
        private readonly string[] _optionalFiles;

        /// <summary>
        /// Creates an immutable model dependency definition from one manifest entry.
        /// </summary>
        /// <param name="id">Canonical HuggingFace model identifier, matching Hub `from_pretrained(...)` style source naming.</param>
        /// <param name="revision">Branch, tag, or commit to resolve from the Hub.</param>
        /// <param name="files">Optional exact repo-relative required file selection. When omitted alongside <paramref name="optionalFiles"/>, the whole repository is required.</param>
        /// <param name="optionalFiles">Optional exact repo-relative file paths that project owners may enable individually.</param>
        public ModelDependencyDefinition(
            string id,
            string revision,
            IReadOnlyList<string> files = null,
            IReadOnlyList<string> optionalFiles = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Revision = revision ?? throw new ArgumentNullException(nameof(revision));
            _files = NormalizeSelectedFiles(files);
            _optionalFiles = NormalizeSelectedFiles(optionalFiles);
        }

        /// <summary>
        /// Canonical HuggingFace model identifier used for generated paths, cache snapshots, and runtime lookup.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Branch, tag, or commit to resolve before downloading.
        /// </summary>
        public string Revision { get; }

        /// <summary>
        /// Exact repo-relative file paths required by this dependency.
        /// Empty can still mean optional files are declared separately.
        /// </summary>
        public IReadOnlyList<string> Files => _files;

        /// <summary>
        /// Exact repo-relative file paths a project owner may opt into for this dependency.
        /// </summary>
        public IReadOnlyList<string> OptionalFiles => _optionalFiles;

        /// <summary>
        /// Whether this dependency declares any explicit required or optional repository file paths.
        /// </summary>
        public bool HasExplicitFileSelection => _files.Length > 0 || _optionalFiles.Length > 0;

        /// <summary>
        /// Whether this dependency still means "whole repository" instead of an explicit required/optional file set.
        /// </summary>
        public bool SelectsWholeRepository => !HasExplicitFileSelection;

        /// <summary>
        /// Effective branch, tag, or commit after applying manifest defaulting rules for omitted revisions.
        /// </summary>
        public string EffectiveRevision => ManifestRevision.NormalizeForResolution(Revision);

        internal string DependencySelectionHash => ComputeSelectionHash(_files, SelectsWholeRepository);

        internal static string[] NormalizeSelectedFiles(IReadOnlyList<string> files)
        {
            return (files ?? Array.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => NormalizeRepoPath(path))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }

        internal static string NormalizeRepoPath(string repoPath)
        {
            return string.IsNullOrWhiteSpace(repoPath)
                ? null
                : repoPath.Trim().Replace('\\', '/');
        }

        internal static string ComputeSelectionHash(IReadOnlyList<string> files, bool selectsWholeRepository = false)
        {
            var normalizedFiles = NormalizeSelectedFiles(files);
            if (selectsWholeRepository)
            {
                return ComputeTextHash("all\n");
            }

            var builder = new StringBuilder();
            builder.AppendLine("selected");
            foreach (var repoPath in normalizedFiles)
            {
                builder.AppendLine(repoPath);
            }

            return ComputeTextHash(builder.ToString());
        }

        internal static bool IsOnnxFile(string repoPath)
        {
            return string.Equals(
                Path.GetExtension(repoPath),
                ".onnx",
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns true for git/Hub repo-hygiene files that have no runtime value in Unity and
        /// should be excluded from the Model Hub UI, size calculations, and import.
        /// The raw cache still contains these files so it remains a faithful mirror of the Hub.
        /// </summary>
        internal static bool IsRepoMetadataFile(string repoPath)
        {
            if (string.IsNullOrEmpty(repoPath))
            {
                return false;
            }

            var fileName = Path.GetFileName(repoPath);
            return string.Equals(fileName, ".gitattributes", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fileName, ".gitignore", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fileName, "README.md", StringComparison.OrdinalIgnoreCase)
                || (fileName.StartsWith("README_", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(Path.GetExtension(fileName), ".md", StringComparison.OrdinalIgnoreCase));
        }

        private static string ComputeTextHash(string text)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(text ?? string.Empty));
            return string.Concat(bytes.Select(value => value.ToString("x2")));
        }
    }
}
