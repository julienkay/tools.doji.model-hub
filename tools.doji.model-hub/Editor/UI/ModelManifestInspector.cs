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
        [Serializable]
        internal sealed class ManifestDocument
        {
            public int version;
            public ManifestModelDocument[] models;
        }

        [Serializable]
        internal sealed class ManifestModelDocument
        {
            public string id;
            public string revision;
            public ManifestFileDocument[] files;
        }

        [Serializable]
        internal sealed class ManifestFileDocument
        {
            public string path;
            public bool optional;
            public string label;

            /// <summary>
            /// Package-default conversion settings. Not part of the JSON form: <c>JsonUtility</c> cannot read the
            /// <c>conversion</c> object, so loaders copy it from the parsed manifest with <see cref="AttachConversions"/>.
            /// </summary>
            [NonSerialized]
            public OnnxConversionRecipe conversion;

            public ModelDependencyFileDefinition ToDefinition()
            {
                return new ModelDependencyFileDefinition(path, optional, label, conversion);
            }
        }

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

        internal static bool TryLoadManifestDocument(
            string assetPath,
            out ManifestDocument document,
            out string absolutePath,
            out string errorMessage)
        {
            document = null;
            if (!TryLoadManifest(assetPath, out var manifest, out var rawJson, out absolutePath, out errorMessage))
            {
                return false;
            }

            document = JsonUtility.FromJson<ManifestDocument>(rawJson);
            if (document == null)
            {
                errorMessage = "Failed to deserialize model manifest document.";
                return false;
            }

            document.models ??= Array.Empty<ManifestModelDocument>();
            AttachConversions(document, manifest);
            return true;
        }

        /// <summary>
        /// Copies each file's <c>conversion</c> settings from the parsed manifest into a document read with
        /// <c>JsonUtility</c>, so saving the document keeps them.
        /// </summary>
        internal static void AttachConversions(ManifestDocument document, PackageManifest manifest)
        {
            if (document?.models == null || manifest == null)
            {
                return;
            }

            foreach (var model in document.models)
            {
                var parsedModel = manifest.Models.FirstOrDefault(item =>
                    string.Equals(item.Id, model?.id?.Trim(), StringComparison.Ordinal));
                foreach (var file in model?.files ?? Array.Empty<ManifestFileDocument>())
                {
                    if (file == null)
                    {
                        continue;
                    }

                    var normalizedPath = ModelDependencyDefinition.NormalizeRepoPath(file.path);
                    file.conversion = parsedModel?.FileEntries
                        .FirstOrDefault(entry => string.Equals(entry.Path, normalizedPath, StringComparison.Ordinal))
                        ?.Conversion;
                }
            }
        }

        internal static string BuildVirtualPackageManifestAssetPath(string packageId)
        {
            return string.IsNullOrWhiteSpace(packageId)
                ? null
                : $"Packages/{packageId}/{Hub.ManifestFileName}";
        }

        internal static string BuildStateKey(int version, ManifestModelEntry[] models)
        {
            var document = new ManifestDocument
            {
                version = version,
                models = (models ?? Array.Empty<ManifestModelEntry>())
                    .Select(model => new ManifestModelDocument
                    {
                        id = model?.ModelId ?? string.Empty,
                        revision = model?.Revision ?? string.Empty,
                        files = ToFileDocuments(model?.FileEntries)
                    })
                    .ToArray()
            };
            return JsonUtility.ToJson(document, false);
        }

        internal static bool TryBuildManifestJson(
            int version,
            ManifestModelEntry[] models,
            out string serializedJson,
            out string errorMessage)
        {
            serializedJson = null;
            errorMessage = null;

            var drafts = (models ?? Array.Empty<ManifestModelEntry>())
                .Select(model => new ManifestModelDraft(
                    model?.ModelId,
                    string.IsNullOrWhiteSpace(model?.Revision) ? "main" : model.Revision,
                    model?.HasExplicitFileSelection == true ? model.FileEntries : null))
                .ToArray();
            if (!ManifestFactory.TryCreateManifest(version, drafts, out var manifest, out errorMessage))
            {
                return false;
            }

            serializedJson = ManifestWriter.ToJson(
                manifest.Version,
                (models ?? Array.Empty<ManifestModelEntry>())
                    .Select(model => new ManifestModelDraft(
                        model?.ModelId ?? string.Empty,
                        string.IsNullOrWhiteSpace(model?.Revision) ? null : model.Revision,
                        model?.HasExplicitFileSelection == true ? model.FileEntries : null)));
            return true;
        }

        internal static ManifestDocument BuildDocument(int version, ManifestModelEntry[] models)
        {
            return new ManifestDocument
            {
                version = version,
                models = (models ?? Array.Empty<ManifestModelEntry>())
                    .Select(model => new ManifestModelDocument
                    {
                        id = model?.ModelId ?? string.Empty,
                        revision = string.IsNullOrWhiteSpace(model?.Revision) ? null : model.Revision,
                        files = model?.HasExplicitFileSelection == true ? ToFileDocuments(model.FileEntries) : null
                    })
                    .ToArray()
            };
        }

        internal static bool TryLoadProjectManifestAssetDocument(
            ProjectManifestAsset asset,
            out ManifestDocument document,
            out string errorMessage)
        {
            document = null;
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

            document = new ManifestDocument
            {
                version = versionProperty.intValue,
                models = Enumerable.Range(0, modelsProperty.arraySize)
                    .Select(index =>
                    {
                        var modelProperty = modelsProperty.GetArrayElementAtIndex(index);
                        var filesProperty = modelProperty.FindPropertyRelative("<Files>k__BackingField");
                        return new ManifestModelDocument
                        {
                            id = modelProperty.FindPropertyRelative("<Id>k__BackingField")?.stringValue,
                            revision = modelProperty.FindPropertyRelative("<Revision>k__BackingField")?.stringValue,
                            files = filesProperty == null
                                ? null
                                : Enumerable.Range(0, filesProperty.arraySize)
                                    .Select(fileIndex =>
                                    {
                                        var fileProperty = filesProperty.GetArrayElementAtIndex(fileIndex);
                                        return new ManifestFileDocument
                                        {
                                            path = fileProperty.FindPropertyRelative("<Path>k__BackingField")?.stringValue,
                                            optional = fileProperty.FindPropertyRelative("<Optional>k__BackingField")?.boolValue == true,
                                            label = fileProperty.FindPropertyRelative("<Label>k__BackingField")?.stringValue,
                                            conversion = ReadConversion(fileProperty)
                                        };
                                    })
                                    .ToArray()
                        };
                    })
                    .ToArray()
            };

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

            var document = BuildDocument(version, models);
            var drafts = (document.models ?? Array.Empty<ManifestModelDocument>())
                .Select(model => new ManifestModelDraft(model.id, model.revision, ToDefinitions(model.files)))
                .ToArray();
            if (!ManifestFactory.TryCreateManifest(document.version, drafts, out _, out errorMessage))
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
            modelsProperty.arraySize = document.models?.Length ?? 0;
            for (var i = 0; i < modelsProperty.arraySize; i++)
            {
                var modelProperty = modelsProperty.GetArrayElementAtIndex(i);
                var sourceModel = document.models[i];
                modelProperty.FindPropertyRelative("<Id>k__BackingField").stringValue = sourceModel.id;
                modelProperty.FindPropertyRelative("<Revision>k__BackingField").stringValue = sourceModel.revision ?? string.Empty;

                var filesProperty = modelProperty.FindPropertyRelative("<Files>k__BackingField");
                var files = sourceModel.files ?? Array.Empty<ManifestFileDocument>();
                filesProperty.arraySize = files.Length;
                for (var fileIndex = 0; fileIndex < files.Length; fileIndex++)
                {
                    var fileProperty = filesProperty.GetArrayElementAtIndex(fileIndex);
                    fileProperty.FindPropertyRelative("<Path>k__BackingField").stringValue = files[fileIndex]?.path;
                    fileProperty.FindPropertyRelative("<Optional>k__BackingField").boolValue = files[fileIndex]?.optional == true;
                    fileProperty.FindPropertyRelative("<Label>k__BackingField").stringValue = files[fileIndex]?.label ?? string.Empty;
                    WriteConversion(fileProperty, files[fileIndex]?.conversion);
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

        private static ManifestModelEntry EditableModelEntryFromManifest(ModelDependencyDefinition model)
        {
            var identifier = model?.Id ?? string.Empty;
            var slashIndex = identifier.IndexOf('/');
            if (slashIndex <= 0 || slashIndex >= identifier.Length - 1)
            {
                return new ManifestModelEntry(
                    string.Empty,
                    identifier,
                    model?.Revision ?? string.Empty,
                    model?.FileEntries?.ToArray());
            }

            return new ManifestModelEntry(
                identifier.Substring(0, slashIndex),
                identifier.Substring(slashIndex + 1),
                model?.Revision ?? string.Empty,
                model?.FileEntries?.ToArray());
        }

        private static ManifestFileDocument[] ToFileDocuments(IReadOnlyList<ModelDependencyFileDefinition> files)
        {
            return (files ?? Array.Empty<ModelDependencyFileDefinition>())
                .Select(file => new ManifestFileDocument { path = file.Path, optional = file.Optional, label = file.Label, conversion = file.Conversion })
                .ToArray();
        }

        private static ModelDependencyFileDefinition[] ToDefinitions(IReadOnlyList<ManifestFileDocument> files)
        {
            return (files ?? Array.Empty<ManifestFileDocument>())
                .Select(file => file?.ToDefinition())
                .ToArray();
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
