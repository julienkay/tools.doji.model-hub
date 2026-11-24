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
        public const string DefaultConversionPipelineVersion = "unity-inference-editor-v3";

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
    }
}
