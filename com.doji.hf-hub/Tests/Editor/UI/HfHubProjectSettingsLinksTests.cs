using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers link targets shown in Project Settings.
    /// </summary>
    internal sealed class HfHubProjectSettingsLinksTests
    {
        /// <summary>
        /// Ensures source links open exact HuggingFace repository file for declared revision and nested source path.
        /// </summary>
        [Test]
        public void GetSourceRepositoryUrl_BuildsHuggingFaceBlobUrl()
        {
            var dependency = new HfHubDiscoveredModelDependency(
                "com.doji.test",
                "Test Package",
                "1.0.0",
                "/tmp/com.doji.test",
                "/tmp/com.doji.test/hf-hub-manifest.json",
                "manifesthash",
                new HfHubModelDependencyDefinition(
                    "test-model",
                    "Test Model",
                    "doji/test-model",
                    "refs/pr/12",
                    "models/encoder/model.onnx"));

            var url = HfHubProjectSettingsLinks.GetSourceRepositoryUrl(dependency);

            Assert.That(
                url,
                Is.EqualTo("https://huggingface.co/doji/test-model/blob/refs%2Fpr%2F12/models/encoder/model.onnx"));
        }
    }
}
