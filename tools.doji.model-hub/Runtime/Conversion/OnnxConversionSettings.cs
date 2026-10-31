using System;
using System.Collections.Generic;
using System.Linq;
using Unity.InferenceEngine;

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// Applies conversion settings to a converted model through public Inference Engine APIs, so the Editor converter
    /// and the runtime ONNX loader produce the same result.
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
        /// Fixes input dimensions and quantizes weights, and returns the resulting model: quantization builds a new
        /// model, so always continue with the return value. Settings that do not match the model are skipped and
        /// described in <paramref name="warnings"/>.
        /// </summary>
        public static Model Apply(Model model, OnnxConversionRecipe recipe, ICollection<string> warnings = null)
        {
            warnings ??= new List<string>();
            if (model == null || recipe == null)
            {
                return model;
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

            var mode = NormalizeQuantizationMode(recipe.QuantizationMode);
            var weightBytesBefore = GetWeightBytes(model);
            switch (mode)
            {
                case "float16":
                    ModelQuantizer.QuantizeWeights(QuantizationType.Float16, ref model);
                    break;
                case "uint8":
                    ModelQuantizer.QuantizeWeights(QuantizationType.Uint8, ref model);
                    break;
            }

            var warning = DescribeQuantizationEffect(mode, weightBytesBefore, GetWeightBytes(model));
            if (warning != null)
            {
                warnings.Add(warning);
            }

            return model;
        }

        /// <summary>
        /// Returns the bytes of all constants of a converted model, as it is saved.
        /// </summary>
        public static long GetWeightBytes(Model model)
        {
            return model?.constants?.Sum(constant => (long)constant.lengthBytes) ?? 0;
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
