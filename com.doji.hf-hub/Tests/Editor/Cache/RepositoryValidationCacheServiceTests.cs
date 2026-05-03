using System.IO;
using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers repository validation cache lookups shared across editor validation surfaces.
    /// </summary>
    internal sealed class RepositoryValidationCacheServiceTests
    {
        private string _cachePath;
        private string _cacheDirectory;
        private string _cacheContentsBackup;

        [SetUp]
        public void SetUp()
        {
            _cachePath = ProjectPaths.ToProjectAbsolutePath("Library/Doji/HfHub/model-manifest-validation/hf-hub-repo-validation.json");
            _cacheDirectory = Path.GetDirectoryName(_cachePath);
            if (File.Exists(_cachePath))
            {
                _cacheContentsBackup = File.ReadAllText(_cachePath);
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrWhiteSpace(_cacheContentsBackup))
            {
                Directory.CreateDirectory(_cacheDirectory);
                File.WriteAllText(_cachePath, _cacheContentsBackup);
                return;
            }

            if (File.Exists(_cachePath))
            {
                File.Delete(_cachePath);
            }
        }

        /// <summary>
        /// Verifies revision-aware cache entries distinguish invalid manifest revisions from valid default `main`.
        /// </summary>
        [Test]
        public void RevisionValidationCache_KeepsRevisionScopedResultsSeparate()
        {
            RepositoryValidationCacheService.WriteCachedRepositoryRevisionExists(
                "onnx-community/tiny-clip-test-ONNX",
                "main",
                true);
            RepositoryValidationCacheService.WriteCachedRepositoryRevisionExists(
                "onnx-community/tiny-clip-test-ONNX",
                "test",
                false);

            var hasMain = RepositoryValidationCacheService.TryGetCachedRepositoryRevisionExists(
                "onnx-community/tiny-clip-test-ONNX",
                "main",
                out var mainExists);
            var hasTest = RepositoryValidationCacheService.TryGetCachedRepositoryRevisionExists(
                "onnx-community/tiny-clip-test-ONNX",
                "test",
                out var testExists);

            Assert.That(hasMain, Is.True);
            Assert.That(mainExists, Is.True);
            Assert.That(hasTest, Is.True);
            Assert.That(testExists, Is.False);
        }

        /// <summary>
        /// Verifies omitted manifest revision normalizes to the same cache key as explicit `main`.
        /// </summary>
        [Test]
        public void RevisionValidationCache_NormalizesEmptyRevisionToMain()
        {
            RepositoryValidationCacheService.WriteCachedRepositoryRevisionExists(
                "hf-internal-testing/tiny-random-bert",
                string.Empty,
                true);

            var hasMain = RepositoryValidationCacheService.TryGetCachedRepositoryRevisionExists(
                "hf-internal-testing/tiny-random-bert",
                "main",
                out var mainExists);

            Assert.That(hasMain, Is.True);
            Assert.That(mainExists, Is.True);
        }
    }
}
