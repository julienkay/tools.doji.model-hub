using UnityEditor;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    internal sealed partial class ModelHubWindow
    {
        internal static string GetStatusBadgeLabel(ModelHubItem item)
        {
            if (item.HasRevisionConflict)
            {
                return Content.ConflictState;
            }

            if (item.State == ModelHubItemState.Ready)
            {
                return Content.ImportedState;
            }

            var hasCacheSnapshot = !string.IsNullOrWhiteSpace(item.RawSnapshotRootPath);
            var hasRuntimeFiles = item.RuntimeFiles.Count > 0;

            if (!hasCacheSnapshot)
            {
                return Content.DownloadNeededState;
            }

            if (item.State == ModelHubItemState.Error)
            {
                return Content.RepairNeededState;
            }

            return hasRuntimeFiles ? Content.UpdateNeededState : Content.ImportNeededState;
        }

        private void DrawStatusBadge(ModelHubItem item, Rect rect)
        {
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, GetStatusBadgeColor(item));
            }

            GUI.Label(
                rect,
                new GUIContent(GetStatusBadgeLabel(item), GetStatusBadgeTooltip(item)),
                _statusBadgeStyle);
        }

        private static float GetStatusBadgeWidth(ModelHubItem item)
        {
            return GetStatusBadgeLabel(item) switch
            {
                var label when label == Content.ImportedState => ImportedStatusBadgeWidth,
                var label when label == Content.DownloadNeededState => DownloadNeededStatusBadgeWidth,
                _ => DefaultStatusBadgeWidth
            };
        }

        private static Color GetSelectionColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.23f, 0.45f, 0.69f, 1f)
                : new Color(0.24f, 0.47f, 0.74f, 1f);
        }

        private static Color GetSelectionBorderColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.31f, 0.56f, 0.82f, 1f)
                : new Color(0.19f, 0.39f, 0.65f, 1f);
        }

        private static Color GetDetailTabActiveTextColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.92f, 0.92f, 0.92f, 1f)
                : new Color(0.18f, 0.18f, 0.18f, 1f);
        }

        private static Color GetDetailTabDisabledTextColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.52f, 0.52f, 0.52f, 1f)
                : new Color(0.55f, 0.55f, 0.55f, 1f);
        }

        private static Color GetDetailTabUnderlineColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.92f, 0.92f, 0.92f, 1f)
                : new Color(1f, 1f, 1f, 1f);
        }

        private static Color GetParentRowColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.20f, 0.20f, 0.20f, 1f)
                : new Color(0.78f, 0.78f, 0.78f, 1f);
        }

        private static Color GetChildRowColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.27f, 0.27f, 0.27f, 1f)
                : new Color(0.88f, 0.88f, 0.88f, 1f);
        }

        private static Color GetDetailsContentBackgroundColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.18f, 0.18f, 0.18f, 1f)
                : new Color(0.92f, 0.92f, 0.92f, 1f);
        }


        internal static string GetStateIconName(ModelHubItem item)
        {
            if (item == null)
            {
                return UpdateNeededStateIconName;
            }

            if (item.HasRevisionConflict || item.State == ModelHubItemState.Error)
            {
                return ErrorStateIconName;
            }

            return GetStatusBadgeLabel(item) switch
            {
                var label when label == Content.DownloadNeededState => DownloadNeededStateIconName,
                var label when label == Content.ImportNeededState => ImportNeededStateIconName,
                var label when label == Content.UpdateNeededState => UpdateNeededStateIconName,
                _ => null
            };
        }

        private void DrawStateIcon(ModelHubItem item, Rect rect)
        {
            InitializeGuiContent();

            if (item.State == ModelHubItemState.Ready)
            {
                GUI.Label(rect, Content.ReadyStateGlyph, _readyStateGlyphStyle);
                return;
            }

            var iconName = GetStateIconName(item);
            GUIContent icon = null;
            if (string.Equals(iconName, ErrorStateIconName, System.StringComparison.Ordinal))
            {
                icon = _errorStateIconContent;
            }
            else if (string.Equals(iconName, UpdateNeededStateIconName, System.StringComparison.Ordinal))
            {
                icon = _actionRequiredStateIconContent;
            }
            else if (!string.IsNullOrWhiteSpace(iconName))
            {
                icon = EditorGUIUtility.IconContent(iconName);
            }

            if (icon != null)
            {
                GUI.Label(rect, icon);
            }
        }

        internal static Color GetStatusBadgeColor(ModelHubItem item)
        {
            if (item.HasRevisionConflict)
            {
                return new Color(0.68f, 0.30f, 0.28f, 1f);
            }

            if (item.State == ModelHubItemState.Ready)
            {
                return new Color(0.34f, 0.55f, 0.36f, 1f);
            }

            if (item.State == ModelHubItemState.Error)
            {
                return new Color(0.68f, 0.30f, 0.28f, 1f);
            }

            return new Color(0.76f, 0.60f, 0.24f, 1f);
        }

        private static string GetStatusBadgeTooltip(ModelHubItem item)
        {
            if (item.HasRevisionConflict)
            {
                return Content.RevisionConflictTooltip;
            }

            var hasCacheSnapshot = !string.IsNullOrWhiteSpace(item.RawSnapshotRootPath);

            if (item.State == ModelHubItemState.Ready)
            {
                return hasCacheSnapshot
                    ? Content.ImportedTooltip
                    : Content.ImportedWithoutCacheTooltip;
            }

            var hasRuntimeFiles = item.RuntimeFiles.Count > 0;

            if (!hasCacheSnapshot)
            {
                return Content.DownloadNeededTooltip;
            }

            if (item.State == ModelHubItemState.Error)
            {
                return hasRuntimeFiles
                    ? Content.RepairNeededTooltip
                    : Content.ImportFailedTooltip;
            }

            return hasRuntimeFiles
                ? Content.UpdateNeededTooltip
                : Content.ImportNeededTooltip;
        }
    }
}
