using Doji.AI.ModelHub.Editor.Bridge;
using Unity.InferenceEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Editor ONNX conversion implementation backed by Unity Inference editor internals.
    /// </summary>
    internal sealed class HfHubEditorOnnxModelConverter : IHfHubOnnxModelConverter
    {
        /// <summary>
        /// Converts one ONNX file on disk into a Sentis model.
        /// </summary>
        public Model ConvertFromFile(string onnxFilePath)
        {
            return HfHubOnnxModelConverterBridge.ConvertFromFile(onnxFilePath);
        }
    }
}
