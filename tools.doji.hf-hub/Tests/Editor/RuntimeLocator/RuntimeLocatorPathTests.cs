using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers runtime locator path conventions under shared StreamingAssets root.
    /// </summary>
    internal sealed class RuntimeLocatorPathTests
    {
        /// <summary>
        /// Verifies runtime `.sentis` paths stay repo-scoped even when resolved commit changes.
        /// </summary>
        [Test]
        public void RuntimePaths_UseSerializedRepositoryAndCommitForGlobalUniqueness()
        {
            var firstPath = ProjectPaths.GetRuntimeSentisAssetPath("hf-internal-testing/tiny-random-bert", "commit-a", "onnx/model.onnx");
            var secondPath = ProjectPaths.GetRuntimeSentisAssetPath("hf-internal-testing/tiny-random-bert", "commit-b", "onnx/model.onnx");

            Assert.That(firstPath, Is.EqualTo("Assets/StreamingAssets/hf-hub/hf-internal-testing/tiny-random-bert/onnx/model.sentis"));
            Assert.That(secondPath, Is.EqualTo("Assets/StreamingAssets/hf-hub/hf-internal-testing/tiny-random-bert/onnx/model.sentis"));
        }

        /// <summary>
        /// Verifies generated runtime paths preserve repository subfolders.
        /// </summary>
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

        /// <summary>
        /// Verifies runtime registries stay scoped under each manifest owner id.
        /// </summary>
        [Test]
        public void RuntimeRegistryPath_IsScopedPerPackage()
        {
            var path = ProjectPaths.GetRuntimeRegistryAssetPath("tools.doji.test");

            Assert.That(path, Is.EqualTo(
                "Assets/StreamingAssets/hf-hub/registries/tools.doji.test/hf-hub-runtime.json"));
        }

        /// <summary>
        /// Verifies different project manifest assets get different binding and registry paths.
        /// </summary>
        [Test]
        public void ProjectManifestPaths_UseDistinctOwnerIdsPerAsset()
        {
            var firstOwnerId = ProjectManifestDiscovery.GetOwnerId(
                "5a899f24b6fd4a1ba127ab1a1b267c7e",
                "Assets/HF Hub/Project Manifest A.asset",
                null);
            var secondOwnerId = ProjectManifestDiscovery.GetOwnerId(
                "2c1a6095dfaa45b2b4f16a2de4db9107",
                "Assets/HF Hub/Project Manifest B.asset",
                null);

            var firstBindingPath = ProjectPaths.GetImportBindingMetadataFilePath(firstOwnerId, "doji/test-model");
            var secondBindingPath = ProjectPaths.GetImportBindingMetadataFilePath(secondOwnerId, "doji/test-model");
            var firstRegistryPath = ProjectPaths.GetRuntimeRegistryAssetPath(firstOwnerId);
            var secondRegistryPath = ProjectPaths.GetRuntimeRegistryAssetPath(secondOwnerId);

            Assert.That(firstBindingPath, Is.Not.EqualTo(secondBindingPath));
            Assert.That(firstRegistryPath, Is.Not.EqualTo(secondRegistryPath));
        }

        /// <summary>
        /// Verifies runtime-relative paths drop project asset prefix.
        /// </summary>
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
