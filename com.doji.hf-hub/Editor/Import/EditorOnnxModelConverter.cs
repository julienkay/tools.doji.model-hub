using System;
using Doji.AI.ModelHub.Editor.Bridge;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Editor ONNX conversion implementation backed by Unity Inference editor internals.
    /// </summary>
    internal sealed class EditorOnnxModelConverter : IOnnxModelConverter
    {
        /// <summary>
        /// Converts one ONNX file on disk into a Sentis model.
        /// </summary>
        public OnnxConversionResult Convert(OnnxConversionRequest request)
        {
            var effectiveRequest = request ?? throw new ArgumentNullException(nameof(request));
            return new OnnxConversionResult(
                OnnxModelConverterBridge.ConvertFromFile(effectiveRequest.OnnxFilePath),
                effectiveRequest.Recipe,
                OnnxImportMetadataUtility.DefaultConversionPipelineVersion);
        }
    }
}
