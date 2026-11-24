namespace Doji.AI.ModelHub
{
    /// <summary>
    /// Converts ONNX files to .sentis files. AI Model Hub uses <c>tools.doji.onnx-model-loader</c>, and
    /// <see cref="Hub.RegisterOnnxConverter"/> can set another converter.
    /// </summary>
    public interface IOnnxModelConverter
    {
        /// <summary>
        /// Converts one ONNX file on disk with the recipe of the request, and writes the model to
        /// <paramref name="sentisPath"/>.
        /// </summary>
        OnnxConversionResult Convert(OnnxConversionRequest request, string sentisPath);
    }
}
