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
                var fileState = session.GetOrCreateFileState(file);
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(file.RepoPath, EditorStyles.boldLabel);
                    DrawOptionalLinkRow(Content.RawCachedFileLabel, file.RawCachedFilePath, ProjectSettingsLinks.RevealPath);
                    DrawOptionalValueRow(Content.RawSizeLabel, FormatByteSize(file.RawSizeInBytes));
                    DrawOptionalValueRow(Content.RecipeSourceLabel, file.RecipeSourceLabel);
                    DrawOptionalLinkRow(Content.OverrideAssetLabel, file.OverrideAssetPath, ProjectSettingsLinks.PingAsset);
                    DrawOptionalLinkRow(Content.RuntimeAssetLabel, file.RuntimeFile?.AssetPath, ProjectSettingsLinks.PingAsset);
                    DrawOptionalValueRow(Content.RuntimeRelativeLabel, file.RuntimeFile?.RelativePath);

                    DrawQuantizationEditor(fileState);
                    DrawDynamicDimsEditor(fileState);
                    fileState.RefreshValidation();

                    if (!string.IsNullOrWhiteSpace(fileState.ValidationMessage))
                    {
                        EditorGUILayout.HelpBox(fileState.ValidationMessage, MessageType.Warning);
                    }
                }

                EditorGUILayout.Space(8f);
            }

            EditorGUILayout.Space();
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
                if (dynamicDims.Count == 0)
                {
                    EditorGUILayout.LabelField(Content.NoDynamicDimensionsMessage, EditorStyles.miniLabel);
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
            var actionLabel = string.IsNullOrWhiteSpace(item.RawSnapshotRootPath)
                ? Content.ApplyAndDownloadLabel
                : Content.ApplyAndReimportLabel;

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(!isDirty))
                {
                    if (GUILayout.Button(Content.RevertLabel, GUILayout.Width(FileActionButtonWidth)))
                    {
                        ResetFileEditSession(item);
                    }
                }

                using (new EditorGUI.DisabledScope(!isDirty || hasValidationErrors || isSyncRunning || item.HasRevisionConflict))
                {
                    if (GUILayout.Button(actionLabel, GUILayout.Width(150f)))
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
            if (item == null || _selectedDetailTab != DetailTab.Files)
            {
                return;
            }

            var session = GetOrCreateFileEditSession(item);
            GUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope(GUILayout.Height(FilesBottomBarHeight)))
            {
                GUILayout.FlexibleSpace();
                DrawFilesActionBar(item, session);
            }
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
