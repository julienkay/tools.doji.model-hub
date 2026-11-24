using System.IO;
using System.Linq;
using NUnit.Framework;
using Unity.InferenceEngine;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers applying conversion settings through public Inference Engine APIs, the same for the Editor converter and
    /// the runtime ONNX loader, and keys of converted models.
    /// </summary>
    internal sealed class OnnxConversionSettingsTests
    {
        [TestCase(null, "none")]
        [TestCase("", "none")]
        [TestCase("None", "none")]
        [TestCase("float16", "float16")]
        [TestCase(" FLOAT16 ", "float16")]
        [TestCase("uint8", "uint8")]
        [TestCase("int8", "uint8")]
        [TestCase("bogus", "none")]
        public void NormalizeQuantizationMode_MapsToInferenceEngineModes(string mode, string expected)
        {
            Assert.That(OnnxConversionSettings.NormalizeQuantizationMode(mode), Is.EqualTo(expected));
        }

        /// <summary>
        /// Verifies an older "int8" setting and "uint8" produce the same conversion key, so they share a converted model.
        /// </summary>
        [Test]
        public void RecipeHash_TreatsInt8AsUint8()
        {
            Assert.That(
                OnnxImportMetadataUtility.ComputeRecipeHash(new OnnxConversionRecipe { QuantizationMode = "int8" }),
                Is.EqualTo(OnnxImportMetadataUtility.ComputeRecipeHash(new OnnxConversionRecipe { QuantizationMode = "uint8" })));
        }

        /// <summary>
        /// Verifies a fixed input shape is applied, and settings that do not match the model are reported, not applied.
        /// </summary>
        [Test]
        public void Apply_FixesInputShapesAndReportsMismatches()
        {
            var model = CreateWeightedModel();
            var input = model.inputs[0];
            var dimensions = new[] { 3, 64 };
            var recipe = new OnnxConversionRecipe
            {
                StaticInputShapeOverrides = new[]
                {
                    new OnnxStaticInputShapeOverride { InputName = input.name, Dimensions = dimensions },
                    new OnnxStaticInputShapeOverride { InputName = "missing", Dimensions = new[] { 1 } }
                }
            };

            var warnings = new System.Collections.Generic.List<string>();
            model = OnnxConversionSettings.Apply(model, recipe, warnings);

            Assert.That(model.inputs[0].shape.ToTensorShape().ToArray(), Is.EqualTo(dimensions));
            Assert.That(warnings.Single(), Does.Contain("`missing`"));

            var wrongRank = new OnnxConversionRecipe
            {
                StaticInputShapeOverrides = new[] { new OnnxStaticInputShapeOverride { InputName = input.name, Dimensions = new[] { 1, 2, 3 } } }
            };
            var rankWarnings = new System.Collections.Generic.List<string>();
            OnnxConversionSettings.Apply(CreateWeightedModel(), wrongRank, rankWarnings);
            Assert.That(rankWarnings.Single(), Does.Contain("dimensions"));
        }

        /// <summary>
        /// Verifies quantization reaches the weights and survives saving: the 64 x 64 weight constant becomes 16-bit
        /// or 8-bit data. The quantizer builds a new model, so the returned model must be used.
        /// </summary>
        [TestCase("float16", DataType.Short)]
        [TestCase("uint8", DataType.Byte)]
        [TestCase("int8", DataType.Byte)]
        public void Apply_QuantizesWeights(string mode, DataType expected)
        {
            var path = Path.Combine(Path.GetTempPath(), $"model-hub-quantized-{System.Guid.NewGuid():N}.sentis");
            try
            {
                var quantized = OnnxConversionSettings.Apply(CreateWeightedModel(), new OnnxConversionRecipe { QuantizationMode = mode });
                ModelWriter.Save(path, quantized);
                var reloaded = ModelLoader.Load(path);

                Assert.That(reloaded.constants.Any(constant => constant.dataType == expected && constant.shape.length == 64 * 64), Is.True);
                Assert.That(reloaded.constants.Any(constant => constant.dataType == DataType.Float && constant.shape.length == 64 * 64), Is.False);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void Apply_LeavesWeightsWithoutQuantization()
        {
            var model = OnnxConversionSettings.Apply(CreateWeightedModel(), new OnnxConversionRecipe());

            Assert.That(model.constants.Any(constant => constant.dataType == DataType.Float && constant.shape.length == 64 * 64), Is.True);
        }

        /// <summary>
        /// Verifies the measured weight bytes shrink with quantization: 16 KiB of float32 weights become 8 KiB or 4 KiB.
        /// </summary>
        [TestCase("none", 64 * 64 * 4)]
        [TestCase("float16", 64 * 64 * 2)]
        [TestCase("uint8", 64 * 64)]
        public void GetWeightBytes_MeasuresQuantizedWeights(string mode, int expectedWeightBytes)
        {
            var warnings = new System.Collections.Generic.List<string>();
            var model = OnnxConversionSettings.Apply(CreateWeightedModel(), new OnnxConversionRecipe { QuantizationMode = mode }, warnings);

            Assert.That(OnnxConversionSettings.GetWeightBytes(model), Is.InRange(expectedWeightBytes, expectedWeightBytes + 64));
            Assert.That(warnings, Is.Empty);
        }

        /// <summary>
        /// Verifies quantization that changes no weight is reported: the quantizer only re-stores weights of a few
        /// layer types, and weights it already quantized.
        /// </summary>
        [Test]
        public void Apply_WarnsWhenQuantizationChangesNoWeights()
        {
            var graph = new FunctionalGraph();
            var input = graph.AddInput<float>(new DynamicTensorShape(-1, 64), "x");
            var bias = Functional.Constant(new TensorShape(64), Enumerable.Range(0, 64).Select(value => value * 0.1f).ToArray());
            var model = graph.Compile(input + bias);

            var warnings = new System.Collections.Generic.List<string>();
            OnnxConversionSettings.Apply(model, new OnnxConversionRecipe { QuantizationMode = "float16" }, warnings);
            Assert.That(warnings.Single(), Does.Contain("changed no weights"));

            var twice = new System.Collections.Generic.List<string>();
            var quantized = OnnxConversionSettings.Apply(CreateWeightedModel(), new OnnxConversionRecipe { QuantizationMode = "uint8" });
            OnnxConversionSettings.Apply(quantized, new OnnxConversionRecipe { QuantizationMode = "uint8" }, twice);
            Assert.That(twice.Single(), Does.Contain("`uint8` changed no weights"));
        }

        [Test]
        public void DescribeQuantizationEffect_ReportsOnlyRequestedQuantizationWithoutEffect()
        {
            Assert.That(OnnxConversionSettings.DescribeQuantizationEffect("none", 100, 100), Is.Null);
            Assert.That(OnnxConversionSettings.DescribeQuantizationEffect("float16", 100, 50), Is.Null);
            Assert.That(OnnxConversionSettings.DescribeQuantizationEffect("float16", 0, 0), Is.Null, "No weights, nothing to report.");
            Assert.That(OnnxConversionSettings.DescribeQuantizationEffect("int8", 100, 100), Does.Contain("`uint8`"));
        }

        /// <summary>
        /// Verifies conversion keys change with the source, the converter, and the settings, and nothing else.
        /// </summary>
        [Test]
        public void ConversionKey_DependsOnSourceConverterAndSettings()
        {
            var key = ConvertedModelStore.ComputeKey("etag", "converter@1", "recipe");

            Assert.That(ConvertedModelStore.ComputeKey("etag", "converter@1", "recipe"), Is.EqualTo(key));
            Assert.That(ConvertedModelStore.ComputeKey("other", "converter@1", "recipe"), Is.Not.EqualTo(key));
            Assert.That(ConvertedModelStore.ComputeKey("etag", "converter@2", "recipe"), Is.Not.EqualTo(key));
            Assert.That(ConvertedModelStore.ComputeKey("etag", "converter@1", "other"), Is.Not.EqualTo(key));
            Assert.That(key, Has.Length.EqualTo(32));
            Assert.That(
                ConvertedModelStore.GetRepositoryFolder("Owner/Model"),
                Is.EqualTo(ConvertedModelStore.GetRepositoryFolder("owner/model")),
                "Spellings of one id share a folder.");
        }

        /// <summary>
        /// Verifies a quantized weight is dequantized right before the layer that reads it, not at the start of the
        /// model, so the float32 weights are not all on the device at once.
        /// </summary>
        [TestCase("float16", "Cast")]
        [TestCase("uint8", "DequantizeUint8")]
        public void Apply_DequantizesEachWeightRightBeforeItsUse(string quantization, string dequantize)
        {
            var graph = new FunctionalGraph();
            var input = graph.AddInput<float>(new DynamicTensorShape(-1, 64), "x");
            FunctionalTensor output = input;
            for (var i = 0; i < 3; i++)
            {
                var weights = Functional.Constant(new TensorShape(64, 64), Enumerable.Range(0, 64 * 64).Select(value => (value + i) * 0.001f).ToArray());
                output = Functional.Relu(Functional.MatMul(output, weights));
            }

            var model = OnnxConversionSettings.Apply(graph.Compile(output), new OnnxConversionRecipe { QuantizationMode = quantization }, new System.Collections.Generic.List<string>());

            var layers = model.layers;
            var dequantizeIndices = Enumerable.Range(0, layers.Count).Where(index => layers[index].opName == dequantize).ToArray();
            Assert.That(dequantizeIndices, Has.Length.EqualTo(3));
            foreach (var index in dequantizeIndices)
            {
                Assert.That(layers[index + 1].inputs, Does.Contain(layers[index].outputs[0]), $"The layer after {layers[index]} reads it.");
            }
        }

        /// <summary>
        /// A model with a dynamic batch input and a 64 x 64 weight matrix, so shapes and quantization are measurable.
        /// </summary>
        private static Model CreateWeightedModel()
        {
            var graph = new FunctionalGraph();
            var input = graph.AddInput<float>(new DynamicTensorShape(-1, 64), "x");
            var weights = Functional.Constant(new TensorShape(64, 64), Enumerable.Range(0, 64 * 64).Select(value => value * 0.001f).ToArray());
            return graph.Compile(Functional.MatMul(input, weights));
        }
    }
}
