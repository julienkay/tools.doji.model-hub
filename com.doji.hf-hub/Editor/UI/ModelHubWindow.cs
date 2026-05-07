using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Provides main repository inventory surface for imported and declared Model Hub content.
    /// </summary>
    internal sealed partial class ModelHubWindow : EditorWindow
    {
        private enum DetailTab
        {
            Summary,
            Storage,
            Dependents,
            Files,
            Diagnostics
        }

        private enum StorageTreemapMode
        {
            Combined,
            SplitByStorage
        }

        private ModelHubReport _report;
        private DateTime _lastRefreshLocalTime;
        private Vector2 _listScrollPosition;
        private Vector2 _detailsScrollPosition;
        private string _searchText = string.Empty;
        private int _selectedIndex = -1;
        private int _selectedIssueIndex = -1;
        private DetailTab _selectedDetailTab = DetailTab.Summary;
        private StorageTreemapMode _storageTreemapMode = StorageTreemapMode.SplitByStorage;
        private GUIContent _refreshIconContent;
        private GUIContent _importAllContent;
        private GUIContent _importContent;
        private GUIContent _headerAddContent;
        private GUIContent _headerMoreContent;
        private GUIContent _jumpToStorageContent;
        private GUIContent _manageContent;
        private GUIContent _locateContent;
        private GUIContent _actionRequiredStateIconContent;
        private GUIContent _errorStateIconContent;
        private GUIStyle _refreshButtonStyle;
        private GUIStyle _statusBadgeStyle;
        private GUIStyle _packageEntryTitleStyle;
        private GUIStyle _pathLinkStyle;
        private GUIStyle _readyStateGlyphStyle;
        private GUIStyle _detailsHeaderTitleStyle;
        private GUIStyle _searchFieldStyle;
        private GUIStyle _sectionFoldoutStyle;
        private GUIStyle _detailsTabLabelStyle;
        private GUIStyle _manageButtonStyle;
        private GUIStyle _manageDropdownArrowStyle;
        private GUIStyle _headerAddButtonStyle;
        private GUIStyle _headerActionButtonStyle;
        private GUIStyle _treemapTileLabelStyle;
        private GUIStyle _treemapModeSegmentStyle;
        private string _hoveredSplitTreemapRepoPath;
        private string _previousHoveredSplitTreemapRepoPath;
        private double _hoveredSplitTreemapChangedAtTime;
        private float _listPaneWidth = DefaultListPaneWidth;
        private bool _isDraggingSplitter;
        private readonly Dictionary<string, bool> _sectionExpandedStates = new(StringComparer.Ordinal);
        private readonly Dictionary<string, RepoFileEditSession> _fileEditSessions = new(StringComparer.Ordinal);
        private readonly IRemoteSnapshotSizeEstimateService _remoteSnapshotSizeEstimateService = new RemoteSnapshotSizeEstimateService();
        private readonly IProjectOptionalFileSelectionSource _optionalFileSelectionSource = new ProjectOptionalFileSelectionSource();
        private Rect _headerAddButtonRect;
        private static readonly QuantizationOption[] DefaultQuantizationOptions =
        {
            new("none", "None"),
            new("float16", "Float16"),
            new("int8", "Int8")
        };

        private const float HeaderHeight = 32;
        private const float HeaderIconSize = 16f;
        private const float DefaultStatusBadgeWidth = 110f;
        private const float ImportedStatusBadgeWidth = 84f;
        private const float DownloadNeededStatusBadgeWidth = 124f;
        private const float SearchRowHeight = 40f;
        private const float SearchFieldHeight = 24f;
        private const float SectionHeaderHeight = 30f;
        private const float ListItemHeight = 28f;
        private const float ListItemPadding = 8f;
        private const float ListItemIconSize = 16f;
        private const float ChildRowIndent = 18f;
        private const float DefaultListPaneWidth = 340f;
        private const float MinListPaneWidth = 240f;
        private const float MinDetailsPaneWidth = 360f;
        private const float SplitterWidth = 4f;
        private const float DetailsHeaderHeight = 34f;
        private const float DetailsTabHeight = 24f;
        private const float DetailsTabUnderlineHeight = 1f;
        private const float DetailsTabWidth = 96f;
        private const float DetailsContentPadding = 6f;
        private const float DetailsContentLeftPadding = 12f;
        private const float FilesBottomBarHeight = 36f;
        private const float HeaderAddButtonWidth = 36f;
        private const float HeaderButtonVerticalPadding = 2f;
        private const float HeaderMoreButtonWidth = 28f;
        private const float HeaderHorizontalPadding = 0f;
        private const float FileActionButtonWidth = 110f;
        private const float StorageTreemapAspectRatio = 2.2f;
        private const double StorageTreemapHoverTransitionDuration = 0.16d;

        [MenuItem("Window/AI Model Hub")]
        private static void OpenFromWindowMenu()
        {
            OpenWindow();
        }

        public static void OpenWindow(bool resetFilters = false)
        {
            var window = GetWindow<ModelHubWindow>(Content.Title);
            window.minSize = new Vector2(840f, 520f);
            if (resetFilters)
            {
                window.ResetFilters();
            }

            window.Refresh();
            window.Show();
        }

        private void OnEnable()
        {
            InitializeGuiContent();
            wantsMouseMove = true;
            BackgroundSyncManager.SyncStateChanged += HandleSyncStateChanged;
            Refresh();
        }

        private void OnDisable()
        {
            BackgroundSyncManager.SyncStateChanged -= HandleSyncStateChanged;
        }

        private void CreateGUI()
        {
            InitializeGuiContent();
            wantsMouseMove = true;
        }

        private void OnGUI()
        {
            InitializeGuiStyles();
            DrawHeader();

            _report ??= EditorOperations.CreateModelHubReport();
            var items = GetFilteredItems();
            var contentRect = new Rect(0f, HeaderHeight, position.width, Mathf.Max(0f, position.height - HeaderHeight));
            var paneLayout = GetPaneLayout(contentRect);

            DrawPaneBackgrounds(paneLayout);
            HandleSplitter(contentRect, paneLayout.SplitterRect);
            DrawSplitter(paneLayout.SplitterRect);
            DrawListPane(paneLayout.ListRect, items);
            DrawDetailsPane(paneLayout.DetailsRect, items);
        }

        private void DrawHeader()
        {
            var headerRect = new Rect(0f, 0f, position.width, HeaderHeight);
            var headerButtonHeight = Mathf.Max(EditorGUIUtility.singleLineHeight, HeaderHeight - (HeaderButtonVerticalPadding * 2f));

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(headerRect, GetHeaderBackgroundColor());
                EditorGUI.DrawRect(
                    new Rect(headerRect.x, headerRect.yMax - 1f, headerRect.width, 1f),
                    GetHeaderBorderColor());
            }

            var contentRect = new Rect(
                headerRect.x + HeaderHorizontalPadding,
                headerRect.y + HeaderButtonVerticalPadding,
                Mathf.Max(0f, headerRect.width - (HeaderHorizontalPadding * 2f)),
                headerButtonHeight);

            GUILayout.BeginArea(contentRect);
            using (new EditorGUILayout.HorizontalScope())
            {
                var addRect = GUILayoutUtility.GetRect(
                    _headerAddContent,
                    _headerAddButtonStyle,
                    GUILayout.Width(HeaderAddButtonWidth),
                    GUILayout.Height(headerButtonHeight));
                _headerAddButtonRect = addRect;
                if (DrawHeaderButton(addRect, _headerAddContent, _headerAddButtonStyle))
                {
                    ShowHeaderAddMenu(addRect);
                }

                GUILayout.FlexibleSpace();
                var moreRect = GUILayoutUtility.GetRect(
                    _headerMoreContent,
                    _headerActionButtonStyle,
                    GUILayout.Width(HeaderMoreButtonWidth),
                    GUILayout.Height(headerButtonHeight));
                if (DrawHeaderButton(moreRect, _headerMoreContent, _headerActionButtonStyle))
                {
                    ShowHeaderMoreMenu(moreRect);
                }
            }

            GUILayout.EndArea();
        }

        private bool DrawHeaderButton(Rect rect, GUIContent content, GUIStyle style)
        {
            var eventType = Event.current.type;
            if (eventType == EventType.Repaint)
            {
                var tint = rect.Contains(Event.current.mousePosition)
                    ? GetHeaderButtonHoverColor()
                    : Color.clear;
                if (tint.a > 0f)
                {
                    EditorGUI.DrawRect(rect, tint);
                }

                if (content?.image != null)
                {
                    var iconRect = new Rect(
                        rect.x + ((rect.width - HeaderIconSize) * 0.5f),
                        rect.y + ((rect.height - HeaderIconSize) * 0.5f),
                        HeaderIconSize,
                        HeaderIconSize);
                    GUI.Label(iconRect, content, style);
                }
                else
                {
                    GUI.Label(rect, content, style);
                }
            }

            return GUI.Button(rect, GUIContent.none, GUIStyle.none);
        }

    }
}
