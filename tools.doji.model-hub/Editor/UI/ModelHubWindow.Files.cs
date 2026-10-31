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
    /// Files tab: repository files, per-file import settings, and the apply bar for unsaved settings.
    /// </summary>
    internal sealed partial class ModelHubWindow
    {
        private const string InferenceModelDarkIconPath = "Packages/com.unity.ai.inference/Editor/d_InferenceEngineModel.png";
        private const string InferenceModelLightIconPath = "Packages/com.unity.ai.inference/Editor/InferenceEngineModel.png";

        private static readonly (string Value, string Label)[] QuantizationOptions =
        {
            ("none", "None"),
            ("float16", "Float16"),
            ("uint8", "UInt8")
        };

        private readonly IRemoteSnapshotSizeEstimateService _remoteSnapshotSizeEstimateService = new RemoteSnapshotSizeEstimateService();
        private readonly IProjectOptionalFileSelectionSource _optionalFileSelectionSource = new ProjectOptionalFileSelectionSource();
        private readonly Dictionary<string, RepoFileEditSession> _fileEditSessions = new(StringComparer.Ordinal);
        private readonly HashSet<string> _expandedFiles = new(StringComparer.Ordinal);
        [SerializeField] private bool _showUnusedFiles;
        private VisualElement _applyBar;

        internal enum FileHeaderIconKind
        {
            Model,
            Text,
            Json,
            Default
        }

        private void BuildFilesTab(VisualElement content, ModelHubItem item)
        {
            var session = GetOrCreateFileEditSession(item);
            var unusedCount = item.Files.Count(file => !file.IsDependencySelected);

            var toolbar = UiKit.Element("mh-files-toolbar");
            toolbar.Add(UiKit.Text(
                item.Files.Any(file => file.SupportsImportRecipeEditing)
                    ? "Expand a model file to change how it is imported."
                    : "Files this project uses from the model.",
                "hf-muted"));
            toolbar.Add(UiKit.Spacer());
            if (unusedCount > 0)
            {
                var showUnused = new Toggle { text = $"Show unused ({unusedCount})", value = _showUnusedFiles };
                showUnused.RegisterValueChangedCallback(evt =>
                {
                    _showUnusedFiles = evt.newValue;
                    RebuildDetails();
                });
                toolbar.Add(showUnused);
            }

            content.Add(toolbar);

            var files = item.Files
                .Where(file => _showUnusedFiles || file.IsDependencySelected)
                .OrderByDescending(file => file.IsDependencySelected)
                .ThenByDescending(file => file.SupportsImportRecipeEditing)
                .ThenBy(file => file.RepoPath, StringComparer.Ordinal);
            foreach (var file in files)
            {
                content.Add(BuildFileCard(item, session, file));
            }

            _applyBar = BuildApplyBar(item, session);
            _detailsPane.Add(_applyBar);
            UpdateApplyBar(item, session);
        }

        private VisualElement BuildFileCard(ModelHubItem item, RepoFileEditSession session, ModelHubFileItem file)
        {
            var card = UiKit.Element("mh-file");
            card.EnableInClassList("mh-file--unused", !file.IsDependencySelected);

            var expandKey = $"{BuildItemKey(item)}|{file.RepoPath}";
            var canExpand = file.SupportsImportRecipeEditing ||
                !string.IsNullOrWhiteSpace(file.RawCachedFilePath) ||
                !string.IsNullOrWhiteSpace(file.RuntimeFile?.FilePath);
            var isExpanded = canExpand && _expandedFiles.Contains(expandKey);

            var header = UiKit.Element("mh-file__header");
            var chevron = UiKit.Text(canExpand ? (isExpanded ? "▾" : "▸") : string.Empty, "mh-file__chevron");
            header.Add(chevron);
            header.Add(BuildFileIcon(file.RepoPath));
            var name = UiKit.Text(file.RepoPath, "mh-file__name");
            name.tooltip = file.RepoPath;
            header.Add(name);
            if (!file.IsDependencySelected)
            {
                header.Add(UiKit.Tag("Not used", "No manifest selects this file, so it is not downloaded or imported."));
            }

            if (file.HasProjectOverride)
            {
                header.Add(UiKit.Tag("Custom settings", "This project overrides the package's import settings for this file."));
            }

            var size = UiKit.FormatBytes(file.RawSizeInBytes);
            if (!string.IsNullOrWhiteSpace(size))
            {
                header.Add(UiKit.Text(size, "mh-file__size"));
            }

            card.Add(header);

            if (!canExpand)
            {
                return card;
            }

            var body = UiKit.Element("mh-file__body");
            UiKit.SetVisible(body, isExpanded);
            card.Add(body);
            if (isExpanded)
            {
                BuildFileBody(body, item, session, file);
            }

            header.RegisterCallback<ClickEvent>(_ =>
            {
                var expand = !_expandedFiles.Contains(expandKey);
                if (expand)
                {
                    _expandedFiles.Add(expandKey);
                    if (body.childCount == 0)
                    {
                        BuildFileBody(body, item, session, file);
                    }
                }
                else
                {
                    _expandedFiles.Remove(expandKey);
                }

                chevron.text = expand ? "▾" : "▸";
                UiKit.SetVisible(body, expand);
            });
            return card;
        }

        private void BuildFileBody(VisualElement body, ModelHubItem item, RepoFileEditSession session, ModelHubFileItem file)
        {
            if (file.SupportsImportRecipeEditing)
            {
                var state = session.GetOrCreateFileState(file);
                body.Add(UiKit.Text("IMPORT SETTINGS", "hf-overline"));
                body.Add(BuildQuantizationField(item, session, state));
                BuildInputShapeFields(body, item, session, state);

                var error = UiKit.Text(state.ValidationMessage, "mh-file-error");
                UiKit.SetVisible(error, !string.IsNullOrWhiteSpace(state.ValidationMessage));
                error.name = "validation";
                body.Add(error);

                if (!string.IsNullOrWhiteSpace(file.RecipeSourceLabel))
                {
                    var source = UiKit.Row("mh-field-row");
                    source.Add(UiKit.Text("Settings from", "hf-kv__key"));
                    source.Add(UiKit.Text(file.RecipeSourceLabel));
                    body.Add(source);
                }

                BuildWeightRows(body, file, state);
            }

            if (!string.IsNullOrWhiteSpace(file.RawCachedFilePath) || !string.IsNullOrWhiteSpace(file.RuntimeFile?.FilePath))
            {
                body.Add(UiKit.Text("LOCATION", "hf-overline"));
                if (!string.IsNullOrWhiteSpace(file.RawCachedFilePath))
                {
                    var rawPath = file.RawCachedFilePath;
                    body.Add(BuildPathRow("Downloaded file", rawPath, UiKit.RevealInFileBrowserLabel, () => ProjectSettingsLinks.RevealPath(rawPath)));
                }

                var runtimePath = file.RuntimeFile?.FilePath;
                if (!string.IsNullOrWhiteSpace(runtimePath) &&
                    !string.Equals(runtimePath, file.RawCachedFilePath, StringComparison.OrdinalIgnoreCase))
                {
                    body.Add(BuildPathRow("Converted model", runtimePath, UiKit.RevealInFileBrowserLabel, () => ProjectSettingsLinks.RevealPath(runtimePath)));
                }
            }
        }

        /// <summary>
        /// Shows what the source file stores and what conversion made of it, with a hint when the setting does not fit
        /// the source.
        /// </summary>
        private static void BuildWeightRows(VisualElement body, ModelHubFileItem file, FileRecipeEditState state)
        {
            var source = state.InspectedModel;
            if (source != null && source.Weights.Count > 0)
            {
                var row = UiKit.Row("mh-field-row");
                row.Add(UiKit.Text("Source weights", "hf-kv__key"));
                row.Add(UiKit.Text(string.Join(" · ", source.Weights.Take(3).Select(weight => $"{UiKit.FormatBytes(weight.Bytes)} {weight.DataType}"))));
                body.Add(row);
            }

            var importedBytes = file.RuntimeFile?.ImportedWeightBytes;
            var convertedBytes = file.RuntimeFile?.ConvertedWeightBytes;
            if (importedBytes > 0 && convertedBytes > 0)
            {
                var row = UiKit.Row("mh-field-row");
                row.Add(UiKit.Text("Converted weights", "hf-kv__key"));
                row.Add(UiKit.Text(importedBytes == convertedBytes
                    ? UiKit.FormatBytes(convertedBytes)
                    : $"{UiKit.FormatBytes(importedBytes)} → {UiKit.FormatBytes(convertedBytes)}"));
                body.Add(row);
            }

            var unsupported = source?.DescribeUnsupportedOperators();
            if (unsupported != null)
            {
                body.Add(UiKit.Text($"This file cannot be converted: {unsupported}", "mh-file-error"));
            }
            else if (source?.IsMostlyFloat16 == true && NormalizeQuantizationMode(state.QuantizationMode) == "none")
            {
                body.Add(UiKit.Text(
                    "Source weights are float16. Conversion expands them to float32, about twice the size. " +
                    "Float16 keeps the source size without losing precision.",
                    "hf-muted"));
            }
        }

        private static VisualElement BuildPathRow(string label, string path, string actionLabel, Action action)
        {
            var row = UiKit.Row("mh-field-row");
            row.Add(UiKit.Text(label, "hf-kv__key"));
            var value = UiKit.Text(Path.GetFileName(path), "hf-ellipsis", "hf-grow");
            value.tooltip = path;
            row.Add(value);
            row.Add(UiKit.LinkButton(actionLabel, action, path));
            return row;
        }

        private VisualElement BuildQuantizationField(ModelHubItem item, RepoFileEditSession session, FileRecipeEditState state)
        {
            var current = NormalizeQuantizationMode(state.QuantizationMode);
            var options = QuantizationOptions.ToList();
            if (options.All(option => !string.Equals(option.Value, current, StringComparison.Ordinal)))
            {
                options.Add((current, current));
            }

            var dropdown = new DropdownField(
                options.Select(option => option.Label).ToList(),
                Math.Max(0, options.FindIndex(option => string.Equals(option.Value, current, StringComparison.Ordinal))));
            dropdown.tooltip = "Store weights at lower precision to shrink the imported model. Lower precision can reduce accuracy.";
            dropdown.RegisterValueChangedCallback(evt =>
            {
                var selected = options.FirstOrDefault(option => string.Equals(option.Label, evt.newValue, StringComparison.Ordinal));
                state.QuantizationMode = selected.Value ?? "none";
                UpdateApplyBar(item, session);
            });

            var row = UiKit.Row("mh-field-row");
            row.Add(UiKit.Text("Quantization", "hf-kv__key"));
            row.Add(dropdown);
            return row;
        }

        private void BuildInputShapeFields(VisualElement body, ModelHubItem item, RepoFileEditSession session, FileRecipeEditState state)
        {
            var fieldsByLabel = new Dictionary<string, List<IntegerField>>(StringComparer.Ordinal);

            IntegerField CreateDimensionField(DynamicDimensionEditState dimension)
            {
                var field = new IntegerField { value = dimension.Value, isDelayed = true };
                field.tooltip = "Fixed size for this dimension at import. Use -1 to keep it dynamic.";
                field.RegisterValueChangedCallback(evt =>
                {
                    dimension.Value = evt.newValue;
                    if (fieldsByLabel.TryGetValue(dimension.Label, out var siblings))
                    {
                        foreach (var sibling in siblings.Where(sibling => sibling != field))
                        {
                            sibling.SetValueWithoutNotify(evt.newValue);
                        }
                    }

                    state.RefreshValidation();
                    var error = body.Q<Label>("validation");
                    if (error != null)
                    {
                        error.text = state.ValidationMessage;
                        UiKit.SetVisible(error, !string.IsNullOrWhiteSpace(state.ValidationMessage));
                    }

                    UpdateApplyBar(item, session);
                });
                if (!fieldsByLabel.TryGetValue(dimension.Label, out var list))
                {
                    list = new List<IntegerField>();
                    fieldsByLabel[dimension.Label] = list;
                }

                list.Add(field);
                return field;
            }

            if (state.InputShapes.Count == 0)
            {
                if (state.DynamicDimensions.Count == 0)
                {
                    return;
                }

                body.Add(UiKit.Text("Input dimensions", "hf-muted"));
                foreach (var dimension in state.DynamicDimensions)
                {
                    var row = UiKit.Row("mh-field-row");
                    row.Add(UiKit.Text(dimension.Label, "hf-kv__key"));
                    row.Add(CreateDimensionField(dimension));
                    body.Add(row);
                }

                return;
            }

            var shapesRow = UiKit.Row("mh-field-row");
            shapesRow.Add(UiKit.Text("Input shapes", "hf-kv__key"));
            body.Add(shapesRow);
            foreach (var input in state.InputShapes)
            {
                var shape = UiKit.Element("mh-input-shape");
                shape.Add(UiKit.Text(input.Name, "mh-input-shape__name"));
                var dims = UiKit.Element("mh-input-shape__dims");
                foreach (var dimension in input.Dimensions)
                {
                    var dim = UiKit.Element("mh-dim");
                    var editable = dimension.IsDynamic
                        ? state.DynamicDimensions.FirstOrDefault(candidate => candidate.Bindings.Any(binding =>
                            string.Equals(binding.InputName, input.Name, StringComparison.Ordinal) &&
                            binding.DimensionIndex == dimension.Index))
                        : null;
                    if (editable != null)
                    {
                        dim.Add(UiKit.Text(dimension.Label));
                        dim.Add(CreateDimensionField(editable));
                    }
                    else
                    {
                        var value = UiKit.Text(dimension.DisplayValue, "mh-dim__static");
                        value.tooltip = "Fixed by the model";
                        dim.Add(value);
                    }

                    dims.Add(dim);
                }

                shape.Add(dims);
                body.Add(shape);
            }
        }

        private VisualElement BuildApplyBar(ModelHubItem item, RepoFileEditSession session)
        {
            var bar = UiKit.Element("mh-apply-bar", "hf-hidden");
            bar.Add(UiKit.Text("Import settings changed. Apply them to convert the model again.", "hf-grow", "hf-wrap"));
            bar.Add(UiKit.Button("Revert", () =>
            {
                _fileEditSessions.Remove(GetFileEditSessionKey(item));
                RebuildDetails();
            }));
            var apply = UiKit.PrimaryButton("Apply & Convert", () => ApplyFileEdits(item, session));
            apply.name = "apply";
            bar.Add(apply);
            return bar;
        }

        private void UpdateApplyBar(ModelHubItem item, RepoFileEditSession session)
        {
            if (_applyBar == null)
            {
                return;
            }

            UiKit.SetVisible(_applyBar, session.IsDirty);
            var apply = _applyBar.Q<Button>("apply");
            var canApply = !session.HasValidationErrors && !BackgroundSyncManager.IsRunning && !item.HasRevisionConflict;
            apply?.SetEnabled(canApply);
            if (apply != null)
            {
                apply.tooltip = session.HasValidationErrors
                    ? "Fix the highlighted settings first."
                    : BackgroundSyncManager.IsRunning
                        ? "Wait for the running setup to finish."
                        : "Save the settings and convert the model again. Converting takes CPU, memory, and time for large models.";
            }
        }

        private void ApplyFileEdits(ModelHubItem item, RepoFileEditSession session)
        {
            EditorOperations.ApplyProjectOverridesAndReimport(item, session.BuildRecipesByRepoPath());
            _fileEditSessions.Remove(GetFileEditSessionKey(item));
            Refresh();
        }

        private RepoFileEditSession GetOrCreateFileEditSession(ModelHubItem item)
        {
            var key = GetFileEditSessionKey(item);
            if (_fileEditSessions.TryGetValue(key, out var existingSession) && existingSession.Matches(item))
            {
                return existingSession;
            }

            var session = RepoFileEditSession.Create(item);
            _fileEditSessions[key] = session;
            return session;
        }

        /// <summary>
        /// Asks what to do with unsaved import settings before a selection or tab change hides them.
        /// </summary>
        private bool TryResolvePendingFileEdits(ModelHubItem item)
        {
            if (item == null || !_fileEditSessions.TryGetValue(GetFileEditSessionKey(item), out var session) || !session.IsDirty)
            {
                return true;
            }

            var choice = EditorUtility.DisplayDialogComplex(
                "Unapplied Import Settings",
                $"You changed import settings for {item.Repository}. Apply them now?",
                "Apply",
                "Discard",
                "Cancel");
            switch (choice)
            {
                case 0:
                    if (session.HasValidationErrors)
                    {
                        EditorUtility.DisplayDialog("Unapplied Import Settings", "Some settings are invalid. Fix them or discard the changes.", "OK");
                        return false;
                    }

                    ApplyFileEdits(item, session);
                    return true;
                case 1:
                    _fileEditSessions.Remove(GetFileEditSessionKey(item));
                    return true;
                default:
                    return false;
            }
        }

        private static string GetFileEditSessionKey(ModelHubItem item)
        {
            return item == null ? string.Empty : $"{item.Repository}\n{item.RequestedRevision}";
        }

        private static VisualElement BuildFileIcon(string repoPath)
        {
            var icon = UiKit.Element("hf-icon");
            var texture = GetFileHeaderIconKind(repoPath) switch
            {
                FileHeaderIconKind.Model => LoadInferenceModelIcon() ?? UiKit.LoadIcon("DefaultAsset Icon"),
                FileHeaderIconKind.Text => UiKit.LoadIcon("TextAsset Icon"),
                FileHeaderIconKind.Json => UiKit.LoadIcon("TextAsset Icon"),
                _ => UiKit.LoadIcon("DefaultAsset Icon")
            };
            if (texture != null)
            {
                icon.style.backgroundImage = new StyleBackground(texture);
            }

            return icon;
        }

        private static Texture2D LoadInferenceModelIcon()
        {
            var preferred = EditorGUIUtility.isProSkin ? InferenceModelDarkIconPath : InferenceModelLightIconPath;
            var fallback = EditorGUIUtility.isProSkin ? InferenceModelLightIconPath : InferenceModelDarkIconPath;
            return AssetDatabase.LoadAssetAtPath<Texture2D>(preferred) ?? AssetDatabase.LoadAssetAtPath<Texture2D>(fallback);
        }

        internal static FileHeaderIconKind GetFileHeaderIconKind(string repoPath)
        {
            var extension = Path.GetExtension(repoPath ?? string.Empty)?.ToLowerInvariant();
            return extension switch
            {
                ".onnx" or ".sentis" or ".safetensors" or ".bin" or ".pt" or ".pth" or ".ckpt" or ".gguf" or ".tflite" or ".pb" or ".h5" => FileHeaderIconKind.Model,
                ".json" => FileHeaderIconKind.Json,
                ".txt" or ".md" => FileHeaderIconKind.Text,
                _ => FileHeaderIconKind.Default
            };
        }

        internal static string NormalizeQuantizationMode(string quantizationMode)
        {
            return OnnxConversionSettings.NormalizeQuantizationMode(quantizationMode);
        }

        internal static OnnxConversionRecipe CloneRecipe(OnnxConversionRecipe recipe)
        {
            var source = recipe ?? new OnnxConversionRecipe();
            return new OnnxConversionRecipe
            {
                QuantizationMode = NormalizeQuantizationMode(source.QuantizationMode),
                StaticInputShapeOverrides = (source.StaticInputShapeOverrides ?? Array.Empty<OnnxStaticInputShapeOverride>())
                    .Where(item => item != null)
                    .Select(item => new OnnxStaticInputShapeOverride
                    {
                        InputName = item.InputName,
                        Dimensions = (item.Dimensions ?? Array.Empty<int>()).ToArray()
                    })
                    .ToArray()
            };
        }
    }
}
