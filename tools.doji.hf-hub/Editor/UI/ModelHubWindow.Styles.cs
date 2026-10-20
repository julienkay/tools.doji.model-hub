using UnityEditor;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    internal sealed partial class ModelHubWindow
    {
        internal const string DownloadNeededStateIconName = "Download-Available@2x";
        internal const string ImportNeededStateIconName = "Import-Available@2x";
        internal const string UpdateNeededStateIconName = "Update-Available@2x";
        internal const string ErrorStateIconName = "console.erroricon";

        private static Color GetHeaderBackgroundColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.20f, 0.20f, 0.20f, 1f)
                : new Color(0.84f, 0.84f, 0.84f, 1f);
        }

        private static Color GetHeaderBorderColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.10f, 0.10f, 0.10f, 1f)
                : new Color(0.65f, 0.65f, 0.65f, 1f);
        }

        private static Color GetHeaderButtonHoverColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(1f, 1f, 1f, 0.06f)
                : new Color(0f, 0f, 0f, 0.05f);
        }

        private void InitializeGuiContent()
        {
            _refreshIconContent ??= EditorGUIUtility.IconContent("Refresh");
            _refreshIconContent.tooltip = Content.RefreshTooltip;
            _importAllContent ??= new GUIContent(Content.ImportAllLabel, Content.ImportAllTooltip);
            _importContent ??= new GUIContent(Content.ImportLabel, Content.ImportTooltip);
            _headerAddContent ??= EditorGUIUtility.IconContent("d_CreateAddNew@2x");
            _headerAddContent.tooltip = Content.HeaderAddTooltip;
            _headerMoreContent ??= EditorGUIUtility.IconContent("_Menu@2x");
            _headerMoreContent.tooltip = Content.HeaderMoreTooltip;
            _jumpToStorageContent ??= EditorGUIUtility.IconContent(
                EditorGUIUtility.isProSkin
                    ? "d_AnalyticsTracker Icon"
                    : "AnalyticsTracker Icon");
            _jumpToStorageContent.tooltip = Content.JumpToStorageTooltip;
            _manageContent ??= new GUIContent(Content.ManageLabel);
            _locateContent ??= new GUIContent(Content.LocateLabel, Content.LocateTooltip);
            _actionRequiredStateIconContent ??= EditorGUIUtility.IconContent(UpdateNeededStateIconName);
            _errorStateIconContent ??= EditorGUIUtility.IconContent(ErrorStateIconName);
        }

        private void InitializeGuiStyles()
        {
            _statusBadgeStyle ??= new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                clipping = TextClipping.Clip,
                normal = { textColor = Color.white },
                hover = { textColor = Color.white },
                active = { textColor = Color.white },
                focused = { textColor = Color.white }
            };
            _packageEntryTitleStyle ??= new GUIStyle(EditorStyles.boldLabel)
            {
                clipping = TextClipping.Clip,
                wordWrap = false,
                stretchWidth = true
            };
            _pathLinkStyle ??= new GUIStyle(EditorStyles.linkLabel)
            {
                clipping = TextClipping.Clip,
                wordWrap = false,
                stretchWidth = true,
                alignment = TextAnchor.MiddleLeft
            };
            _refreshButtonStyle ??= new GUIStyle(GUI.skin.button)
            {
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                alignment = TextAnchor.MiddleCenter
            };
            _readyStateGlyphStyle ??= new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
                hover = { textColor = Color.white },
                active = { textColor = Color.white },
                focused = { textColor = Color.white }
            };
            _detailsHeaderTitleStyle ??= new GUIStyle(EditorStyles.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
                clipping = TextClipping.Clip
            };
            _sectionFoldoutStyle ??= new GUIStyle(EditorStyles.foldout)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                fixedHeight = EditorGUIUtility.singleLineHeight
            };
            _detailsTabLabelStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Normal,
                clipping = TextClipping.Clip,
                wordWrap = false
            };
            _manageButtonStyle ??= new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(10, 18, 2, 2)
            };
            _manageDropdownArrowStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                clipping = TextClipping.Overflow,
                normal =
                {
                    textColor = EditorGUIUtility.isProSkin
                        ? new Color(0.82f, 0.82f, 0.82f, 1f)
                        : new Color(0.30f, 0.30f, 0.30f, 1f)
                }
            };
            _headerAddButtonStyle ??= new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 18,
                fixedHeight = 0f,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                clipping = TextClipping.Overflow
            };
            _headerActionButtonStyle ??= new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 18,
                fixedHeight = 0f,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                clipping = TextClipping.Overflow
            };
            var headerTextColor = EditorGUIUtility.isProSkin
                ? new Color(0.86f, 0.86f, 0.86f, 1f)
                : new Color(0.12f, 0.12f, 0.12f, 1f);
            _headerAddButtonStyle.normal.textColor = headerTextColor;
            _headerAddButtonStyle.hover.textColor = headerTextColor;
            _headerAddButtonStyle.active.textColor = headerTextColor;
            _headerAddButtonStyle.focused.textColor = headerTextColor;
            _headerActionButtonStyle.normal.textColor = headerTextColor;
            _headerActionButtonStyle.hover.textColor = headerTextColor;
            _headerActionButtonStyle.active.textColor = headerTextColor;
            _headerActionButtonStyle.focused.textColor = headerTextColor;
            _searchFieldStyle ??= new GUIStyle(GUI.skin.FindStyle(Content.SearchStyleName) ?? EditorStyles.toolbarSearchField)
            {
                fixedHeight = SearchFieldHeight,
                stretchHeight = false,
                alignment = TextAnchor.MiddleLeft
            };
            _treemapTileLabelStyle ??= new GUIStyle(EditorStyles.whiteMiniLabel)
            {
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
                clipping = TextClipping.Clip,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };
            _treemapModeSegmentStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                fixedHeight = 22f,
                padding = new RectOffset(6, 6, 3, 3),
                margin = new RectOffset(0, 0, 0, 0),
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
            var neutralTextColor = EditorGUIUtility.isProSkin
                ? new Color(0.90f, 0.92f, 0.94f, 1f)
                : new Color(0.18f, 0.20f, 0.22f, 1f);
            _treemapModeSegmentStyle.normal.textColor = neutralTextColor;
            _treemapModeSegmentStyle.hover.textColor = neutralTextColor;
            _treemapModeSegmentStyle.active.textColor = neutralTextColor;
            _treemapModeSegmentStyle.focused.textColor = neutralTextColor;
        }
    }
}
