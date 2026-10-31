using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OnnxModelSignatureReader = Doji.ONNX.OnnxModelSignatureReader;
using LoaderOnnxModelDimension = Doji.ONNX.OnnxModelDimension;
using LoaderOnnxModelInputSignature = Doji.ONNX.OnnxModelInputSignature;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Reads lightweight ONNX metadata: inputs for the shape settings, and weight types and operators to check the
    /// source before conversion. It never reads weight data.
    /// </summary>
    internal static class OnnxModelInspector
    {
        /// <summary>
        /// Quantization operators the Inference Engine ONNX importer cannot import (2.6). A graph with any of them
        /// fails to convert, whatever the conversion settings.
        /// </summary>
        internal static readonly string[] UnsupportedQuantizationOperators =
        {
            "ConvInteger",
            "DequantizeLinear",
            "DynamicQuantizeLinear",
            "GatherBlockQuantized",
            "MatMulInteger",
            "MatMulIntegerToFloat",
            "MatMulNBits",
            "QAttention",
            "QGemm",
            "QLinearAdd",
            "QLinearConv",
            "QLinearMatMul",
            "QLinearMul",
            "QuantizeLinear"
        };

        public static OnnxInspectedModel Inspect(string onnxFilePath)
        {
            if (string.IsNullOrWhiteSpace(onnxFilePath) || !File.Exists(onnxFilePath))
            {
                return null;
            }

            try
            {
                var signature = OnnxModelSignatureReader.ReadSignature(onnxFilePath);
                var inputs = (signature.Inputs ?? Array.Empty<LoaderOnnxModelInputSignature>())
                    .Where(input => input != null)
                    .Select(input => new OnnxInspectedInput(
                        input.Name,
                        (input.Dimensions ?? Array.Empty<LoaderOnnxModelDimension>())
                            .Select(dimension => CreateDimension(input.Name, dimension))
                            .ToArray()))
                    .ToArray();
                return new OnnxInspectedModel(
                    inputs,
                    (signature.WeightBytes ?? Array.Empty<Doji.ONNX.OnnxWeightBytes>())
                        .Where(weight => weight != null && weight.Bytes > 0)
                        .Select(weight => new OnnxInspectedWeights(weight.DataType.ToString(), weight.Bytes))
                        .ToArray(),
                    (signature.OperatorTypes ?? Array.Empty<string>())
                        .Where(op => UnsupportedQuantizationOperators.Contains(op, StringComparer.Ordinal))
                        .ToArray());
            }
            catch
            {
                return null;
            }
        }

        private static OnnxInspectedDimension CreateDimension(string inputName, LoaderOnnxModelDimension dimension)
        {
            var dimParam = dimension.DimParam;
            var label = !string.IsNullOrWhiteSpace(dimParam)
                ? dimParam
                : $"{inputName}[{dimension.Index}]";

            if (dimension.DimValue.HasValue)
            {
                var staticValue = (int)Math.Min(dimension.DimValue.Value, int.MaxValue);
                return new OnnxInspectedDimension(label, dimension.Index, staticValue, false, !string.IsNullOrWhiteSpace(dimParam));
            }

            return new OnnxInspectedDimension(label, dimension.Index, -1, true, !string.IsNullOrWhiteSpace(dimParam));
        }
    }

    /// <summary>
    /// Holds inspected ONNX input metadata for one file.
    /// </summary>
    internal sealed class OnnxInspectedModel
    {
        private readonly OnnxInspectedInput[] _inputs;
        private readonly OnnxInspectedWeights[] _weights;
        private readonly string[] _unsupportedOperators;

        public OnnxInspectedModel(
            IReadOnlyList<OnnxInspectedInput> inputs,
            IReadOnlyList<OnnxInspectedWeights> weights = null,
            IReadOnlyList<string> unsupportedOperators = null)
        {
            _inputs = inputs?.ToArray() ?? Array.Empty<OnnxInspectedInput>();
            _weights = weights?.OrderByDescending(weight => weight.Bytes).ToArray() ?? Array.Empty<OnnxInspectedWeights>();
            _unsupportedOperators = unsupportedOperators?.ToArray() ?? Array.Empty<string>();
        }

        public IReadOnlyList<OnnxInspectedInput> Inputs => _inputs;

        /// <summary>
        /// Initializer bytes per ONNX data type, largest first.
        /// </summary>
        public IReadOnlyList<OnnxInspectedWeights> Weights => _weights;

        /// <summary>
        /// Operators of the graph that Inference Engine cannot import, from <see cref="OnnxModelInspector.UnsupportedQuantizationOperators"/>.
        /// </summary>
        public IReadOnlyList<string> UnsupportedOperators => _unsupportedOperators;

        /// <summary>
        /// True when float16 holds most initializer bytes. The importer expands float16 to float32, so such a model
        /// converts to about twice its size unless it is quantized to float16.
        /// </summary>
        public bool IsMostlyFloat16
        {
            get
            {
                var total = _weights.Sum(weight => weight.Bytes);
                var float16 = _weights.Where(weight => weight.DataType == "Float16").Sum(weight => weight.Bytes);
                return total > 0 && float16 * 2 > total;
            }
        }

        /// <summary>
        /// Message for a graph that cannot be converted because of quantized operators, or null.
        /// </summary>
        public string DescribeUnsupportedOperators()
        {
            return _unsupportedOperators.Length == 0
                ? null
                : "it uses quantized ONNX operators that Inference Engine cannot import " +
                    $"({string.Join(", ", _unsupportedOperators.Select(op => $"`{op}`"))}). " +
                    "Select the float model of the repository, or a `.sentis` file.";
        }
        public int DynamicDimensionCount => _inputs.Sum(input => input.Dimensions.Count(dimension => dimension.IsDynamicByDefault));
    }

    /// <summary>
    /// Initializer bytes of one ONNX data type, for example <c>Float16</c>.
    /// </summary>
    internal sealed class OnnxInspectedWeights
    {
        public OnnxInspectedWeights(string dataType, long bytes)
        {
            DataType = dataType;
            Bytes = bytes;
        }

        public string DataType { get; }
        public long Bytes { get; }
    }

    /// <summary>
    /// Holds inspected metadata for one ONNX input tensor.
    /// </summary>
    internal sealed class OnnxInspectedInput
    {
        private readonly OnnxInspectedDimension[] _dimensions;

        public OnnxInspectedInput(string name, IReadOnlyList<OnnxInspectedDimension> dimensions)
        {
            Name = name;
            _dimensions = dimensions?.ToArray() ?? Array.Empty<OnnxInspectedDimension>();
        }

        public string Name { get; }
        public IReadOnlyList<OnnxInspectedDimension> Dimensions => _dimensions;
    }

    /// <summary>
    /// Holds inspected metadata for one ONNX input dimension.
    /// </summary>
    internal sealed class OnnxInspectedDimension
    {
        public OnnxInspectedDimension(string label, int index, int defaultValue, bool isDynamicByDefault, bool isNamed)
        {
            Label = label;
            Index = index;
            DefaultValue = defaultValue;
            IsDynamicByDefault = isDynamicByDefault;
            IsNamed = isNamed;
        }

        public string Label { get; }
        public int Index { get; }
        public int DefaultValue { get; }
        public bool IsDynamicByDefault { get; }
        public bool IsNamed { get; }
    }
}
