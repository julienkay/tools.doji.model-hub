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
        public ManifestModelEntry(string owner, string name, string revision)
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

        public static ManifestModelEntry FromDocumentModel(ModelManifestInspectorUtility.ManifestModelDocument model)
        {
            var identifier = model?.id ?? string.Empty;
            var slashIndex = identifier.IndexOf('/');
            if (slashIndex <= 0 || slashIndex >= identifier.Length - 1)
            {
                return new ManifestModelEntry(string.Empty, identifier, model?.revision ?? string.Empty);
            }

            return new ManifestModelEntry(
                identifier.Substring(0, slashIndex),
                identifier.Substring(slashIndex + 1),
                model?.revision ?? string.Empty);
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
        private ReorderableList _list;
        private GUIStyle _revisionPlaceholderStyle;

        public ManifestModelListEditor(
            ManifestModelEntry[] models,
            bool isEditable,
            Action onChanged,
            Func<string, ManifestModelValidationState> getValidationState = null)
        {
            _models = models ?? Array.Empty<ManifestModelEntry>();
            _isEditable = isEditable;
            _onChanged = onChanged;
            _getValidationState = getValidationState;
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

        private void DrawElement(Rect rect, int index, bool isActive, bool isFocused)
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
            var iconRect = new Rect(revisionRect.xMax + 4f, rect.y, iconWidth, EditorGUIUtility.singleLineHeight);

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
            }
            else
            {
                EditorGUI.LabelField(ownerRect, entry.Owner);
                EditorGUI.LabelField(modelRect, entry.Name);
                EditorGUI.LabelField(revisionRect, entry.Revision);
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
}
