using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

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
                var model = ParseOnnxModel(onnxFilePath);
                var graph = GetPropertyValue(model, "Graph");
                if (graph == null)
                {
                    return null;
                }

                var initializerNames = new HashSet<string>(
                    GetEnumerable(graph, "Initializer")
                        .Select(initializer => GetStringProperty(initializer, "Name"))
                        .Where(name => !string.IsNullOrWhiteSpace(name)),
                    StringComparer.Ordinal);
                var inputs = new List<OnnxInspectedInput>();

                foreach (var input in GetEnumerable(graph, "Input"))
                {
                    var inputName = GetStringProperty(input, "Name");
                    if (initializerNames.Contains(inputName))
                    {
                        continue;
                    }

                    var dims = GetEnumerable(GetPropertyValue(GetPropertyValue(GetPropertyValue(input, "Type"), "TensorType"), "Shape"), "Dim")
                        .Select((dim, index) => CreateDimension(inputName, index, dim))
                        .ToArray();
                    inputs.Add(new OnnxInspectedInput(inputName, dims));
                }

                return new OnnxInspectedModel(inputs);
            }
            catch
            {
                return null;
            }
        }

        private static object ParseOnnxModel(string onnxFilePath)
        {
            var modelType = Type.GetType("Unity.InferenceEngine.Editor.Onnx.ModelProto, Unity.InferenceEngine.Editor", throwOnError: true);
            var parser = GetPropertyValue(null, modelType, "Parser");
            var parseFrom = parser?.GetType().GetMethod("ParseFrom", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(byte[]) }, null);
            return parseFrom?.Invoke(parser, new object[] { File.ReadAllBytes(onnxFilePath) });
        }

        private static OnnxInspectedDimension CreateDimension(string inputName, int index, object dim)
        {
            var dimParam = GetStringProperty(dim, "DimParam");
            var valueCase = GetPropertyValue(dim, "ValueCase")?.ToString();
            var label = !string.IsNullOrWhiteSpace(dimParam)
                ? dimParam
                : $"{inputName}[{index}]";

            if (string.Equals(valueCase, "DimValue", StringComparison.Ordinal))
            {
                var rawValue = GetPropertyValue(dim, "DimValue");
                var staticValue = rawValue is long longValue
                    ? (int)Math.Min(longValue, int.MaxValue)
                    : Convert.ToInt32(rawValue);
                return new OnnxInspectedDimension(label, index, staticValue, false, !string.IsNullOrWhiteSpace(dimParam));
            }

            return new OnnxInspectedDimension(label, index, -1, true, !string.IsNullOrWhiteSpace(dimParam));
        }

        private static IEnumerable<object> GetEnumerable(object instance, string propertyName)
        {
            return (GetPropertyValue(instance, propertyName) as IEnumerable)?.Cast<object>() ?? Array.Empty<object>();
        }

        private static string GetStringProperty(object instance, string propertyName)
        {
            return GetPropertyValue(instance, propertyName) as string;
        }

        private static object GetPropertyValue(object instance, string propertyName)
        {
            return GetPropertyValue(instance, instance?.GetType(), propertyName);
        }

        private static object GetPropertyValue(object instance, Type type, string propertyName)
        {
            var property = type?.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            return property?.GetValue(instance);
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
