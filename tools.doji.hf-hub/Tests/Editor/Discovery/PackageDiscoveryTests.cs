using System;
using System.Collections.Generic;
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

            var zebraPackage = CreatePackage("tools.doji.zebra", "Zebra Tools", "1.2.3", firstManifest);
            var alphaPackage = CreatePackage("tools.doji.alpha", null, null, secondManifest);

            var report = ManifestDiscovery.DiscoverPackageManifests(new[] { zebraPackage, alphaPackage });

            Assert.That(report.Issues, Is.Empty);
            Assert.That(report.Dependencies.Select(dependency => dependency.PackageId), Is.EqualTo(new[] { "tools.doji.alpha", "tools.doji.zebra" }));

            var alphaDependency = report.Dependencies[0];
            Assert.That(alphaDependency.PackageDisplayName, Is.EqualTo("tools.doji.alpha"));
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
            var packageWithoutManifestPath = CreatePackage("tools.doji.nomodels", "No Models", "1.0.0", manifestContents: null);
            var packageWithoutResolvedPath = new InstalledPackageSnapshot("tools.doji.unresolved", "Unresolved", "1.0.0", string.Empty);

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

            var invalidPackage = CreatePackage("tools.doji.invalid", "Invalid Package", "2.0.0", invalidManifest);
            var validPackage = CreatePackage("tools.doji.valid", "Valid Package", "2.1.0", validManifest);

            var report = ManifestDiscovery.DiscoverPackageManifests(new[] { invalidPackage, validPackage });

            Assert.That(report.Dependencies.Count, Is.EqualTo(1));
            Assert.That(report.Dependencies[0].PackageId, Is.EqualTo("tools.doji.valid"));
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
            Assert.That(manifest.Models[0].EffectiveRevision, Is.EqualTo("main"));
        }

        /// <summary>
        /// Verifies manifest parser preserves optional explicit file selection on one dependency entry.
        /// </summary>
        [Test]
        public void TryParse_ReadsExplicitFileSelection()
        {
            const string manifestContents = @"{
  ""version"": 1,
  ""models"": [
    {
      ""id"": ""hf-internal-testing/tiny-random-bert"",
      ""files"": [
        { ""path"": ""onnx/model.onnx"" },
        { ""path"": ""tokenizer.json"" }
      ]
    }
  ]
}";

            var success = ManifestParser.TryParse(manifestContents, out var manifest, out var errorMessage);

            Assert.That(success, Is.True);
            Assert.That(errorMessage, Is.Null);
            Assert.That(manifest.Models[0].HasExplicitFileSelection, Is.True);
            Assert.That(
                manifest.Models[0].FileEntries.Select(file => file.Path),
                Is.EqualTo(new[] { "onnx/model.onnx", "tokenizer.json" }));
            Assert.That(manifest.Models[0].FileEntries.Any(file => file.Optional), Is.False);
        }

        /// <summary>
        /// Verifies manifest parser preserves optional explicit file selection alongside required files.
        /// </summary>
        [Test]
        public void TryParse_ReadsOptionalFileSelection()
        {
            const string manifestContents = @"{
  ""version"": 1,
  ""models"": [
    {
      ""id"": ""hf-internal-testing/tiny-random-bert"",
      ""files"": [
        { ""path"": ""tokenizer.json"" },
        { ""path"": ""onnx/model.onnx"", ""optional"": true },
        { ""path"": ""onnx/model_q4.onnx"", ""optional"": true }
      ]
    }
  ]
}";

            var success = ManifestParser.TryParse(manifestContents, out var manifest, out var errorMessage);

            Assert.That(success, Is.True);
            Assert.That(errorMessage, Is.Null);
            Assert.That(
                manifest.Models[0].FileEntries.Where(file => !file.Optional).Select(file => file.Path),
                Is.EqualTo(new[] { "tokenizer.json" }));
            Assert.That(
                manifest.Models[0].FileEntries.Where(file => file.Optional).Select(file => file.Path),
                Is.EqualTo(new[] { "onnx/model.onnx", "onnx/model_q4.onnx" }));
        }

        /// <summary>
        /// Verifies outdated `optionalFiles` key fails with a migration hint instead of silently
        /// turning the dependency into a whole-repository download.
        /// </summary>
        [Test]
        public void TryParse_RejectsLegacyOptionalFilesWithMigrationHint()
        {
            const string manifestContents = @"{
  ""version"": 1,
  ""models"": [
    {
      ""id"": ""julienkay/sentis-MiDaS"",
      ""optionalFiles"": [ ""onnx/midas_v21_small_256.onnx"" ]
    }
  ]
}";

            var success = ManifestParser.TryParse(manifestContents, out var manifest, out var errorMessage);

            Assert.That(success, Is.False);
            Assert.That(manifest, Is.Null);
            Assert.That(errorMessage, Does.Contain("Manifest model at index 0 has unknown property `optionalFiles`"));
            Assert.That(errorMessage, Does.Contain(@"""optional"": true"));
        }

        /// <summary>
        /// Verifies unknown keys fail at root, model, and file level.
        /// </summary>
        [TestCase(@"{ ""version"": 1, ""models"": [], ""name"": ""x"" }", "Manifest root has unknown property `name`")]
        [TestCase(@"{ ""version"": 1, ""models"": [ { ""id"": ""a/b"", ""revison"": ""main"" } ] }", "Manifest model at index 0 has unknown property `revison`")]
        [TestCase(@"{ ""version"": 1, ""models"": [ { ""id"": ""a/b"", ""files"": [ { ""path"": ""m.onnx"", ""optinal"": true } ] } ] }", "Manifest model at index 0 `files[0]` has unknown property `optinal`")]
        public void TryParse_RejectsUnknownProperties(string manifestContents, string expectedMessage)
        {
            var success = ManifestParser.TryParse(manifestContents, out var manifest, out var errorMessage);

            Assert.That(success, Is.False);
            Assert.That(manifest, Is.Null);
            Assert.That(errorMessage, Does.StartWith(expectedMessage));
        }

        /// <summary>
        /// Verifies a non-boolean optional flag fails instead of silently meaning required.
        /// </summary>
        [Test]
        public void TryParse_RejectsNonBooleanOptionalFlag()
        {
            const string manifestContents = @"{
  ""version"": 1,
  ""models"": [
    {
      ""id"": ""a/b"",
      ""files"": [ { ""path"": ""m.onnx"", ""optional"": ""true"" } ]
    }
  ]
}";

            var success = ManifestParser.TryParse(manifestContents, out _, out var errorMessage);

            Assert.That(success, Is.False);
            Assert.That(errorMessage, Does.Contain("`files[0].optional` as a boolean"));
        }

        /// <summary>
        /// Verifies manifest validation rejects duplicate selected file paths.
        /// </summary>
        [Test]
        public void TryParse_RejectsDuplicateSelectedFilePath()
        {
            const string manifestContents = @"{
  ""version"": 1,
  ""models"": [
    {
      ""id"": ""hf-internal-testing/tiny-random-bert"",
      ""files"": [
        { ""path"": ""onnx/model.onnx"" },
        { ""path"": ""onnx/model.onnx"", ""optional"": true }
      ]
    }
  ]
}";

            var success = ManifestParser.TryParse(manifestContents, out var manifest, out var errorMessage);

            Assert.That(success, Is.False);
            Assert.That(manifest, Is.Null);
            Assert.That(errorMessage, Is.EqualTo("Manifest model `hf-internal-testing/tiny-random-bert` declares file `onnx/model.onnx` more than once."));
        }

        /// <summary>
        /// Verifies editor discovery applies owner-scoped optional file selections without leaking them to sibling owners.
        /// </summary>
        [Test]
        public void ApplyProjectOptionalSelections_UsesOwnerScopedOverrides()
        {
            var firstDependency = new DiscoveredModelDependency(
                "tools.doji.first",
                "First Package",
                "1.0.0",
                "/tmp/tools.doji.first",
                "/tmp/tools.doji.first/hf-hub-manifest.json",
                "hash-a",
                new ModelDependencyDefinition(
                    "doji/test-model",
                    "main",
                    new[]
                    {
                        new ModelDependencyFileDefinition("model.onnx", true),
                        new ModelDependencyFileDefinition("model_q4.onnx", true)
                    }));
            var secondDependency = new DiscoveredModelDependency(
                "tools.doji.second",
                "Second Package",
                "1.0.0",
                "/tmp/tools.doji.second",
                "/tmp/tools.doji.second/hf-hub-manifest.json",
                "hash-b",
                new ModelDependencyDefinition(
                    "doji/test-model",
                    "main",
                    new[]
                    {
                        new ModelDependencyFileDefinition("model.onnx", true),
                        new ModelDependencyFileDefinition("model_q4.onnx", true)
                    }));
            var report = new DiscoveryReport(
                new[] { firstDependency, secondDependency },
                Array.Empty<ManifestDiscoveryIssue>());
            var selectionSource = new FakeProjectOptionalFileSelectionSource(
                ("tools.doji.first", "doji/test-model", new[] { "model_q4.onnx" }));

            var resolved = EditorManifestDiscovery.ApplyProjectOptionalSelections(report, selectionSource);

            Assert.That(resolved.Dependencies.Count, Is.EqualTo(2));

            var firstResolved = resolved.Dependencies.Single(dependency => dependency.PackageId == "tools.doji.first");
            Assert.That(firstResolved.SelectedFiles, Is.EqualTo(new[] { "model_q4.onnx" }));
            Assert.That(firstResolved.GetEnabledOptionalRepoPaths(), Is.EqualTo(new[] { "model_q4.onnx" }));

            var secondResolved = resolved.Dependencies.Single(dependency => dependency.PackageId == "tools.doji.second");
            Assert.That(secondResolved.SelectedFiles, Is.Empty);
            Assert.That(secondResolved.GetEnabledOptionalRepoPaths(), Is.Empty);
        }

        /// <summary>
        /// Verifies central revision normalization preserves explicit text while still resolving omitted revisions to `main`.
        /// </summary>
        [Test]
        public void EffectiveRevision_UsesCentralResolutionRule()
        {
            Assert.That(new ModelDependencyDefinition("hf-internal-testing/tiny-random-bert", string.Empty).EffectiveRevision, Is.EqualTo("main"));
            Assert.That(new ModelDependencyDefinition("hf-internal-testing/tiny-random-bert", "main").EffectiveRevision, Is.EqualTo("main"));
            Assert.That(new ModelDependencyDefinition("hf-internal-testing/tiny-random-bert", "refs/pr/12").EffectiveRevision, Is.EqualTo("refs/pr/12"));
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
            Assert.That(dependency.PackageDisplayName, Is.EqualTo("HF Hub Project Manifest"));
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
        /// Verifies ProjectSettings manifest JSON is discovered alongside ScriptableObject project manifest assets.
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
            Directory.CreateDirectory(Path.Combine(projectRoot, "ProjectSettings", "Packages", "tools.doji.hf-hub"));
            File.WriteAllText(Path.Combine(projectRoot, "ProjectSettings", "Packages", "tools.doji.hf-hub", Hub.ManifestFileName), manifestContents);

            var report = ProjectManifestDiscovery.DiscoverProjectSettingsManifest(projectRoot);

            Assert.That(report.Issues, Is.Empty);
            Assert.That(report.Dependencies.Count, Is.EqualTo(1));
            Assert.That(report.Dependencies[0].PackageId, Is.EqualTo(ProjectManifestDiscovery.ProjectSettingsManifestOwnerId));
            Assert.That(report.Dependencies[0].PackageDisplayName, Is.EqualTo("Project Settings"));
            Assert.That(report.Dependencies[0].ManifestPath, Is.EqualTo(Path.Combine(projectRoot, "ProjectSettings", "Packages", "tools.doji.hf-hub", Hub.ManifestFileName)));
            Assert.That(report.Dependencies[0].Repository, Is.EqualTo("hf-internal-testing/tiny-random-bert"));
        }

        /// <summary>
        /// Verifies invalid ProjectSettings manifest JSON reports a discovery issue instead of crashing scan.
        /// </summary>
        [Test]
        public void DiscoverProjectSettingsManifest_ReportsInvalidManifest()
        {
            var projectRoot = Path.Combine(_rootDirectory, "BrokenProjectRoot");
            Directory.CreateDirectory(Path.Combine(projectRoot, "ProjectSettings", "Packages", "tools.doji.hf-hub"));
            File.WriteAllText(Path.Combine(projectRoot, "ProjectSettings", "Packages", "tools.doji.hf-hub", Hub.ManifestFileName), "{");

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
            var success = ProjectSettingsModelManifestStore.TryAddModelId(
                "onnx-community/tiny-clip-test-ONNX",
                "main",
                null,
                out var errorMessage,
                projectRoot);

            var manifestPath = Path.Combine(projectRoot, "ProjectSettings", "Packages", "tools.doji.hf-hub", Hub.ManifestFileName);
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

        /// <summary>
        /// Verifies explicit `main` revision stays explicit when reloading the raw project-settings manifest document.
        /// </summary>
        [Test]
        public void TryLoadManifestDocument_PreservesExplicitMainRevision()
        {
            var projectRoot = Path.Combine(_rootDirectory, "ExplicitMainProjectRoot");
            var manifestDirectory = Path.Combine(projectRoot, "ProjectSettings", "Packages", "tools.doji.hf-hub");
            Directory.CreateDirectory(manifestDirectory);
            File.WriteAllText(
                Path.Combine(manifestDirectory, Hub.ManifestFileName),
                @"{
  ""version"": 1,
  ""models"": [
    {
      ""id"": ""hf-internal-testing/tiny-random-bert"",
      ""revision"": ""main""
    }
  ]
}");

            var success = ProjectSettingsModelManifestStore.TryLoadManifestDocument(projectRoot, out var document, out var errorMessage);

            Assert.That(success, Is.True);
            Assert.That(errorMessage, Is.Null);
            Assert.That(document.models[0].revision, Is.EqualTo("main"));
        }

        /// <summary>
        /// Verifies Project Settings manifest writing preserves an explicitly entered `main` revision in JSON.
        /// </summary>
        [Test]
        public void TryWriteManifest_PreservesExplicitMainRevision()
        {
            var projectRoot = Path.Combine(_rootDirectory, "WriteExplicitMainProjectRoot");
            var success = ProjectSettingsModelManifestStore.TryWriteManifest(
                1,
                new[]
                {
                    new ManifestModelDraft("hf-internal-testing/tiny-random-bert", "main")
                },
                out var errorMessage,
                projectRoot);

            var manifestPath = Path.Combine(projectRoot, "ProjectSettings", "Packages", "tools.doji.hf-hub", Hub.ManifestFileName);
            var contents = File.ReadAllText(manifestPath);

            Assert.That(success, Is.True);
            Assert.That(errorMessage, Is.Null);
            Assert.That(contents, Does.Contain(@"""revision"": ""main"""));
        }

        /// <summary>
        /// Verifies clearing an explicit `main` revision back to empty preserves the authored empty revision text in project-settings JSON.
        /// </summary>
        [Test]
        public void TryWriteManifest_PreservesEmptyRevisionWhenCleared()
        {
            var projectRoot = Path.Combine(_rootDirectory, "WriteEmptyRevisionProjectRoot");
            var success = ProjectSettingsModelManifestStore.TryWriteManifest(
                1,
                new[]
                {
                    new ManifestModelDraft("hf-internal-testing/tiny-random-bert", string.Empty)
                },
                out var errorMessage,
                projectRoot);

            var manifestPath = Path.Combine(projectRoot, "ProjectSettings", "Packages", "tools.doji.hf-hub", Hub.ManifestFileName);
            var contents = File.ReadAllText(manifestPath);

            Assert.That(success, Is.True);
            Assert.That(errorMessage, Is.Null);
            Assert.That(contents, Does.Contain(@"""revision"": """""));
        }

        /// <summary>
        /// Verifies local package filtering keeps only package roots inside current project and outside PackageCache.
        /// </summary>
        [Test]
        public void GetProjectLocalPackages_FiltersToProjectLocalPackages()
        {
            var projectRoot = Path.Combine(_rootDirectory, "ProjectRoot");
            var packagesPath = Path.Combine(projectRoot, "Packages", "tools.doji.local");
            var packageCachePath = Path.Combine(projectRoot, "Library", "PackageCache", "tools.doji.cache");
            Directory.CreateDirectory(packagesPath);
            Directory.CreateDirectory(packageCachePath);

            var options = LocalPackageManifestStore.GetProjectLocalPackages(
                new[]
                {
                    new InstalledPackageSnapshot("tools.doji.local", "Local", "1.0.0", packagesPath),
                    new InstalledPackageSnapshot("tools.doji.cache", "Cache", "1.0.0", packageCachePath),
                    new InstalledPackageSnapshot("tools.doji.external", "External", "1.0.0", Path.Combine(_rootDirectory, "ExternalPkg"))
                },
                projectRoot);

            Assert.That(options.Select(option => option.PackageId).ToArray(), Is.EqualTo(new[] { "tools.doji.local" }));
        }

        /// <summary>
        /// Verifies package add flow creates a new package manifest when one does not exist yet.
        /// </summary>
        [Test]
        public void TryAddModelId_CreatesLocalPackageManifestJson()
        {
            var packageRoot = Path.Combine(_rootDirectory, "ProjectRoot", "Packages", "tools.doji.local");
            Directory.CreateDirectory(packageRoot);
            var package = new LocalPackageManifestStore.LocalPackageOption("tools.doji.local", "Local", packageRoot);

            var success = LocalPackageManifestStore.TryAddModelId(
                package,
                "hf-internal-testing/tiny-random-bert",
                "main",
                null,
                out var errorMessage);

            var manifestPath = Path.Combine(packageRoot, Hub.ManifestFileName);
            Assert.That(success, Is.True);
            Assert.That(errorMessage, Is.Null);
            Assert.That(File.Exists(manifestPath), Is.True);
            Assert.That(ManifestParser.TryParse(File.ReadAllText(manifestPath), out var manifest, out errorMessage), Is.True);
            Assert.That(manifest.Models.Select(model => model.Id).ToArray(), Is.EqualTo(new[] { "hf-internal-testing/tiny-random-bert" }));
        }

        /// <summary>
        /// Verifies local package manifest writing preserves explicit `main` revisions instead of silently canonicalizing them away.
        /// </summary>
        [Test]
        public void TryAddModelId_PreservesExplicitMainRevisionInExistingLocalPackageManifest()
        {
            var packageRoot = Path.Combine(_rootDirectory, "ProjectRoot", "Packages", "tools.doji.local");
            Directory.CreateDirectory(packageRoot);
            File.WriteAllText(
                Path.Combine(packageRoot, Hub.ManifestFileName),
                @"{
  ""version"": 1,
  ""models"": [
    {
      ""id"": ""hf-internal-testing/tiny-random-bert"",
      ""revision"": ""main""
    }
  ]
}");
            var package = new LocalPackageManifestStore.LocalPackageOption("tools.doji.local", "Local", packageRoot);

            var success = LocalPackageManifestStore.TryAddModelId(
                package,
                "hf-internal-testing/tiny-random-gpt2",
                "main",
                null,
                out var errorMessage);

            var contents = File.ReadAllText(Path.Combine(packageRoot, Hub.ManifestFileName));
            Assert.That(success, Is.True);
            Assert.That(errorMessage, Is.Null);
            Assert.That(contents, Does.Contain(@"""revision"": ""main"""));
        }

        /// <summary>
        /// Verifies project-settings manifest authoring writes explicit selected files when provided.
        /// </summary>
        [Test]
        public void TryAddModelId_WritesExplicitFilesToProjectSettingsManifest()
        {
            var projectRoot = Path.Combine(_rootDirectory, "ProjectFilesSelectionRoot");

            var success = ProjectSettingsModelManifestStore.TryAddModelId(
                "onnx-community/tiny-clip-test-ONNX",
                "main",
                new[] { "onnx/model.onnx" },
                out var errorMessage,
                projectRoot);

            var manifestPath = Path.Combine(projectRoot, "ProjectSettings", "Packages", "tools.doji.hf-hub", Hub.ManifestFileName);
            var contents = File.ReadAllText(manifestPath);

            Assert.That(success, Is.True);
            Assert.That(errorMessage, Is.Null);
            Assert.That(contents, Does.Contain(@"""files"": ["));
            Assert.That(contents, Does.Contain(@"""onnx/model.onnx"""));
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
                var filesProperty = modelProperty.FindPropertyRelative("<Files>k__BackingField");
                var files = models[i].Files ?? Array.Empty<ProjectManifestFileAssetEntry>();
                filesProperty.arraySize = files.Length;
                for (var fileIndex = 0; fileIndex < files.Length; fileIndex++)
                {
                    var fileProperty = filesProperty.GetArrayElementAtIndex(fileIndex);
                    fileProperty.FindPropertyRelative("<Path>k__BackingField").stringValue = files[fileIndex].Path;
                    fileProperty.FindPropertyRelative("<Optional>k__BackingField").boolValue = files[fileIndex].Optional;
                }
            }

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Verifies IsProjectSettingsManifestOwnerId is true only for the canonical project-settings owner id.
        /// </summary>
        [Test]
        public void IsProjectSettingsManifestOwnerId_ReturnsTrueOnlyForProjectSettingsId()
        {
            Assert.That(ProjectManifestDiscovery.IsProjectSettingsManifestOwnerId(ProjectManifestDiscovery.ProjectSettingsManifestOwnerId), Is.True);
            Assert.That(ProjectManifestDiscovery.IsProjectSettingsManifestOwnerId(ProjectManifestDiscovery.ProjectManifestAssetFallbackOwnerId), Is.False);
            Assert.That(ProjectManifestDiscovery.IsProjectSettingsManifestOwnerId("project-manifest-abc123"), Is.False);
            Assert.That(ProjectManifestDiscovery.IsProjectSettingsManifestOwnerId("com.some.package"), Is.False);
            Assert.That(ProjectManifestDiscovery.IsProjectSettingsManifestOwnerId(null), Is.False);
        }

        /// <summary>
        /// Verifies IsProjectManifestAssetOwnerId is true for the fallback id and the prefixed guid form, but not for the project-settings id.
        /// </summary>
        [Test]
        public void IsProjectManifestAssetOwnerId_ReturnsTrueForAssetOwnerIds()
        {
            Assert.That(ProjectManifestDiscovery.IsProjectManifestAssetOwnerId(ProjectManifestDiscovery.ProjectManifestAssetFallbackOwnerId), Is.True);
            Assert.That(ProjectManifestDiscovery.IsProjectManifestAssetOwnerId("project-manifest-abc123def456"), Is.True);
            Assert.That(ProjectManifestDiscovery.IsProjectManifestAssetOwnerId(ProjectManifestDiscovery.ProjectSettingsManifestOwnerId), Is.False);
            Assert.That(ProjectManifestDiscovery.IsProjectManifestAssetOwnerId("com.some.package"), Is.False);
            Assert.That(ProjectManifestDiscovery.IsProjectManifestAssetOwnerId(null), Is.False);
        }

        /// <summary>
        /// Verifies GetOwnerDisplayName returns distinct labels for project-settings vs project-manifest-asset owners.
        /// </summary>
        [Test]
        public void GetOwnerDisplayName_ReturnsDifferentLabelsForProjectSettingsAndManifestAsset()
        {
            var settingsLabel = ProjectManifestDiscovery.GetOwnerDisplayName(ProjectManifestDiscovery.ProjectSettingsManifestOwnerId);
            var assetLabel = ProjectManifestDiscovery.GetOwnerDisplayName(ProjectManifestDiscovery.ProjectManifestAssetFallbackOwnerId);
            var guidAssetLabel = ProjectManifestDiscovery.GetOwnerDisplayName("project-manifest-abc123");
            var packageLabel = ProjectManifestDiscovery.GetOwnerDisplayName("com.some.package");

            Assert.That(settingsLabel, Is.EqualTo("Project Settings"));
            Assert.That(assetLabel, Is.Not.EqualTo(settingsLabel));
            Assert.That(guidAssetLabel, Is.Not.EqualTo(settingsLabel));
            Assert.That(packageLabel, Is.EqualTo("com.some.package"));
        }

        private sealed class FakeProjectOptionalFileSelectionSource : IProjectOptionalFileSelectionSource
        {
            private readonly (string ownerId, string modelId, string[] enabledOptionalFiles)[] _entries;

            public FakeProjectOptionalFileSelectionSource(params (string ownerId, string modelId, string[] enabledOptionalFiles)[] entries)
            {
                _entries = entries ?? Array.Empty<(string ownerId, string modelId, string[] enabledOptionalFiles)>();
            }

            public string[] GetEnabledOptionalFiles(string ownerId, string modelId, IReadOnlyList<string> declaredOptionalFiles)
            {
                var entry = _entries.FirstOrDefault(item =>
                    string.Equals(item.ownerId, ownerId, StringComparison.Ordinal) &&
                    string.Equals(item.modelId, modelId, StringComparison.Ordinal));
                return entry.enabledOptionalFiles ?? Array.Empty<string>();
            }

            public void SetEnabledOptionalFiles(string ownerId, string modelId, IReadOnlyList<string> enabledOptionalFiles)
            {
            }
        }
    }
}
