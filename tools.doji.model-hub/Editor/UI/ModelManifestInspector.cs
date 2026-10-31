using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Shows the manifest editor for AI Model Hub model manifests and Unity's built-in inspector for other text assets.
    /// </summary>
    [CustomEditor(typeof(TextAsset), false)]
    internal sealed class ModelManifestInspector : UnityEditor.Editor
    {
        private ManifestEditorElement _editor;
        private UnityEditor.Editor _builtInInspector;

        public override VisualElement CreateInspectorGUI()
        {
            var assetPath = AssetDatabase.GetAssetPath(target);
            if (!ModelManifestInspectorUtility.IsModelManifestAssetPath(assetPath))
            {
                return null;
            }

            var root = new VisualElement();
            UiKit.ApplyTheme(root, ManifestEditorElement.StyleSheet);
            root.AddToClassList("me-host");
            _editor = new ManifestEditorElement(
                new AssetManifestDocumentStore(assetPath),
                new ManifestEditorOptions { ShowOpenModelHubButton = true });
            root.Add(_editor);
            return root;
        }

        public override void OnInspectorGUI()
        {
            if (_builtInInspector == null)
            {
                var builtInInspectorType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.TextAssetInspector");
                if (builtInInspectorType != null)
                {
                    CreateCachedEditor(target, builtInInspectorType, ref _builtInInspector);
                }
            }

            if (_builtInInspector != null)
            {
                _builtInInspector.OnInspectorGUI();
                return;
            }

            DrawDefaultInspector();
        }

        private void OnDisable()
        {
            ManifestInspectorUnsavedChanges.PromptLater(_editor, target);
            if (_builtInInspector != null)
            {
                DestroyImmediate(_builtInInspector);
                _builtInInspector = null;
            }
        }
    }

    /// <summary>
    /// Asks about unapplied manifest edits after an inspector closes, and reselects the manifest on Cancel.
    /// </summary>
    internal static class ManifestInspectorUnsavedChanges
    {
        public static void PromptLater(ManifestEditorElement editor, UnityEngine.Object target)
        {
            if (editor == null || !editor.HasPendingChanges)
            {
                return;
            }

            EditorApplication.delayCall += () =>
            {
                if (!editor.PromptForUnsavedChanges() && target != null)
                {
                    Selection.activeObject = target;
                    EditorGUIUtility.PingObject(target);
                }
            };
        }
    }

    /// <summary>
    /// Resolves model manifest inspector state from Unity asset paths and hides the package-path plumbing.
    /// </summary>
    internal static class ModelManifestInspectorUtility
    {
        internal static bool IsModelManifestAssetPath(string assetPath)
        {
            return !string.IsNullOrWhiteSpace(assetPath) &&
                string.Equals(Path.GetFileName(assetPath), Hub.ManifestFileName, StringComparison.Ordinal) &&
                (assetPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase) ||
                    assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase));
        }

        internal static UnityEditor.PackageManager.PackageInfo FindPackageInfoForAssetPath(string assetPath)
        {
            return IsModelManifestAssetPath(assetPath)
                ? UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath)
                : null;
        }

        internal static bool TryLoadManifest(
            string assetPath,
            out PackageManifest manifest,
            out string rawJson,
            out string absolutePath,
            out string errorMessage)
        {
            manifest = null;
            rawJson = null;
            absolutePath = null;
            errorMessage = null;

            if (!TryGetAbsoluteManifestPath(assetPath, out absolutePath, out errorMessage))
            {
                return false;
            }

            try
            {
                rawJson = File.ReadAllText(absolutePath);
            }
            catch (Exception exception)
            {
                errorMessage = $"Failed to read manifest: {exception.Message}";
                return false;
            }

            return ManifestParser.TryParse(rawJson, out manifest, out errorMessage);
        }

        /// <summary>
        /// Loads a manifest file as editor rows, with revisions as the file writes them.
        /// </summary>
        internal static bool TryLoadManifestEntries(
            string assetPath,
            out int version,
            out ManifestModelEntry[] models,
            out string errorMessage)
        {
            version = 1;
            models = Array.Empty<ManifestModelEntry>();
            if (!TryLoadManifest(assetPath, out var manifest, out _, out _, out errorMessage))
            {
                return false;
            }

            version = manifest.Version;
            models = ToEntries(manifest);
            return true;
        }

        internal static ManifestModelEntry[] ToEntries(PackageManifest manifest)
        {
            return (manifest?.Models ?? Array.Empty<ModelDependencyDefinition>())
                .Select(model => ManifestModelEntry.Create(model.Id, model.Revision, model.FileEntries.ToArray()))
                .ToArray();
        }

        internal static string BuildVirtualPackageManifestAssetPath(string packageId)
        {
            return string.IsNullOrWhiteSpace(packageId)
                ? null
                : $"Packages/{packageId}/{Hub.ManifestFileName}";
        }

        /// <summary>
        /// Returns the JSON the rows would save as, so the editor can tell whether anything changed. Incomplete rows
        /// count too.
        /// </summary>
        internal static string BuildStateKey(int version, ManifestModelEntry[] models)
        {
            return ManifestWriter.ToJson(version, ToDrafts(models));
        }

        private static IEnumerable<ManifestModelDraft> ToDrafts(IEnumerable<ManifestModelEntry> models)
        {
            return (models ?? Array.Empty<ManifestModelEntry>())
                .Select(model => new ManifestModelDraft(
                    model?.ModelId ?? string.Empty,
                    string.IsNullOrWhiteSpace(model?.Revision) ? null : model.Revision,
                    model?.HasExplicitFileSelection == true ? model.FileEntries : null));
        }

        internal static bool TryBuildManifestJson(
            int version,
            ManifestModelEntry[] models,
            out string serializedJson,
            out string errorMessage)
        {
            serializedJson = null;
            errorMessage = null;

            var drafts = ToDrafts(models).ToArray();
            if (!ManifestFactory.TryCreateManifest(version, drafts, out var manifest, out errorMessage))
            {
                return false;
            }

            serializedJson = ManifestWriter.ToJson(manifest.Version, drafts);
            return true;
        }

        internal static bool TryLoadProjectManifestAsset(
            ProjectManifestAsset asset,
            out int version,
            out ManifestModelEntry[] models,
            out string errorMessage)
        {
            version = 1;
            models = Array.Empty<ManifestModelEntry>();
            errorMessage = null;

            if (asset == null)
            {
                errorMessage = "Project manifest asset is missing.";
                return false;
            }

            var serializedObject = new SerializedObject(asset);
            var versionProperty = serializedObject.FindProperty("<Version>k__BackingField");
            var modelsProperty = serializedObject.FindProperty("<Models>k__BackingField");
            if (versionProperty == null || modelsProperty == null)
            {
                errorMessage = "Project manifest asset schema could not be read.";
                return false;
            }

            version = versionProperty.intValue;
            models = Enumerable.Range(0, modelsProperty.arraySize)
                .Select(index =>
                {
                    var modelProperty = modelsProperty.GetArrayElementAtIndex(index);
                    var filesProperty = modelProperty.FindPropertyRelative("<Files>k__BackingField");
                    return ManifestModelEntry.Create(
                        modelProperty.FindPropertyRelative("<Id>k__BackingField")?.stringValue,
                        modelProperty.FindPropertyRelative("<Revision>k__BackingField")?.stringValue,
                        filesProperty == null
                            ? null
                            : Enumerable.Range(0, filesProperty.arraySize)
                                .Select(fileIndex =>
                                {
                                    var fileProperty = filesProperty.GetArrayElementAtIndex(fileIndex);
                                    return new ModelDependencyFileDefinition(
                                        fileProperty.FindPropertyRelative("<Path>k__BackingField")?.stringValue,
                                        fileProperty.FindPropertyRelative("<Optional>k__BackingField")?.boolValue == true,
                                        fileProperty.FindPropertyRelative("<Label>k__BackingField")?.stringValue,
                                        ReadConversion(fileProperty));
                                })
                                .ToArray());
                })
                .ToArray();
            return true;
        }

        internal static bool TryWriteProjectManifestAssetDocument(
            ProjectManifestAsset asset,
            int version,
            ManifestModelEntry[] models,
            out string errorMessage)
        {
            errorMessage = null;
            if (asset == null)
            {
                errorMessage = "Project manifest asset is missing.";
                return false;
            }

            var drafts = ToDrafts(models).ToArray();
            if (!ManifestFactory.TryCreateManifest(version, drafts, out _, out errorMessage))
            {
                return false;
            }

            var serializedObject = new SerializedObject(asset);
            var versionProperty = serializedObject.FindProperty("<Version>k__BackingField");
            var modelsProperty = serializedObject.FindProperty("<Models>k__BackingField");
            if (versionProperty == null || modelsProperty == null)
            {
                errorMessage = "Project manifest asset schema could not be written.";
                return false;
            }

            versionProperty.intValue = version;
            modelsProperty.arraySize = drafts.Length;
            for (var i = 0; i < drafts.Length; i++)
            {
                var modelProperty = modelsProperty.GetArrayElementAtIndex(i);
                modelProperty.FindPropertyRelative("<Id>k__BackingField").stringValue = drafts[i].Id;
                modelProperty.FindPropertyRelative("<Revision>k__BackingField").stringValue = drafts[i].Revision ?? string.Empty;

                var filesProperty = modelProperty.FindPropertyRelative("<Files>k__BackingField");
                var files = drafts[i].FileEntries ?? Array.Empty<ModelDependencyFileDefinition>();
                filesProperty.arraySize = files.Count;
                for (var fileIndex = 0; fileIndex < files.Count; fileIndex++)
                {
                    var fileProperty = filesProperty.GetArrayElementAtIndex(fileIndex);
                    fileProperty.FindPropertyRelative("<Path>k__BackingField").stringValue = files[fileIndex].Path;
                    fileProperty.FindPropertyRelative("<Optional>k__BackingField").boolValue = files[fileIndex].Optional;
                    fileProperty.FindPropertyRelative("<Label>k__BackingField").stringValue = files[fileIndex].Label ?? string.Empty;
                    WriteConversion(fileProperty, files[fileIndex].Conversion);
                }
            }

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            var assetPath = AssetDatabase.GetAssetPath(asset);
            if (!string.IsNullOrWhiteSpace(assetPath))
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            }
            return true;
        }

        private static OnnxConversionRecipe ReadConversion(SerializedProperty fileProperty)
        {
            var quantization = fileProperty.FindPropertyRelative("<Quantization>k__BackingField")?.stringValue;
            var shapesProperty = fileProperty.FindPropertyRelative("<InputShapes>k__BackingField");
            var shapes = shapesProperty == null
                ? Array.Empty<ProjectManifestInputShapeAssetEntry>()
                : Enumerable.Range(0, shapesProperty.arraySize)
                    .Select(index =>
                    {
                        var shapeProperty = shapesProperty.GetArrayElementAtIndex(index);
                        var dimensionsProperty = shapeProperty.FindPropertyRelative("<Dimensions>k__BackingField");
                        return new ProjectManifestInputShapeAssetEntry
                        {
                            Name = shapeProperty.FindPropertyRelative("<Name>k__BackingField")?.stringValue,
                            Dimensions = dimensionsProperty == null
                                ? Array.Empty<int>()
                                : Enumerable.Range(0, dimensionsProperty.arraySize)
                                    .Select(dimension => dimensionsProperty.GetArrayElementAtIndex(dimension).intValue)
                                    .ToArray()
                        };
                    })
                    .ToArray();
            return ProjectManifestFileAssetEntry.ToRecipe(quantization, shapes);
        }

        private static void WriteConversion(SerializedProperty fileProperty, OnnxConversionRecipe recipe)
        {
            fileProperty.FindPropertyRelative("<Quantization>k__BackingField").stringValue = recipe == null
                ? string.Empty
                : OnnxConversionSettings.NormalizeQuantizationMode(recipe.QuantizationMode);
            var shapes = recipe?.StaticInputShapeOverrides ?? Array.Empty<OnnxStaticInputShapeOverride>();
            var shapesProperty = fileProperty.FindPropertyRelative("<InputShapes>k__BackingField");
            shapesProperty.arraySize = shapes.Length;
            for (var index = 0; index < shapes.Length; index++)
            {
                var shapeProperty = shapesProperty.GetArrayElementAtIndex(index);
                shapeProperty.FindPropertyRelative("<Name>k__BackingField").stringValue = shapes[index]?.InputName ?? string.Empty;
                var dimensions = shapes[index]?.Dimensions ?? Array.Empty<int>();
                var dimensionsProperty = shapeProperty.FindPropertyRelative("<Dimensions>k__BackingField");
                dimensionsProperty.arraySize = dimensions.Length;
                for (var dimension = 0; dimension < dimensions.Length; dimension++)
                {
                    dimensionsProperty.GetArrayElementAtIndex(dimension).intValue = dimensions[dimension];
                }
            }
        }

        internal static bool TryGetPackageRelativePath(string assetPath, string packageId, out string relativePath)
        {
            relativePath = null;
            if (string.IsNullOrWhiteSpace(assetPath) || string.IsNullOrWhiteSpace(packageId))
            {
                return false;
            }

            var prefix = $"Packages/{packageId}/";
            if (!assetPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            relativePath = assetPath.Substring(prefix.Length).Replace('/', Path.DirectorySeparatorChar);
            return true;
        }

        internal static bool IsManifestEditable(string assetPath, string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(assetPath) || string.IsNullOrWhiteSpace(absolutePath))
            {
                return false;
            }

            if (assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                return !IsFileReadOnly(absolutePath);
            }

            var packageInfo = FindPackageInfoForAssetPath(assetPath);
            if (packageInfo == null || string.IsNullOrWhiteSpace(packageInfo.resolvedPath))
            {
                return false;
            }

            var normalizedResolvedPath = packageInfo.resolvedPath.Replace('\\', '/');
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."))
                .Replace('\\', '/')
                .TrimEnd('/');
            if (normalizedResolvedPath.StartsWith($"{projectRoot}/Library/PackageCache/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return !IsFileReadOnly(absolutePath);
        }

        private static bool TryGetAbsoluteManifestPath(string assetPath, out string absolutePath, out string errorMessage)
        {
            absolutePath = null;
            errorMessage = null;

            if (!IsModelManifestAssetPath(assetPath))
            {
                errorMessage = "Selected asset is not an AI Model Hub model manifest.";
                return false;
            }

            if (assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                absolutePath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", assetPath.Replace('/', Path.DirectorySeparatorChar)));
                return true;
            }

            var packageInfo = FindPackageInfoForAssetPath(assetPath);
            if (packageInfo == null || string.IsNullOrWhiteSpace(packageInfo.resolvedPath))
            {
                errorMessage = $"Could not resolve package root for '{assetPath}'.";
                return false;
            }

            if (!TryGetPackageRelativePath(assetPath, packageInfo.name, out var relativePath))
            {
                errorMessage = $"Could not resolve package-relative path for '{assetPath}'.";
                return false;
            }

            absolutePath = Path.Combine(packageInfo.resolvedPath, relativePath);
            return true;
        }

        private static bool IsFileReadOnly(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath) || !File.Exists(absolutePath))
            {
                return true;
            }

            return (File.GetAttributes(absolutePath) & FileAttributes.ReadOnly) != 0;
        }
    }
}
