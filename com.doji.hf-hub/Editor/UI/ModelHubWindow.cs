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
            Dependents,
            Files,
            Diagnostics,
            DiscoveryIssues
        }

        private ModelHubReport _report;
        private DateTime _lastRefreshLocalTime;
        private Vector2 _listScrollPosition;
        private Vector2 _detailsScrollPosition;
        private string _searchText = string.Empty;
        private int _selectedIndex = -1;
        private DetailTab _selectedDetailTab = DetailTab.Summary;
        private GUIContent _refreshIconContent;
        private GUIContent _importAllContent;
        private GUIContent _importContent;
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
        private float _listPaneWidth = DefaultListPaneWidth;
        private bool _isDraggingSplitter;
        private readonly Dictionary<string, bool> _sectionExpandedStates = new(StringComparer.Ordinal);
        private readonly Dictionary<string, RepoFileEditSession> _fileEditSessions = new(StringComparer.Ordinal);
        private readonly IRemoteSnapshotSizeEstimateService _remoteSnapshotSizeEstimateService = new RemoteSnapshotSizeEstimateService();
        private static readonly QuantizationOption[] DefaultQuantizationOptions =
        {
            new("none", "None"),
            new("float16", "Float16"),
            new("int8", "Int8")
        };

        private const float HeaderHeight = 24f;
        private const float StatusBadgeWidth = 110f;
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
        private const float ManageButtonWidth = 72f;
        private const float FileActionButtonWidth = 110f;

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
            GUILayout.BeginArea(headerRect, EditorStyles.toolbar);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(_importAllContent, EditorStyles.toolbarButton, GUILayout.Width(90f)))
                {
                    EditorOperations.StartBackgroundSync();
                    Refresh();
                }
            }

            GUILayout.EndArea();
        }

    }
}
