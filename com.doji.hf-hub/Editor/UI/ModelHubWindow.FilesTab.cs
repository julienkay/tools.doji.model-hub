using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Owns per-file recipe editing UI inside Model Hub files tab.
    /// </summary>
    internal sealed partial class ModelHubWindow
    {
        private const string InferenceModelDarkIconPath = "Packages/com.unity.ai.inference/Editor/d_InferenceEngineModel.png";
        private const string InferenceModelLightIconPath = "Packages/com.unity.ai.inference/Editor/InferenceEngineModel.png";
        private const float FileTitleIconSize = 14f;
        private const float FileTitleLeftGap = 3f;
        private const float FileTitleIconGap = 4f;

        private static Texture2D _inferenceModelIcon;
        private static bool _didResolveInferenceModelIcon;

        /// <summary>
        /// Small popup option model for quantization mode selection.
        /// </summary>
        private readonly struct QuantizationOption
        {
            public QuantizationOption(string value, string label)
            {
                Value = value;
                Label = label;
            }

            public string Value { get; }
            public string Label { get; }
        }

        /// <summary>
        /// Draws one repo's file rows and their effective editable import recipe state.
        /// </summary>
        private void DrawFilesSection(ModelHubItem item)
        {
            if (item.Files.Count == 0)
            {
                EditorGUILayout.HelpBox(Content.NoRuntimeFilesMessage, MessageType.Info);
                EditorGUILayout.Space();
                return;
            }

            var session = GetOrCreateFileEditSession(item);
            foreach (var file in item.Files)
            {
                var fileState = file.SupportsImportRecipeEditing
                    ? session.GetOrCreateFileState(file)
                    : null;
                var previousColor = GUI.color;
                if (!file.IsDependencySelected)
                {
                    GUI.color = new Color(previousColor.r, previousColor.g, previousColor.b, previousColor.a * 0.45f);
                }

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    DrawFileHeader(fileState, file.RepoPath);
                    if (fileState?.IsExpanded == true)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Space(4f);
                            using (new EditorGUILayout.VerticalScope())
                            {
                                DrawOptionalLinkRow(Content.RawCachedFileLabel, file.RawCachedFilePath, ProjectSettingsLinks.RevealPath);
                                DrawOptionalValueRow(Content.RawSizeLabel, FormatByteSize(file.RawSizeInBytes));
                                DrawOptionalLinkRow(Content.RuntimeAssetLabel, file.RuntimeFile?.AssetPath, ProjectSettingsLinks.PingAsset);
                                DrawOptionalValueRow(Content.RuntimeRelativeLabel, file.RuntimeFile?.RelativePath);
                                DrawOptionalValueRow(Content.RecipeSourceLabel, file.RecipeSourceLabel);
                                DrawOptionalLinkRow(Content.OverrideAssetLabel, file.OverrideAssetPath, ProjectSettingsLinks.PingAsset);
                                EditorGUILayout.Space(4f);
                                EditorGUILayout.LabelField("Import Settings", EditorStyles.boldLabel);
                                DrawQuantizationEditor(fileState);
                                DrawDynamicDimsEditor(fileState);
                                fileState.RefreshValidation();

                                if (!string.IsNullOrWhiteSpace(fileState.ValidationMessage))
                                {
                                    EditorGUILayout.HelpBox(fileState.ValidationMessage, MessageType.Warning);
                                }
                            }

                            GUILayout.Space(4f);
                        }
                    }
                }

                GUI.color = previousColor;
                EditorGUILayout.Space(8f);
            }

            EditorGUILayout.Space();
        }

        /// <summary>
        /// Draws the file block title row with the same model icon used by the inference package when available.
        /// </summary>
        private static void DrawFileHeader(FileRecipeEditState fileState, string repoPath)
        {
            var rowRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            var foldoutRect = new Rect(rowRect.x, rowRect.y, 14f, rowRect.height);
            var contentRect = new Rect(rowRect.x + 14f + FileTitleLeftGap, rowRect.y, rowRect.width - 14f - FileTitleLeftGap, rowRect.height);

            if (fileState != null)
            {
                fileState.IsExpanded = EditorGUI.Foldout(foldoutRect, fileState.IsExpanded, GUIContent.none, true);
            }
            else
            {
                EditorGUI.LabelField(foldoutRect, GUIContent.none);
            }

            var icon = GetInferenceModelIcon();
            if (icon == null)
            {
                DrawFileHeaderLabel(contentRect, repoPath);
                return;
            }

            var iconY = contentRect.y + ((contentRect.height - FileTitleIconSize) * 0.5f);
            var iconRect = new Rect(contentRect.x, iconY, FileTitleIconSize, FileTitleIconSize);
            var labelOffset = FileTitleIconSize + FileTitleIconGap;
            var labelRect = new Rect(contentRect.x + labelOffset, contentRect.y, contentRect.width - labelOffset, contentRect.height);
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit, true);
            DrawFileHeaderLabel(labelRect, repoPath);
        }

        private static Texture2D GetInferenceModelIcon()
        {
            if (_didResolveInferenceModelIcon)
            {
                return _inferenceModelIcon;
            }

            _didResolveInferenceModelIcon = true;
            var preferredIconPath = EditorGUIUtility.isProSkin ? InferenceModelDarkIconPath : InferenceModelLightIconPath;
            _inferenceModelIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(preferredIconPath);
            if (_inferenceModelIcon != null)
            {
                return _inferenceModelIcon;
            }

            var fallbackIconPath = EditorGUIUtility.isProSkin ? InferenceModelLightIconPath : InferenceModelDarkIconPath;
            _inferenceModelIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(fallbackIconPath);
            return _inferenceModelIcon;
        }
        private void DrawQuantizationEditor(FileRecipeEditState fileState)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(Content.QuantizationModeLabel);
                var options = BuildQuantizationOptions(fileState.QuantizationMode);
                var normalizedMode = NormalizeQuantizationMode(fileState.QuantizationMode);
                var currentIndex = Array.FindIndex(options, option => string.Equals(option.Value, normalizedMode, StringComparison.Ordinal));
                if (currentIndex < 0)
                {
                    currentIndex = 0;
                }

                var nextIndex = EditorGUILayout.Popup(currentIndex, options.Select(option => option.Label).ToArray());
                if (nextIndex >= 0 && nextIndex < options.Length)
                {
                    fileState.QuantizationMode = options[nextIndex].Value;
                }
            }
        }

        private void DrawDynamicDimsEditor(FileRecipeEditState fileState)
        {
            var dynamicDims = fileState.DynamicDimensions;
            var foldoutLabel = $"{Content.DynamicInputShapeDimensionsLabel} ({dynamicDims.Count})";
            fileState.DynamicDimensionsExpanded = EditorGUILayout.Foldout(
                fileState.DynamicDimensionsExpanded,
                new GUIContent(foldoutLabel, Content.DynamicDimsTooltip),
                true);
            if (!fileState.DynamicDimensionsExpanded)
            {
                return;
            }

            using (new EditorGUI.DisabledScope(dynamicDims.Count == 0))
            {
                EditorGUI.indentLevel++;
                if (dynamicDims.Count == 0)
                {
                    EditorGUILayout.LabelField(Content.NoDynamicDimensionsMessage, EditorStyles.miniLabel);
                    EditorGUI.indentLevel--;
                    return;
                }

                foreach (var dynamicDim in dynamicDims)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.PrefixLabel(dynamicDim.Label);
                        dynamicDim.Value = EditorGUILayout.IntField(dynamicDim.Value);
                    }
                }

                EditorGUI.indentLevel--;
            }
        }

        /// <summary>
        /// Draws repo-level apply and revert actions for current file edit session.
        /// </summary>
        private void DrawFilesActionBar(ModelHubItem item, RepoFileEditSession session)
        {
            var hasValidationErrors = session.HasValidationErrors;
            var isDirty = session.IsDirty;
            var isSyncRunning = BackgroundSyncManager.IsRunning;
            var actionContent = new GUIContent(
                Content.ApplyLabel,
                string.IsNullOrWhiteSpace(item.RawSnapshotRootPath)
                    ? Content.ApplyAndDownloadTooltip
                    : Content.ApplyAndReimportTooltip);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!isDirty))
                {
                    if (GUILayout.Button(Content.RevertLabel, GUILayout.Width(84f)))
                    {
                        GUI.FocusControl(null);
                        ResetFileEditSession(item);
                    }
                }

                using (new EditorGUI.DisabledScope(!isDirty || hasValidationErrors || isSyncRunning || item.HasRevisionConflict))
                {
                    if (GUILayout.Button(actionContent, GUILayout.Width(110f)))
                    {
                        EditorOperations.ApplyProjectOverridesAndReimport(item, session.BuildRecipesByRepoPath());
                        _fileEditSessions.Remove(GetFileEditSessionKey(item));
                        Refresh();
                    }
                }
            }
        }

        /// <summary>
        /// Anchors file editing actions below scroll view so they stay reachable while browsing long file lists.
        /// </summary>
        private void DrawFilesBottomBar(ModelHubItem item)
        {
            if (item == null || _selectedDetailTab != DetailTab.Files || !item.Files.Any(file => file.SupportsImportRecipeEditing))
            {
                return;
            }

            var session = GetOrCreateFileEditSession(item);
            GUILayout.Space(8f);
            var barRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(FilesBottomBarHeight));
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(barRect, GetDetailsContentBackgroundColor());
            }

            GUILayout.Space(6f);
            GUILayout.FlexibleSpace();
            GUILayout.BeginVertical();
            GUILayout.FlexibleSpace();
            DrawFilesActionBar(item, session);
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();
            GUILayout.Space(DetailsContentLeftPadding);
            EditorGUILayout.EndHorizontal();
        }

        private RepoFileEditSession GetOrCreateFileEditSession(ModelHubItem item)
        {
            var key = GetFileEditSessionKey(item);
            if (_fileEditSessions.TryGetValue(key, out var existingSession) &&
                existingSession.Matches(item))
            {
                return existingSession;
            }

            var session = RepoFileEditSession.Create(item);
            _fileEditSessions[key] = session;
            return session;
        }

        /// <summary>
        /// Resolves unsaved file-tab edits before selection or tab changes can hide them.
        /// </summary>
        private bool TryResolvePendingFileEdits(ModelHubItem item)
        {
            if (_selectedDetailTab != DetailTab.Files || item == null)
            {
                return true;
            }

            var session = GetOrCreateFileEditSession(item);
            if (!session.IsDirty)
            {
                return true;
            }

            var choice = EditorUtility.DisplayDialogComplex(
                Content.UnsavedImportSettingsTitle,
                string.Format(Content.UnsavedImportSettingsFormat, item.Repository),
                Content.SaveLabel,
                Content.DiscardLabel,
                Content.CancelLabel);
            switch (choice)
            {
                case 0:
                    if (session.HasValidationErrors)
                    {
                        EditorUtility.DisplayDialog(
                            Content.UnsavedImportSettingsTitle,
                            Content.InvalidImportSettingsMessage,
                            Content.OkLabel);
                        return false;
                    }

                    EditorOperations.ApplyProjectOverridesAndReimport(item, session.BuildRecipesByRepoPath());
                    _fileEditSessions.Remove(GetFileEditSessionKey(item));
                    Refresh();
                    return true;
                case 1:
                    _fileEditSessions.Remove(GetFileEditSessionKey(item));
                    return true;
                default:
                    return false;
            }
        }

        private void ResetFileEditSession(ModelHubItem item)
        {
            if (item == null)
            {
                return;
            }

            _fileEditSessions[GetFileEditSessionKey(item)] = RepoFileEditSession.Create(item);
            Repaint();
        }

        private static string GetFileEditSessionKey(ModelHubItem item)
        {
            return item == null
                ? string.Empty
                : $"{item.Repository}\n{item.RequestedRevision}";
        }

        private static void DrawFileHeaderLabel(Rect rect, string repoPath)
        {
            EditorGUI.LabelField(rect, repoPath, EditorStyles.boldLabel);
        }

        private static QuantizationOption[] BuildQuantizationOptions(string currentMode)
        {
            var normalizedMode = NormalizeQuantizationMode(currentMode);
            return DefaultQuantizationOptions
                .Concat(string.IsNullOrWhiteSpace(normalizedMode) || DefaultQuantizationOptions.Any(option => string.Equals(option.Value, normalizedMode, StringComparison.Ordinal))
                    ? Array.Empty<QuantizationOption>()
                    : new[] { new QuantizationOption(normalizedMode, normalizedMode) })
                .GroupBy(option => option.Value, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();
        }

        internal static string NormalizeQuantizationMode(string quantizationMode)
        {
            return string.IsNullOrWhiteSpace(quantizationMode)
                ? "none"
                : quantizationMode.Trim();
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
