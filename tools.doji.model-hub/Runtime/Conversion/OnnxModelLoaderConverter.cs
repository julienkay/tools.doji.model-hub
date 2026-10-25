#if MODEL_HUB_ONNX_MODEL_LOADER
using UnityEngine;

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// Converts ONNX files in players with <c>tools.doji.onnx-model-loader</c>, which aims to produce the same model as
    /// the Editor importer. Compiled only when that package is installed.
    /// </summary>
    internal sealed class OnnxModelLoaderConverter : IOnnxModelConverter
    {
        public const string PipelineName = "doji-onnx-model-loader";

        public OnnxConversionResult Convert(OnnxConversionRequest request)
        {
            return new OnnxConversionResult(
                Doji.AI.OnnxModelLoader.Load(request.OnnxFilePath),
                request.Recipe,
                PipelineName);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            // The build records this converter's version in the registry, so conversions are keyed by it.
            RuntimeModels.OnnxConverter ??= new OnnxModelLoaderConverter();
        }
    }
}
#endif
