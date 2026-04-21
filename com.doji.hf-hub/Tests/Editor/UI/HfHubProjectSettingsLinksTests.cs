using NUnit.Framework;
using System;
using System.Linq;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers link targets shown in Project Settings.
    /// </summary>
    internal sealed class HfHubProjectSettingsLinksTests
    {
        /// <summary>
        /// Ensures source links open repository tree view for declared revision.
        /// </summary>
        [Test]
        public void GetSourceRepositoryUrl_BuildsHuggingFaceTreeUrl()
        {
            var dependency = new HfHubDiscoveredModelDependency(
                "com.doji.test",
                "Test Package",
                "1.0.0",
                "/tmp/com.doji.test",
                "/tmp/com.doji.test/hf-hub-manifest.json",
                "manifesthash",
                new HfHubModelDependencyDefinition(
                    "doji/test-model",
                    "refs/pr/12"));

            var url = HfHubProjectSettingsLinks.GetSourceRepositoryUrl(dependency);

            Assert.That(
                url,
                Is.EqualTo("https://huggingface.co/doji/test-model/tree/refs%2Fpr%2F12"));
        }

        /// <summary>
        /// Ensures absolute manifest paths under project root normalize to Unity asset paths.
        /// </summary>
        [Test]
        public void TryGetProjectAssetPath_NormalizesProjectRelativeAssetPath()
        {
            var projectRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, ".."));
            var manifestPath = System.IO.Path.Combine(projectRoot, "Packages", "com.doji.test", "hf-hub-manifest.json");

            var assetPath = HfHubProjectSettingsLinks.TryGetProjectAssetPath(manifestPath);

            Assert.That(assetPath, Is.EqualTo("Packages/com.doji.test/hf-hub-manifest.json"));
        }

        /// <summary>
        /// Ensures locate targets collapse duplicate manifest paths and sort stable by label.
        /// </summary>
        [Test]
        public void BuildManifestLocateTargets_DeduplicatesManifestPaths()
        {
            var sharedManifestPath = "Packages/com.doji.pkg/hf-hub-manifest.json";
            var item = new HfHubModelHubItem(
                "hf-internal-testing/tiny-random-bert",
                "main",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                HfHubModelHubItemState.Ready,
                new[]
                {
                    CreateDependencyStatus("com.doji.beta", "Beta", "beta-model", sharedManifestPath),
                    CreateDependencyStatus("com.doji.alpha", "Alpha", "alpha-model", sharedManifestPath),
                    CreateDependencyStatus("project", "Project", "project-model", "Assets/HF Hub/HF Hub Project Manifest.asset")
                },
                Array.Empty<HfHubModelHubDiagnostic>(),
                Array.Empty<HfHubImportedRuntimeFileMetadata>());

            var targets = HfHubProjectSettingsLinks.BuildManifestLocateTargets(item);

            Assert.That(targets.Select(target => target.Label).ToArray(), Is.EqualTo(new[]
            {
                "Alpha / alpha-model",
                "Project / project-model"
            }));
            Assert.That(targets.Select(target => target.ManifestPath).ToArray(), Is.EqualTo(new[]
            {
                sharedManifestPath,
                "Assets/HF Hub/HF Hub Project Manifest.asset"
            }));
        }

        private static HfHubDependencyStatus CreateDependencyStatus(
            string packageId,
            string packageDisplayName,
            string modelId,
            string manifestPath)
        {
            var dependency = new HfHubDiscoveredModelDependency(
                packageId,
                packageDisplayName,
                "1.0.0",
                $"/tmp/{packageId}",
                manifestPath,
                $"{packageId}-hash",
                new HfHubModelDependencyDefinition(
                    modelId,
                    "main"));

            return new HfHubDependencyStatus(
                dependency,
                HfHubDependencyState.Ready,
                HfHubDependencyReasonCode.None,
                "Ready",
                null,
                null,
                null,
                null,
                null,
                null,
                null);
        }
    }
}
