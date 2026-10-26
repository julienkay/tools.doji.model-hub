using System;
using System.Linq;
using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers the Inspector model picker data and how file labels survive manifest edits.
    /// </summary>
    internal sealed class HubModelPickerTests
    {
        /// <summary>
        /// Verifies the picker lists only model files, with their labels, and marks each as included, not turned on, or
        /// not set up.
        /// </summary>
        [Test]
        public void Build_ListsModelFilesWithTheirState()
        {
            var dependency = new DiscoveredModelDependency(
                "tools.doji.midas",
                "MiDaS",
                "1.0.0",
                "/tmp/midas",
                "/tmp/midas/model-hub-manifest.json",
                "hash",
                new ModelDependencyDefinition("julienkay/sentis-MiDaS", "main", new[]
                {
                    new ModelDependencyFileDefinition("config.json"),
                    new ModelDependencyFileDefinition("onnx/small.onnx", label: "Small"),
                    new ModelDependencyFileDefinition("onnx/large.onnx", optional: true),
                    new ModelDependencyFileDefinition("onnx/tiny.onnx", optional: true)
                }),
                new[] { "onnx/tiny.onnx" });
            var status = new DependencyStatus(
                dependency, DependencyState.Missing, DependencyReasonCode.None, "Missing.", null, null, null, null, null, null, null);
            var included = HubModelFile.FromRegistry(
                new RuntimeRegistry
                {
                    packages = new[]
                    {
                        new RuntimePackageEntry
                        {
                            packageId = "tools.doji.midas",
                            models = new[] { new RuntimeModelEntry { repoId = "julienkay/sentis-midas", files = new[] { new RuntimeModelFile { repoPath = "onnx/small.onnx" } } } }
                        }
                    }
                },
                null);

            var entries = HubModelPicker.Build(new StatusReport(Array.Empty<ManifestDiscoveryIssue>(), new[] { status }), included);

            Assert.That(entries.Select(entry => entry.RepoPath), Is.EqualTo(new[] { "onnx/large.onnx", "onnx/small.onnx", "onnx/tiny.onnx" }));
            var byPath = entries.ToDictionary(entry => entry.RepoPath);
            Assert.That(byPath["onnx/small.onnx"].State, Is.EqualTo(HubModelPicker.FileState.Included), "Repository ids match without case.");
            Assert.That(byPath["onnx/small.onnx"].DisplayName, Is.EqualTo("Small"));
            Assert.That(byPath["onnx/large.onnx"].State, Is.EqualTo(HubModelPicker.FileState.NotTurnedOn));
            Assert.That(byPath["onnx/tiny.onnx"].State, Is.EqualTo(HubModelPicker.FileState.NotSetUp));
            Assert.That(byPath["onnx/small.onnx"].OwnerName, Is.EqualTo("MiDaS"));
        }

        /// <summary>
        /// Verifies a manifest written from the Inspector keeps labels and leaves out empty ones.
        /// </summary>
        [Test]
        public void TryBuildManifestJson_KeepsLabelsAndOmitsEmptyOnes()
        {
            var success = ModelManifestInspectorUtility.TryBuildManifestJson(
                1,
                new[]
                {
                    new ManifestModelEntry("julienkay", "sentis-MiDaS", "main", new[]
                    {
                        new ModelDependencyFileDefinition("onnx/small.onnx", label: "Small (63 MB)"),
                        new ModelDependencyFileDefinition("onnx/large.onnx", optional: true)
                    })
                },
                out var json,
                out var errorMessage);

            Assert.That(success, Is.True, errorMessage);
            Assert.That(json, Does.Contain(@"""label"": ""Small (63 MB)"""));
            Assert.That(json.Split(new[] { "\"label\"" }, StringSplitOptions.None), Has.Length.EqualTo(2), "Only the labelled file has a label.");
            Assert.That(ManifestParser.TryParse(json, out var manifest, out errorMessage), Is.True, errorMessage);
            Assert.That(manifest.Models[0].FileEntries.Select(file => file.Label), Is.EqualTo(new[] { null, "Small (63 MB)" }));
        }
    }
}
