using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Computes stable conversion fingerprints used by import metadata and stale detection.
    /// </summary>
    internal static class OnnxImportMetadataUtility
    {
        /// <summary>
        /// Converter id of the Editor import: Unity's Editor ONNX converter, followed by
        /// <see cref="OnnxConversionSettings.Apply"/>. Combined with the Inference Engine version in conversion keys.
        /// </summary>
        public const string DefaultConversionPipelineVersion = "unity-inference-editor-v2";

        public static OnnxConversionRecipe CreateDefaultRecipe()
        {
            return new OnnxConversionRecipe();
        }

        public static string ComputeRecipeHash(OnnxConversionRecipe recipe)
        {
            var effectiveRecipe = recipe ?? CreateDefaultRecipe();
            var builder = new StringBuilder();
            builder.Append(OnnxConversionSettings.NormalizeQuantizationMode(effectiveRecipe.QuantizationMode));
            builder.Append('\n');

            foreach (var shapeOverride in (effectiveRecipe.StaticInputShapeOverrides ?? Array.Empty<OnnxStaticInputShapeOverride>())
                         .OrderBy(item => item?.InputName, StringComparer.Ordinal))
            {
                if (shapeOverride == null)
                {
                    continue;
                }

                builder.Append(shapeOverride.InputName ?? string.Empty);
                builder.Append('=');
                builder.Append(string.Join(",", shapeOverride.Dimensions ?? Array.Empty<int>()));
                builder.Append('\n');
            }

            return TextHash.Sha256(builder.ToString());
        }

        public static string ComputeInputSignatureHash(OnnxModelSignature signature)
        {
            if (signature == null)
            {
                return null;
            }

            var builder = new StringBuilder();
            foreach (var input in (signature.Inputs ?? Array.Empty<OnnxModelInputSignature>())
                         .OrderBy(item => item?.Name, StringComparer.Ordinal))
            {
                if (input == null)
                {
                    continue;
                }

                builder.Append(input.Name ?? string.Empty);
                builder.Append('|');
                builder.Append(input.ElementType ?? string.Empty);
                builder.Append('|');
                builder.Append(input.ShapeSummary ?? string.Empty);
                builder.Append('\n');
            }

            return TextHash.Sha256(builder.ToString());
        }

        public static string ComputeRecipeSetHash(IEnumerable<string> repoPaths)
        {
            var defaultRecipeHash = ComputeRecipeHash(CreateDefaultRecipe());
            var builder = new StringBuilder();
            foreach (var repoPath in (repoPaths ?? Array.Empty<string>())
                         .Where(path => !string.IsNullOrWhiteSpace(path))
                         .Distinct(StringComparer.Ordinal)
                         .OrderBy(path => path, StringComparer.Ordinal))
            {
                builder.Append(repoPath);
                builder.Append(':');
                builder.Append(defaultRecipeHash);
                builder.Append('\n');
            }

            return TextHash.Sha256(builder.ToString());
        }

        public static string ComputeRecipeSetHash(IEnumerable<RawCacheFileMetadata> files)
        {
            return ComputeRecipeSetHash(
                (files ?? Array.Empty<RawCacheFileMetadata>())
                .Where(file => file != null && IsOnnxFile(file.RepoPath))
                .Select(file => file.RepoPath));
        }

        public static string ComputeRecipeSetHash(IEnumerable<ImportedRuntimeFileMetadata> files)
        {
            var builder = new StringBuilder();
            foreach (var file in (files ?? Array.Empty<ImportedRuntimeFileMetadata>())
                         .Where(file => file != null && string.Equals(file.Kind, "sentis", StringComparison.Ordinal))
                         .OrderBy(file => file.RepoPath, StringComparer.Ordinal))
            {
                builder.Append(file.RepoPath ?? string.Empty);
                builder.Append(':');
                builder.Append(file.RecipeHash ?? string.Empty);
                builder.Append('\n');
            }

            return TextHash.Sha256(builder.ToString());
        }

        private static bool IsOnnxFile(string repoPath)
        {
            return string.Equals(
                System.IO.Path.GetExtension(repoPath),
                ".onnx",
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
