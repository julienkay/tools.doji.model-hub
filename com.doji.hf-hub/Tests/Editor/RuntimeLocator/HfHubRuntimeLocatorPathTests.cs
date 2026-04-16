using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers runtime locator path conventions under shared StreamingAssets root.
    /// </summary>
    internal sealed class HfHubRuntimeLocatorPathTests
    {
        [Test]
        public void RuntimePaths_UsePackageAndModelSegmentsForGlobalUniqueness()
        {
            var firstPath = HfHubProjectPaths.GetRuntimeSentisAssetPath("com.doji.alpha", "tiny-model", "model.onnx");
            var secondPath = HfHubProjectPaths.GetRuntimeSentisAssetPath("com.doji.beta", "tiny-model", "model.onnx");

            Assert.That(firstPath, Does.Contain("Assets/StreamingAssets/Doji/HfHub/Models/com.doji.alpha/tiny-model"));
            Assert.That(secondPath, Does.Contain("Assets/StreamingAssets/Doji/HfHub/Models/com.doji.beta/tiny-model"));
            Assert.That(firstPath, Is.Not.EqualTo(secondPath));
        }

        [Test]
        public void RuntimeSourcePath_PreservesRelativeRepoFoldersWithinModelFolder()
        {
            var path = HfHubProjectPaths.GetRuntimeSourceAssetPath(
                "com.doji.test",
                "clip-model",
                "tokenizer/config.json");

            Assert.That(path, Is.EqualTo(
                "Assets/StreamingAssets/Doji/HfHub/Models/com.doji.test/clip-model/repo/tokenizer/config.json"));
        }

        [Test]
        public void RuntimeRegistryPath_IsScopedPerPackage()
        {
            var path = HfHubProjectPaths.GetRuntimeRegistryAssetPath("com.doji.test");

            Assert.That(path, Is.EqualTo(
                "Assets/StreamingAssets/Doji/HfHub/Registries/com.doji.test/hf-hub-runtime.json"));
        }

        [Test]
        public void ToStreamingAssetsRelativePath_RemovesProjectStreamingAssetsPrefix()
        {
            var relativePath = HfHubProjectPaths.ToStreamingAssetsRelativePath(
                "Assets/StreamingAssets/Doji/HfHub/Models/com.doji.test/model/model.sentis");

            Assert.That(relativePath, Is.EqualTo(
                "Doji/HfHub/Models/com.doji.test/model/model.sentis"));
        }
    }
}
