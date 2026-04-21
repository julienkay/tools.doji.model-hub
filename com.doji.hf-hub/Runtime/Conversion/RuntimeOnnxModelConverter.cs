using System;
using Unity.InferenceEngine;

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// Placeholder runtime ONNX conversion entry point for future custom runtime-capable converter packages.
    /// </summary>
    public sealed class RuntimeOnnxModelConverter : IOnnxModelConverter
    {
        /// <summary>
        /// Converts one ONNX file on disk into a Sentis model.
        /// </summary>
        public Model ConvertFromFile(string onnxFilePath)
        {
            throw new NotImplementedException(
                "Runtime ONNX conversion is not implemented yet. Add a runtime-capable converter package and wire it into HF Hub.");
        }
    }
}
