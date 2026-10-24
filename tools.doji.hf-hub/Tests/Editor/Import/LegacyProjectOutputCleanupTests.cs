using System.IO;
using NUnit.Framework;
using UnityEditor;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers removing the files older HF Hub versions generated in StreamingAssets, without touching pulled models or
    /// files HF Hub did not generate.
    /// </summary>
    internal sealed class LegacyProjectOutputCleanupTests
    {
        private const string Root = "Assets/StreamingAssets/hf-hub-cleanup-test";
        private TestProjectStateScope _projectScope;

        [SetUp]
        public void SetUp()
        {
            _projectScope = new TestProjectStateScope(Root);
        }

        [TearDown]
        public void TearDown()
        {
            _projectScope.Dispose();
        }

        [Test]
        public void Clean_RemovesOldGeneratedFilesAndKeepsEverythingElse()
        {
            Write("registries/tools.doji.a/hf-hub-runtime.json");
            Write("doji/old-model/onnx/model.sentis");
            Write("models/doji/pulled/onnx/model.sentis");
            Write("someone/else/notes.txt");
            Write("someone/else/model.sentis");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            var result = LegacyProjectOutputCleanup.Clean(Root);

            Assert.That(result.Deleted, Is.EquivalentTo(new[] { $"{Root}/registries", $"{Root}/doji" }));
            Assert.That(result.Kept, Is.EqualTo(new[] { $"{Root}/someone" }));
            Assert.That(Directory.Exists(Absolute("registries")), Is.False);
            Assert.That(Directory.Exists(Absolute("doji")), Is.False);
            Assert.That(File.Exists(Absolute("models/doji/pulled/onnx/model.sentis")), Is.True, "Pulled models stay.");
            Assert.That(File.Exists(Absolute("someone/else/notes.txt")), Is.True);
        }

        [Test]
        public void Clean_DoesNothingWithoutTheFolder()
        {
            var result = LegacyProjectOutputCleanup.Clean(Root + "-missing");

            Assert.That(result.Deleted, Is.Empty);
            Assert.That(result.Kept, Is.Empty);
        }

        private static void Write(string relativePath)
        {
            var path = Absolute(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "x");
        }

        private static string Absolute(string relativePath)
        {
            return ProjectPaths.ToAbsolutePath($"{Root}/{relativePath}");
        }
    }
}
