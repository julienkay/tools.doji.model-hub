using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    internal sealed class OnnxModelInspectorTests
    {
        [Test]
        public void Inspect_MapsSignatureReaderInputs()
        {
            var onnxPath = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(onnxPath, BuildOnnxModel());

                var inspectedModel = OnnxModelInspector.Inspect(onnxPath);

                Assert.That(inspectedModel, Is.Not.Null);
                Assert.That(inspectedModel.Inputs.Count, Is.EqualTo(1));

                var input = inspectedModel.Inputs[0];
                Assert.That(input.Name, Is.EqualTo("input"));
                Assert.That(input.Dimensions.Count, Is.EqualTo(3));
                Assert.That(input.Dimensions[0].Label, Is.EqualTo("input[0]"));
                Assert.That(input.Dimensions[0].DefaultValue, Is.EqualTo(1));
                Assert.That(input.Dimensions[0].IsDynamicByDefault, Is.False);
                Assert.That(input.Dimensions[1].Label, Is.EqualTo("batch"));
                Assert.That(input.Dimensions[1].DefaultValue, Is.EqualTo(-1));
                Assert.That(input.Dimensions[1].IsDynamicByDefault, Is.True);
                Assert.That(input.Dimensions[1].IsNamed, Is.True);
                Assert.That(input.Dimensions[2].Label, Is.EqualTo("input[2]"));
                Assert.That(input.Dimensions[2].DefaultValue, Is.EqualTo(-1));
                Assert.That(input.Dimensions[2].IsDynamicByDefault, Is.True);
            }
            finally
            {
                File.Delete(onnxPath);
            }
        }

        /// <summary>
        /// Verifies weight types and unsupported quantization operators come from metadata, so the Files tab can
        /// advise and import can refuse before conversion.
        /// </summary>
        [Test]
        public void Inspect_ReportsWeightTypesAndUnsupportedOperators()
        {
            var onnxPath = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(onnxPath, BuildQuantizedOnnxModel());

                var inspectedModel = OnnxModelInspector.Inspect(onnxPath);

                Assert.That(inspectedModel.Weights.Select(weight => (weight.DataType, weight.Bytes)), Is.EqualTo(new[] { ("Float16", 2048L), ("Float", 64L) }));
                Assert.That(inspectedModel.IsMostlyFloat16, Is.True);
                Assert.That(inspectedModel.UnsupportedOperators, Is.EqualTo(new[] { "MatMulNBits" }));
                Assert.That(inspectedModel.DescribeUnsupportedOperators(), Does.Contain("`MatMulNBits`"));
            }
            finally
            {
                File.Delete(onnxPath);
            }
        }

        [Test]
        public void OnnxInspectedModel_WithoutMostlyFloat16Weights_GivesNoAdvice()
        {
            var model = new OnnxInspectedModel(
                Array.Empty<OnnxInspectedInput>(),
                new[] { new OnnxInspectedWeights("Float", 100), new OnnxInspectedWeights("Float16", 100) });

            Assert.That(model.IsMostlyFloat16, Is.False);
            Assert.That(model.DescribeUnsupportedOperators(), Is.Null);
            Assert.That(new OnnxInspectedModel(Array.Empty<OnnxInspectedInput>()).IsMostlyFloat16, Is.False);
        }

        [Test]
        public void Inspect_WhenInspectionFails_ReturnsNull()
        {
            var onnxPath = Path.GetTempFileName();
            try
            {
                File.WriteAllText(onnxPath, "not protobuf");

                Assert.That(OnnxModelInspector.Inspect(onnxPath), Is.Null);
            }
            finally
            {
                File.Delete(onnxPath);
            }
        }

        static byte[] BuildOnnxModel()
        {
            using var graph = new MemoryStream();
            WriteInitializer(graph, "weight");
            WriteGraphInput(graph, "input", DimValue(1), DimParam("batch"), UnknownDim());
            WriteGraphInput(graph, "weight", DimValue(16));

            using var model = new MemoryStream();
            WriteMessageField(model, 7, graph.ToArray());
            return model.ToArray();
        }

        /// <summary>
        /// A graph with 1024 float16 weights, 16 float32 weights, and a <c>MatMulNBits</c> node.
        /// </summary>
        internal static byte[] BuildQuantizedOnnxModel()
        {
            using var graph = new MemoryStream();
            using (var node = new MemoryStream())
            {
                WriteStringField(node, 4, "MatMulNBits");
                WriteMessageField(graph, 1, node.ToArray());
            }

            WriteInitializer(graph, "weight", dataType: 10, dims: new[] { 32L, 32L });
            WriteInitializer(graph, "bias", dataType: 1, dims: new[] { 16L });
            WriteGraphInput(graph, "input", DimValue(1), DimValue(32));

            using var model = new MemoryStream();
            WriteMessageField(model, 7, graph.ToArray());
            return model.ToArray();
        }

        static void WriteInitializer(Stream graph, string name, int dataType = 0, long[] dims = null)
        {
            using var tensor = new MemoryStream();
            foreach (var dim in dims ?? Array.Empty<long>())
                WriteVarintField(tensor, 1, (ulong)dim);
            if (dataType != 0)
                WriteVarintField(tensor, 2, (ulong)dataType);
            WriteStringField(tensor, 8, name);
            WriteMessageField(graph, 5, tensor.ToArray());
        }

        static void WriteGraphInput(Stream graph, string name, params DimensionSpec[] dimensions)
        {
            using var valueInfo = new MemoryStream();
            WriteStringField(valueInfo, 1, name);

            using var typeProto = new MemoryStream();
            using var tensorType = new MemoryStream();
            WriteVarintField(tensorType, 1, 1);

            using var shape = new MemoryStream();
            foreach (var dimension in dimensions)
            {
                using var dim = new MemoryStream();
                if (dimension.DimValue.HasValue)
                    WriteVarintField(dim, 1, (ulong)dimension.DimValue.Value);
                if (dimension.DimParam != null)
                    WriteStringField(dim, 2, dimension.DimParam);
                WriteMessageField(shape, 1, dim.ToArray());
            }

            WriteMessageField(tensorType, 2, shape.ToArray());
            WriteMessageField(typeProto, 1, tensorType.ToArray());
            WriteMessageField(valueInfo, 2, typeProto.ToArray());
            WriteMessageField(graph, 11, valueInfo.ToArray());
        }

        static DimensionSpec DimValue(long value) => new DimensionSpec(value, null);
        static DimensionSpec DimParam(string value) => new DimensionSpec(null, value);
        static DimensionSpec UnknownDim() => new DimensionSpec(null, null);

        static void WriteStringField(Stream stream, int field, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            WriteTag(stream, field, 2);
            WriteVarint(stream, (ulong)bytes.Length);
            stream.Write(bytes, 0, bytes.Length);
        }

        static void WriteVarintField(Stream stream, int field, ulong value)
        {
            WriteTag(stream, field, 0);
            WriteVarint(stream, value);
        }

        static void WriteMessageField(Stream stream, int field, byte[] message)
        {
            WriteTag(stream, field, 2);
            WriteVarint(stream, (ulong)message.Length);
            stream.Write(message, 0, message.Length);
        }

        static void WriteTag(Stream stream, int field, int wireType)
        {
            WriteVarint(stream, (ulong)((field << 3) | wireType));
        }

        static void WriteVarint(Stream stream, ulong value)
        {
            while (value >= 0x80)
            {
                stream.WriteByte((byte)(value | 0x80));
                value >>= 7;
            }

            stream.WriteByte((byte)value);
        }

        readonly struct DimensionSpec
        {
            public readonly long? DimValue;
            public readonly string DimParam;

            public DimensionSpec(long? dimValue, string dimParam)
            {
                DimValue = dimValue;
                DimParam = dimParam;
            }
        }
    }
}
