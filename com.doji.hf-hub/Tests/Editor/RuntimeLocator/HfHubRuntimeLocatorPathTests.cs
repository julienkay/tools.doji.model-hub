using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers runtime locator path conventions under shared StreamingAssets root.
    /// </summary>
    internal sealed class HfHubRuntimeLocatorPathTests
    {
        [Test]
        public void RuntimePaths_UseSerializedRepositoryAndCommitForGlobalUniqueness()
        {
            var firstPath = HfHubProjectPaths.GetRuntimeSentisAssetPath("hf-internal-testing/tiny-random-bert", "commit-a", "model.onnx");
            var secondPath = HfHubProjectPaths.GetRuntimeSentisAssetPath("hf-internal-testing/tiny-random-bert", "commit-b", "model.onnx");

            Assert.That(firstPath, Does.Contain("Assets/StreamingAssets/hf-hub/models--hf-internal-testing--tiny-random-bert/snapshots/commit-a"));
            Assert.That(secondPath, Does.Contain("Assets/StreamingAssets/hf-hub/models--hf-internal-testing--tiny-random-bert/snapshots/commit-b"));
            Assert.That(firstPath, Is.Not.EqualTo(secondPath));
        }

        [Test]
        public void RuntimeSourcePath_PreservesRelativeRepoFoldersWithinModelFolder()
        {
            var path = HfHubProjectPaths.GetRuntimeSourceAssetPath(
                "hf-internal-testing/tiny-random-bert",
                "abc123",
                "tokenizer/config.json");

            Assert.That(path, Is.EqualTo(
                "Assets/StreamingAssets/hf-hub/models--hf-internal-testing--tiny-random-bert/snapshots/abc123/repo/tokenizer/config.json"));
        }

        [Test]
        public void RuntimeRegistryPath_IsScopedPerPackage()
        {
            var path = HfHubProjectPaths.GetRuntimeRegistryAssetPath("com.doji.test");

            Assert.That(path, Is.EqualTo(
                "Assets/StreamingAssets/hf-hub/registries/com.doji.test/hf-hub-runtime.json"));
        }

        [Test]
        public void ToStreamingAssetsRelativePath_RemovesProjectStreamingAssetsPrefix()
        {
            var relativePath = HfHubProjectPaths.ToStreamingAssetsRelativePath(
                "Assets/StreamingAssets/hf-hub/models--hf-internal-testing--tiny-random-bert/snapshots/abc123/model.sentis");

            Assert.That(relativePath, Is.EqualTo(
                "hf-hub/models--hf-internal-testing--tiny-random-bert/snapshots/abc123/model.sentis"));
        }
    }
}
