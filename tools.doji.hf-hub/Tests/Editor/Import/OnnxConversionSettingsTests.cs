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
            var path = Path.Combine(Path.GetTempPath(), $"hf-hub-quantized-{System.Guid.NewGuid():N}.sentis");
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
