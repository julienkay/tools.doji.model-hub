using System.Linq;
using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers per-file model formats, ONNX companion files, and how a dependency's selection maps to model files.
    /// </summary>
    internal sealed class ModelFilesTests
    {
        private static readonly string[] MidasFiles =
        {
            ".gitattributes",
            "README.md",
            "onnx/midas_v21_small_256.onnx",
            "onnx/dpt_beit_large_512.onnx",
            "sentis/midas_v21_small_256.sentis",
            "sentis/dpt_beit_large_512.sentis"
        };

        [TestCase("onnx/model.onnx", ModelFileFormat.Onnx)]
        [TestCase("MODEL.ONNX", ModelFileFormat.Onnx)]
        [TestCase("sentis/model.sentis", ModelFileFormat.Sentis)]
        [TestCase("onnx/model.onnx_data", ModelFileFormat.None)]
        [TestCase("tokenizer.json", ModelFileFormat.None)]
        [TestCase("model.gguf", ModelFileFormat.None)]
        [TestCase(null, ModelFileFormat.None)]
        public void GetFormat_UsesTheExtension(string repoPath, ModelFileFormat expected)
        {
            Assert.That(ModelFiles.GetFormat(repoPath), Is.EqualTo(expected));
        }

        /// <summary>
        /// Verifies external weights next to an ONNX file are its companions, and files of other models are not.
        /// </summary>
        [Test]
        public void GetCompanionFiles_FindsExternalWeightsNextToTheModel()
        {
            var files = new[]
            {
                "onnx/model.onnx",
                "onnx/model.onnx_data",
                "onnx/model.onnx.data",
                "onnx/model.onnx_data_1",
                "onnx/model.onnx_data_2",
                "onnx/model_fp16.onnx",
                "onnx/model_fp16.onnx_data",
                "model.onnx_data",
                "onnx/model.onnx_data_"
            };

            Assert.That(
                ModelFiles.GetCompanionFiles("onnx/model.onnx", files),
                Is.EqualTo(new[] { "onnx/model.onnx.data", "onnx/model.onnx_data", "onnx/model.onnx_data_1", "onnx/model.onnx_data_2" }));
            Assert.That(ModelFiles.GetCompanionFiles("sentis/model.sentis", files), Is.Empty);
        }

        [Test]
        public void Describe_LabelsRepositoryFormats()
        {
            Assert.That(ModelFiles.Describe(MidasFiles), Is.EqualTo(RepositoryModelFormat.Mixed));
            Assert.That(ModelFiles.Describe(new[] { "a.onnx", "config.json" }), Is.EqualTo(RepositoryModelFormat.Onnx));
            Assert.That(ModelFiles.Describe(new[] { "a.sentis" }), Is.EqualTo(RepositoryModelFormat.Sentis));
            Assert.That(ModelFiles.Describe(new[] { "README.md" }), Is.EqualTo(RepositoryModelFormat.None));
            Assert.That(ModelFiles.GetLabel(RepositoryModelFormat.Mixed), Is.EqualTo("ONNX + Sentis"));
        }

        /// <summary>
        /// Verifies a listed ONNX file brings its external weights into the selection, so they download with it.
        /// </summary>
        [Test]
        public void GetSelectedRepoPaths_AddsCompanionFilesOfListedOnnxFiles()
        {
            var dependency = CreateDependency("onnx/model.onnx");

            var selected = dependency.GetSelectedRepoPaths(new[] { "onnx/model.onnx", "onnx/model.onnx_data", "onnx/other.onnx_data" });

            Assert.That(selected, Is.EqualTo(new[] { "onnx/model.onnx", "onnx/model.onnx_data" }));
            Assert.That(dependency.GetSelectedModelRepoPaths(new[] { "onnx/model.onnx", "onnx/model.onnx_data" }), Is.EqualTo(new[] { "onnx/model.onnx" }));
        }

        /// <summary>
        /// Verifies a mixed repository works when the manifest lists one format, and each listed file keeps its format.
        /// </summary>
        [Test]
        public void ListedFilesOfAMixedRepository_SelectEachFormat()
        {
            var sentis = CreateDependency("sentis/midas_v21_small_256.sentis");
            var onnx = CreateDependency("onnx/midas_v21_small_256.onnx");

            Assert.That(sentis.GetSelectionProblem(MidasFiles), Is.Null);
            Assert.That(sentis.GetSelectedModelRepoPaths(MidasFiles), Is.EqualTo(new[] { "sentis/midas_v21_small_256.sentis" }));
            Assert.That(sentis.GetSelectedOnnxRepoPaths(MidasFiles), Is.Empty);
            Assert.That(onnx.GetSelectionProblem(MidasFiles), Is.Null);
            Assert.That(onnx.GetSelectedOnnxRepoPaths(MidasFiles), Is.EqualTo(new[] { "onnx/midas_v21_small_256.onnx" }));
        }

        /// <summary>
        /// Verifies AI Model Hub never picks a format silently for a whole-repository dependency on a mixed repository.
        /// </summary>
        [Test]
        public void GetSelectionProblem_RejectsWholeRepositoryOfAMixedRepository()
        {
            var problem = CreateDependency().GetSelectionProblem(MidasFiles);

            Assert.That(problem, Does.Contain("both ONNX and Sentis files"));
            Assert.That(CreateDependency().GetSelectionProblem(new[] { "a.sentis", "config.json" }), Is.Null);
        }

        [Test]
        public void GetSelectionProblem_RejectsSelectionsWithoutModelFiles()
        {
            Assert.That(CreateDependency("config.json").GetSelectionProblem(MidasFiles), Does.Contain("does not select any model file"));
            Assert.That(CreateDependency().GetSelectionProblem(new[] { "README.md" }), Does.Contain("does not select any model file"));
        }

        /// <summary>
        /// Verifies optional Sentis files count as a choice the owner must make, like optional ONNX files.
        /// </summary>
        [Test]
        public void RequiresOptionalModelSelection_CoversSentisFiles()
        {
            var dependency = new DiscoveredModelDependency(
                "tools.doji.test",
                "Test Package",
                "1.0.0",
                "/tmp/tools.doji.test",
                "/tmp/tools.doji.test/model-hub-manifest.json",
                "hash",
                new ModelDependencyDefinition(
                    "julienkay/sentis-MiDaS",
                    "main",
                    new[] { new ModelDependencyFileDefinition("sentis/midas_v21_small_256.sentis", optional: true) }));

            Assert.That(dependency.RequiresOptionalModelSelection, Is.True);
        }

        private static DiscoveredModelDependency CreateDependency(params string[] files)
        {
            return new DiscoveredModelDependency(
                "tools.doji.test",
                "Test Package",
                "1.0.0",
                "/tmp/tools.doji.test",
                "/tmp/tools.doji.test/model-hub-manifest.json",
                "hash",
                new ModelDependencyDefinition(
                    "julienkay/sentis-MiDaS",
                    "main",
                    files.Select(file => new ModelDependencyFileDefinition(file)).ToArray()));
        }
    }
}
