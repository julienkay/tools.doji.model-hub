using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Doji.AI;
using LoaderOnnxModelDimension = Doji.AI.OnnxModelDimension;
using LoaderOnnxModelInputSignature = Doji.AI.OnnxModelInputSignature;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Reads lightweight ONNX input metadata needed for importer-style shape configuration UI.
    /// </summary>
    internal static class OnnxModelInspector
    {
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
                return new OnnxInspectedModel(inputs);
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

        public OnnxInspectedModel(IReadOnlyList<OnnxInspectedInput> inputs)
        {
            _inputs = inputs?.ToArray() ?? Array.Empty<OnnxInspectedInput>();
        }

        public IReadOnlyList<OnnxInspectedInput> Inputs => _inputs;
        public int DynamicDimensionCount => _inputs.Sum(input => input.Dimensions.Count(dimension => dimension.IsDynamicByDefault));
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
