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

        /// <summary>
        /// Creates an immutable model dependency definition from one manifest entry.
        /// </summary>
        /// <param name="id">Canonical HuggingFace model identifier, matching Hub `from_pretrained(...)` style source naming.</param>
        /// <param name="revision">Branch, tag, or commit to resolve from the Hub.</param>
        /// <param name="files">Optional exact repo-relative file selection. When omitted, the whole repository is required.</param>
        public ModelDependencyDefinition(
            string id,
            string revision,
            IReadOnlyList<string> files = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Revision = revision ?? throw new ArgumentNullException(nameof(revision));
            _files = NormalizeSelectedFiles(files);
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
        /// Empty means the whole repository is required.
        /// </summary>
        public IReadOnlyList<string> Files => _files;

        /// <summary>
        /// Whether this dependency selects an explicit subset of repository files instead of the whole repository.
        /// </summary>
        public bool HasExplicitFileSelection => _files.Length > 0;

        /// <summary>
        /// Effective branch, tag, or commit after applying manifest defaulting rules for omitted revisions.
        /// </summary>
        public string EffectiveRevision => ManifestRevision.NormalizeForResolution(Revision);

        internal string DependencySelectionHash => ComputeSelectionHash(_files);

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

        internal static string ComputeSelectionHash(IReadOnlyList<string> files)
        {
            var normalizedFiles = NormalizeSelectedFiles(files);
            if (normalizedFiles.Length == 0)
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

        private static string ComputeTextHash(string text)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(text ?? string.Empty));
            return string.Concat(bytes.Select(value => value.ToString("x2")));
        }
    }
}
