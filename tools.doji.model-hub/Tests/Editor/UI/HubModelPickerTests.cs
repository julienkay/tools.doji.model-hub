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
        /// not downloaded.
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
            Assert.That(byPath["onnx/tiny.onnx"].State, Is.EqualTo(HubModelPicker.FileState.NeedsDownload));
            Assert.That(byPath["onnx/small.onnx"].OwnerName, Is.EqualTo("MiDaS"));
        }

        /// <summary>
        /// Verifies a downloaded file whose ONNX model waits for a conversion asks to convert, with the reason, and a
        /// downloaded file that needs only the import step asks to set up.
        /// </summary>
        [Test]
        public void Build_TellsConversionFromSetup()
        {
            var dependency = new DiscoveredModelDependency(
                "tools.doji.midas", "MiDaS", "1.0.0", "/tmp/midas", "/tmp/midas/model-hub-manifest.json",
                new ModelDependencyDefinition("julienkay/sentis-MiDaS", "main", new[]
                {
                    new ModelDependencyFileDefinition("onnx/small.onnx"),
                    new ModelDependencyFileDefinition("sentis/small.sentis")
                }),
                Array.Empty<string>());
            var status = new DependencyStatus(
                dependency, DependencyState.Missing, DependencyReasonCode.ImportBindingMissing, "Not imported.",
                null, null, null, null, null, null, null, null,
                new[] { "onnx/small.onnx" }, ConversionCause.SettingsChanged);

            var byPath = HubModelPicker.Build(new StatusReport(Array.Empty<ManifestDiscoveryIssue>(), new[] { status }), null)
                .ToDictionary(entry => entry.RepoPath);

            Assert.That(byPath["onnx/small.onnx"].State, Is.EqualTo(HubModelPicker.FileState.NeedsConversion));
            Assert.That(byPath["onnx/small.onnx"].ConversionCause, Is.EqualTo(ConversionCause.SettingsChanged));
            Assert.That(byPath["sentis/small.sentis"].State, Is.EqualTo(HubModelPicker.FileState.NeedsSetup));
        }

        /// <summary>
        /// Verifies the status line under the dropdown names the next step for each state, shows progress while AI
        /// Model Hub works, and shows nothing for a file that loads. An optional file that is not turned on reads as
        /// not downloaded, like any other file that is not in the cache.
        /// </summary>
        [TestCase(HubModelPicker.FileState.Included, false, null, null)]
        [TestCase(HubModelPicker.FileState.NotTurnedOn, false, "Not downloaded yet", "Download")]
        [TestCase(HubModelPicker.FileState.NeedsDownload, false, "Not downloaded yet", "Download")]
        [TestCase(HubModelPicker.FileState.NeedsDownload, true, "Downloading… 42%", "Details")]
        [TestCase(HubModelPicker.FileState.NeedsConversion, false, "Downloaded, needs a conversion to Sentis", "Convert")]
        [TestCase(HubModelPicker.FileState.NeedsConversion, true, "Converting… 42%", "Details")]
        [TestCase(HubModelPicker.FileState.NeedsSetup, false, "Downloaded, not set up for this project yet", "Set up")]
        public void GetStatus_NamesTheNextStep(HubModelPicker.FileState state, bool syncing, string text, string action)
        {
            var entry = new HubModelPicker.Entry { RepoId = "julienkay/sentis-MiDaS", RepoPath = "onnx/small.onnx", State = state };

            var status = HubModelReferenceDrawer.GetStatus(entry, entry.RepoPath, syncing, 0.42f);

            Assert.That(status.Text, Is.EqualTo(text));
            Assert.That(status.Action, Is.EqualTo(action));
        }

        /// <summary>
        /// Verifies a file that no manifest declares shows a warning without an action, and None shows nothing.
        /// </summary>
        [Test]
        public void GetStatus_WarnsAboutAFileNoManifestDeclares()
        {
            var status = HubModelReferenceDrawer.GetStatus(null, "onnx/gone.onnx", false, 0f);

            Assert.That(status.Kind, Is.EqualTo(HubModelReferenceDrawer.StatusKind.NotDeclared));
            Assert.That(status.Action, Is.Null);
            Assert.That(HubModelReferenceDrawer.GetStatus(null, null, false, 0f).Kind, Is.EqualTo(HubModelReferenceDrawer.StatusKind.None));
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
