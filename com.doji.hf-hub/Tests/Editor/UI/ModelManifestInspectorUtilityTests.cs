using NUnit.Framework;
using System.IO;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers asset-path targeting rules for the Model manifest custom inspector.
    /// </summary>
    internal sealed class ModelManifestInspectorUtilityTests
    {
        /// <summary>
        /// Ensures the inspector only activates for HF Hub manifest filenames under Unity asset roots.
        /// </summary>
        [Test]
        public void IsModelManifestAssetPath_MatchesSupportedManifestLocations()
        {
            Assert.That(
                ModelManifestInspectorUtility.IsModelManifestAssetPath("Packages/com.example.sample-package/hf-hub-manifest.json"),
                Is.True);
            Assert.That(
                ModelManifestInspectorUtility.IsModelManifestAssetPath("Assets/Local Package/hf-hub-manifest.json"),
                Is.True);
            Assert.That(
                ModelManifestInspectorUtility.IsModelManifestAssetPath("Packages/com.example.sample-package/package.json"),
                Is.False);
            Assert.That(
                ModelManifestInspectorUtility.IsModelManifestAssetPath("ProjectSettings/hf-hub-manifest.json"),
                Is.False);
        }

        /// <summary>
        /// Ensures package-relative path extraction preserves the manifest filename.
        /// </summary>
        [Test]
        public void TryGetPackageRelativePath_ReturnsPackageRelativeManifestPath()
        {
            var success = ModelManifestInspectorUtility.TryGetPackageRelativePath(
                "Packages/com.example.sample-package/hf-hub-manifest.json",
                "com.example.sample-package",
                out var relativePath);

            Assert.That(success, Is.True);
            Assert.That(relativePath.Replace('\\', '/'), Is.EqualTo("hf-hub-manifest.json"));
        }

        /// <summary>
        /// Ensures the virtual package manifest asset path matches the package id convention.
        /// </summary>
        [Test]
        public void BuildVirtualPackageManifestAssetPath_UsesPackageAssetConvention()
        {
            var assetPath = ModelManifestInspectorUtility.BuildVirtualPackageManifestAssetPath("com.example.sample-package");

            Assert.That(assetPath, Is.EqualTo("Packages/com.example.sample-package/hf-hub-manifest.json"));
        }

        /// <summary>
        /// Ensures project-local manifest files are editable when the backing file is not read-only.
        /// </summary>
        [Test]
        public void IsManifestEditable_ReturnsTrueForWritableProjectAsset()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var absolutePath = Path.Combine(projectRoot, "Assets", "Local Package", "hf-hub-manifest.json");

            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
            File.WriteAllText(absolutePath, "{}");

            try
            {
                var isEditable = ModelManifestInspectorUtility.IsManifestEditable(
                    "Assets/Local Package/hf-hub-manifest.json",
                    absolutePath);

                Assert.That(isEditable, Is.True);
            }
            finally
            {
                File.Delete(absolutePath);
            }
        }
    }
}
