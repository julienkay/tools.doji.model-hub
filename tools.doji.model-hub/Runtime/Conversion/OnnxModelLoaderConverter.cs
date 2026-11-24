using System.Collections.Generic;
using System.Linq;

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// Converts ONNX files with <c>tools.doji.onnx-model-loader</c>, in the Editor and in players. The loader reads
    /// each weight, quantizes it, and writes it to the .sentis file one at a time, so a conversion never holds all
    /// weights in memory, quantized or not.
    /// </summary>
    internal sealed class OnnxModelLoaderConverter : IOnnxModelConverter
    {
        /// <summary>
        /// The first part of the converter id. The suffix changes when the output of this converter changes, so models
        /// convert again.
        /// </summary>
        public const string PipelineName = "doji-onnx-model-loader-v3";

        public OnnxConversionResult Convert(OnnxConversionRequest request, string sentisPath)
        {
            var recipe = request.Recipe;
            var mode = OnnxConversionSettings.NormalizeQuantizationMode(recipe.QuantizationMode);
            var warnings = new List<string>();
            long importedWeightBytes = 0, convertedWeightBytes = 0;
            OnnxModelLoader.Save(request.OnnxFilePath, sentisPath, OnnxConversionSettings.GetQuantizationType(mode), model =>
            {
                OnnxConversionSettings.ApplyInputShapes(model, recipe, warnings);
                convertedWeightBytes = OnnxConversionSettings.GetWeightBytes(model);
                importedWeightBytes = mode == "none" ? convertedWeightBytes : OnnxConversionSettings.GetFloatWeightBytes(model);
            });

            var effect = OnnxConversionSettings.DescribeQuantizationEffect(mode, importedWeightBytes, convertedWeightBytes);
            if (effect != null)
            {
                warnings.Add(effect);
            }

            return new OnnxConversionResult(
                recipe,
                PipelineName,
                importedWeightBytes,
                convertedWeightBytes,
                warnings.Select(warning => new OnnxConversionDiagnostic { Severity = "warning", Message = warning }).ToArray());
        }
    }
}
