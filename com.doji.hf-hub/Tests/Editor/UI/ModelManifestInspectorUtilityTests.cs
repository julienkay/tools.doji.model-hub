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
        /// Ensures dirty-state tracking preserves the exact typed revision text instead of collapsing it to manifest defaults.
        /// </summary>
        [Test]
        public void BuildStateKey_UsesSharedManifestModelEntries()
        {
            var stateKey = ModelManifestInspectorUtility.BuildStateKey(
                1,
                new[]
                {
                    new ManifestModelEntry("hf-internal-testing", "tiny-random-bert", "main")
                });

            Assert.That(stateKey, Does.Contain("\"id\":\"hf-internal-testing/tiny-random-bert\""));
            Assert.That(stateKey, Does.Contain("\"revision\":\"main\""));
        }

        /// <summary>
        /// Ensures manifest dirty-state tracking also includes explicit file selections.
        /// </summary>
        [Test]
        public void BuildStateKey_IncludesExplicitFileSelection()
        {
            var stateKey = ModelManifestInspectorUtility.BuildStateKey(
                1,
                new[]
                {
                    new ManifestModelEntry(
                        "hf-internal-testing",
                        "tiny-random-bert",
                        "main",
                        new[] { "onnx/model.onnx" })
                });

            Assert.That(stateKey, Does.Contain("\"files\":[\"onnx/model.onnx\"]"));
        }

        /// <summary>
        /// Ensures manifest dirty-state tracking also includes optional file selections.
        /// </summary>
        [Test]
        public void BuildStateKey_IncludesOptionalFileSelection()
        {
            var stateKey = ModelManifestInspectorUtility.BuildStateKey(
                1,
                new[]
                {
                    new ManifestModelEntry(
                        "hf-internal-testing",
                        "tiny-random-bert",
                        "main",
                        files: null,
                        optionalFiles: new[] { "onnx/model_q4.onnx" })
                });

            Assert.That(stateKey, Does.Contain("\"optionalFiles\":[\"onnx/model_q4.onnx\"]"));
        }

        /// <summary>
        /// Ensures manifest serialization omits empty revision but preserves an explicitly entered `main`.
        /// </summary>
        [Test]
        public void TryBuildManifestJson_PreservesExplicitMainButOmitsEmptyRevision()
        {
            var success = ModelManifestInspectorUtility.TryBuildManifestJson(
                1,
                new[]
                {
                    new ManifestModelEntry("hf-internal-testing", "tiny-random-bert", string.Empty),
                    new ManifestModelEntry("hf-internal-testing", "tiny-random-gpt2", "main")
                },
                out var serializedJson,
                out var errorMessage);

            Assert.That(success, Is.True);
            Assert.That(errorMessage, Is.Null);
            Assert.That(serializedJson, Does.Contain("\"id\": \"hf-internal-testing/tiny-random-bert\""));
            Assert.That(serializedJson, Does.Contain("\"id\": \"hf-internal-testing/tiny-random-gpt2\""));
            Assert.That(serializedJson, Does.Contain("\"revision\": \"main\""));
            Assert.That(serializedJson, Does.Not.Contain("\"id\": \"hf-internal-testing/tiny-random-bert\",\n      \"revision\""));
        }

        /// <summary>
        /// Ensures manifest serialization writes selected files only when explicit file selection is present.
        /// </summary>
        [Test]
        public void TryBuildManifestJson_WritesExplicitFilesOnlyForSelectedEntries()
        {
            var success = ModelManifestInspectorUtility.TryBuildManifestJson(
                1,
                new[]
                {
                    new ManifestModelEntry("hf-internal-testing", "tiny-random-bert", "main"),
                    new ManifestModelEntry("hf-internal-testing", "tiny-random-gpt2", "main", new[] { "onnx/model.onnx" })
                },
                out var serializedJson,
                out var errorMessage);

            Assert.That(success, Is.True);
            Assert.That(errorMessage, Is.Null);
            Assert.That(serializedJson, Does.Contain(@"""files"": ["));
            Assert.That(serializedJson, Does.Contain(@"""onnx/model.onnx"""));
        }

        /// <summary>
        /// Ensures manifest serialization writes optional files when a row declares them.
        /// </summary>
        [Test]
        public void TryBuildManifestJson_WritesOptionalFiles()
        {
            var success = ModelManifestInspectorUtility.TryBuildManifestJson(
                1,
                new[]
                {
                    new ManifestModelEntry(
                        "hf-internal-testing",
                        "tiny-random-bert",
                        "main",
                        files: new[] { "tokenizer.json" },
                        optionalFiles: new[] { "onnx/model.onnx" })
                },
                out var serializedJson,
                out var errorMessage);

            Assert.That(success, Is.True);
            Assert.That(errorMessage, Is.Null);
            Assert.That(serializedJson, Does.Contain(@"""optionalFiles"": ["));
            Assert.That(serializedJson, Does.Contain(@"""onnx/model.onnx"""));
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

        /// <summary>
        /// Ensures ScriptableObject project manifests convert into shared editor document shape without losing explicit file selection.
        /// </summary>
        [Test]
        public void TryLoadProjectManifestAssetDocument_ReadsSerializedAssetModels()
        {
            var asset = ScriptableObject.CreateInstance<ProjectManifestAsset>();
            SetProjectManifest(asset, 1, new[]
            {
                new ProjectManifestModelAssetEntry
                {
                    Id = "onnx-community/tiny-clip-test-ONNX",
                    Revision = "refs/pr/7",
                    Files = new[] { "onnx/model.onnx" },
                    OptionalFiles = new[] { "onnx/model_q4.onnx" }
                }
            });

            var success = ModelManifestInspectorUtility.TryLoadProjectManifestAssetDocument(asset, out var document, out var errorMessage);

            Assert.That(success, Is.True);
            Assert.That(errorMessage, Is.Null);
            Assert.That(document.version, Is.EqualTo(1));
            Assert.That(document.models[0].id, Is.EqualTo("onnx-community/tiny-clip-test-ONNX"));
            Assert.That(document.models[0].revision, Is.EqualTo("refs/pr/7"));
            Assert.That(document.models[0].files, Is.EqualTo(new[] { "onnx/model.onnx" }));
            Assert.That(document.models[0].optionalFiles, Is.EqualTo(new[] { "onnx/model_q4.onnx" }));
        }

        /// <summary>
        /// Ensures ScriptableObject project manifests reuse shared validation and normalization before persisting edits.
        /// </summary>
        [Test]
        public void TryWriteProjectManifestAssetDocument_NormalizesAndWritesSharedEditorModels()
        {
            var asset = ScriptableObject.CreateInstance<ProjectManifestAsset>();

            var success = ModelManifestInspectorUtility.TryWriteProjectManifestAssetDocument(
                asset,
                1,
                new[]
                {
                    new ManifestModelEntry(
                        "onnx-community",
                        "tiny-clip-test-ONNX",
                        string.Empty,
                        new[] { " tokenizer.json ", "onnx\\model.onnx", "onnx/model.onnx" },
                        new[] { " onnx\\model_q4.onnx ", "onnx/model_q4.onnx" })
                },
                out var errorMessage);

            Assert.That(success, Is.True);
            Assert.That(errorMessage, Is.Null);

            var serializedObject = new UnityEditor.SerializedObject(asset);
            Assert.That(serializedObject.FindProperty("<Version>k__BackingField").intValue, Is.EqualTo(1));
            var modelProperty = serializedObject.FindProperty("<Models>k__BackingField").GetArrayElementAtIndex(0);
            Assert.That(modelProperty.FindPropertyRelative("<Id>k__BackingField").stringValue, Is.EqualTo("onnx-community/tiny-clip-test-ONNX"));
            Assert.That(modelProperty.FindPropertyRelative("<Revision>k__BackingField").stringValue, Is.Empty);
            var filesProperty = modelProperty.FindPropertyRelative("<Files>k__BackingField");
            Assert.That(filesProperty.arraySize, Is.EqualTo(2));
            Assert.That(filesProperty.GetArrayElementAtIndex(0).stringValue, Is.EqualTo("onnx/model.onnx"));
            Assert.That(filesProperty.GetArrayElementAtIndex(1).stringValue, Is.EqualTo("tokenizer.json"));
            var optionalFilesProperty = modelProperty.FindPropertyRelative("<OptionalFiles>k__BackingField");
            Assert.That(optionalFilesProperty.arraySize, Is.EqualTo(1));
            Assert.That(optionalFilesProperty.GetArrayElementAtIndex(0).stringValue, Is.EqualTo("onnx/model_q4.onnx"));
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
                var files = models[i].Files ?? System.Array.Empty<string>();
                filesProperty.arraySize = files.Length;
                for (var fileIndex = 0; fileIndex < files.Length; fileIndex++)
                {
                    filesProperty.GetArrayElementAtIndex(fileIndex).stringValue = files[fileIndex];
                }

                var optionalFilesProperty = modelProperty.FindPropertyRelative("<OptionalFiles>k__BackingField");
                var optionalFiles = models[i].OptionalFiles ?? System.Array.Empty<string>();
                optionalFilesProperty.arraySize = optionalFiles.Length;
                for (var fileIndex = 0; fileIndex < optionalFiles.Length; fileIndex++)
                {
                    optionalFilesProperty.GetArrayElementAtIndex(fileIndex).stringValue = optionalFiles[fileIndex];
                }
            }

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
