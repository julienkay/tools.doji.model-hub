using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    internal sealed class ModelHubFileIconTests
    {
        /// <summary>
        /// Files tab header icons keep AI models visually distinct from text-style files.
        /// </summary>
        [TestCase("model.onnx", ModelHubWindow.FileHeaderIconKind.Model)]
        [TestCase("weights.safetensors", ModelHubWindow.FileHeaderIconKind.Model)]
        [TestCase("notes.txt", ModelHubWindow.FileHeaderIconKind.Text)]
        [TestCase("README.md", ModelHubWindow.FileHeaderIconKind.Text)]
        [TestCase("config.json", ModelHubWindow.FileHeaderIconKind.Json)]
        [TestCase("tokenizer.model", ModelHubWindow.FileHeaderIconKind.Default)]
        public void GetFileHeaderIconKind_MapsKnownExtensions(string repoPath, ModelHubWindow.FileHeaderIconKind expectedKind)
        {
            Assert.That(ModelHubWindow.GetFileHeaderIconKind(repoPath), Is.EqualTo(expectedKind));
        }
    }
}
