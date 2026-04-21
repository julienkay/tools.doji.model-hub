using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Doji.AI.ModelHub.Sandbox.Editor.Tests
{
    /// <summary>
    /// Covers local fake artifact generation used by companion-project visual HF Hub scenarios.
    /// </summary>
    internal sealed class VisualScenarioServiceTests
    {
        private string _cacheRoot;
        private string _scenarioAssetPath;

        [SetUp]
        public void SetUp()
        {
            _cacheRoot = Path.Combine(Path.GetTempPath(), "Doji.ModelHub.VisualSandboxTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_cacheRoot);
            Environment.SetEnvironmentVariable("DOJI_HF_HUB_CACHE_ROOT", _cacheRoot);
            _scenarioAssetPath = AssetDatabase.GenerateUniqueAssetPath("Assets/HF Hub/Scenario Sandbox/Scenario Test Asset.asset");
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.LoadMainAssetAtPath(_scenarioAssetPath) != null)
            {
                AssetDatabase.DeleteAsset(_scenarioAssetPath);
            }

            AssetDatabase.DeleteAsset("Assets/HF Hub/Scenario Sandbox/Generated");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            var sandboxStreamingRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "StreamingAssets", "hf-hub", "sandbox"));
            if (Directory.Exists(sandboxStreamingRoot))
            {
                Directory.Delete(sandboxStreamingRoot, true);
            }

            var importsRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "Doji", "HfHub", "imports"));
            if (Directory.Exists(importsRoot))
            {
                Directory.Delete(importsRoot, true);
            }

            if (Directory.Exists(_cacheRoot))
            {
                Directory.Delete(_cacheRoot, true);
            }

            Environment.SetEnvironmentVariable("DOJI_HF_HUB_CACHE_ROOT", null);
        }

        /// <summary>
        /// Verifies import-needed scenario writes cache artifacts but no import metadata yet.
        /// </summary>
        [Test]
        public void ApplyScenario_CreatesImportNeededArtifactsWithoutImportMetadata()
        {
            var scenario = CreateScenario(
                new VisualScenarioEntry
                {
                    Repository = "sandbox/import-needed-test",
                    Revision = "main",
                    ResolvedCommitHash = "sandbox-import-needed-001",
                    State = VisualScenarioState.ImportNeeded
                });

            VisualScenarioService.ApplyScenario(scenario);

            var importMetadataAbsolutePath = Path.Combine(
                Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                VisualScenarioService.GetImportMetadataProjectRelativePath(scenario.Entries[0]));

            Assert.That(File.Exists(VisualScenarioService.GetRevisionRefPath(scenario.Entries[0])), Is.True);
            Assert.That(File.Exists(Path.Combine(_cacheRoot, "raw", "sandbox--import-needed-test", "snapshots", "sandbox-import-needed-001", "snapshot.hfhub.json")), Is.True);
            Assert.That(File.Exists(importMetadataAbsolutePath), Is.False);
            Assert.That(
                AssetDatabase.LoadAssetAtPath<ProjectManifestAsset>(VisualScenarioService.GetGeneratedManifestAssetPath(scenario, 0)),
                Is.Not.Null);
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
            Assert.That(AssetDatabase.LoadMainAssetAtPath(VisualScenarioService.GetRuntimeSentisAssetPath(scenario.Entries[0])), Is.Null);
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
                    State = VisualScenarioState.ImportNeeded
                },
                new VisualScenarioEntry
                {
                    Repository = "sandbox/shared-repo-test",
                    Revision = "main",
                    ResolvedCommitHash = "sandbox-shared-repo-001",
                    State = VisualScenarioState.ImportNeeded
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
                    State = VisualScenarioState.ImportNeeded
                },
                new VisualScenarioEntry
                {
                    Repository = "sandbox/revision-ambiguity-test",
                    Revision = "refs/pr/42",
                    ResolvedCommitHash = "sandbox-ambiguity-pr42-001",
                    State = VisualScenarioState.ImportNeeded
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
    }
}
