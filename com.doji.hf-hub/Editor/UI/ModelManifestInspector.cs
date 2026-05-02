using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Shows a focused inspector for HF Hub model manifests while leaving normal text assets on Unity's built-in inspector.
    /// </summary>
    [CustomEditor(typeof(TextAsset), false)]
    internal sealed class ModelManifestInspector : UnityEditor.Editor
    {
        private const float ContentInset = 14f;
        private string _inspectedAssetPath;
        private ManifestDocumentEditor _editor;
        private UnityEditor.Editor _builtInInspector;
        private bool _suppressUnsavedPrompt;

        public override void OnInspectorGUI()
        {
            var assetPath = AssetDatabase.GetAssetPath(target);
            if (!ModelManifestInspectorUtility.IsModelManifestAssetPath(assetPath))
            {
                DrawFallbackInspector();
                return;
            }

            EnsureEditor(assetPath);

            var previousGuiEnabled = GUI.enabled;
            GUI.enabled = true;
            try
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(ContentInset);
                    using (new EditorGUILayout.VerticalScope())
                    {
                        _editor.Draw();
                    }
                }
            }
            finally
            {
                GUI.enabled = previousGuiEnabled;
            }
        }

        private void OnDisable()
        {
            if (!_suppressUnsavedPrompt && _editor is { HasUserEdits: true, HasPendingChanges: true })
            {
                var targetObject = target;
                var assetPath = _inspectedAssetPath;
                EditorApplication.delayCall += () =>
                {
                    _suppressUnsavedPrompt = true;
                    _editor?.PromptForUnsavedChanges(
                        targetObject,
                        () =>
                        {
                            Selection.activeObject = targetObject;
                            if (targetObject != null)
                            {
                                EditorGUIUtility.PingObject(targetObject);
                            }
                        });
                    _suppressUnsavedPrompt = false;
                };
            }

            if (_builtInInspector != null)
            {
                DestroyImmediate(_builtInInspector);
                _builtInInspector = null;
            }
        }

        private void EnsureEditor(string assetPath)
        {
            if (string.Equals(_inspectedAssetPath, assetPath, StringComparison.Ordinal) && _editor != null)
            {
                return;
            }

            _inspectedAssetPath = assetPath;
            _editor = new ManifestDocumentEditor(
                new AssetManifestDocumentStore(assetPath),
                new ManifestDocumentEditorOptions
                {
                    Title = "Model Dependencies",
                    ShowVersion = false,
                    ShowOpenModelHubButton = true,
                    ShowReadOnlyInfoBox = true,
                    RequestRepaint = Repaint
                });
        }

        private void DrawFallbackInspector()
        {
            if (_builtInInspector == null)
            {
                var builtInInspectorType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.TextAssetInspector");
                if (builtInInspectorType != null)
                {
                    UnityEditor.Editor.CreateCachedEditor(target, builtInInspectorType, ref _builtInInspector);
                }
            }

            if (_builtInInspector != null)
            {
                _builtInInspector.OnInspectorGUI();
                return;
            }

            DrawDefaultInspector();
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
            public string[] files;
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
            if (!TryLoadManifest(assetPath, out _, out var rawJson, out absolutePath, out errorMessage))
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
            return true;
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
                        files = model?.Files?.ToArray()
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
                    model?.Files))
                .ToArray();
            if (!ManifestFactory.TryCreateManifest(version, drafts, out var manifest, out errorMessage))
            {
                return false;
            }

            var document = new ManifestDocument
            {
                version = manifest.Version,
                models = (models ?? Array.Empty<ManifestModelEntry>())
                    .Select(model => new ManifestModelDocument
                    {
                        id = model?.ModelId ?? string.Empty,
                        revision = string.IsNullOrWhiteSpace(model?.Revision) ? null : model.Revision,
                        files = model?.HasExplicitFileSelection == true ? model.Files.ToArray() : null
                    })
                    .ToArray()
            };

            serializedJson = JsonUtility.ToJson(document, true);
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
                        files = model?.HasExplicitFileSelection == true ? model.Files.ToArray() : null
                    })
                    .ToArray()
            };
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
                    model?.Revision ?? string.Empty);
            }

            return new ManifestModelEntry(
                identifier.Substring(0, slashIndex),
                identifier.Substring(slashIndex + 1),
                model?.Revision ?? string.Empty);
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
                errorMessage = "Selected asset is not an HF Hub model manifest.";
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
