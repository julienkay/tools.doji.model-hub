using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers where runtime models live inside StreamingAssets, in builds and when pulled into the project.
    /// </summary>
    internal sealed class RuntimeLocatorPathTests
    {
        /// <summary>
        /// Verifies a runtime model keeps its repository folders and gets the .sentis extension.
        /// </summary>
        [TestCase("onnx/model.onnx", "hf-hub/models/hf-internal-testing/tiny-random-bert/onnx/model.sentis")]
        [TestCase("sentis/model.sentis", "hf-hub/models/hf-internal-testing/tiny-random-bert/sentis/model.sentis")]
        [TestCase("../../escape.onnx", "hf-hub/models/hf-internal-testing/tiny-random-bert/escape.sentis")]
        public void RuntimeModelRelativePath_KeepsRepositoryFolders(string repoPath, string expected)
        {
            Assert.That(ProjectPaths.GetRuntimeModelRelativePath("hf-internal-testing/tiny-random-bert", repoPath), Is.EqualTo(expected));
        }

        /// <summary>
        /// Verifies a model pulled into the project sits where a build would put it.
        /// </summary>
        [Test]
        public void PulledModelAssetPath_MatchesTheBuildLayout()
        {
            Assert.That(
                ProjectPaths.GetPulledModelAssetPath("doji/test-model", "onnx/model.onnx"),
                Is.EqualTo("Assets/StreamingAssets/hf-hub/models/doji/test-model/onnx/model.sentis"));
        }

        /// <summary>
        /// Verifies different project manifest assets get different binding paths.
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

            Assert.That(
                ProjectPaths.GetImportBindingMetadataFilePath(firstOwnerId, "doji/test-model"),
                Is.Not.EqualTo(ProjectPaths.GetImportBindingMetadataFilePath(secondOwnerId, "doji/test-model")));
        }
    }
}
