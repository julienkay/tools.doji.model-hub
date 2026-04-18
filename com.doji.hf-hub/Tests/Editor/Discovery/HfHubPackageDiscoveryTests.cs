using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor.Tests
{
    internal sealed class HfHubPackageDiscoveryTests
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
        /// Tests that discovery reads valid package manifests and turns them into consistent dependency records.
        /// "Normalized" here means the result includes fallback package display and version values, resolved manifest paths, and computed manifest hashes.
        /// </summary>
        [Test]
        public void DiscoverPackageManifests_ReturnsNormalizedDependenciesSortedByPackageId()
        {
            const string firstManifest = @"{
  ""version"": 1,
  ""models"": [
    {
      ""id"": ""vision-base"",
      ""displayName"": ""Vision Base"",
      ""repository"": ""doji/vision-base"",
      ""revision"": ""refs/tags/v1"",
      ""sourceFile"": ""models/vision.onnx""
    }
  ]
}";

            const string secondManifest = @"{
  ""version"": 1,
  ""models"": [
    {
      ""id"": ""text-small"",
      ""displayName"": ""Text Small"",
      ""repository"": ""doji/text-small"",
      ""revision"": ""main"",
      ""sourceFile"": ""model.onnx""
    }
  ]
}";

            var zebraPackage = CreatePackage(
                "com.doji.zebra",
                "Zebra Tools",
                "1.2.3",
                firstManifest);
            var alphaPackage = CreatePackage(
                "com.doji.alpha",
                null,
                null,
                secondManifest);

            var report = HfHubManifestDiscovery.DiscoverPackageManifests(new[]
            {
                zebraPackage,
                alphaPackage
            });

            Assert.That(report.Issues, Is.Empty);
            Assert.That(report.Dependencies.Select(dependency => dependency.PackageId), Is.EqualTo(new[]
            {
                "com.doji.alpha",
                "com.doji.zebra"
            }));

            var alphaDependency = report.Dependencies[0];
            Assert.That(alphaDependency.PackageDisplayName, Is.EqualTo("com.doji.alpha"));
            Assert.That(alphaDependency.PackageVersion, Is.EqualTo("unknown"));
            Assert.That(alphaDependency.ModelId, Is.EqualTo("text-small"));
            Assert.That(alphaDependency.Repository, Is.EqualTo("doji/text-small"));
            Assert.That(alphaDependency.ManifestPath, Is.EqualTo(Path.Combine(alphaPackage.ResolvedPath, ModelHub.ManifestFileName)));
            Assert.That(alphaDependency.ManifestHash, Is.EqualTo(ComputeSha256(secondManifest)));

            var zebraDependency = report.Dependencies[1];
            Assert.That(zebraDependency.PackageDisplayName, Is.EqualTo("Zebra Tools"));
            Assert.That(zebraDependency.PackageVersion, Is.EqualTo("1.2.3"));
            Assert.That(zebraDependency.ModelDisplayName, Is.EqualTo("Vision Base"));
            Assert.That(zebraDependency.Revision, Is.EqualTo("refs/tags/v1"));
            Assert.That(zebraDependency.SourceFile, Is.EqualTo("models/vision.onnx"));
            Assert.That(zebraDependency.PackageResolvedPath, Is.EqualTo(zebraPackage.ResolvedPath));
        }

        /// <summary>
        /// Tests that discovery skips packages when there is no Hub manifest file or no resolved package path to read from.
        /// </summary>
        [Test]
        public void DiscoverPackageManifests_SkipsPackagesWithoutManifestOrResolvedPath()
        {
            var packageWithoutManifestPath = CreatePackage(
                "com.doji.nomodels",
                "No Models",
                "1.0.0",
                manifestContents: null);
            var packageWithoutResolvedPath = new HfHubInstalledPackageSnapshot(
                "com.doji.unresolved",
                "Unresolved",
                "1.0.0",
                string.Empty);

            var report = HfHubManifestDiscovery.DiscoverPackageManifests(new[]
            {
                packageWithoutManifestPath,
                packageWithoutResolvedPath
            });

            Assert.That(report.Dependencies, Is.Empty);
            Assert.That(report.Issues, Is.Empty);
        }

        /// <summary>
        /// Tests that one broken manifest is reported as discovery issue while valid manifests from other packages are still returned.
        /// </summary>
        [Test]
        public void DiscoverPackageManifests_ReportsInvalidManifestAndContinues()
        {
            const string invalidManifest = @"{
  ""version"": 1,
  ""models"": [
    {
      ""id"": ""broken-model"",
      ""displayName"": ""Broken Model""
    }
  ]
}";

            const string validManifest = @"{
  ""version"": 1,
  ""models"": [
    {
      ""id"": ""kept-model"",
      ""displayName"": ""Kept Model"",
      ""repository"": ""doji/kept-model"",
      ""revision"": ""main"",
      ""sourceFile"": ""kept.onnx""
    }
  ]
}";

            var invalidPackage = CreatePackage(
                "com.doji.invalid",
                "Invalid Package",
                "2.0.0",
                invalidManifest);
            var validPackage = CreatePackage(
                "com.doji.valid",
                "Valid Package",
                "2.1.0",
                validManifest);

            var report = HfHubManifestDiscovery.DiscoverPackageManifests(new[]
            {
                invalidPackage,
                validPackage
            });

            Assert.That(report.Dependencies.Count, Is.EqualTo(1));
            Assert.That(report.Dependencies[0].PackageId, Is.EqualTo("com.doji.valid"));
            Assert.That(report.Issues.Count, Is.EqualTo(1));

            var issue = report.Issues[0];
            Assert.That(issue.PackageId, Is.EqualTo("com.doji.invalid"));
            Assert.That(issue.ManifestPath, Is.EqualTo(Path.Combine(invalidPackage.ResolvedPath, ModelHub.ManifestFileName)));
            Assert.That(issue.Message, Is.EqualTo("Manifest model at index 0 must define a string `repository`."));
        }

        /// <summary>
        /// Tests that manifest parsing fails when same model id appears more than once in one package manifest.
        /// </summary>
        [Test]
        public void TryParse_RejectsDuplicateModelIds()
        {
            const string manifestContents = @"{
  ""version"": 1,
  ""models"": [
    {
      ""id"": ""shared-id"",
      ""displayName"": ""First"",
      ""repository"": ""doji/first"",
      ""revision"": ""main"",
      ""sourceFile"": ""first.onnx""
    },
    {
      ""id"": ""shared-id"",
      ""displayName"": ""Second"",
      ""repository"": ""doji/second"",
      ""revision"": ""main"",
      ""sourceFile"": ""second.onnx""
    }
  ]
}";

            var success = HfHubManifestParser.TryParse(manifestContents, out var manifest, out var errorMessage);

            Assert.That(success, Is.False);
            Assert.That(manifest, Is.Null);
            Assert.That(errorMessage, Is.EqualTo("Manifest model id `shared-id` is declared more than once."));
        }

        /// <summary>
        /// Tests that project manifest assets are normalized into standard dependency records under the shared project owner id.
        /// </summary>
        [Test]
        public void DiscoverProjectManifests_ReturnsNormalizedDependencies()
        {
            var asset = ScriptableObject.CreateInstance<HfHubProjectManifestAsset>();
            SetProjectManifest(asset, 1, new[]
            {
                new HfHubProjectManifestModelAssetEntry
                {
                    Id = "tiny-clip-onnx",
                    DisplayName = "Tiny CLIP ONNX",
                    Repository = "onnx-community/tiny-clip-test-ONNX",
                    Revision = "main",
                    SourceFile = "onnx/model.onnx"
                }
            });

            var snapshot = new HfHubProjectManifestAssetSnapshot(
                "Assets/HF Hub/HF Hub Project Manifest.asset",
                Path.Combine(_rootDirectory, "Assets", "HF Hub", "HF Hub Project Manifest.asset"),
                _rootDirectory,
                asset);

            var report = HfHubProjectManifestDiscovery.DiscoverProjectManifests(new[] { snapshot });

            Assert.That(report.Issues, Is.Empty);
            Assert.That(report.Dependencies.Count, Is.EqualTo(1));

            var dependency = report.Dependencies[0];
            Assert.That(dependency.PackageId, Is.EqualTo(HfHubProjectManifestDiscovery.ProjectManifestOwnerId));
            Assert.That(dependency.PackageDisplayName, Is.EqualTo("Project"));
            Assert.That(dependency.PackageVersion, Is.EqualTo("project"));
            Assert.That(dependency.Repository, Is.EqualTo("onnx-community/tiny-clip-test-ONNX"));
            Assert.That(dependency.SourceFile, Is.EqualTo("onnx/model.onnx"));
        }

        /// <summary>
        /// Tests that invalid project manifest assets report the same validation issues as JSON manifests.
        /// </summary>
        [Test]
        public void DiscoverProjectManifests_ReportsInvalidProjectManifestAsset()
        {
            var asset = ScriptableObject.CreateInstance<HfHubProjectManifestAsset>();
            SetProjectManifest(asset, 1, new[]
            {
                new HfHubProjectManifestModelAssetEntry
                {
                    Id = "broken-model",
                    DisplayName = "Broken Model",
                    Repository = string.Empty,
                    Revision = "main",
                    SourceFile = "model.onnx"
                }
            });

            var snapshot = new HfHubProjectManifestAssetSnapshot(
                "Assets/HF Hub/Broken Manifest.asset",
                Path.Combine(_rootDirectory, "Assets", "HF Hub", "Broken Manifest.asset"),
                _rootDirectory,
                asset);

            var report = HfHubProjectManifestDiscovery.DiscoverProjectManifests(new[] { snapshot });

            Assert.That(report.Dependencies, Is.Empty);
            Assert.That(report.Issues.Count, Is.EqualTo(1));
            Assert.That(report.Issues[0].PackageId, Is.EqualTo(HfHubProjectManifestDiscovery.ProjectManifestOwnerId));
            Assert.That(report.Issues[0].Message, Is.EqualTo("Manifest model at index 0 must define a non-empty `repository`."));
        }

        private HfHubInstalledPackageSnapshot CreatePackage(
            string packageId,
            string displayName,
            string version,
            string manifestContents)
        {
            var packageDirectory = Path.Combine(_rootDirectory, packageId);
            Directory.CreateDirectory(packageDirectory);

            if (manifestContents != null)
            {
                File.WriteAllText(Path.Combine(packageDirectory, ModelHub.ManifestFileName), manifestContents);
            }

            return new HfHubInstalledPackageSnapshot(packageId, displayName, version, packageDirectory);
        }

        private static string ComputeSha256(string value)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(value);
            var hashBytes = sha256.ComputeHash(bytes);
            return string.Concat(hashBytes.Select(hashByte => hashByte.ToString("x2")));
        }

        private static void SetProjectManifest(
            HfHubProjectManifestAsset asset,
            int version,
            HfHubProjectManifestModelAssetEntry[] models)
        {
            var serializedObject = new UnityEditor.SerializedObject(asset);
            serializedObject.FindProperty("<Version>k__BackingField").intValue = version;

            var modelsProperty = serializedObject.FindProperty("<Models>k__BackingField");
            modelsProperty.arraySize = models.Length;
            for (var i = 0; i < models.Length; i++)
            {
                var modelProperty = modelsProperty.GetArrayElementAtIndex(i);
                modelProperty.FindPropertyRelative("<Id>k__BackingField").stringValue = models[i].Id;
                modelProperty.FindPropertyRelative("<DisplayName>k__BackingField").stringValue = models[i].DisplayName;
                modelProperty.FindPropertyRelative("<Repository>k__BackingField").stringValue = models[i].Repository;
                modelProperty.FindPropertyRelative("<Revision>k__BackingField").stringValue = models[i].Revision;
                modelProperty.FindPropertyRelative("<SourceFile>k__BackingField").stringValue = models[i].SourceFile;
            }

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
