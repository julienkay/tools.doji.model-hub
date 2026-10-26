using System;
using System.IO;
using System.Linq;
using Doji.AI.ModelHub.Editor.Tests;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Doji.AI.ModelHub.Sandbox.Editor.Tests
{
    /// <summary>
    /// Covers local fake artifact generation used by companion-project visual AI Model Hub scenarios.
    /// </summary>
    internal sealed class VisualScenarioServiceTests
    {
        private string _cacheRoot;
        private string _scenarioAssetPath;
        private TestProjectStateScope _projectScope;

        [SetUp]
        public void SetUp()
        {
            _cacheRoot = Path.Combine(Path.GetTempPath(), "Doji.ModelHub.VisualSandboxTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_cacheRoot);
            Environment.SetEnvironmentVariable("HF_HUB_CACHE", _cacheRoot);
            Environment.SetEnvironmentVariable("DOJI_MODEL_HUB_DATA_ROOT", _cacheRoot + "-modelhub-data");
            _scenarioAssetPath = AssetDatabase.GenerateUniqueAssetPath("Assets/AI Model Hub/Scenario Sandbox/Scenario Test Asset.asset");
            _projectScope = new TestProjectStateScope(
                TestProjectStateScope.DefaultRoots.Append("Assets/AI Model Hub/Scenario Sandbox/Generated").ToArray());
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.LoadMainAssetAtPath(_scenarioAssetPath) != null)
            {
                AssetDatabase.DeleteAsset(_scenarioAssetPath);
            }

            _projectScope?.Dispose();
            _projectScope = null;

            if (Directory.Exists(_cacheRoot))
            {
                Directory.Delete(_cacheRoot, true);
            }

            Environment.SetEnvironmentVariable("HF_HUB_CACHE", null);
            Environment.SetEnvironmentVariable("DOJI_MODEL_HUB_DATA_ROOT", null);
            if (Directory.Exists(_cacheRoot + "-modelhub-data"))
            {
                Directory.Delete(_cacheRoot + "-modelhub-data", true);
            }
        }

        /// <summary>
        /// Verifies import-needed scenario writes cache artifacts but no import metadata yet.
        /// </summary>
        [Test]
        public void ApplyScenario_CreatesConversionNeededArtifactsWithoutImportMetadata()
        {
            var scenario = CreateScenario(
                new VisualScenarioEntry
                {
                    Repository = "sandbox/import-needed-test",
                    Revision = "main",
                    ResolvedCommitHash = "sandbox-import-needed-001",
                    State = VisualScenarioState.ConversionNeeded
                });

            VisualScenarioService.ApplyScenario(scenario);

            var importMetadataAbsolutePath = Path.Combine(
                Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                VisualScenarioService.GetImportMetadataProjectRelativePath(scenario.Entries[0]));

            Assert.That(File.Exists(VisualScenarioService.GetRevisionRefPath(scenario.Entries[0])), Is.True);
            Assert.That(File.Exists(VisualScenarioService.GetSnapshotMetadataPath("sandbox/import-needed-test", "sandbox-import-needed-001")), Is.True);
            Assert.That(File.Exists(importMetadataAbsolutePath), Is.False);
            Assert.That(
                AssetDatabase.LoadAssetAtPath<ProjectManifestAsset>(VisualScenarioService.GetGeneratedManifestAssetPath(scenario, 0)),
                Is.Not.Null);
        }

        /// <summary>
        /// Verifies explicit single-file scenarios write selected files into manifest while keeping full repository inventory in raw metadata.
        /// </summary>
        [Test]
        public void ApplyScenario_WritesExplicitSingleFileManifestAndRepositoryInventory()
        {
            var scenario = CreateScenario(
                new VisualScenarioEntry
                {
                    Repository = "sandbox/single-file-selected",
                    Revision = "main",
                    ResolvedCommitHash = "sandbox-single-file-001",
                    RepoPath = "models/model_small.onnx",
                    SelectedFiles = new[]
                    {
                        "models/model_small.onnx"
                    },
                    RepositoryFiles = new[]
                    {
                        "models/model_large.onnx",
                        "models/model_small.onnx"
                    },
                    State = VisualScenarioState.ConversionNeeded
                });

            VisualScenarioService.ApplyScenario(scenario);

            var manifest = AssetDatabase.LoadAssetAtPath<ProjectManifestAsset>(
                VisualScenarioService.GetGeneratedManifestAssetPath(scenario, 0));
            var rawMetadataPath = VisualScenarioService.GetSnapshotMetadataPath("sandbox/single-file-selected", "sandbox-single-file-001");
            var rawMetadata = JsonUtility.FromJson<RawCacheMetadataDocument>(File.ReadAllText(rawMetadataPath));

            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest.Models, Has.Length.EqualTo(1));
            Assert.That(manifest.Models[0].Files.Select(file => file.Path), Is.EqualTo(new[] { "models/model_small.onnx" }));
            Assert.That(manifest.Models[0].Files.Select(file => file.Optional), Is.EqualTo(new[] { false }));
            Assert.That(rawMetadata.RepositoryFiles.Select(file => file.RepoPath), Is.EqualTo(new[]
            {
                "models/model_large.onnx",
                "models/model_small.onnx"
            }));
            Assert.That(rawMetadata.Files.Select(file => file.RepoPath), Is.EqualTo(new[] { "models/model_small.onnx" }));
        }

        /// <summary>
        /// Verifies partial-warning scenario persists fake import diagnostics into import metadata JSON.
        /// </summary>
        [Test]
        public void ApplyScenario_WritesPartialConversionWarningIntoImportMetadata()
        {
            var scenario = CreateScenario(
                new VisualScenarioEntry
                {
                    Repository = "sandbox/partial-warning-test",
                    Revision = "main",
                    ResolvedCommitHash = "sandbox-partial-warning-001",
                    State = VisualScenarioState.PartialConversionWarning,
                    PartialWarningRepoPath = "models/model_bnb4.onnx",
                    PartialWarningDetail = "unsupported operators remain."
                });

            VisualScenarioService.ApplyScenario(scenario);

            var metadataAbsolutePath = Path.Combine(
                Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                VisualScenarioService.GetSharedImportMetadataProjectRelativePath(scenario.Entries[0]));
            var metadataContents = File.ReadAllText(metadataAbsolutePath);

            Assert.That(metadataContents, Does.Contain("Partial ONNX conversion"));
            Assert.That(metadataContents, Does.Contain("model_bnb4.onnx"));
            Assert.That(metadataContents, Does.Contain("warning"));
        }

        /// <summary>
        /// Verifies cleanup removes generated manifest and fake local artifacts for scenario entries.
        /// </summary>
        [Test]
        public void CleanupScenario_RemovesGeneratedArtifacts()
        {
            var scenario = CreateScenario(
                new VisualScenarioEntry
                {
                    Repository = "sandbox/cleanup-test",
                    Revision = "main",
                    ResolvedCommitHash = "sandbox-cleanup-001",
                    State = VisualScenarioState.Imported
                });

            VisualScenarioService.ApplyScenario(scenario);
            VisualScenarioService.CleanupScenario(scenario);

            Assert.That(
                AssetDatabase.LoadAssetAtPath<ProjectManifestAsset>(VisualScenarioService.GetGeneratedManifestAssetPath(scenario, 0)),
                Is.Null);
            Assert.That(File.Exists(VisualScenarioService.GetRevisionRefPath(scenario.Entries[0])), Is.False);
            Assert.That(File.Exists(VisualScenarioService.GetRuntimeModelPath(scenario.Entries[0])), Is.False);
        }

        /// <summary>
        /// Verifies duplicate repo declarations materialize into separate project manifest assets so discovery can see multiple dependents.
        /// </summary>
        [Test]
        public void ApplyScenario_CreatesSeparateManifestAssetsWhenScenarioRepeatsRepository()
        {
            var scenario = CreateScenario(
                new VisualScenarioEntry
                {
                    Repository = "sandbox/shared-repo-test",
                    Revision = "main",
                    ResolvedCommitHash = "sandbox-shared-repo-001",
                    State = VisualScenarioState.ConversionNeeded
                },
                new VisualScenarioEntry
                {
                    Repository = "sandbox/shared-repo-test",
                    Revision = "main",
                    ResolvedCommitHash = "sandbox-shared-repo-001",
                    State = VisualScenarioState.ConversionNeeded
                });

            VisualScenarioService.ApplyScenario(scenario);

            var firstManifest = AssetDatabase.LoadAssetAtPath<ProjectManifestAsset>(
                VisualScenarioService.GetGeneratedManifestAssetPath(scenario, 0));
            var secondManifest = AssetDatabase.LoadAssetAtPath<ProjectManifestAsset>(
                VisualScenarioService.GetGeneratedManifestAssetPath(scenario, 1));

            Assert.That(firstManifest, Is.Not.Null);
            Assert.That(secondManifest, Is.Not.Null);
            Assert.That(VisualScenarioService.GetGeneratedManifestAssetPath(scenario, 0), Is.Not.EqualTo(VisualScenarioService.GetGeneratedManifestAssetPath(scenario, 1)));
            Assert.That(firstManifest.Models, Has.Length.EqualTo(1));
            Assert.That(secondManifest.Models, Has.Length.EqualTo(1));
            Assert.That(firstManifest.Models[0].Id, Is.EqualTo("sandbox/shared-repo-test"));
            Assert.That(secondManifest.Models[0].Id, Is.EqualTo("sandbox/shared-repo-test"));
            Assert.That(firstManifest.Models[0].Revision, Is.EqualTo("main"));
            Assert.That(secondManifest.Models[0].Revision, Is.EqualTo("main"));
        }

        /// <summary>
        /// Verifies revision-ambiguity scenarios generate separate manifests and revision refs for the same repository.
        /// </summary>
        [Test]
        public void ApplyScenario_CreatesSeparateManifestAssetsForRevisionAmbiguity()
        {
            var scenario = CreateScenario(
                new VisualScenarioEntry
                {
                    Repository = "sandbox/revision-ambiguity-test",
                    Revision = "main",
                    ResolvedCommitHash = "sandbox-ambiguity-main-001",
                    State = VisualScenarioState.ConversionNeeded
                },
                new VisualScenarioEntry
                {
                    Repository = "sandbox/revision-ambiguity-test",
                    Revision = "refs/pr/42",
                    ResolvedCommitHash = "sandbox-ambiguity-pr42-001",
                    State = VisualScenarioState.ConversionNeeded
                });

            VisualScenarioService.ApplyScenario(scenario);

            var mainManifest = AssetDatabase.LoadAssetAtPath<ProjectManifestAsset>(
                VisualScenarioService.GetGeneratedManifestAssetPath(scenario, 0));
            var prManifest = AssetDatabase.LoadAssetAtPath<ProjectManifestAsset>(
                VisualScenarioService.GetGeneratedManifestAssetPath(scenario, 1));

            Assert.That(mainManifest, Is.Not.Null);
            Assert.That(prManifest, Is.Not.Null);
            Assert.That(mainManifest.Models, Has.Length.EqualTo(1));
            Assert.That(prManifest.Models, Has.Length.EqualTo(1));
            Assert.That(mainManifest.Models[0].Revision, Is.EqualTo("main"));
            Assert.That(prManifest.Models[0].Revision, Is.EqualTo("refs/pr/42"));
            Assert.That(File.Exists(VisualScenarioService.GetRevisionRefPath(scenario.Entries[0])), Is.True);
            Assert.That(File.Exists(VisualScenarioService.GetRevisionRefPath(scenario.Entries[1])), Is.True);
        }

        private VisualScenarioAsset CreateScenario(params VisualScenarioEntry[] entries)
        {
            var scenario = ScriptableObject.CreateInstance<VisualScenarioAsset>();
            scenario.Entries = entries ?? Array.Empty<VisualScenarioEntry>();
            AssetDatabase.CreateAsset(scenario, _scenarioAssetPath);
            return scenario;
        }

        [Serializable]
        private sealed class RepositoryFileDocument
        {
            public string RepoPath;
            public long SizeInBytes;
        }

        [Serializable]
        private sealed class RawCacheFileDocument
        {
            public string RepoPath;
            public string CachedFilePath;
            public long SizeInBytes;
        }

        [Serializable]
        private sealed class RawCacheMetadataDocument
        {
            public RepositoryFileDocument[] RepositoryFiles;
            public RawCacheFileDocument[] Files;
        }
    }
}
