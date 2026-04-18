using System;
using Unity.InferenceEngine;
using Unity.InferenceEngine.Editor.Onnx;

namespace Doji.AI.ModelHub.Editor.Bridge
{
    /// <summary>
    /// Exposes Unity Inference internal ONNX conversion through a small public bridge compiled into the editor assembly.
    /// </summary>
    public static class HfHubOnnxModelConverterBridge
    {
        /// <summary>
        /// Converts one ONNX file on disk into a Sentis `Model` without importing that ONNX file as a Unity asset.
        /// </summary>
        public static Model ConvertFromFile(string onnxFilePath)
        {
            if (string.IsNullOrWhiteSpace(onnxFilePath))
            {
                throw new ArgumentException("ONNX file path is required.", nameof(onnxFilePath));
            }

            var converter = new ONNXModelConverter(onnxFilePath);
            return converter.Convert();
        }
    }
}
