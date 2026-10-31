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
    public sealed class ModelDependencyFileDefinition
    {
        public ModelDependencyFileDefinition(
            string path,
            bool optional = false,
            string label = null,
            OnnxConversionRecipe conversion = null)
        {
            Path = NormalizePath(path);
            Optional = optional;
            Label = NormalizeLabel(label);
            Conversion = conversion?.Clone();
        }

        /// <summary>
        /// Exact repository-relative file path selected by this dependency.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Whether the selected file is project-selectable rather than always required.
        /// </summary>
        public bool Optional { get; }

        /// <summary>
        /// Name to show for the file in model pickers, for example <c>Small (63 MB)</c>. Null when the manifest sets none.
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// Conversion settings the manifest owner recommends for this ONNX file, such as quantization. Null when the
        /// manifest sets none. A project override replaces them.
        /// </summary>
        public OnnxConversionRecipe Conversion { get; }

        internal static string NormalizePath(string path)
        {
            return ModelDependencyDefinition.NormalizeRepoPath(path);
        }

        internal static string NormalizeLabel(string label)
        {
            return string.IsNullOrWhiteSpace(label) ? null : label.Trim();
        }
    }

    /// <summary>
    /// Describes one exact file selected by a model dependency manifest entry.
    /// </summary>
    [Serializable]
    public sealed class ModelDependencyDefinition
    {
        private readonly ModelDependencyFileDefinition[] _fileEntries;

        /// <summary>
        /// Creates an immutable model dependency definition from one manifest entry.
        /// </summary>
        /// <param name="id">Canonical HuggingFace model identifier, matching Hub `from_pretrained(...)` style source naming.</param>
        /// <param name="revision">Branch, tag, or commit to resolve from the Hub.</param>
        /// <param name="files">Optional exact repo-relative selected files. Optional entries are project-selectable; required entries are always selected.</param>
        public ModelDependencyDefinition(
            string id,
            string revision,
            IReadOnlyList<ModelDependencyFileDefinition> files = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Revision = revision ?? throw new ArgumentNullException(nameof(revision));
            _fileEntries = NormalizeFileEntries(files);
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
        /// Exact repo-relative file selections, with optional flag stored beside each path.
        /// Empty means the whole repository is selected.
        /// </summary>
        public IReadOnlyList<ModelDependencyFileDefinition> FileEntries => _fileEntries;

        /// <summary>
        /// Whether this dependency declares any explicit required or optional repository file paths.
        /// </summary>
        public bool HasExplicitFileSelection => _fileEntries.Length > 0;

        /// <summary>
        /// Whether this dependency still means "whole repository" instead of an explicit required/optional file set.
        /// </summary>
        public bool SelectsWholeRepository => !HasExplicitFileSelection;

        /// <summary>
        /// Effective branch, tag, or commit after applying manifest defaulting rules for omitted revisions.
        /// </summary>
        public string EffectiveRevision => ManifestRevision.NormalizeForResolution(Revision);

        internal string DependencySelectionHash => ComputeSelectionHash(
            _fileEntries.Where(file => !file.Optional).Select(file => file.Path).ToArray(),
            SelectsWholeRepository);

        internal static ModelDependencyFileDefinition[] NormalizeFileEntries(IReadOnlyList<ModelDependencyFileDefinition> files)
        {
            return (files ?? Array.Empty<ModelDependencyFileDefinition>())
                .Where(file => !string.IsNullOrWhiteSpace(file?.Path))
                .Select(file => new ModelDependencyFileDefinition(file.Path, file.Optional, file.Label, file.Conversion))
                .GroupBy(file => file.Path, StringComparer.Ordinal)
                .Select(group => group.Last())
                .OrderBy(file => file.Path, StringComparer.Ordinal)
                .ToArray();
        }

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
