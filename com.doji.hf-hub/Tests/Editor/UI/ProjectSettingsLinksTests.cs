using NUnit.Framework;
using System;
using System.Linq;
using System.IO;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers link targets shown in Project Settings.
    /// </summary>
    internal sealed class ProjectSettingsLinksTests
    {
        /// <summary>
        /// Ensures source links open repository tree view for declared revision.
        /// </summary>
        [Test]
        public void GetSourceRepositoryUrl_BuildsHuggingFaceTreeUrl()
        {
            var dependency = new DiscoveredModelDependency(
                "com.doji.test",
                "Test Package",
                "1.0.0",
                "/tmp/com.doji.test",
                "/tmp/com.doji.test/hf-hub-manifest.json",
                "manifesthash",
                new ModelDependencyDefinition(
                    "doji/test-model",
                    "refs/pr/12"));

            var url = ProjectSettingsLinks.GetSourceRepositoryUrl(dependency);

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

            var assetPath = ProjectSettingsLinks.TryGetProjectAssetPath(manifestPath);

            Assert.That(assetPath, Is.EqualTo("Packages/com.doji.test/hf-hub-manifest.json"));
        }

        /// <summary>
        /// Ensures locate targets collapse duplicate manifest paths and sort stable by label.
        /// </summary>
        [Test]
        public void BuildManifestLocateTargets_DeduplicatesManifestPaths()
        {
            var sharedManifestPath = "Packages/com.doji.pkg/hf-hub-manifest.json";
            var item = new ModelHubItem(
                "hf-internal-testing/tiny-random-bert",
                "main",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                ModelHubItemState.Ready,
                new[]
                {
                    CreateDependencyStatus("com.doji.beta", "Beta", "beta-model", sharedManifestPath),
                    CreateDependencyStatus("com.doji.alpha", "Alpha", "alpha-model", sharedManifestPath),
                    CreateDependencyStatus("project", "Project", "project-model", "Assets/HF Hub/HF Hub Project Manifest.asset")
                },
                Array.Empty<ModelHubDiagnostic>(),
                Array.Empty<ImportedRuntimeFileMetadata>());

            var targets = ProjectSettingsLinks.BuildManifestLocateTargets(item);

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

        /// <summary>
        /// Ensures package-cache manifests are treated as package-manager owned rather than directly editable project files.
        /// </summary>
        [Test]
        public void GetManifestOwnerResolutionInfo_ClassifiesInstalledPackageCacheAsPackageManagerOwned()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var packageRoot = Path.Combine(projectRoot, "Library", "PackageCache", "com.doji.pkg@1.2.3");
            var dependency = new DiscoveredModelDependency(
                "com.doji.pkg",
                "Locked Package",
                "1.2.3",
                packageRoot,
                Path.Combine(packageRoot, "hf-hub-manifest.json"),
                "manifesthash",
                new ModelDependencyDefinition(
                    "doji/test-model",
                    "refs/tags/v1"));

            var info = ProjectSettingsLinks.GetManifestOwnerResolutionInfo(dependency);

            Assert.That(info.Kind, Is.EqualTo(ProjectSettingsLinks.ManifestOwnerResolutionKind.InstalledPackage));
            Assert.That(info.IsDirectlyEditable, Is.False);
            Assert.That(info.SuggestsPackageManager, Is.True);
            Assert.That(info.ResolutionHint, Does.Contain("Package Manager"));
        }

        /// <summary>
        /// Ensures copied conflict summary includes owner source and package-manager guidance for locked package owners.
        /// </summary>
        [Test]
        public void BuildRevisionConflictSummary_ListsOwnerKindsAndResolutionHints()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var packageRoot = Path.Combine(projectRoot, "Library", "PackageCache", "com.doji.pkg@1.2.3");
            var item = new ModelHubItem(
                "doji/test-model",
                "main",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                ModelHubItemState.Error,
                new[]
                {
                    CreateDependencyStatus(
                        "project",
                        "Project",
                        "project-model",
                        "Assets/HF Hub/HF Hub Project Manifest.asset",
                        packageResolvedPath: Path.Combine(projectRoot, "Assets", "HF Hub")),
                    CreateDependencyStatus(
                        "com.doji.pkg",
                        "Locked Package",
                        "package-model",
                        Path.Combine(packageRoot, "hf-hub-manifest.json"),
                        packageVersion: "1.2.3",
                        packageResolvedPath: packageRoot,
                        revision: "refs/tags/v1",
                        reasonCode: DependencyReasonCode.RevisionConflict)
                },
                Array.Empty<ModelHubDiagnostic>(),
                Array.Empty<ImportedRuntimeFileMetadata>());

            var summary = ProjectSettingsLinks.BuildRevisionConflictSummary(item);

            Assert.That(summary, Does.Contain("HF Hub revision conflict"));
            Assert.That(summary, Does.Contain("Project manifest"));
            Assert.That(summary, Does.Contain("Installed package"));
            Assert.That(summary, Does.Contain("Package Manager"));
            Assert.That(summary, Does.Contain("Assets/HF Hub/HF Hub Project Manifest.asset"));
            Assert.That(summary, Does.Contain("Library/PackageCache"));
        }

        private static DependencyStatus CreateDependencyStatus(
            string packageId,
            string packageDisplayName,
            string modelId,
            string manifestPath,
            string packageVersion = "1.0.0",
            string packageResolvedPath = null,
            string revision = "main",
            DependencyReasonCode reasonCode = DependencyReasonCode.None)
        {
            var dependency = new DiscoveredModelDependency(
                packageId,
                packageDisplayName,
                packageVersion,
                packageResolvedPath ?? $"/tmp/{packageId}",
                manifestPath,
                $"{packageId}-hash",
                new ModelDependencyDefinition(
                    modelId,
                    revision));

            return new DependencyStatus(
                dependency,
                reasonCode == DependencyReasonCode.None ? DependencyState.Ready : DependencyState.Failed,
                reasonCode,
                reasonCode == DependencyReasonCode.None ? "Ready" : "Conflict",
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
