using System;
using System.Collections.Generic;
using System.Linq;
using Unity.InferenceEngine;

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// Describes one ONNX conversion request so editor and future runtime paths can share one contract.
    /// </summary>
    [Serializable]
    public sealed class OnnxConversionRequest
    {
        /// <summary>
        /// Creates immutable conversion input for one ONNX file.
        /// </summary>
        public OnnxConversionRequest(string onnxFilePath, OnnxConversionRecipe recipe = null)
        {
            OnnxFilePath = onnxFilePath ?? throw new ArgumentNullException(nameof(onnxFilePath));
            Recipe = recipe ?? new OnnxConversionRecipe();
        }

        /// <summary>
        /// Absolute source ONNX path on local disk.
        /// </summary>
        public string OnnxFilePath { get; }

        /// <summary>
        /// Effective recipe applied when generating runtime Sentis output.
        /// </summary>
        public OnnxConversionRecipe Recipe { get; }
    }

    /// <summary>
    /// Captures normalized output of one ONNX conversion attempt.
    /// </summary>
    [Serializable]
    public sealed class OnnxConversionResult
    {
        private readonly OnnxConversionDiagnostic[] _diagnostics;

        /// <summary>
        /// Creates immutable conversion result including diagnostics and discovered signature data.
        /// </summary>
        public OnnxConversionResult(
            Model model,
            OnnxConversionRecipe appliedRecipe,
            string conversionPipelineVersion,
            OnnxModelSignature inputSignature = null,
            IReadOnlyList<OnnxConversionDiagnostic> diagnostics = null)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
            AppliedRecipe = appliedRecipe ?? new OnnxConversionRecipe();
            ConversionPipelineVersion = string.IsNullOrWhiteSpace(conversionPipelineVersion)
                ? "unknown"
                : conversionPipelineVersion;
            InputSignature = inputSignature;
            _diagnostics = diagnostics?.ToArray() ?? Array.Empty<OnnxConversionDiagnostic>();
        }

        /// <summary>
        /// Converted Sentis model ready to serialize.
        /// </summary>
        public Model Model { get; }

        /// <summary>
        /// Recipe actually applied by converter.
        /// </summary>
        public OnnxConversionRecipe AppliedRecipe { get; }

        /// <summary>
        /// Converter pipeline fingerprint used for stale detection.
        /// </summary>
        public string ConversionPipelineVersion { get; }

        /// <summary>
        /// Lightweight model signature discovered during conversion or inspection.
        /// </summary>
        public OnnxModelSignature InputSignature { get; }

        /// <summary>
        /// Conversion diagnostics that should survive later status refreshes.
        /// </summary>
        public IReadOnlyList<OnnxConversionDiagnostic> Diagnostics => _diagnostics;
    }

    /// <summary>
    /// Stores conversion settings that materially affect generated Sentis output.
    /// </summary>
    [Serializable]
    public sealed class OnnxConversionRecipe
    {
        private OnnxStaticInputShapeOverride[] _staticInputShapeOverrides = Array.Empty<OnnxStaticInputShapeOverride>();

        /// <summary>
        /// Quantization mode requested for generated runtime model.
        /// </summary>
        public string QuantizationMode { get; set; } = "none";

        /// <summary>
        /// Optional static shape overrides keyed by model input name.
        /// </summary>
        public OnnxStaticInputShapeOverride[] StaticInputShapeOverrides
        {
            get => _staticInputShapeOverrides;
            set => _staticInputShapeOverrides = value ?? Array.Empty<OnnxStaticInputShapeOverride>();
        }

        /// <summary>
        /// Returns a deep copy, so callers cannot change a recipe another object holds.
        /// </summary>
        public OnnxConversionRecipe Clone()
        {
            return new OnnxConversionRecipe
            {
                QuantizationMode = QuantizationMode,
                StaticInputShapeOverrides = StaticInputShapeOverrides
                    .Where(item => item != null)
                    .Select(item => new OnnxStaticInputShapeOverride
                    {
                        InputName = item.InputName,
                        Dimensions = item.Dimensions.ToArray()
                    })
                    .ToArray()
            };
        }
    }

    /// <summary>
    /// Overrides one input shape when dynamic ONNX dimensions need project-specific concrete values.
    /// </summary>
    [Serializable]
    public sealed class OnnxStaticInputShapeOverride
    {
        private int[] _dimensions = Array.Empty<int>();

        /// <summary>
        /// Input name as declared by ONNX graph.
        /// </summary>
        public string InputName { get; set; }

        /// <summary>
        /// Full static dimension list to apply for this input.
        /// </summary>
        public int[] Dimensions
        {
            get => _dimensions;
            set => _dimensions = value ?? Array.Empty<int>();
        }
    }

    /// <summary>
    /// Stores one conversion warning or error in runtime-safe form.
    /// </summary>
    [Serializable]
    public sealed class OnnxConversionDiagnostic
    {
        /// <summary>
        /// Severity string such as `warning` or `error`.
        /// </summary>
        public string Severity { get; set; }

        /// <summary>
        /// User-visible diagnostic message.
        /// </summary>
        public string Message { get; set; }
    }

    /// <summary>
    /// Describes lightweight signature data discovered for one ONNX model.
    /// </summary>
    [Serializable]
    public sealed class OnnxModelSignature
    {
        private OnnxModelInputSignature[] _inputs = Array.Empty<OnnxModelInputSignature>();

        /// <summary>
        /// Input signatures discovered for model graph.
        /// </summary>
        public OnnxModelInputSignature[] Inputs
        {
            get => _inputs;
            set => _inputs = value ?? Array.Empty<OnnxModelInputSignature>();
        }
    }

    /// <summary>
    /// Describes one model input in lightweight string form for UI and stale hashing.
    /// </summary>
    [Serializable]
    public sealed class OnnxModelInputSignature
    {
        /// <summary>
        /// Input tensor name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Element type summary when known.
        /// </summary>
        public string ElementType { get; set; }

        /// <summary>
        /// Human-readable shape summary when known.
        /// </summary>
        public string ShapeSummary { get; set; }
    }
}
