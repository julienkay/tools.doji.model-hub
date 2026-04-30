using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Networking;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Shows a focused inspector for HF Hub model manifests while leaving normal text assets on Unity's built-in inspector.
    /// Unity imports package JSON files as <see cref="TextAsset"/>, so the inspector has to hook that broad type and opt in only
    /// for <c>hf-hub-manifest.json</c> assets. The class also carries editor-only workarounds for package read-only scopes,
    /// domain reload state loss, and background repository validation.
    /// </summary>
    [CustomEditor(typeof(TextAsset), false)]
    internal sealed class ModelManifestInspector : UnityEditor.Editor
    {
        private const float ContentInset = 14f;
        private GUIStyle _infoBoxStyle;
        private GUIStyle _infoLabelStyle;
        private string _inspectedAssetPath;
        private int _version;
        private string _loadedStateKey;
        private ModelManifestInspectorUtility.ManifestDocument _loadedDocument;
        private string _absolutePath;
        private string _loadErrorMessage;
        private bool _isEditable;
        private EditableModelEntry[] _models = Array.Empty<EditableModelEntry>();
        private DependencyValidationState[] _validationStates = Array.Empty<DependencyValidationState>();
        private int _validationVersion;
        private int _seenValidationCacheRevision = -1;
        private ReorderableList _modelList;
        private UnityEditor.Editor _builtInInspector;
        private bool _hasUserEdits;
        private bool _suppressUnsavedPrompt;

        public override void OnInspectorGUI()
        {
            var assetPath = AssetDatabase.GetAssetPath(target);
            if (!ModelManifestInspectorUtility.IsModelManifestAssetPath(assetPath))
            {
                DrawFallbackInspector();
                return;
            }

            InitializeStyles();
            EnsureState(assetPath);

            // Unity commonly wraps package asset inspectors in a disabled GUI scope. Re-enable the body here so package-cache
            // manifests can still expose useful actions while local and embedded packages stay editable through our own gates.
            var previousGuiEnabled = GUI.enabled;
            GUI.enabled = true;
            try
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(ContentInset);
                    using (new EditorGUILayout.VerticalScope())
                    {
                        DrawManifestInspectorBody(assetPath);
                    }
                }
            }
            finally
            {
                GUI.enabled = previousGuiEnabled;
            }
        }

        private void DrawManifestInspectorBody(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(_loadErrorMessage))
            {
                RefreshValidationStatesFromCacheIfNeeded();
                DrawManifestFields();
                DrawApplyRevertButtons(assetPath);
            }
            else
            {
                EditorGUILayout.HelpBox(_loadErrorMessage, MessageType.Error);
            }

            EditorGUILayout.Space();
            DrawOpenModelHubButton();

            if (!_isEditable)
            {
                DrawReadOnlyInfoBox();
            }
        }

        private void DrawManifestFields()
        {
            EditorGUILayout.LabelField("Manifest", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.IntField("Version", _version);
            }

            EditorGUILayout.LabelField("Model Dependencies", EditorStyles.boldLabel);
            EditorGUILayout.Space(2f);
            _modelList.DoLayoutList();
        }

        private void DrawApplyRevertButtons(string assetPath)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(!HasPendingChanges))
                {
                    if (GUILayout.Button("Revert", GUILayout.Width(90f)))
                    {
                        ReloadEditableState(_loadedDocument);
                        QueueDependencyValidation();
                    }
                }

                using (new EditorGUI.DisabledScope(!_isEditable || !HasPendingChanges))
                {
                    if (GUILayout.Button("Apply", GUILayout.Width(90f)))
                    {
                        ApplyChanges(assetPath);
                    }
                }
            }
        }

        private static void DrawOpenModelHubButton()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open Model Hub", GUILayout.Width(120f)))
                {
                    ModelHubWindow.OpenWindow();
                }
            }
        }

        private void DrawReadOnlyInfoBox()
        {
            EditorGUILayout.Space();
            using (new EditorGUILayout.VerticalScope(_infoBoxStyle))
            {
                EditorGUILayout.LabelField(
                    "This manifest source is read-only from the current project. Edit the package source directly, or embed/localize the package first.",
                    _infoLabelStyle);
            }
        }

        private void OnDisable()
        {
            // Selection changes and script reloads both tear down the inspector through OnDisable, so the unsaved-changes
            // prompt needs to be delayed until Unity finishes switching inspector targets.
            if (!_suppressUnsavedPrompt && _hasUserEdits && HasPendingChanges)
            {
                var targetObject = target;
                var assetPath = _inspectedAssetPath;
                EditorApplication.delayCall += () => PromptForUnsavedChanges(targetObject, assetPath);
            }

            if (_builtInInspector != null)
            {
                DestroyImmediate(_builtInInspector);
                _builtInInspector = null;
            }
        }

        private void DrawFallbackInspector()
        {
            if (_builtInInspector == null)
            {
                // Keep non-manifest text assets on Unity's own inspector instead of trying to recreate that UI.
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

        private void InitializeStyles()
        {
            _infoBoxStyle ??= new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(10, 10, 8, 8),
                margin = new RectOffset(0, 0, 4, 0)
            };
            _infoLabelStyle ??= new GUIStyle(EditorStyles.wordWrappedMiniLabel)
            {
                wordWrap = true,
                richText = false
            };
        }

        private void EnsureState(string assetPath)
        {
            // Domain reload can keep the selected asset while dropping transient inspector UI state such as ReorderableList.
            // Treat a missing list as a forced rebuild even when the selected path did not change.
            if (string.Equals(_inspectedAssetPath, assetPath, StringComparison.Ordinal) && _modelList != null)
            {
                return;
            }

            _inspectedAssetPath = assetPath;
            _version = 1;
            _loadedStateKey = string.Empty;
            _loadedDocument = null;
            _absolutePath = null;
            _loadErrorMessage = null;
            _isEditable = false;
            _models = Array.Empty<EditableModelEntry>();
            _validationStates = Array.Empty<DependencyValidationState>();
            _seenValidationCacheRevision = -1;
            _hasUserEdits = false;

            ModelManifestInspectorUtility.TryLoadManifestDocument(assetPath, out var document, out var absolutePath, out var errorMessage);
            _absolutePath = absolutePath;
            _loadErrorMessage = errorMessage;
            _isEditable = ModelManifestInspectorUtility.IsManifestEditable(assetPath, absolutePath);
            _loadedDocument = document;
            ReloadEditableState(document);
            ConfigureModelList();
            RefreshValidationStatesFromCache();
        }

        // Compare normalized manifest content instead of raw JSON so button state ignores formatting and implicit "main" handling.
        private bool HasPendingChanges =>
            !string.Equals(ModelManifestInspectorUtility.BuildStateKey(_version, _models), _loadedStateKey, StringComparison.Ordinal);

        private void ReloadEditableState(ModelManifestInspectorUtility.ManifestDocument document = null)
        {
            if (document != null)
            {
                _version = document.version;
                _models = (document.models ?? Array.Empty<ModelManifestInspectorUtility.ManifestModelDocument>())
                    .Select(EditableModelEntry.FromDocumentModel)
                    .ToArray();
                _loadErrorMessage = null;
                _loadedStateKey = ModelManifestInspectorUtility.BuildStateKey(_version, _models);
                _hasUserEdits = false;
                RefreshValidationStatesFromCache();
                return;
            }

            _version = 1;
            _models = Array.Empty<EditableModelEntry>();
            _loadedStateKey = ModelManifestInspectorUtility.BuildStateKey(_version, _models);
            _hasUserEdits = false;
            RefreshValidationStatesFromCache();
        }

        private void AddModel()
        {
            Array.Resize(ref _models, _models.Length + 1);
            _models[^1] = new EditableModelEntry(string.Empty, string.Empty, string.Empty);
            _hasUserEdits = true;
            ConfigureModelList();
        }

        private void RemoveModelAt(int modelIndex)
        {
            if (modelIndex < 0 || modelIndex >= _models.Length)
            {
                return;
            }

            for (var i = modelIndex; i < _models.Length - 1; i++)
            {
                _models[i] = _models[i + 1];
            }

            Array.Resize(ref _models, _models.Length - 1);
            _hasUserEdits = true;
            ConfigureModelList();
        }

        private void ConfigureModelList()
        {
            _modelList = new ReorderableList(_models, typeof(EditableModelEntry), _isEditable, true, _isEditable, _isEditable)
            {
                drawHeaderCallback = DrawModelListHeader,
                drawElementCallback = DrawModelListElement,
                onAddCallback = _ =>
                {
                    AddModel();
                },
                onRemoveCallback = list =>
                {
                    RemoveModelAt(list.index);
                },
                onReorderCallback = _ =>
                {
                    _models = _modelList.list.Cast<EditableModelEntry>().ToArray();
                    _hasUserEdits = true;
                },
                elementHeight = EditorGUIUtility.singleLineHeight + 6f
            };
        }

        private void DrawModelListHeader(Rect rect)
        {
            const float ownerWidth = 150f;
            const float revisionWidth = 120f;
            const float iconWidth = 18f;
            var modelWidth = rect.width - ownerWidth - revisionWidth - iconWidth - 20f;
            var ownerRect = new Rect(rect.x + 14f, rect.y, ownerWidth, rect.height);
            var modelRect = new Rect(ownerRect.xMax + 6f, rect.y, Mathf.Max(60f, modelWidth), rect.height);
            var revisionRect = new Rect(modelRect.xMax + 6f, rect.y, revisionWidth, rect.height);
            var iconRect = new Rect(revisionRect.xMax + 4f, rect.y, iconWidth, rect.height);

            EditorGUI.LabelField(ownerRect, "Owner");
            EditorGUI.LabelField(modelRect, "Model");
            EditorGUI.LabelField(revisionRect, "Revision");
            EditorGUI.LabelField(iconRect, GUIContent.none);
        }

        private void DrawModelListElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            if (index < 0 || index >= _models.Length)
            {
                return;
            }

            var entry = _models[index];
            rect.y += 2f;

            const float ownerWidth = 150f;
            const float revisionWidth = 120f;
            const float iconWidth = 18f;
            var ownerRect = new Rect(rect.x, rect.y, ownerWidth, EditorGUIUtility.singleLineHeight);
            var modelRect = new Rect(ownerRect.xMax + 6f, rect.y, Mathf.Max(60f, rect.width - ownerWidth - revisionWidth - iconWidth - 16f), EditorGUIUtility.singleLineHeight);
            var revisionRect = new Rect(modelRect.xMax + 6f, rect.y, revisionWidth, EditorGUIUtility.singleLineHeight);
            var iconRect = new Rect(revisionRect.xMax + 4f, rect.y, 18f, EditorGUIUtility.singleLineHeight);

            if (_isEditable)
            {
                EditorGUI.BeginChangeCheck();
                var nextOwner = EditorGUI.TextField(ownerRect, entry.Owner);
                var nextName = EditorGUI.TextField(modelRect, entry.Name);
                var nextRevision = EditorGUI.TextField(revisionRect, entry.Revision);
                if (EditorGUI.EndChangeCheck())
                {
                    entry.Owner = nextOwner;
                    entry.Name = nextName;
                    entry.Revision = nextRevision;
                    _hasUserEdits = true;
                }
            }
            else
            {
                EditorGUI.LabelField(ownerRect, entry.Owner);
                EditorGUI.LabelField(modelRect, entry.Name);
                EditorGUI.LabelField(revisionRect, entry.Revision);
            }

            DrawValidationIcon(iconRect, entry);
        }

        private void ApplyChanges(string assetPath)
        {
            if (!ModelManifestInspectorUtility.TryBuildManifestJson(_version, _models, out var serializedJson, out var errorMessage))
            {
                EditorUtility.DisplayDialog("HF Hub Model Manifest", errorMessage, "OK");
                return;
            }

            try
            {
                File.WriteAllText(_absolutePath, serializedJson);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                _loadedDocument = ModelManifestInspectorUtility.BuildDocument(_version, _models);
                _loadedStateKey = ModelManifestInspectorUtility.BuildStateKey(_version, _models);
                _loadErrorMessage = null;
                _hasUserEdits = false;
                QueueDependencyValidation();
                GUI.FocusControl(null);
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog(
                    "HF Hub Model Manifest",
                    $"Failed to write manifest: {exception.Message}",
                    "OK");
            }
        }

        /// <summary>
        /// Keeps the split owner/model editing model while still round-tripping manifest ids as <c>owner/name</c>.
        /// </summary>
        internal sealed class EditableModelEntry
        {
            public EditableModelEntry(string owner, string name, string revision)
            {
                Owner = owner ?? string.Empty;
                Name = name ?? string.Empty;
                Revision = revision ?? string.Empty;
            }

            public string Owner { get; set; }

            public string Name { get; set; }

            public string Revision { get; set; }

            public string ModelId => string.IsNullOrWhiteSpace(Owner)
                ? Name ?? string.Empty
                : $"{Owner?.Trim()}/{Name?.Trim()}";

            public static EditableModelEntry FromDocumentModel(ModelManifestInspectorUtility.ManifestModelDocument model)
            {
                var identifier = model?.id ?? string.Empty;
                var slashIndex = identifier.IndexOf('/');
                if (slashIndex <= 0 || slashIndex >= identifier.Length - 1)
                {
                    return new EditableModelEntry(string.Empty, identifier, model?.revision ?? string.Empty);
                }

                return new EditableModelEntry(
                    identifier.Substring(0, slashIndex),
                    identifier.Substring(slashIndex + 1),
                    model.revision ?? string.Empty);
            }
        }

        private void PromptForUnsavedChanges(UnityEngine.Object targetObject, string assetPath)
        {
            if (_suppressUnsavedPrompt || !_hasUserEdits || !HasPendingChanges)
            {
                return;
            }

            var manifestLabel = string.IsNullOrWhiteSpace(assetPath)
                ? Hub.ManifestFileName
                : assetPath;
            var choice = EditorUtility.DisplayDialogComplex(
                "Unsaved Model Manifest",
                $"Unapplied model manifest changes for '{manifestLabel}'.",
                "Apply",
                "Discard",
                "Cancel");
            switch (choice)
            {
                case 0:
                    _suppressUnsavedPrompt = true;
                    ApplyChanges(assetPath);
                    break;
                case 1:
                    _suppressUnsavedPrompt = true;
                    ReloadEditableState(_loadedDocument);
                    break;
                default:
                    EditorApplication.delayCall += () =>
                    {
                        if (targetObject != null)
                        {
                            Selection.activeObject = targetObject;
                            EditorGUIUtility.PingObject(targetObject);
                        }
                    };
                    break;
            }
        }

        private void DrawValidationIcon(Rect rect, EditableModelEntry entry)
        {
            var validationState = GetValidationState(entry.ModelId);
            if (validationState == null)
            {
                return;
            }

            var tooltip = validationState.Status switch
            {
                DependencyValidationStatus.Unknown => "Repository availability has not been checked yet.",
                DependencyValidationStatus.Checking => "Checking Hugging Face repository availability.",
                DependencyValidationStatus.Valid => "Repository exists on Hugging Face.",
                DependencyValidationStatus.Invalid => validationState.Message ?? "Repository was not found on Hugging Face.",
                _ => null
            };

            var iconContent = validationState.Status switch
            {
                DependencyValidationStatus.Unknown => EditorGUIUtility.IconContent("d_WaitSpin00"),
                DependencyValidationStatus.Checking => EditorGUIUtility.IconContent("d_WaitSpin00"),
                DependencyValidationStatus.Valid => EditorGUIUtility.IconContent("TestPassed"),
                DependencyValidationStatus.Invalid => EditorGUIUtility.IconContent("console.warnicon"),
                _ => null
            };
            if (iconContent?.image == null)
            {
                var fallbackText = validationState.Status switch
                {
                    DependencyValidationStatus.Unknown => "...",
                    DependencyValidationStatus.Checking => "...",
                    DependencyValidationStatus.Valid => "OK",
                    DependencyValidationStatus.Invalid => "!",
                    _ => string.Empty
                };
                EditorGUI.LabelField(rect, new GUIContent(fallbackText, tooltip));
                return;
            }

            EditorGUI.LabelField(rect, new GUIContent(iconContent.image, tooltip));
        }

        private void QueueDependencyValidation()
        {
            // Background validation is intentionally tied to Apply/Revert instead of every keystroke. That keeps the inspector
            // quiet while editing and reuses the last applied manifest state as the validation input.
            if (!_isEditable ||
                string.IsNullOrWhiteSpace(_inspectedAssetPath) ||
                !_inspectedAssetPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var version = ++_validationVersion;
            var modelIds = _models
                .Select(model => model.ModelId?.Trim())
                .Where(modelId => !string.IsNullOrWhiteSpace(modelId))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (modelIds.Length == 0)
            {
                _validationStates = Array.Empty<DependencyValidationState>();
                Repaint();
                return;
            }

            ReplaceValidationStates(modelIds.Select(modelId =>
                new DependencyValidationState(modelId, DependencyValidationStatus.Checking, null)).ToArray());
            Repaint();
            _ = ValidateDependenciesAsync(modelIds, version);
        }

        private void RefreshValidationStatesFromCache()
        {
            var modelIds = _models
                .Select(model => model.ModelId?.Trim())
                .Where(modelId => !string.IsNullOrWhiteSpace(modelId))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (modelIds.Length == 0)
            {
                _validationStates = Array.Empty<DependencyValidationState>();
                return;
            }

            _validationStates = modelIds
                .Select(modelId =>
                {
                    if (!ModelManifestInspectorUtility.TryGetCachedDependencyValidation(modelId, out var exists))
                    {
                        return new DependencyValidationState(modelId, DependencyValidationStatus.Unknown, null);
                    }

                    return new DependencyValidationState(
                        modelId,
                        exists ? DependencyValidationStatus.Valid : DependencyValidationStatus.Invalid,
                        exists ? null : "Repository was not found on Hugging Face.");
                })
                .ToArray();
            _seenValidationCacheRevision = ModelManifestInspectorUtility.DependencyValidationCacheRevision;
        }

        private void RefreshValidationStatesFromCacheIfNeeded()
        {
            if (_seenValidationCacheRevision == ModelManifestInspectorUtility.DependencyValidationCacheRevision)
            {
                return;
            }

            if (_validationStates.Any(state => state.Status == DependencyValidationStatus.Checking))
            {
                return;
            }

            RefreshValidationStatesFromCache();
        }

        private async Task ValidateDependenciesAsync(string[] modelIds, int version)
        {
            var results = new DependencyValidationState[modelIds.Length];
            for (var i = 0; i < modelIds.Length; i++)
            {
                results[i] = await ValidateDependencyAsync(modelIds[i]);
            }

            EditorApplication.delayCall += () =>
            {
                // Ignore stale completions from older validation passes when the manifest changed again before the requests finished.
                if (version != _validationVersion)
                {
                    return;
                }

                RefreshValidationStatesFromCache();
                ReplaceValidationStates(results);
                if (this != null)
                {
                    Repaint();
                }

                InternalEditorUtility.RepaintAllViews();
            };
        }

        private static async Task<DependencyValidationState> ValidateDependencyAsync(string modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId))
            {
                return new DependencyValidationState(modelId, DependencyValidationStatus.Unknown, null);
            }

            if (ModelManifestInspectorUtility.TryGetCachedDependencyValidation(modelId, out var cachedExists))
            {
                return new DependencyValidationState(
                    modelId,
                    cachedExists ? DependencyValidationStatus.Valid : DependencyValidationStatus.Invalid,
                    cachedExists ? null : "Repository was not found on Hugging Face.");
            }

            try
            {
                var success = await ModelManifestInspectorUtility.GetOrStartDependencyValidationAsync(
                    modelId,
                    async () =>
                    {
                        using var request = UnityWebRequest.Head($"https://huggingface.co/{modelId.Trim()}");
                        request.timeout = 10;
                        await SendRequestAsync(request, CancellationToken.None);
                        return request.responseCode is >= 200 and < 400;
                    });
                ModelManifestInspectorUtility.WriteCachedDependencyValidation(modelId, success);
                return new DependencyValidationState(
                    modelId,
                    success ? DependencyValidationStatus.Valid : DependencyValidationStatus.Invalid,
                    success ? null : "Repository was not found on Hugging Face.");
            }
            catch (Exception exception)
            {
                return new DependencyValidationState(modelId, DependencyValidationStatus.Invalid, exception.Message);
            }
        }

        private static Task SendRequestAsync(UnityWebRequest request, CancellationToken cancellationToken)
        {
            // UnityWebRequest is callback-based. Wrap it once here so validation can stay on straightforward async/await code.
            var tcs = new TaskCompletionSource<bool>();
            var operation = request.SendWebRequest();
            cancellationToken.Register(() =>
            {
                if (!request.isDone)
                {
                    request.Abort();
                }
            });
            operation.completed += _ =>
            {
                if (request.result == UnityWebRequest.Result.ConnectionError)
                {
                    tcs.TrySetException(new InvalidOperationException(request.error));
                    return;
                }

                tcs.TrySetResult(true);
            };
            return tcs.Task;
        }

        private void ReplaceValidationStates(DependencyValidationState[] states)
        {
            _validationStates = states ?? Array.Empty<DependencyValidationState>();
        }

        private DependencyValidationState GetValidationState(string modelId)
        {
            return _validationStates.FirstOrDefault(state =>
                string.Equals(state.ModelId, modelId, StringComparison.Ordinal));
        }

        private enum DependencyValidationStatus
        {
            Unknown,
            Checking,
            Valid,
            Invalid
        }

        private sealed class DependencyValidationState
        {
            public DependencyValidationState(string modelId, DependencyValidationStatus status, string message)
            {
                ModelId = modelId;
                Status = status;
                Message = message;
            }

            public string ModelId { get; }
            public DependencyValidationStatus Status { get; }
            public string Message { get; }
        }
    }

    /// <summary>
    /// Resolves model manifest inspector state from Unity asset paths and hides the package-path and cache-specific plumbing.
    /// </summary>
    internal static class ModelManifestInspectorUtility
    {
        // Repo-validation cache lives in Library so it survives inspector redraws and script reloads without becoming project data.
        // Entries expire only by age: any cached result older than DependencyValidationCacheTtl is treated as missing and the
        // next explicit validation pass (Apply/Revert) will fetch a fresh result. Failed network requests do not overwrite cache,
        // so the last successful answer remains visible until it naturally ages out.
        private const string DependencyValidationCacheProjectPath = "Library/Doji/HfHub/model-manifest-validation/hf-hub-repo-validation.json";
        private static readonly TimeSpan DependencyValidationCacheTtl = TimeSpan.FromDays(1);
        private static readonly object InFlightDependencyValidationGate = new();
        private static readonly System.Collections.Generic.Dictionary<string, Task<bool>> InFlightDependencyValidationByModelId =
            new(StringComparer.Ordinal);
        private static int _dependencyValidationCacheRevision;

        internal static int DependencyValidationCacheRevision => _dependencyValidationCacheRevision;

        /// <summary>
        /// Mirrors the JSON document shape that the inspector edits and reserializes.
        /// </summary>
        [Serializable]
        internal sealed class ManifestDocument
        {
            public int version;
            public ManifestModelDocument[] models;
        }

        /// <summary>
        /// Mirrors one JSON model entry while preserving an omitted revision as <see langword="null"/>.
        /// </summary>
        [Serializable]
        internal sealed class ManifestModelDocument
        {
            public string id;
            public string revision;
        }

        [Serializable]
        private sealed class DependencyValidationCacheDocument
        {
            public DependencyValidationCacheEntry[] entries;
        }

        [Serializable]
        private sealed class DependencyValidationCacheEntry
        {
            public string modelId;
            public bool exists;
            public string checkedAtUtc;
        }

        /// <summary>
        /// Identifies Unity asset paths that should switch the broad <see cref="TextAsset"/> inspector over to the model manifest UI.
        /// The check stays path-based because package manifest JSON files do not have a dedicated Unity asset type.
        /// </summary>
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

        internal static string BuildStateKey(int version, ModelManifestInspector.EditableModelEntry[] models)
        {
            var document = BuildDocument(version, models);
            return JsonUtility.ToJson(document, false);
        }

        internal static bool TryBuildManifestJson(
            int version,
            ModelManifestInspector.EditableModelEntry[] models,
            out string serializedJson,
            out string errorMessage)
        {
            serializedJson = null;
            errorMessage = null;

            var drafts = (models ?? Array.Empty<ModelManifestInspector.EditableModelEntry>())
                .Select(model => new ManifestModelDraft(model?.ModelId, string.IsNullOrWhiteSpace(model?.Revision) ? "main" : model.Revision))
                .ToArray();
            if (!ManifestFactory.TryCreateManifest(version, drafts, out var manifest, out errorMessage))
            {
                return false;
            }

            var document = BuildDocument(
                manifest.Version,
                manifest.Models
                    .Select(model => EditableModelEntryFromManifest(model))
                    .ToArray());

            serializedJson = JsonUtility.ToJson(document, true);
            return true;
        }

        internal static ManifestDocument BuildDocument(int version, ModelManifestInspector.EditableModelEntry[] models)
        {
            return new ManifestDocument
            {
                version = version,
                models = (models ?? Array.Empty<ModelManifestInspector.EditableModelEntry>())
                    .Select(model => new ManifestModelDocument
                    {
                        id = model?.ModelId ?? string.Empty,
                        revision = string.IsNullOrWhiteSpace(model?.Revision) ? null : model.Revision
                    })
                    .ToArray()
            };
        }

        private static ModelManifestInspector.EditableModelEntry EditableModelEntryFromManifest(ModelDependencyDefinition model)
        {
            var identifier = model?.Id ?? string.Empty;
            var slashIndex = identifier.IndexOf('/');
            if (slashIndex <= 0 || slashIndex >= identifier.Length - 1)
            {
                return new ModelManifestInspector.EditableModelEntry(
                    string.Empty,
                    identifier,
                    string.Equals(model?.Revision, "main", StringComparison.Ordinal) ? string.Empty : model?.Revision ?? string.Empty);
            }

            return new ModelManifestInspector.EditableModelEntry(
                identifier.Substring(0, slashIndex),
                identifier.Substring(slashIndex + 1),
                string.Equals(model?.Revision, "main", StringComparison.Ordinal) ? string.Empty : model?.Revision ?? string.Empty);
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

            // PackageCache entries belong to the shared package install, so keep them read-only even though they may still
            // resolve to a normal file on disk.
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

        internal static bool TryGetCachedDependencyValidation(string modelId, out bool exists)
        {
            exists = false;
            if (string.IsNullOrWhiteSpace(modelId))
            {
                return false;
            }

            var cachePath = ProjectPaths.ToProjectAbsolutePath(DependencyValidationCacheProjectPath);
            if (!File.Exists(cachePath))
            {
                return false;
            }

            try
            {
                var document = JsonUtility.FromJson<DependencyValidationCacheDocument>(File.ReadAllText(cachePath));
                var entry = (document?.entries ?? Array.Empty<DependencyValidationCacheEntry>())
                    .FirstOrDefault(candidate => string.Equals(candidate.modelId, modelId, StringComparison.Ordinal));
                if (entry == null ||
                    string.IsNullOrWhiteSpace(entry.checkedAtUtc) ||
                    !DateTime.TryParse(entry.checkedAtUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var checkedAtUtc))
                {
                    return false;
                }

                // Stale entries are ignored rather than eagerly deleted so reads stay cheap and writes only happen on fresh checks.
                if ((DateTime.UtcNow - checkedAtUtc) > DependencyValidationCacheTtl)
                {
                    return false;
                }

                exists = entry.exists;
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static void WriteCachedDependencyValidation(string modelId, bool exists)
        {
            if (string.IsNullOrWhiteSpace(modelId))
            {
                return;
            }

            // Every successful check overwrites the existing entry and resets its TTL window from "now".
            var cachePath = ProjectPaths.ToProjectAbsolutePath(DependencyValidationCacheProjectPath);
            var directory = Path.GetDirectoryName(cachePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var document = ReadDependencyValidationCache(cachePath) ?? new DependencyValidationCacheDocument
            {
                entries = Array.Empty<DependencyValidationCacheEntry>()
            };
            var entries = (document.entries ?? Array.Empty<DependencyValidationCacheEntry>()).ToList();
            var existingEntry = entries.FirstOrDefault(entry => string.Equals(entry.modelId, modelId, StringComparison.Ordinal));
            if (existingEntry == null)
            {
                existingEntry = new DependencyValidationCacheEntry
                {
                    modelId = modelId
                };
                entries.Add(existingEntry);
            }

            existingEntry.exists = exists;
            existingEntry.checkedAtUtc = DateTime.UtcNow.ToString("O");
            document.entries = entries
                .OrderBy(entry => entry.modelId, StringComparer.Ordinal)
                .ToArray();

            File.WriteAllText(cachePath, JsonUtility.ToJson(document, true));
            _dependencyValidationCacheRevision++;
        }

        internal static Task<bool> GetOrStartDependencyValidationAsync(string modelId, Func<Task<bool>> factory)
        {
            if (string.IsNullOrWhiteSpace(modelId))
            {
                return Task.FromResult(false);
            }

            lock (InFlightDependencyValidationGate)
            {
                // Multiple inspectors or rapid Apply/Revert cycles can ask for the same repo at once. Share one task so the
                // user sees one status update and Hugging Face sees one request.
                if (InFlightDependencyValidationByModelId.TryGetValue(modelId, out var existingTask))
                {
                    return existingTask;
                }

                var task = RunDependencyValidationAsync(modelId, factory);
                InFlightDependencyValidationByModelId[modelId] = task;
                return task;
            }
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

        private static DependencyValidationCacheDocument ReadDependencyValidationCache(string cachePath)
        {
            if (string.IsNullOrWhiteSpace(cachePath) || !File.Exists(cachePath))
            {
                return null;
            }

            try
            {
                return JsonUtility.FromJson<DependencyValidationCacheDocument>(File.ReadAllText(cachePath));
            }
            catch
            {
                return null;
            }
        }

        private static async Task<bool> RunDependencyValidationAsync(string modelId, Func<Task<bool>> factory)
        {
            try
            {
                return await factory();
            }
            finally
            {
                lock (InFlightDependencyValidationGate)
                {
                    InFlightDependencyValidationByModelId.Remove(modelId);
                }
            }
        }
    }
}
