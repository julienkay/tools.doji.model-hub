using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// What AI Model Hub does with one repository file. The extension decides it; repositories have no format of their own.
    /// </summary>
    public enum ModelFileFormat
    {
        /// <summary>
        /// Not a model file, for example a config, tokenizer, or ONNX external weights.
        /// </summary>
        None,

        /// <summary>
        /// An ONNX model. AI Model Hub converts it to a Sentis model.
        /// </summary>
        Onnx,

        /// <summary>
        /// A Sentis model. AI Model Hub loads it as downloaded.
        /// </summary>
        Sentis
    }

    /// <summary>
    /// Which model formats a repository contains, for labels before anything is downloaded.
    /// </summary>
    public enum RepositoryModelFormat
    {
        None,
        Onnx,
        Sentis,
        Mixed
    }

    /// <summary>
    /// Rules for model files: format by extension, ONNX companion files, and repository labels.
    /// </summary>
    public static class ModelFiles
    {
        private static readonly string[] OnnxExternalDataSuffixes = { ".onnx_data", ".onnx.data" };

        /// <summary>
        /// Returns the format of one repository file from its extension.
        /// </summary>
        public static ModelFileFormat GetFormat(string repoPath)
        {
            var extension = Path.GetExtension(repoPath ?? string.Empty);
            if (string.Equals(extension, ".onnx", StringComparison.OrdinalIgnoreCase))
            {
                return ModelFileFormat.Onnx;
            }

            return string.Equals(extension, ".sentis", StringComparison.OrdinalIgnoreCase)
                ? ModelFileFormat.Sentis
                : ModelFileFormat.None;
        }

        public static bool IsModelFile(string repoPath)
        {
            return GetFormat(repoPath) != ModelFileFormat.None;
        }

        /// <summary>
        /// Returns the repository files an ONNX model cannot load without: its external weights, named
        /// <c>model.onnx_data</c>, <c>model.onnx.data</c>, or numbered parts such as <c>model.onnx_data_1</c>, in the same
        /// folder. Exporters and <c>transformers.js</c> use these names.
        /// </summary>
        public static IReadOnlyList<string> GetCompanionFiles(string onnxRepoPath, IEnumerable<string> repositoryFiles)
        {
            if (GetFormat(onnxRepoPath) != ModelFileFormat.Onnx || repositoryFiles == null)
            {
                return Array.Empty<string>();
            }

            var prefixes = OnnxExternalDataSuffixes
                .Select(suffix => onnxRepoPath.Substring(0, onnxRepoPath.Length - ".onnx".Length) + suffix)
                .ToArray();
            return repositoryFiles
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Where(path => prefixes.Any(prefix =>
                    string.Equals(path, prefix, StringComparison.Ordinal) ||
                    (path.StartsWith(prefix + "_", StringComparison.Ordinal) && path.Length > prefix.Length + 1)))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Returns which model formats a set of repository files contains.
        /// </summary>
        public static RepositoryModelFormat Describe(IEnumerable<string> repositoryFiles)
        {
            var formats = (repositoryFiles ?? Array.Empty<string>())
                .Select(GetFormat)
                .Where(format => format != ModelFileFormat.None)
                .Distinct()
                .ToArray();
            return formats.Length switch
            {
                0 => RepositoryModelFormat.None,
                1 => formats[0] == ModelFileFormat.Onnx ? RepositoryModelFormat.Onnx : RepositoryModelFormat.Sentis,
                _ => RepositoryModelFormat.Mixed
            };
        }

        /// <summary>
        /// A short label for a repository format, for example "ONNX + Sentis".
        /// </summary>
        public static string GetLabel(RepositoryModelFormat format)
        {
            return format switch
            {
                RepositoryModelFormat.Onnx => "ONNX",
                RepositoryModelFormat.Sentis => "Sentis",
                RepositoryModelFormat.Mixed => "ONNX + Sentis",
                _ => "No model files"
            };
        }
    }
}
