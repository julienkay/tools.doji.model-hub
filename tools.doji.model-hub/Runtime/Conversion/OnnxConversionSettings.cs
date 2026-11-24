using System;
using System.Collections.Generic;
using System.Linq;
using Unity.InferenceEngine;

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// Helpers for the conversion settings of ONNX files.
    /// </summary>
    public static class OnnxConversionSettings
    {
        /// <summary>
        /// Normalizes a quantization mode: <c>none</c>, <c>float16</c>, or <c>uint8</c>. The older name <c>int8</c> means
        /// <c>uint8</c>, the 8-bit quantization Inference Engine supports.
        /// </summary>
        public static string NormalizeQuantizationMode(string quantizationMode)
        {
            var mode = quantizationMode?.Trim().ToLowerInvariant();
            return mode switch
            {
                "float16" => "float16",
                "uint8" or "int8" => "uint8",
                _ => "none"
            };
        }

        /// <summary>
        /// Returns the quantization type of a quantization mode, or null for <c>none</c>.
        /// </summary>
        public static QuantizationType? GetQuantizationType(string quantizationMode)
        {
            return NormalizeQuantizationMode(quantizationMode) switch
            {
                "float16" => QuantizationType.Float16,
                "uint8" => QuantizationType.Uint8,
                _ => null
            };
        }

        /// <summary>
        /// Fixes the input dimensions that the recipe sets. Settings that do not match the model are skipped and
        /// described in <paramref name="warnings"/>.
        /// </summary>
        public static void ApplyInputShapes(Model model, OnnxConversionRecipe recipe, ICollection<string> warnings)
        {
            if (model == null || recipe == null)
            {
                return;
            }

            foreach (var shapeOverride in recipe.StaticInputShapeOverrides ?? Array.Empty<OnnxStaticInputShapeOverride>())
            {
                if (string.IsNullOrWhiteSpace(shapeOverride?.InputName))
                {
                    continue;
                }

                var index = model.inputs.FindIndex(input => string.Equals(input.name, shapeOverride.InputName, StringComparison.Ordinal));
                if (index < 0)
                {
                    warnings.Add($"Input shape setting for `{shapeOverride.InputName}` was ignored: the model has no input with that name.");
                    continue;
                }

                var input = model.inputs[index];
                var dimensions = shapeOverride.Dimensions ?? Array.Empty<int>();
                if (input.shape.isRankDynamic || input.shape.rank != dimensions.Length)
                {
                    warnings.Add($"Input shape setting for `{shapeOverride.InputName}` was ignored: it has {dimensions.Length} dimensions, the model input has {(input.shape.isRankDynamic ? "a dynamic rank" : input.shape.rank.ToString())}.");
                    continue;
                }

                if (dimensions.Any(value => value < -1))
                {
                    warnings.Add($"Input shape setting for `{shapeOverride.InputName}` was ignored: dimensions must be -1 or larger.");
                    continue;
                }

                input.shape = new DynamicTensorShape(dimensions.AsSpan());
                model.inputs[index] = input;
            }
        }

        /// <summary>
        /// Returns the bytes of all constants of a converted model, as it is saved.
        /// </summary>
        public static long GetWeightBytes(Model model)
        {
            return model?.constants?.Sum(constant => (long)constant.lengthBytes) ?? 0;
        }

        /// <summary>
        /// Returns the bytes of all constants as float32: quantized float16 and uint8 weights count four bytes each.
        /// </summary>
        public static long GetFloatWeightBytes(Model model)
        {
            return model?.constants?.Sum(constant => constant.dataType == DataType.Short || constant.dataType == DataType.Byte
                ? constant.shape.length * 4L
                : constant.lengthBytes) ?? 0;
        }

        /// <summary>
        /// Returns a warning when quantization was requested but changed no weight, or null. Inference Engine
        /// quantizes only float32 weights of <c>Conv</c>, <c>ConvTranspose</c>, <c>Gather</c>, <c>Dense</c>, and
        /// <c>MatMul</c> layers.
        /// </summary>
        public static string DescribeQuantizationEffect(string quantizationMode, long weightBytesBefore, long weightBytesAfter)
        {
            var mode = NormalizeQuantizationMode(quantizationMode);
            if (mode == "none" || weightBytesBefore <= 0 || weightBytesAfter < weightBytesBefore)
            {
                return null;
            }

            return $"Quantization `{mode}` changed no weights: the model has no float32 weights in layers Inference Engine " +
                "quantizes (Conv, ConvTranspose, Gather, Dense, MatMul).";
        }
    }
}
