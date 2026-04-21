using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers runtime locator path conventions under shared StreamingAssets root.
    /// </summary>
    internal sealed class RuntimeLocatorPathTests
    {
        [Test]
        public void RuntimePaths_UseSerializedRepositoryAndCommitForGlobalUniqueness()
        {
            var firstPath = ProjectPaths.GetRuntimeSentisAssetPath("hf-internal-testing/tiny-random-bert", "commit-a", "onnx/model.onnx");
            var secondPath = ProjectPaths.GetRuntimeSentisAssetPath("hf-internal-testing/tiny-random-bert", "commit-b", "onnx/model.onnx");

            Assert.That(firstPath, Is.EqualTo("Assets/StreamingAssets/hf-hub/hf-internal-testing/tiny-random-bert/onnx/model.sentis"));
            Assert.That(secondPath, Is.EqualTo("Assets/StreamingAssets/hf-hub/hf-internal-testing/tiny-random-bert/onnx/model.sentis"));
        }

        [Test]
        public void RuntimeSentisPath_PreservesRelativeRepoFoldersWithinSnapshot()
        {
            var path = ProjectPaths.GetRuntimeSentisAssetPath(
                "hf-internal-testing/tiny-random-bert",
                "abc123",
                "tokenizer/config.onnx");

            Assert.That(path, Is.EqualTo(
                "Assets/StreamingAssets/hf-hub/hf-internal-testing/tiny-random-bert/tokenizer/config.sentis"));
        }

        [Test]
        public void RuntimeRegistryPath_IsScopedPerPackage()
        {
            var path = ProjectPaths.GetRuntimeRegistryAssetPath("com.doji.test");

            Assert.That(path, Is.EqualTo(
                "Assets/StreamingAssets/hf-hub/registries/com.doji.test/hf-hub-runtime.json"));
        }

        [Test]
        public void ToStreamingAssetsRelativePath_RemovesProjectStreamingAssetsPrefix()
        {
            var relativePath = ProjectPaths.ToStreamingAssetsRelativePath(
                "Assets/StreamingAssets/hf-hub/hf-internal-testing/tiny-random-bert/onnx/model.sentis");

            Assert.That(relativePath, Is.EqualTo(
                "hf-hub/hf-internal-testing/tiny-random-bert/onnx/model.sentis"));
        }
    }
}
