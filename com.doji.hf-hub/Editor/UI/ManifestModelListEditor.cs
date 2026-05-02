using System;
using System.Collections.Generic;
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
    /// Keeps the split owner/model editing model while still round-tripping manifest ids as <c>owner/name</c>.
    /// </summary>
    internal sealed class ManifestModelEntry
    {
        public ManifestModelEntry(string owner, string name, string revision, string[] files = null)
        {
            Owner = owner ?? string.Empty;
            Name = name ?? string.Empty;
            Revision = revision ?? string.Empty;
            Files = ModelDependencyDefinition.NormalizeSelectedFiles(files);
        }

        public string Owner { get; set; }
        public string Name { get; set; }
        public string Revision { get; set; }
        public string[] Files { get; set; }

        public string ModelId => string.IsNullOrWhiteSpace(Owner)
            ? Name ?? string.Empty
            : $"{Owner?.Trim()}/{Name?.Trim()}";

        public bool HasExplicitFileSelection => (Files ?? Array.Empty<string>()).Length > 0;
        public string FileSelectionSummary => HasExplicitFileSelection
            ? Files.Length == 1
                ? "1 file"
                : $"{Files.Length} files"
            : "All files";

        public static ManifestModelEntry FromDocumentModel(ModelManifestInspectorUtility.ManifestModelDocument model)
        {
            var identifier = model?.id ?? string.Empty;
            var slashIndex = identifier.IndexOf('/');
            if (slashIndex <= 0 || slashIndex >= identifier.Length - 1)
            {
                return new ManifestModelEntry(string.Empty, identifier, model?.revision ?? string.Empty, model?.files);
            }

            return new ManifestModelEntry(
                identifier.Substring(0, slashIndex),
                identifier.Substring(slashIndex + 1),
                model?.revision ?? string.Empty,
                model?.files);
        }
    }

    internal enum ManifestModelValidationStatus
    {
        Unknown,
        Checking,
        Valid,
        Invalid
    }

    /// <summary>
    /// Captures one repository validation result for one manifest row.
    /// </summary>
    internal sealed class ManifestModelValidationState
    {
        public ManifestModelValidationState(string modelId, ManifestModelValidationStatus status, string message)
        {
            ModelId = modelId;
            Status = status;
            Message = message;
        }

        public string ModelId { get; }
        public ManifestModelValidationStatus Status { get; }
        public string Message { get; }
    }

    /// <summary>
    /// Renders and manages the shared owner/model/revision manifest editing list.
    /// </summary>
    internal sealed class ManifestModelListEditor
    {
        private const string RevisionPlaceholder = "main";
        private ManifestModelEntry[] _models;
        private readonly bool _isEditable;
        private readonly Func<string, ManifestModelValidationState> _getValidationState;
        private readonly Action _onChanged;
        private readonly Action _requestRepaint;
        private ReorderableList _list;
        private GUIStyle _revisionPlaceholderStyle;

        public ManifestModelListEditor(
            ManifestModelEntry[] models,
            bool isEditable,
            Action onChanged,
            Func<string, ManifestModelValidationState> getValidationState = null,
            Action requestRepaint = null)
        {
            _models = models ?? Array.Empty<ManifestModelEntry>();
            _isEditable = isEditable;
            _onChanged = onChanged;
            _getValidationState = getValidationState;
            _requestRepaint = requestRepaint ?? (() => { });
            ConfigureList();
        }

        public ManifestModelEntry[] Models => _models;

        public void SetModels(ManifestModelEntry[] models)
        {
            _models = models ?? Array.Empty<ManifestModelEntry>();
            ConfigureList();
        }

        public void DoLayoutList()
        {
            _list.DoLayoutList();
        }

        private void ConfigureList()
        {
            _list = new ReorderableList(_models, typeof(ManifestModelEntry), _isEditable, true, _isEditable, _isEditable)
            {
                drawHeaderCallback = DrawHeader,
                drawElementCallback = DrawElement,
                onAddCallback = _ =>
                {
                    Array.Resize(ref _models, _models.Length + 1);
                    _models[^1] = new ManifestModelEntry(string.Empty, string.Empty, string.Empty);
                    ConfigureList();
                    _onChanged?.Invoke();
                },
                onRemoveCallback = list =>
                {
                    if (list.index < 0 || list.index >= _models.Length)
                    {
                        return;
                    }

                    for (var i = list.index; i < _models.Length - 1; i++)
                    {
                        _models[i] = _models[i + 1];
                    }

                    Array.Resize(ref _models, _models.Length - 1);
                    ConfigureList();
                    _onChanged?.Invoke();
                },
                onReorderCallback = _ =>
                {
                    _models = _list.list.Cast<ManifestModelEntry>().ToArray();
                    _onChanged?.Invoke();
                },
                elementHeight = EditorGUIUtility.singleLineHeight + 6f
            };
        }

        private void DrawHeader(Rect rect)
        {
            const float ownerWidth = 120f;
            const float revisionWidth = 110f;
            const float filesWidth = 96f;
            const float iconWidth = 18f;
            var modelWidth = rect.width - ownerWidth - revisionWidth - filesWidth - iconWidth - 26f;
            var ownerRect = new Rect(rect.x + 14f, rect.y, ownerWidth, rect.height);
            var modelRect = new Rect(ownerRect.xMax + 6f, rect.y, Mathf.Max(60f, modelWidth), rect.height);
            var revisionRect = new Rect(modelRect.xMax + 6f, rect.y, revisionWidth, rect.height);
            var filesRect = new Rect(revisionRect.xMax + 6f, rect.y, filesWidth, rect.height);
            var iconRect = new Rect(filesRect.xMax + 4f, rect.y, iconWidth, rect.height);

            EditorGUI.LabelField(ownerRect, "Owner");
            EditorGUI.LabelField(modelRect, "Model");
            EditorGUI.LabelField(revisionRect, "Revision");
            EditorGUI.LabelField(filesRect, "Files");
            EditorGUI.LabelField(iconRect, GUIContent.none);
        }

        private void DrawElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            if (index < 0 || index >= _models.Length)
            {
                return;
            }

            var entry = _models[index];
            rect.y += 2f;

            const float ownerWidth = 120f;
            const float revisionWidth = 110f;
            const float filesWidth = 96f;
            const float iconWidth = 18f;
            var ownerRect = new Rect(rect.x, rect.y, ownerWidth, EditorGUIUtility.singleLineHeight);
            var modelRect = new Rect(ownerRect.xMax + 6f, rect.y, Mathf.Max(60f, rect.width - ownerWidth - revisionWidth - filesWidth - iconWidth - 22f), EditorGUIUtility.singleLineHeight);
            var revisionRect = new Rect(modelRect.xMax + 6f, rect.y, revisionWidth, EditorGUIUtility.singleLineHeight);
            var filesRect = new Rect(revisionRect.xMax + 6f, rect.y, filesWidth, EditorGUIUtility.singleLineHeight);
            var iconRect = new Rect(filesRect.xMax + 4f, rect.y, iconWidth, EditorGUIUtility.singleLineHeight);

            if (_isEditable)
            {
                EditorGUI.BeginChangeCheck();
                var nextOwner = EditorGUI.TextField(ownerRect, entry.Owner);
                var nextName = EditorGUI.TextField(modelRect, entry.Name);
                var revisionControlName = $"ManifestRevisionField_{index}";
                GUI.SetNextControlName(revisionControlName);
                var nextRevision = EditorGUI.TextField(revisionRect, entry.Revision);
                if (EditorGUI.EndChangeCheck())
                {
                    entry.Owner = nextOwner;
                    entry.Name = nextName;
                    entry.Revision = nextRevision;
                    _onChanged?.Invoke();
                }

                DrawRevisionPlaceholder(revisionRect, revisionControlName, entry.Revision);
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(entry.ModelId)))
                {
                    if (GUI.Button(filesRect, entry.FileSelectionSummary))
                    {
                        PopupWindow.Show(
                            filesRect,
                            new ManifestFileSelectionPopup(
                                entry.ModelId,
                                string.IsNullOrWhiteSpace(entry.Revision) ? RevisionPlaceholder : entry.Revision,
                                entry.Files,
                                files =>
                                {
                                    entry.Files = ModelDependencyDefinition.NormalizeSelectedFiles(files);
                                    _onChanged?.Invoke();
                                    _requestRepaint();
                                },
                                _requestRepaint));
                    }
                }
            }
            else
            {
                EditorGUI.LabelField(ownerRect, entry.Owner);
                EditorGUI.LabelField(modelRect, entry.Name);
                EditorGUI.LabelField(revisionRect, entry.Revision);
                EditorGUI.LabelField(filesRect, entry.FileSelectionSummary);
            }

            DrawValidationIcon(iconRect, entry);
        }

        private void DrawValidationIcon(Rect rect, ManifestModelEntry entry)
        {
            if (_getValidationState == null)
            {
                return;
            }

            var validationState = _getValidationState(entry.ModelId);
            if (validationState == null)
            {
                return;
            }

            var tooltip = validationState.Status switch
            {
                ManifestModelValidationStatus.Unknown => "Repository availability has not been checked yet. Apply or revert manifest changes to validate it.",
                ManifestModelValidationStatus.Checking => "Checking Hugging Face repository availability.",
                ManifestModelValidationStatus.Valid => "Repository exists on Hugging Face.",
                ManifestModelValidationStatus.Invalid => validationState.Message ?? "Repository was not found on Hugging Face.",
                _ => null
            };

            var iconContent = validationState.Status switch
            {
                ManifestModelValidationStatus.Unknown => EditorGUIUtility.IconContent("d__Help@2x"),
                ManifestModelValidationStatus.Checking => EditorGUIUtility.IconContent("d_WaitSpin00"),
                ManifestModelValidationStatus.Valid => EditorGUIUtility.IconContent("TestPassed"),
                ManifestModelValidationStatus.Invalid => EditorGUIUtility.IconContent("console.warnicon"),
                _ => null
            };
            if (iconContent?.image == null)
            {
                var fallbackText = validationState.Status switch
                {
                    ManifestModelValidationStatus.Unknown => "?",
                    ManifestModelValidationStatus.Checking => "...",
                    ManifestModelValidationStatus.Valid => "OK",
                    ManifestModelValidationStatus.Invalid => "!",
                    _ => string.Empty
                };
                EditorGUI.LabelField(rect, new GUIContent(fallbackText, tooltip));
                return;
            }

            EditorGUI.LabelField(rect, new GUIContent(iconContent.image, tooltip));
        }

        private void DrawRevisionPlaceholder(Rect rect, string controlName, string revision)
        {
            if (!string.IsNullOrWhiteSpace(revision) ||
                string.Equals(GUI.GetNameOfFocusedControl(), controlName, StringComparison.Ordinal))
            {
                return;
            }

            _revisionPlaceholderStyle ??= new GUIStyle(EditorStyles.textField)
            {
                normal =
                {
                    textColor = EditorGUIUtility.isProSkin
                        ? new Color(1f, 1f, 1f, 0.35f)
                        : new Color(0f, 0f, 0f, 0.35f)
                },
                hover =
                {
                    textColor = EditorGUIUtility.isProSkin
                        ? new Color(1f, 1f, 1f, 0.35f)
                        : new Color(0f, 0f, 0f, 0.35f)
                },
                active =
                {
                    textColor = EditorGUIUtility.isProSkin
                        ? new Color(1f, 1f, 1f, 0.35f)
                        : new Color(0f, 0f, 0f, 0.35f)
                },
                focused =
                {
                    textColor = EditorGUIUtility.isProSkin
                        ? new Color(1f, 1f, 1f, 0.35f)
                        : new Color(0f, 0f, 0f, 0.35f)
                }
            };

            EditorGUI.LabelField(rect, RevisionPlaceholder, _revisionPlaceholderStyle);
        }
    }

    /// <summary>
    /// Shares the package-manifest validation UX across manifest editing surfaces.
    /// </summary>
    internal sealed class ManifestModelValidationController
    {
        private readonly bool _writePersistentCache;
        private readonly Func<bool> _canValidate;
        private readonly Action _requestRepaint;
        private ManifestModelValidationState[] _states = Array.Empty<ManifestModelValidationState>();
        private int _validationVersion;
        private int _seenValidationCacheRevision = -1;

        public ManifestModelValidationController(bool writePersistentCache, Func<bool> canValidate, Action requestRepaint)
        {
            _writePersistentCache = writePersistentCache;
            _canValidate = canValidate ?? (() => true);
            _requestRepaint = requestRepaint;
        }

        public void RefreshFromCache(IEnumerable<string> modelIds)
        {
            var distinctModelIds = NormalizeModelIds(modelIds);
            if (distinctModelIds.Length == 0)
            {
                _states = Array.Empty<ManifestModelValidationState>();
                _seenValidationCacheRevision = RepositoryValidationCacheService.CacheRevision;
                return;
            }

            _states = distinctModelIds
                .Select(modelId =>
                {
                    if (!RepositoryValidationCacheService.TryGetCachedRepositoryExists(modelId, out var exists))
                    {
                        return new ManifestModelValidationState(modelId, ManifestModelValidationStatus.Unknown, null);
                    }

                    return new ManifestModelValidationState(
                        modelId,
                        exists ? ManifestModelValidationStatus.Valid : ManifestModelValidationStatus.Invalid,
                        exists ? null : "Repository was not found on Hugging Face.");
                })
                .ToArray();
            _seenValidationCacheRevision = RepositoryValidationCacheService.CacheRevision;
        }

        public void RefreshFromCacheIfNeeded(IEnumerable<string> modelIds)
        {
            if (_seenValidationCacheRevision == RepositoryValidationCacheService.CacheRevision)
            {
                return;
            }

            if (_states.Any(state => state.Status == ManifestModelValidationStatus.Checking))
            {
                return;
            }

            RefreshFromCache(modelIds);
        }

        public void QueueUnknownValidationIfNeeded()
        {
            var modelIds = _states
                .Where(state => state.Status == ManifestModelValidationStatus.Unknown)
                .Select(state => state.ModelId)
                .Where(modelId => RepositoryValidationCacheService.TryMarkSessionValidationRequested(modelId))
                .ToArray();
            if (modelIds.Length == 0)
            {
                return;
            }

            QueueValidation(modelIds);
        }

        public void QueueValidation(IEnumerable<string> modelIds)
        {
            if (!_canValidate())
            {
                return;
            }

            var distinctModelIds = NormalizeModelIds(modelIds);
            var version = ++_validationVersion;
            if (distinctModelIds.Length == 0)
            {
                _states = Array.Empty<ManifestModelValidationState>();
                _requestRepaint?.Invoke();
                return;
            }

            _states = distinctModelIds
                .Select(modelId => new ManifestModelValidationState(modelId, ManifestModelValidationStatus.Checking, null))
                .ToArray();
            _requestRepaint?.Invoke();
            _ = ValidateDependenciesAsync(distinctModelIds, version);
        }

        public ManifestModelValidationState GetState(string modelId)
        {
            return _states.FirstOrDefault(state =>
                string.Equals(state.ModelId, modelId, StringComparison.Ordinal));
        }

        private async Task ValidateDependenciesAsync(string[] modelIds, int version)
        {
            var results = new ManifestModelValidationState[modelIds.Length];
            for (var i = 0; i < modelIds.Length; i++)
            {
                results[i] = await ValidateDependencyAsync(modelIds[i], _writePersistentCache);
            }

            EditorApplication.delayCall += () =>
            {
                if (version != _validationVersion)
                {
                    return;
                }

                _states = results ?? Array.Empty<ManifestModelValidationState>();
                RefreshFromCache(modelIds);
                ReplaceKnownStates(results);
                _requestRepaint?.Invoke();
                InternalEditorUtility.RepaintAllViews();
            };
        }

        private void ReplaceKnownStates(ManifestModelValidationState[] results)
        {
            if (results == null || results.Length == 0)
            {
                return;
            }

            var replacementByModelId = results.ToDictionary(result => result.ModelId, StringComparer.Ordinal);
            _states = _states
                .Select(state => replacementByModelId.TryGetValue(state.ModelId, out var replacement) ? replacement : state)
                .ToArray();
        }

        private static string[] NormalizeModelIds(IEnumerable<string> modelIds)
        {
            return (modelIds ?? Array.Empty<string>())
                .Where(modelId => !string.IsNullOrWhiteSpace(modelId))
                .Select(modelId => modelId.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        internal static async Task<ManifestModelValidationState> ValidateDependencyAsync(string modelId, bool writePersistentCache)
        {
            if (string.IsNullOrWhiteSpace(modelId))
            {
                return new ManifestModelValidationState(modelId, ManifestModelValidationStatus.Unknown, null);
            }

            if (RepositoryValidationCacheService.TryGetCachedRepositoryExists(modelId, out var cachedExists))
            {
                return new ManifestModelValidationState(
                    modelId,
                    cachedExists ? ManifestModelValidationStatus.Valid : ManifestModelValidationStatus.Invalid,
                    cachedExists ? null : "Repository was not found on Hugging Face.");
            }

            try
            {
                var success = await RepositoryValidationCacheService.GetOrStartRepositoryValidationAsync(
                    modelId,
                    async () =>
                    {
                        using var request = UnityWebRequest.Head($"https://huggingface.co/{modelId.Trim()}");
                        request.timeout = 10;
                        await SendRequestAsync(request, CancellationToken.None);
                        return request.responseCode is >= 200 and < 400;
                    });
                if (writePersistentCache)
                {
                    RepositoryValidationCacheService.WriteCachedRepositoryExists(modelId, success);
                }

                return new ManifestModelValidationState(
                    modelId,
                    success ? ManifestModelValidationStatus.Valid : ManifestModelValidationStatus.Invalid,
                    success ? null : "Repository was not found on Hugging Face.");
            }
            catch (Exception exception)
            {
                return new ManifestModelValidationState(modelId, ManifestModelValidationStatus.Invalid, exception.Message);
            }
        }

        private static Task SendRequestAsync(UnityWebRequest request, CancellationToken cancellationToken)
        {
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
    }

    /// <summary>
    /// Lets package authors switch between whole-repository scope and explicit repo file selection.
    /// </summary>
    internal sealed class ManifestFileSelectionPopup : PopupWindowContent
    {
        private enum FileFilterMode
        {
            All,
            Onnx,
            Selected
        }

        private const float PopupWidth = 520f;
        private const float PopupHeight = 420f;

        private readonly string _repository;
        private readonly string _revision;
        private readonly Action<string[]> _onApply;
        private readonly Action _requestRepaint;
        private readonly IHttpClient _httpClient = new HttpClient();
        private readonly HashSet<string> _selectedFiles;

        private Vector2 _scrollPosition;
        private string _searchText = string.Empty;
        private FileFilterMode _filterMode;
        private RepositoryFileMetadata[] _repositoryFiles = Array.Empty<RepositoryFileMetadata>();
        private string[] _missingSelectedFiles = Array.Empty<string>();
        private bool _useExplicitSelection;
        private bool _loadStarted;
        private bool _isLoading;
        private string _loadErrorMessage;

        public ManifestFileSelectionPopup(
            string repository,
            string revision,
            IReadOnlyList<string> selectedFiles,
            Action<string[]> onApply,
            Action requestRepaint = null)
        {
            _repository = repository?.Trim();
            _revision = string.IsNullOrWhiteSpace(revision) ? "main" : revision.Trim();
            _onApply = onApply ?? throw new ArgumentNullException(nameof(onApply));
            _requestRepaint = requestRepaint ?? (() => { });
            _selectedFiles = new HashSet<string>(
                ModelDependencyDefinition.NormalizeSelectedFiles(selectedFiles),
                StringComparer.Ordinal);
            _useExplicitSelection = _selectedFiles.Count > 0;
        }

        public override Vector2 GetWindowSize()
        {
            return new Vector2(PopupWidth, PopupHeight);
        }

        public override void OnGUI(Rect rect)
        {
            EnsureInventoryLoadStarted();

            EditorGUILayout.LabelField($"{_repository}@{_revision}", EditorStyles.boldLabel);

            var scopeIndex = GUILayout.Toolbar(_useExplicitSelection ? 1 : 0, new[] { "Whole Repo", "Selected Files" });
            _useExplicitSelection = scopeIndex == 1;
            EditorGUILayout.Space(4f);

            if (_isLoading)
            {
                EditorGUILayout.HelpBox("Loading repository file inventory from Hugging Face.", MessageType.None);
            }
            else if (!string.IsNullOrWhiteSpace(_loadErrorMessage))
            {
                EditorGUILayout.HelpBox(_loadErrorMessage, MessageType.Warning);
            }
            else
            {
                DrawInventoryControls();
                DrawInventoryList();
            }

            GUILayout.FlexibleSpace();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(_useExplicitSelection && _selectedFiles.Count == 0))
                {
                    if (GUILayout.Button("Apply", GUILayout.Width(96f)))
                    {
                        _onApply(_useExplicitSelection
                            ? _selectedFiles.OrderBy(path => path, StringComparer.Ordinal).ToArray()
                            : null);
                        editorWindow?.Close();
                    }
                }
            }
        }

        private void EnsureInventoryLoadStarted()
        {
            if (_loadStarted || string.IsNullOrWhiteSpace(_repository))
            {
                return;
            }

            _loadStarted = true;
            _isLoading = true;
            _ = LoadInventoryAsync();
        }

        private async Task LoadInventoryAsync()
        {
            try
            {
                var inventory = await _httpClient.GetRepositoryInventoryAsync(_repository, _revision, CancellationToken.None);
                var repositoryFiles = inventory.Files
                    .OrderBy(file => file.RepoPath, StringComparer.Ordinal)
                    .ToArray();
                var repositoryPaths = new HashSet<string>(
                    repositoryFiles.Select(file => file.RepoPath),
                    StringComparer.Ordinal);
                var missingSelectedFiles = _selectedFiles
                    .Where(path => !repositoryPaths.Contains(path))
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .ToArray();

                EditorApplication.delayCall += () =>
                {
                    _repositoryFiles = repositoryFiles;
                    _missingSelectedFiles = missingSelectedFiles;
                    _isLoading = false;
                    _requestRepaint();
                    editorWindow?.Repaint();
                    InternalEditorUtility.RepaintAllViews();
                };
            }
            catch (Exception exception)
            {
                EditorApplication.delayCall += () =>
                {
                    _isLoading = false;
                    _loadErrorMessage = $"Could not load repository files: {exception.Message}";
                    _requestRepaint();
                    editorWindow?.Repaint();
                    InternalEditorUtility.RepaintAllViews();
                };
            }
        }

        private void DrawInventoryControls()
        {
            EditorGUI.BeginChangeCheck();
            _searchText = EditorGUILayout.TextField("Search", _searchText);
            _filterMode = (FileFilterMode)GUILayout.Toolbar((int)_filterMode, new[] { "All", "ONNX", "Selected" });
            if (EditorGUI.EndChangeCheck())
            {
                _requestRepaint();
            }

            if (_missingSelectedFiles.Length > 0)
            {
                EditorGUILayout.HelpBox(
                    $"Selected file(s) not found at this revision: {string.Join(", ", _missingSelectedFiles)}.",
                    MessageType.Warning);
            }

            if (_useExplicitSelection)
            {
                EditorGUILayout.LabelField("Selection", $"{_selectedFiles.Count} files");
            }
        }

        private void DrawInventoryList()
        {
            var filteredFiles = (_repositoryFiles ?? Array.Empty<RepositoryFileMetadata>())
                .Where(ShouldIncludeFile)
                .ToArray();
            if (filteredFiles.Length == 0)
            {
                EditorGUILayout.HelpBox("No repository files match the current filter.", MessageType.Info);
                return;
            }

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            foreach (var file in filteredFiles)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (_useExplicitSelection)
                    {
                        var isSelected = _selectedFiles.Contains(file.RepoPath);
                        var nextSelected = EditorGUILayout.Toggle(isSelected, GUILayout.Width(18f));
                        if (nextSelected != isSelected)
                        {
                            if (nextSelected)
                            {
                                _selectedFiles.Add(file.RepoPath);
                            }
                            else
                            {
                                _selectedFiles.Remove(file.RepoPath);
                            }
                        }
                    }
                    else
                    {
                        GUILayout.Space(22f);
                    }

                    EditorGUILayout.LabelField(file.RepoPath);
                    GUILayout.Label(FormatSize(file.SizeInBytes), EditorStyles.miniLabel, GUILayout.Width(72f));
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private bool ShouldIncludeFile(RepositoryFileMetadata file)
        {
            if (file == null || string.IsNullOrWhiteSpace(file.RepoPath))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(_searchText) &&
                file.RepoPath.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            return _filterMode switch
            {
                FileFilterMode.Onnx => ModelDependencyDefinition.IsOnnxFile(file.RepoPath),
                FileFilterMode.Selected => _selectedFiles.Contains(file.RepoPath),
                _ => true
            };
        }

        private static string FormatSize(long? sizeInBytes)
        {
            if (!sizeInBytes.HasValue)
            {
                return string.Empty;
            }

            if (sizeInBytes.Value < 1024)
            {
                return $"{sizeInBytes.Value} B";
            }

            if (sizeInBytes.Value < 1024 * 1024)
            {
                return $"{sizeInBytes.Value / 1024f:0.#} KB";
            }

            return $"{sizeInBytes.Value / 1024f / 1024f:0.#} MB";
        }
    }
}
