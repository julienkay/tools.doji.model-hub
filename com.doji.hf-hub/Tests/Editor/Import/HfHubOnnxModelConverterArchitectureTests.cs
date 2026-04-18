using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers architectural ONNX conversion contracts used by editor now and runtime later.
    /// </summary>
    internal sealed class HfHubOnnxModelConverterArchitectureTests
    {
        /// <summary>
        /// Verifies current runtime converter placeholder stays explicit until a runtime-capable ONNX package is added.
        /// </summary>
        [Test]
        public void RuntimeOnnxConverter_ThrowsNotImplemented()
        {
            var converter = new HfHubRuntimeOnnxModelConverter();

            Assert.That(
                () => converter.ConvertFromFile("/tmp/model.onnx"),
                Throws.TypeOf<System.NotImplementedException>());
        }
    }
}
