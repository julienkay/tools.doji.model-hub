using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers package and project manifest discovery for repo-level dependency declarations.
    /// </summary>
    internal sealed class PackageDiscoveryTests
    {
        private string _rootDirectory;

        [SetUp]
        public void SetUp()
        {
            _rootDirectory = Path.Combine(
                Path.GetTempPath(),
                "Doji.ModelHub.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rootDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_rootDirectory))
            {
                Directory.Delete(_rootDirectory, true);
            }
        }

        /// <summary>
        /// Verifies discovery reads valid repo-level manifests and normalizes owner metadata.
        /// </summary>
        [Test]
        public void DiscoverPackageManifests_ReturnsNormalizedDependenciesSortedByPackageId()
        {
            const string firstManifest = @"{
  ""version"": 1,
  ""models"": [
    {
      ""id"": ""doji/vision-base"",
      ""revision"": ""refs/tags/v1""
    }
  ]
}";

            const string secondManifest = @"{
  ""version"": 1,
  ""models"": [
    {
      ""id"": ""doji/text-small"",
      ""revision"": ""main""
    }
  ]
}";

            var zebraPackage = CreatePackage("com.doji.zebra", "Zebra Tools", "1.2.3", firstManifest);
            var alphaPackage = CreatePackage("com.doji.alpha", null, null, secondManifest);

            var report = ManifestDiscovery.DiscoverPackageManifests(new[] { zebraPackage, alphaPackage });

            Assert.That(report.Issues, Is.Empty);
            Assert.That(report.Dependencies.Select(dependency => dependency.PackageId), Is.EqualTo(new[] { "com.doji.alpha", "com.doji.zebra" }));

            var alphaDependency = report.Dependencies[0];
            Assert.That(alphaDependency.PackageDisplayName, Is.EqualTo("com.doji.alpha"));
            Assert.That(alphaDependency.PackageVersion, Is.EqualTo("unknown"));
            Assert.That(alphaDependency.ModelId, Is.EqualTo("doji/text-small"));
            Assert.That(alphaDependency.Repository, Is.EqualTo("doji/text-small"));
            Assert.That(alphaDependency.ManifestPath, Is.EqualTo(Path.Combine(alphaPackage.ResolvedPath, Hub.ManifestFileName)));
            Assert.That(alphaDependency.ManifestHash, Is.EqualTo(ComputeSha256(secondManifest)));

            var zebraDependency = report.Dependencies[1];
            Assert.That(zebraDependency.PackageDisplayName, Is.EqualTo("Zebra Tools"));
            Assert.That(zebraDependency.PackageVersion, Is.EqualTo("1.2.3"));
            Assert.That(zebraDependency.ModelId, Is.EqualTo("doji/vision-base"));
            Assert.That(zebraDependency.Revision, Is.EqualTo("refs/tags/v1"));
            Assert.That(zebraDependency.PackageResolvedPath, Is.EqualTo(zebraPackage.ResolvedPath));
        }

        /// <summary>
        /// Verifies discovery skips packages with no manifest or no resolved path.
        /// </summary>
        [Test]
        public void DiscoverPackageManifests_SkipsPackagesWithoutManifestOrResolvedPath()
        {
            var packageWithoutManifestPath = CreatePackage("com.doji.nomodels", "No Models", "1.0.0", manifestContents: null);
            var packageWithoutResolvedPath = new InstalledPackageSnapshot("com.doji.unresolved", "Unresolved", "1.0.0", string.Empty);

            var report = ManifestDiscovery.DiscoverPackageManifests(new[] { packageWithoutManifestPath, packageWithoutResolvedPath });

            Assert.That(report.Dependencies, Is.Empty);
            Assert.That(report.Issues, Is.Empty);
        }

        /// <summary>
        /// Verifies one broken manifest is reported while valid manifests from other packages still load.
        /// </summary>
        [Test]
        public void DiscoverPackageManifests_ReportsInvalidManifestAndContinues()
        {
            const string invalidManifest = "{";

            const string validManifest = @"{
  ""version"": 1,
  ""models"": [
    {
      ""id"": ""doji/kept-model"",
      ""revision"": ""main""
    }
  ]
}";

            var invalidPackage = CreatePackage("com.doji.invalid", "Invalid Package", "2.0.0", invalidManifest);
            var validPackage = CreatePackage("com.doji.valid", "Valid Package", "2.1.0", validManifest);

            var report = ManifestDiscovery.DiscoverPackageManifests(new[] { invalidPackage, validPackage });

            Assert.That(report.Dependencies.Count, Is.EqualTo(1));
            Assert.That(report.Dependencies[0].PackageId, Is.EqualTo("com.doji.valid"));
            Assert.That(report.Issues.Count, Is.EqualTo(1));
            Assert.That(report.Issues[0].Message, Does.Contain("invalid JSON").Or.Contain("JSON"));
        }

        /// <summary>
        /// Verifies manifest parsing still rejects duplicate ids inside one owner manifest.
        /// </summary>
        [Test]
        public void TryParse_RejectsDuplicateModelIds()
        {
            const string manifestContents = @"{
  ""version"": 1,
  ""models"": [
    {
      ""id"": ""doji/first"",
      ""revision"": ""main""
    },
    {
      ""id"": ""doji/first"",
      ""revision"": ""main""
    }
  ]
}";

            var success = ManifestParser.TryParse(manifestContents, out var manifest, out var errorMessage);

            Assert.That(success, Is.False);
            Assert.That(manifest, Is.Null);
            Assert.That(errorMessage, Is.EqualTo("Manifest model id `doji/first` is declared more than once."));
        }

        /// <summary>
        /// Verifies manifest parser defaults missing revision to main.
        /// </summary>
        [Test]
        public void TryParse_DefaultsMissingRevisionToMain()
        {
            const string manifestContents = @"{
  ""version"": 1,
  ""models"": [
    {
      ""id"": ""hf-internal-testing/tiny-random-bert""
    }
  ]
}";

            var success = ManifestParser.TryParse(manifestContents, out var manifest, out var errorMessage);

            Assert.That(success, Is.True);
            Assert.That(errorMessage, Is.Null);
            Assert.That(manifest.Models[0].Id, Is.EqualTo("hf-internal-testing/tiny-random-bert"));
            Assert.That(manifest.Models[0].Revision, Is.EqualTo("main"));
        }

        /// <summary>
        /// Verifies project manifest assets normalize to same repo-level dependency shape as JSON manifests.
        /// </summary>
        [Test]
        public void DiscoverProjectManifestAssets_ReturnsNormalizedDependencies()
        {
            var asset = ScriptableObject.CreateInstance<ProjectManifestAsset>();
            SetProjectManifest(asset, 1, new[]
            {
                new ProjectManifestModelAssetEntry
                {
                    Id = "onnx-community/tiny-clip-test-ONNX",
                    Revision = "main"
                }
            });

            var snapshot = new ProjectManifestAssetSnapshot(
                "Assets/HF Hub/HF Hub Project Manifest.asset",
                Path.Combine(_rootDirectory, "Assets", "HF Hub", "HF Hub Project Manifest.asset"),
                _rootDirectory,
                asset);

            var report = ProjectManifestDiscovery.DiscoverProjectManifestAssets(new[] { snapshot });

            Assert.That(report.Issues, Is.Empty);
            Assert.That(report.Dependencies.Count, Is.EqualTo(1));

            var dependency = report.Dependencies[0];
            Assert.That(
                dependency.PackageId,
                Is.EqualTo(ProjectManifestDiscovery.GetOwnerId(null, snapshot.AssetPath, snapshot.ManifestPath)));
            Assert.That(ProjectManifestDiscovery.IsProjectManifestOwnerId(dependency.PackageId), Is.True);
            Assert.That(dependency.PackageDisplayName, Is.EqualTo("Project"));
            Assert.That(dependency.PackageVersion, Is.EqualTo("project"));
            Assert.That(dependency.Repository, Is.EqualTo("onnx-community/tiny-clip-test-ONNX"));
        }

        /// <summary>
        /// Verifies invalid project manifest assets report same validation issue shape as JSON manifests.
        /// </summary>
        [Test]
        public void DiscoverProjectManifestAssets_ReportsInvalidProjectManifestAsset()
        {
            const string assetGuid = "d8adf79a35f94f26a1bd188d74e7298f";
            var asset = ScriptableObject.CreateInstance<ProjectManifestAsset>();
            SetProjectManifest(asset, 1, new[]
            {
                new ProjectManifestModelAssetEntry
                {
                    Id = string.Empty,
                    Revision = "main"
                }
            });

            var snapshot = new ProjectManifestAssetSnapshot(
                "Assets/HF Hub/Broken Manifest.asset",
                Path.Combine(_rootDirectory, "Assets", "HF Hub", "Broken Manifest.asset"),
                _rootDirectory,
                asset,
                assetGuid);

            var report = ProjectManifestDiscovery.DiscoverProjectManifestAssets(new[] { snapshot });

            Assert.That(report.Dependencies, Is.Empty);
            Assert.That(report.Issues.Count, Is.EqualTo(1));
            Assert.That(
                report.Issues[0].PackageId,
                Is.EqualTo(ProjectManifestDiscovery.GetOwnerId(assetGuid, snapshot.AssetPath, snapshot.ManifestPath)));
            Assert.That(report.Issues[0].Message, Is.EqualTo("Manifest model at index 0 must define a non-empty `id`."));
        }

        /// <summary>
        /// Verifies different project manifest assets keep distinct owner ids even when they declare same repository.
        /// </summary>
        [Test]
        public void DiscoverProjectManifestAssets_AssignsUniqueOwnerIdsPerProjectAsset()
        {
            const string firstGuid = "5a899f24b6fd4a1ba127ab1a1b267c7e";
            const string secondGuid = "2c1a6095dfaa45b2b4f16a2de4db9107";
            var firstAsset = ScriptableObject.CreateInstance<ProjectManifestAsset>();
            var secondAsset = ScriptableObject.CreateInstance<ProjectManifestAsset>();
            var models = new[]
            {
                new ProjectManifestModelAssetEntry
                {
                    Id = "onnx-community/tiny-clip-test-ONNX",
                    Revision = "main"
                }
            };

            SetProjectManifest(firstAsset, 1, models);
            SetProjectManifest(secondAsset, 1, models);

            var report = ProjectManifestDiscovery.DiscoverProjectManifestAssets(new[]
            {
                new ProjectManifestAssetSnapshot(
                    "Assets/HF Hub/Project Manifest A.asset",
                    Path.Combine(_rootDirectory, "Assets", "HF Hub", "Project Manifest A.asset"),
                    _rootDirectory,
                    firstAsset,
                    firstGuid),
                new ProjectManifestAssetSnapshot(
                    "Assets/HF Hub/Project Manifest B.asset",
                    Path.Combine(_rootDirectory, "Assets", "HF Hub", "Project Manifest B.asset"),
                    _rootDirectory,
                    secondAsset,
                    secondGuid)
            });

            Assert.That(report.Issues, Is.Empty);
            Assert.That(report.Dependencies.Count, Is.EqualTo(2));
            Assert.That(
                report.Dependencies.Select(dependency => dependency.PackageId).Distinct().ToArray(),
                Is.EqualTo(new[]
                {
                    ProjectManifestDiscovery.GetOwnerId(firstGuid, "Assets/HF Hub/Project Manifest A.asset", Path.Combine(_rootDirectory, "Assets", "HF Hub", "Project Manifest A.asset")),
                    ProjectManifestDiscovery.GetOwnerId(secondGuid, "Assets/HF Hub/Project Manifest B.asset", Path.Combine(_rootDirectory, "Assets", "HF Hub", "Project Manifest B.asset"))
                }));
        }

        /// <summary>
        /// Verifies ProjectSettings manifest JSON is discovered alongside legacy project manifest assets.
        /// </summary>
        [Test]
        public void DiscoverProjectSettingsManifest_ReturnsNormalizedDependencies()
        {
            const string manifestContents = @"{
  ""version"": 1,
  ""models"": [
    {
      ""id"": ""hf-internal-testing/tiny-random-bert""
    }
  ]
}";

            var projectRoot = Path.Combine(_rootDirectory, "ProjectRoot");
            Directory.CreateDirectory(Path.Combine(projectRoot, "ProjectSettings"));
            File.WriteAllText(Path.Combine(projectRoot, "ProjectSettings", Hub.ManifestFileName), manifestContents);

            var report = ProjectManifestDiscovery.DiscoverProjectSettingsManifest(projectRoot);

            Assert.That(report.Issues, Is.Empty);
            Assert.That(report.Dependencies.Count, Is.EqualTo(1));
            Assert.That(report.Dependencies[0].PackageId, Is.EqualTo(ProjectManifestDiscovery.ProjectSettingsManifestOwnerId));
            Assert.That(report.Dependencies[0].PackageDisplayName, Is.EqualTo("Project"));
            Assert.That(report.Dependencies[0].ManifestPath, Is.EqualTo(Path.Combine(projectRoot, "ProjectSettings", Hub.ManifestFileName)));
            Assert.That(report.Dependencies[0].Repository, Is.EqualTo("hf-internal-testing/tiny-random-bert"));
        }

        /// <summary>
        /// Verifies invalid ProjectSettings manifest JSON reports a discovery issue instead of crashing scan.
        /// </summary>
        [Test]
        public void DiscoverProjectSettingsManifest_ReportsInvalidManifest()
        {
            var projectRoot = Path.Combine(_rootDirectory, "BrokenProjectRoot");
            Directory.CreateDirectory(Path.Combine(projectRoot, "ProjectSettings"));
            File.WriteAllText(Path.Combine(projectRoot, "ProjectSettings", Hub.ManifestFileName), "{");

            var report = ProjectManifestDiscovery.DiscoverProjectSettingsManifest(projectRoot);

            Assert.That(report.Dependencies, Is.Empty);
            Assert.That(report.Issues.Count, Is.EqualTo(1));
            Assert.That(report.Issues[0].PackageId, Is.EqualTo(ProjectManifestDiscovery.ProjectSettingsManifestOwnerId));
            Assert.That(report.Issues[0].Message, Does.Contain("JSON").Or.Contain("invalid JSON"));
        }

        /// <summary>
        /// Verifies adding a model creates the ProjectSettings manifest and appends normalized entry content.
        /// </summary>
        [Test]
        public void TryAddModelId_CreatesProjectSettingsManifestJson()
        {
            var projectRoot = Path.Combine(_rootDirectory, "AuthoringProjectRoot");
            Directory.CreateDirectory(Path.Combine(projectRoot, "ProjectSettings"));

            var success = ProjectSettingsModelManifestStore.TryAddModelId(
                "onnx-community/tiny-clip-test-ONNX",
                "main",
                out var errorMessage,
                projectRoot);

            var manifestPath = Path.Combine(projectRoot, "ProjectSettings", Hub.ManifestFileName);
            Assert.That(success, Is.True);
            Assert.That(errorMessage, Is.Null);
            Assert.That(File.Exists(manifestPath), Is.True);

            var report = ProjectManifestDiscovery.DiscoverProjectSettingsManifest(projectRoot);
            Assert.That(report.Issues, Is.Empty);
            Assert.That(report.Dependencies.Select(dependency => dependency.Repository).ToArray(), Is.EqualTo(new[]
            {
                "onnx-community/tiny-clip-test-ONNX"
            }));
        }

        private InstalledPackageSnapshot CreatePackage(string packageId, string displayName, string version, string manifestContents)
        {
            var packageDirectory = Path.Combine(_rootDirectory, packageId);
            Directory.CreateDirectory(packageDirectory);

            if (manifestContents != null)
            {
                File.WriteAllText(Path.Combine(packageDirectory, Hub.ManifestFileName), manifestContents);
            }

            return new InstalledPackageSnapshot(packageId, displayName, version, packageDirectory);
        }

        private static string ComputeSha256(string value)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(value);
            var hashBytes = sha256.ComputeHash(bytes);
            return string.Concat(hashBytes.Select(hashByte => hashByte.ToString("x2")));
        }

        private static void SetProjectManifest(ProjectManifestAsset asset, int version, ProjectManifestModelAssetEntry[] models)
        {
            var serializedObject = new UnityEditor.SerializedObject(asset);
            serializedObject.FindProperty("<Version>k__BackingField").intValue = version;

            var modelsProperty = serializedObject.FindProperty("<Models>k__BackingField");
            modelsProperty.arraySize = models.Length;
            for (var i = 0; i < models.Length; i++)
            {
                var modelProperty = modelsProperty.GetArrayElementAtIndex(i);
                modelProperty.FindPropertyRelative("<Id>k__BackingField").stringValue = models[i].Id;
                modelProperty.FindPropertyRelative("<Revision>k__BackingField").stringValue = models[i].Revision;
            }

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
