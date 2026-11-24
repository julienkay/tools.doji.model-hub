using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Unity.InferenceEngine;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers the conversion settings, the ONNX converter that applies them, and keys of converted models.
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
        public void ApplyInputShapes_FixesInputShapesAndReportsMismatches()
        {
            var graph = new FunctionalGraph();
            var x = graph.AddInput<float>(new DynamicTensorShape(-1, 64), "x");
            var model = graph.Compile(Functional.Relu(x));
            var dimensions = new[] { 3, 64 };
            var recipe = new OnnxConversionRecipe
            {
                StaticInputShapeOverrides = new[]
                {
                    new OnnxStaticInputShapeOverride { InputName = "x", Dimensions = dimensions },
                    new OnnxStaticInputShapeOverride { InputName = "missing", Dimensions = new[] { 1 } }
                }
            };

            var warnings = new List<string>();
            OnnxConversionSettings.ApplyInputShapes(model, recipe, warnings);

            Assert.That(model.inputs[0].shape.ToTensorShape().ToArray(), Is.EqualTo(dimensions));
            Assert.That(warnings.Single(), Does.Contain("`missing`"));

            var wrongRank = new OnnxConversionRecipe
            {
                StaticInputShapeOverrides = new[] { new OnnxStaticInputShapeOverride { InputName = "x", Dimensions = new[] { 1, 2, 3 } } }
            };
            var rankWarnings = new List<string>();
            OnnxConversionSettings.ApplyInputShapes(model, wrongRank, rankWarnings);
            Assert.That(rankWarnings.Single(), Does.Contain("dimensions"));
        }

        /// <summary>
        /// Verifies the converter quantizes the 64 x 64 weight of a MatMul in the saved file, keeps the bias float32,
        /// fixes the input shape, and measures the weight bytes before and after.
        /// </summary>
        [TestCase("none", DataType.Float, 64 * 64 * 4)]
        [TestCase("float16", DataType.Short, 64 * 64 * 2)]
        [TestCase("uint8", DataType.Byte, 64 * 64)]
        [TestCase("int8", DataType.Byte, 64 * 64)]
        public void Convert_QuantizesWeightsAndFixesInputShapes(string mode, DataType expected, int weightBytes)
        {
            var recipe = new OnnxConversionRecipe
            {
                QuantizationMode = mode,
                StaticInputShapeOverrides = new[] { new OnnxStaticInputShapeOverride { InputName = "x", Dimensions = new[] { 2, 64 } } }
            };

            var (result, model) = Convert("matmul.onnx", recipe);

            Assert.That(model.constants.Any(constant => constant.dataType == expected && constant.shape.length == 64 * 64), Is.True);
            Assert.That(model.constants.Any(constant => constant.dataType == DataType.Float && constant.shape.length == 64), Is.True);
            Assert.That(model.inputs[0].shape.ToTensorShape().ToArray(), Is.EqualTo(new[] { 2, 64 }));
            Assert.That(result.ImportedWeightBytes, Is.EqualTo((64 * 64 + 64) * 4));
            Assert.That(result.ConvertedWeightBytes, Is.InRange(weightBytes + 64 * 4, weightBytes + 64 * 4 + 64));
            Assert.That(result.Diagnostics, Is.Empty);
        }

        /// <summary>
        /// Verifies quantization that changes no weight is reported: rank-1 weights such as biases stay float32.
        /// </summary>
        [Test]
        public void Convert_WarnsWhenQuantizationChangesNoWeights()
        {
            var (result, _) = Convert("add.onnx", new OnnxConversionRecipe { QuantizationMode = "float16" });

            Assert.That(result.Diagnostics.Single().Message, Does.Contain("`float16` changed no weights"));
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
        /// Converts an ONNX file of TestData~ to a temporary .sentis file and loads it.
        /// </summary>
        private static (OnnxConversionResult result, Model model) Convert(string fileName, OnnxConversionRecipe recipe)
        {
            var path = Path.Combine(Path.GetTempPath(), $"model-hub-conversion-{System.Guid.NewGuid():N}.sentis");
            try
            {
                var result = new OnnxModelLoaderConverter().Convert(new OnnxConversionRequest(GetTestDataPath(fileName), recipe), path);
                return (result, ModelLoader.Load(path));
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static string GetTestDataPath(string fileName)
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(OnnxConversionSettingsTests).Assembly);
            return Path.Combine(package.resolvedPath, "Tests", "Editor", "Import", "TestData~", fileName);
        }
    }
}
