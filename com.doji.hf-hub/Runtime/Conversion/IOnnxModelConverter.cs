using Unity.InferenceEngine;

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// Converts ONNX source files into Sentis models so HF Hub can share one conversion contract across editor and future runtime paths.
    /// </summary>
    public interface IOnnxModelConverter
    {
        /// <summary>
        /// Converts one ONNX file on disk into a Sentis model.
        /// </summary>
        Model ConvertFromFile(string onnxFilePath);
    }
}
