using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    internal sealed class ModelHubFileEditingTests
    {
        /// <summary>
        /// Static inspected input dimensions become read-only shape rows for the Files tab.
        /// </summary>
        [Test]
        public void BuildInputShapes_WithStaticInputs_ReturnsDisplayRows()
        {
            var inspectedModel = new OnnxInspectedModel(new[]
            {
                new OnnxInspectedInput(
                    "image",
                    new[]
                    {
                        new OnnxInspectedDimension("image[0]", 0, 1, false, false),
                        new OnnxInspectedDimension("image[1]", 1, 3, false, false),
                        new OnnxInspectedDimension("image[2]", 2, 224, false, false),
                        new OnnxInspectedDimension("image[3]", 3, 224, false, false)
                    })
            });

            var inputShapes = FileRecipeEditState.BuildInputShapes(inspectedModel);

            Assert.That(inputShapes.Count, Is.EqualTo(1));
            Assert.That(inputShapes[0].Name, Is.EqualTo("image"));
            Assert.That(inputShapes[0].DisplayValue, Is.EqualTo("[1, 3, 224, 224]"));
            Assert.That(inputShapes[0].Dimensions.Count, Is.EqualTo(4));
            Assert.That(inputShapes[0].Dimensions[2].DisplayValue, Is.EqualTo("224"));
            Assert.That(inputShapes[0].Dimensions[2].IsDynamic, Is.False);
        }
    }
}
