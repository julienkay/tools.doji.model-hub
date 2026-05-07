using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Loads and saves one JSON-backed manifest document for a host editor surface.
    /// </summary>
    internal interface IManifestDocumentStore
    {
        string DisplayName { get; }
        bool IsEditable { get; }
        bool ShouldPersistValidationCache { get; }
        bool TryLoad(out ModelManifestInspectorUtility.ManifestDocument document, out string errorMessage);
        bool TrySave(int version, ManifestModelEntry[] models, out string errorMessage);
    }

    /// <summary>
    /// Controls host-specific chrome around the shared manifest document editor.
    /// </summary>
    internal sealed class ManifestDocumentEditorOptions
    {
        public string Title { get; set; }
        public bool ShowVersion { get; set; }
        public bool ShowOpenModelHubButton { get; set; }
        public bool ShowReadOnlyInfoBox { get; set; }
        public Action AfterApply { get; set; }
        public Action RequestRepaint { get; set; }
    }

    /// <summary>
    /// Owns shared manifest editing state, dirty tracking, validation, and apply/revert behavior.
    /// </summary>
    internal sealed class ManifestDocumentEditor
    {
        private readonly IManifestDocumentStore _store;
        private readonly ManifestDocumentEditorOptions _options;
        private readonly ManifestModelValidationController _validationController;
        private ManifestModelListEditor _modelListEditor;
        private int _version = 1;
        private string _loadedStateKey = string.Empty;
        private string _loadErrorMessage;
        private ManifestModelEntry[] _models = Array.Empty<ManifestModelEntry>();
        private bool _hasUserEdits;

        public ManifestDocumentEditor(IManifestDocumentStore store, ManifestDocumentEditorOptions options)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _options = options ?? new ManifestDocumentEditorOptions();
            _validationController = new ManifestModelValidationController(
                _store.ShouldPersistValidationCache,
                canValidate: () => _store.IsEditable,
                requestRepaint: _options.RequestRepaint ?? (() => { }));
            Reload();
        }

        public bool HasPendingChanges =>
            !string.Equals(ModelManifestInspectorUtility.BuildStateKey(_version, _models), _loadedStateKey, StringComparison.Ordinal);

        public bool HasUserEdits => _hasUserEdits;

        public void Draw()
        {
            if (string.IsNullOrWhiteSpace(_loadErrorMessage))
            {
                _validationController.RefreshFromCacheIfNeeded(GetValidationTargets());
                _validationController.QueueUnknownValidationIfNeeded();
                DrawManifestFields();
                DrawApplyRevertButtons();
            }
            else
            {
                EditorGUILayout.HelpBox(_loadErrorMessage, MessageType.Error);
            }

            if (_options.ShowOpenModelHubButton)
            {
                EditorGUILayout.Space();
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Open Model Hub", GUILayout.Width(120f)))
                    {
                        ModelHubWindow.OpenWindow();
                    }
                }
            }

            if (_options.ShowReadOnlyInfoBox && !_store.IsEditable)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(
                    "This manifest source is read-only from the current project. Edit the package source directly, or embed/localize the package first.",
                    MessageType.Info);
            }
        }

        public void Reload()
        {
            _version = 1;
            _models = Array.Empty<ManifestModelEntry>();
            _loadErrorMessage = null;
            _hasUserEdits = false;

            if (_store.TryLoad(out var document, out var errorMessage))
            {
                if (document != null)
                {
                    _version = document.version;
                    _models = (document.models ?? Array.Empty<ModelManifestInspectorUtility.ManifestModelDocument>())
                        .Select(ManifestModelEntry.FromDocumentModel)
                        .ToArray();
                }
            }
            else
            {
                _loadErrorMessage = errorMessage;
            }

            _loadedStateKey = ModelManifestInspectorUtility.BuildStateKey(_version, _models);
            _validationController.RefreshFromCache(GetValidationTargets());
            if (_modelListEditor == null)
            {
                _modelListEditor = new ManifestModelListEditor(
                    _models,
                    _store.IsEditable,
                    onChanged: () =>
                    {
                        _models = _modelListEditor.Models;
                        _hasUserEdits = true;
                    },
                    getValidationState: (modelId, revision) => _validationController.GetState(modelId, revision),
                    requestRepaint: _options.RequestRepaint);
            }
            else
            {
                _modelListEditor.SetModels(_models);
            }
        }

        public void PromptForUnsavedChanges(UnityEngine.Object targetObject, Action restoreSelection)
        {
            if (!_hasUserEdits || !HasPendingChanges)
            {
                return;
            }

            var choice = EditorUtility.DisplayDialogComplex(
                "Unsaved Model Manifest",
                $"Unapplied model manifest changes for '{_store.DisplayName}'.",
                "Apply",
                "Discard",
                "Cancel");
            switch (choice)
            {
                case 0:
                    Apply();
                    break;
                case 1:
                    Reload();
                    break;
                default:
                    EditorApplication.delayCall += () =>
                    {
                        if (targetObject != null)
                        {
                            restoreSelection?.Invoke();
                        }
                    };
                    break;
            }
        }

        private void DrawManifestFields()
        {
            if (!string.IsNullOrWhiteSpace(_options.Title))
            {
                EditorGUILayout.LabelField(_options.Title, EditorStyles.boldLabel);
            }

            if (_options.ShowVersion)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.IntField("Version", _version);
                }
            }

            EditorGUILayout.Space(2f);
            _modelListEditor.DoLayoutList();
        }

        private void DrawApplyRevertButtons()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(!HasPendingChanges))
                {
                    if (GUILayout.Button("Revert", GUILayout.Width(90f)))
                    {
                        Reload();
                        QueueDependencyValidation();
                    }
                }

                using (new EditorGUI.DisabledScope(!_store.IsEditable || !HasPendingChanges))
                {
                    if (GUILayout.Button("Apply", GUILayout.Width(90f)))
                    {
                        Apply();
                    }
                }
            }
        }

        private void Apply()
        {
            var firstIncompleteRowIndex = Array.FindIndex(_models, ManifestModelListEditor.IsIncomplete);
            if (firstIncompleteRowIndex >= 0)
            {
                EditorUtility.DisplayDialog(
                    "HF Hub Model Manifest",
                    $"Manifest entry {firstIncompleteRowIndex + 1} must define both Owner and Model before applying.",
                    "OK");
                return;
            }

            if (!_store.TrySave(_version, _models, out var errorMessage))
            {
                EditorUtility.DisplayDialog("HF Hub Model Manifest", errorMessage, "OK");
                return;
            }

            _loadedStateKey = ModelManifestInspectorUtility.BuildStateKey(_version, _models);
            _loadErrorMessage = null;
            _hasUserEdits = false;
            QueueDependencyValidation();
            GUI.FocusControl(null);
            _options.AfterApply?.Invoke();
        }

        private void QueueDependencyValidation()
        {
            _validationController.QueueValidation(GetValidationTargets());
        }

        private ManifestModelValidationController.ValidationTarget[] GetValidationTargets()
        {
            return _models
                .Where(model => model?.HasCompleteModelId == true)
                .Select(model => new ManifestModelValidationController.ValidationTarget(
                    model?.ModelId,
                    model?.Revision))
                .ToArray();
        }
    }

    /// <summary>
    /// Binds the shared editor to one manifest file already represented by a Unity asset path.
    /// </summary>
    internal sealed class AssetManifestDocumentStore : IManifestDocumentStore
    {
        private readonly string _assetPath;
        private readonly string _displayName;
        private readonly string _absolutePath;

        public AssetManifestDocumentStore(string assetPath)
        {
            _assetPath = assetPath;
            _displayName = string.IsNullOrWhiteSpace(assetPath) ? Hub.ManifestFileName : assetPath;
            ModelManifestInspectorUtility.TryLoadManifest(assetPath, out _, out _, out _absolutePath, out _);
        }

        public string DisplayName => _displayName;
        public bool IsEditable => ModelManifestInspectorUtility.IsManifestEditable(_assetPath, _absolutePath);
        public bool ShouldPersistValidationCache =>
            !string.IsNullOrWhiteSpace(_assetPath) &&
            _assetPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase);

        public bool TryLoad(out ModelManifestInspectorUtility.ManifestDocument document, out string errorMessage)
        {
            return ModelManifestInspectorUtility.TryLoadManifestDocument(_assetPath, out document, out _, out errorMessage);
        }

        public bool TrySave(int version, ManifestModelEntry[] models, out string errorMessage)
        {
            errorMessage = null;
            if (!ModelManifestInspectorUtility.TryBuildManifestJson(version, models, out var serializedJson, out errorMessage))
            {
                return false;
            }

            try
            {
                System.IO.File.WriteAllText(_absolutePath, serializedJson);
                AssetDatabase.ImportAsset(_assetPath, ImportAssetOptions.ForceUpdate);
                return true;
            }
            catch (Exception exception)
            {
                errorMessage = $"Failed to write manifest: {exception.Message}";
                return false;
            }
        }
    }

    /// <summary>
    /// Binds the shared editor to the project-scoped manifest stored under ProjectSettings.
    /// </summary>
    internal sealed class ProjectSettingsManifestDocumentStore : IManifestDocumentStore
    {
        public string DisplayName => ProjectSettingsModelManifestStore.GetManifestPath();
        public bool IsEditable => true;
        public bool ShouldPersistValidationCache => true;

        public bool TryLoad(out ModelManifestInspectorUtility.ManifestDocument document, out string errorMessage)
        {
            return ProjectSettingsModelManifestStore.TryLoadManifestDocument(null, out document, out errorMessage);
        }

        public bool TrySave(int version, ManifestModelEntry[] models, out string errorMessage)
        {
            var drafts = (models ?? Array.Empty<ManifestModelEntry>())
                .Select(model => new ManifestModelDraft(
                    model?.ModelId,
                    model?.Revision,
                    model?.Files,
                    model?.OptionalFiles))
                .ToArray();
            return ProjectSettingsModelManifestStore.TryWriteManifest(version, drafts, out errorMessage);
        }
    }

    /// <summary>
    /// Binds shared manifest editor to one `ProjectManifestAsset` so ScriptableObject manifests get same UX as JSON manifests.
    /// </summary>
    internal sealed class ProjectManifestAssetDocumentStore : IManifestDocumentStore
    {
        private readonly ProjectManifestAsset _asset;
        private readonly string _assetPath;

        public ProjectManifestAssetDocumentStore(ProjectManifestAsset asset)
        {
            _asset = asset;
            _assetPath = asset == null ? string.Empty : AssetDatabase.GetAssetPath(asset);
        }

        public string DisplayName => string.IsNullOrWhiteSpace(_assetPath) ? _asset?.name ?? "Project Manifest Asset" : _assetPath;
        public bool IsEditable => _asset != null;
        public bool ShouldPersistValidationCache => true;

        public bool TryLoad(out ModelManifestInspectorUtility.ManifestDocument document, out string errorMessage)
        {
            return ModelManifestInspectorUtility.TryLoadProjectManifestAssetDocument(_asset, out document, out errorMessage);
        }

        public bool TrySave(int version, ManifestModelEntry[] models, out string errorMessage)
        {
            return ModelManifestInspectorUtility.TryWriteProjectManifestAssetDocument(_asset, version, models, out errorMessage);
        }
    }
}
