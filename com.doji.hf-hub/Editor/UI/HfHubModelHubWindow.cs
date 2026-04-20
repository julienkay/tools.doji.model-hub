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
    /// Provides the main repository inventory surface for imported and declared Model Hub content.
    /// </summary>
    internal sealed class HfHubModelHubWindow : EditorWindow
    {
        private static class Content
        {
            public const string Title = "AI Model Hub";
            public const string SearchStyleName = "ToolbarSeachTextField";
            public const string SearchCancelStyleName = "ToolbarSeachCancelButton";
            public const string SearchCancelEmptyStyleName = "ToolbarSeachCancelButtonEmpty";
            public const string RefreshTooltip = "Refresh Model Hub status and repository inventory.";
            public const string ImportAllLabel = "Import All";
            public const string ImportAllTooltip = "Resolve declared repositories, download missing raw snapshots, and import generated runtime assets.";
            public const string ImportLabel = "Import";
            public const string ImportTooltip = "Resolve and import only this repository entry.";
            public const string DownloadLabel = "Download";
            public const string DownloadTooltip = "Download this repository into cache first, then import it into the project.";
            public const string SyncInProgressTooltip = "A model sync is already running. Wait for it to finish before starting another import or download.";
            public const string ManageLabel = "Manage";
            public const string RemoveLabel = "Remove From Project";
            public const string RemoveTooltip = "Remove generated runtime assets and import metadata from this project.";
            public const string RemoveFromCacheLabel = "Remove From Cache";
            public const string RemoveFromCacheTooltip = "Remove downloaded raw repository files from this machine's shared cache and keep generated project assets.";
            public const string RemoveFromCacheDisabledTooltip = "This repository no longer has raw cache files on this machine.";
            public const string LocateLabel = "Locate";
            public const string LocateTooltip = "Ping manifest asset for selected dependency owner.";
            public const string DiscoveryIssuesBannerFormat = "{0} manifest discovery issue(s) need maintainer attention.";
            public const string LastRefreshFormat = "Last refresh {0}";
            public const string NoRepositoriesMatchFilters = "No repositories match the current filters.";
            public const string EmptyStateMessage = "Declare or sync a manifest dependency to populate Model Hub.";
            public const string SummarySection = "Details";
            public const string DependentsSection = "Dependents";
            public const string FilesSection = "Files";
            public const string DiagnosticsSection = "Diagnostics";
            public const string DiscoveryIssuesSection = "Discovery Issues";
            public const string RepositoryLabel = "Repository";
            public const string RequestedRevisionLabel = "Requested Revision";
            public const string ResolvedCommitLabel = "Resolved Commit";
            public const string ManifestOwnersLabel = "Manifest Owners";
            public const string GeneratedSentisFilesLabel = "Generated .sentis Files";
            public const string RawSnapshotLabel = "Cache Location";
            public const string CacheSizeOnDiskLabel = "Cache Size";
            public const string EstimatedCacheDownloadSizeLabel = "Cache Estimate";
            public const string RuntimeFolderLabel = "Runtime Folder";
            public const string RuntimeAssetsSizeOnDiskLabel = "Runtime Size";
            public const string EstimatedRuntimeAssetsSizeLabel = "Runtime Estimate";
            public const string OwnerLabel = "Owner";
            public const string ModelIdLabel = "Model Id";
            public const string ManifestLabel = "Manifest";
            public const string RuntimeRegistryLabel = "Runtime Registry";
            public const string RuntimeAssetLabel = "Runtime Asset";
            public const string RuntimeRelativeLabel = "Runtime Relative";
            public const string NoRuntimeFilesMessage = "No generated runtime files are registered for this repository yet.";
            public const string NoDiagnosticsMessage = "No diagnostics for this repository.";
            public const string UnresolvedCommitPlaceholder = "<not resolved>";
            public const string ImportedState = "Imported";
            public const string ImportNeededState = "Import Needed";
            public const string UpdateNeededState = "Update Needed";
            public const string DownloadNeededState = "Download Needed";
            public const string RepairNeededState = "Needs Repair";
            public const string SelectionBorderColor = "#3A72B0";
            public const string ReadyStateGlyph = "✓";
        }

        private enum DetailTab
        {
            Summary,
            Dependents,
            Files,
            Diagnostics,
            DiscoveryIssues
        }

        private HfHubModelHubReport _report;
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
        private readonly Dictionary<string, bool> _sectionExpandedStates = new Dictionary<string, bool>(StringComparer.Ordinal);
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
        private const float ManageButtonWidth = 80f;

        [MenuItem("Window/AI Model Hub")]
        private static void OpenFromWindowMenu()
        {
            OpenWindow();
        }

        public static void OpenWindow()
        {
            var window = GetWindow<HfHubModelHubWindow>(Content.Title);
            window.minSize = new Vector2(840f, 520f);
            window.Refresh();
            window.Show();
        }

        private void OnEnable()
        {
            InitializeGuiContent();
            HfHubBackgroundSyncManager.SyncStateChanged += HandleSyncStateChanged;
            Refresh();
        }

        private void OnDisable()
        {
            HfHubBackgroundSyncManager.SyncStateChanged -= HandleSyncStateChanged;
        }

        private void CreateGUI()
        {
            InitializeGuiContent();
        }

        private void OnGUI()
        {
            InitializeGuiStyles();
            DrawHeader();

            _report ??= HfHubEditorOperations.CreateModelHubReport();
            var items = GetFilteredItems();
            var contentRect = new Rect(0f, HeaderHeight, position.width, Mathf.Max(0f, position.height - HeaderHeight));
            var paneLayout = GetPaneLayout(contentRect);

            DrawPaneBackgrounds(paneLayout);
            HandleSplitter(contentRect, paneLayout.SplitterRect);
            DrawSplitter(paneLayout.SplitterRect);
            DrawListPane(paneLayout.ListRect, items);
            DrawDetailsPane(paneLayout.DetailsRect, items);
        }

        private void DrawListPane(Rect rect, IReadOnlyList<HfHubModelHubItem> items)
        {
            GUILayout.BeginArea(rect);
            using (new EditorGUILayout.VerticalScope())
            {
                DrawListToolbar();
                _listScrollPosition = EditorGUILayout.BeginScrollView(_listScrollPosition, false, false);

                if (_report.DiscoveryIssues.Count > 0)
                {
                    EditorGUILayout.HelpBox(
                        string.Format(Content.DiscoveryIssuesBannerFormat, _report.DiscoveryIssues.Count),
                        MessageType.Warning);
                }

                if (items.Count == 0)
                {
                    EditorGUILayout.HelpBox(Content.NoRepositoriesMatchFilters, MessageType.Info);
                }

                foreach (var group in BuildListGroups(items))
                {
                    DrawListGroup(group, items);
                }

                EditorGUILayout.EndScrollView();

                EditorGUILayout.Space(4f);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(GetLastRefreshLabel(), EditorStyles.miniLabel);
                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button(_refreshIconContent, _refreshButtonStyle, GUILayout.Width(20f), GUILayout.Height(20f)))
                    {
                        Refresh();
                    }
                }
            }
            GUILayout.EndArea();
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
                    HfHubEditorOperations.StartBackgroundSync();
                    Refresh();
                }
            }
            GUILayout.EndArea();
        }

        private void DrawListToolbar()
        {
            var rowRect = GUILayoutUtility.GetRect(0f, SearchRowHeight, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                EditorStyles.helpBox.Draw(rowRect, false, false, false, false);
            }

            var contentRect = new Rect(
                rowRect.x + ListItemPadding,
                rowRect.y + ((rowRect.height - SearchFieldHeight) * 0.5f),
                Mathf.Max(0f, rowRect.width - (ListItemPadding * 2f)),
                SearchFieldHeight);

            var cancelButtonWidth = 14f;
            var searchFieldRect = new Rect(
                contentRect.x,
                contentRect.y,
                Mathf.Max(0f, contentRect.width - cancelButtonWidth),
                contentRect.height);
            var cancelRect = new Rect(
                searchFieldRect.xMax,
                contentRect.y,
                cancelButtonWidth,
                contentRect.height);

            var updatedSearchText = GUI.TextField(
                searchFieldRect,
                _searchText,
                _searchFieldStyle);
            if (!string.Equals(updatedSearchText, _searchText, StringComparison.Ordinal))
            {
                _searchText = updatedSearchText;
                _selectedIndex = -1;
            }

            var cancelStyleName = string.IsNullOrEmpty(_searchText)
                ? Content.SearchCancelEmptyStyleName
                : Content.SearchCancelStyleName;
            var cancelStyle = GUI.skin.FindStyle(cancelStyleName) ?? GUIStyle.none;
            if (GUI.Button(cancelRect, GUIContent.none, cancelStyle))
            {
                _searchText = string.Empty;
                _selectedIndex = -1;
                GUI.FocusControl(null);
            }

            EditorGUILayout.Space(4f);
        }

        private void DrawListItem(int index, HfHubModelHubItem item, bool isChildRow = false)
        {
            var itemRect = GUILayoutUtility.GetRect(0f, ListItemHeight, GUILayout.ExpandWidth(true));
            var isSelected = _selectedIndex == index;

            if (Event.current.type == EventType.Repaint)
            {
                // Paint rows directly so selection fill, child-row tint, and full-row hit targets stay
                // aligned without fighting nested IMGUI layout controls.
                EditorGUI.DrawRect(
                    itemRect,
                    isSelected
                        ? GetSelectionColor()
                        : (isChildRow ? GetChildRowColor() : GetParentRowColor()));
                if (isSelected)
                {
                    EditorGUI.DrawRect(new Rect(itemRect.x, itemRect.y, itemRect.width, 1f), GetSelectionBorderColor());
                    EditorGUI.DrawRect(new Rect(itemRect.x, itemRect.yMax - 1f, itemRect.width, 1f), GetSelectionBorderColor());
                }
            }

            if (GUI.Button(itemRect, GUIContent.none, GUIStyle.none))
            {
                _selectedIndex = index;
                GUI.FocusControl(null);
            }

            var contentRect = new Rect(
                itemRect.x + ListItemPadding + (isChildRow ? ChildRowIndent : 0f),
                itemRect.y,
                itemRect.width - (ListItemPadding * 2f) - (isChildRow ? ChildRowIndent : 0f),
                itemRect.height);

            // Vertically center both title and status icon from the row height itself so the list stays
            // visually stable as editor skins/font metrics vary.
            var lineHeight = EditorGUIUtility.singleLineHeight;
            var contentY = contentRect.y + Mathf.Max(0f, (contentRect.height - lineHeight) * 0.5f);
            var iconY = contentRect.y + Mathf.Max(0f, (contentRect.height - ListItemIconSize) * 0.5f);

            var titleRect = new Rect(
                contentRect.x,
                contentY,
                contentRect.width - ListItemIconSize - 6f,
                lineHeight);
            GUI.Label(titleRect, GetRepositoryDisplayName(item.Repository), _packageEntryTitleStyle);

            var iconRect = new Rect(
                contentRect.xMax - ListItemIconSize,
                iconY,
                ListItemIconSize,
                ListItemIconSize);
            DrawStateIcon(item.State, iconRect);
        }

        private void DrawListGroup(HfHubListGroup group, IReadOnlyList<HfHubModelHubItem> items)
        {
            var expanded = GetIsSectionExpanded(group.Owner);
            var selectedItem = _selectedIndex >= 0 && _selectedIndex < items.Count
                ? items[_selectedIndex]
                : null;
            var containsSelectedItem = selectedItem != null && group.Items.Contains(selectedItem);
            var headerRect = GUILayoutUtility.GetRect(0f, SectionHeaderHeight, GUILayout.ExpandWidth(true));

            if (Event.current.type == EventType.Repaint)
            {
                EditorStyles.helpBox.Draw(headerRect, false, false, false, false);
            }

            var foldoutRect = new Rect(
                headerRect.x + ListItemPadding,
                headerRect.y + ((headerRect.height - EditorGUIUtility.singleLineHeight) * 0.5f),
                Mathf.Max(0f, headerRect.width - (ListItemPadding * 2f)),
                EditorGUIUtility.singleLineHeight);
            var updatedExpanded = EditorGUI.Foldout(foldoutRect, expanded, group.Owner, true, _sectionFoldoutStyle);
            if (updatedExpanded != expanded)
            {
                _sectionExpandedStates[group.Owner] = updatedExpanded;
            }

            if (updatedExpanded)
            {
                foreach (var item in group.Items)
                {
                    var index = IndexOfItem(items, item);
                    if (index >= 0)
                    {
                        DrawListItem(index, item, true);
                    }
                }

                return;
            }

            if (!containsSelectedItem)
            {
                return;
            }

            // Package Manager-like behavior: collapsing owner group can hide sibling models, but keep
            // selected child visible so details pane does not point at an off-screen entry.
            var selectedIndex = IndexOfItem(items, selectedItem);
            if (selectedIndex >= 0)
            {
                DrawListItem(selectedIndex, selectedItem, true);
            }
        }

        private void DrawDetailsPane(Rect rect, IReadOnlyList<HfHubModelHubItem> items)
        {
            GUILayout.BeginArea(rect);
            using (new EditorGUILayout.VerticalScope())
            {
                _detailsScrollPosition = EditorGUILayout.BeginScrollView(_detailsScrollPosition);

                if (items.Count == 0)
                {
                    EditorGUILayout.HelpBox(Content.EmptyStateMessage, MessageType.Info);
                    EditorGUILayout.EndScrollView();
                }
                else
                {
                    if (_selectedIndex < 0 || _selectedIndex >= items.Count)
                    {
                        _selectedIndex = 0;
                    }

                    DrawDetails(items[_selectedIndex]);
                    EditorGUILayout.EndScrollView();
                }
            }
            GUILayout.EndArea();
        }

        private PaneLayout GetPaneLayout(Rect contentRect)
        {
            var maxListPaneWidth = Mathf.Max(MinListPaneWidth, contentRect.width - MinDetailsPaneWidth - SplitterWidth);
            _listPaneWidth = Mathf.Clamp(_listPaneWidth, MinListPaneWidth, maxListPaneWidth);

            var listRect = new Rect(contentRect.x, contentRect.y, _listPaneWidth, contentRect.height);
            var splitterRect = new Rect(listRect.xMax, contentRect.y, SplitterWidth, contentRect.height);
            var detailsRect = new Rect(splitterRect.xMax, contentRect.y, contentRect.xMax - splitterRect.xMax, contentRect.height);
            return new PaneLayout(listRect, splitterRect, detailsRect);
        }

        private void HandleSplitter(Rect contentRect, Rect splitterRect)
        {
            var currentEvent = Event.current;
            EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeHorizontal);

            switch (currentEvent.type)
            {
                case EventType.MouseDown when currentEvent.button == 0 && splitterRect.Contains(currentEvent.mousePosition):
                    _isDraggingSplitter = true;
                    currentEvent.Use();
                    break;
                case EventType.MouseDrag when _isDraggingSplitter:
                    // Manual splitter gives Package Manager-style pane resizing without relying on
                    // GUILayout widths that are hard to drag interactively in IMGUI.
                    var maxListPaneWidth = Mathf.Max(MinListPaneWidth, contentRect.width - MinDetailsPaneWidth - SplitterWidth);
                    _listPaneWidth = Mathf.Clamp(currentEvent.mousePosition.x - contentRect.x - (SplitterWidth * 0.5f), MinListPaneWidth, maxListPaneWidth);
                    Repaint();
                    currentEvent.Use();
                    break;
                case EventType.MouseUp when _isDraggingSplitter:
                    _isDraggingSplitter = false;
                    currentEvent.Use();
                    break;
            }
        }

        private static void DrawSplitter(Rect splitterRect)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            var lineRect = new Rect(splitterRect.x + ((splitterRect.width - 1f) * 0.5f), splitterRect.y, 1f, splitterRect.height);
            var color = EditorGUIUtility.isProSkin
                ? new Color(0.20f, 0.20f, 0.20f, 1f)
                : new Color(0.72f, 0.72f, 0.72f, 1f);
            EditorGUI.DrawRect(lineRect, color);
        }

        private static void DrawPaneBackgrounds(PaneLayout paneLayout)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            // Package Manager uses stronger pane contrast than default IMGUI layout. Fill backgrounds
            // explicitly so sidebar reads as separate navigation surface instead of one flat editor area.
            var listColor = EditorGUIUtility.isProSkin
                ? new Color(0.17f, 0.17f, 0.17f, 1f)
                : new Color(0.82f, 0.82f, 0.82f, 1f);
            var detailsColor = EditorGUIUtility.isProSkin
                ? new Color(0.22f, 0.22f, 0.22f, 1f)
                : new Color(0.76f, 0.76f, 0.76f, 1f);

            EditorGUI.DrawRect(paneLayout.ListRect, listColor);
            EditorGUI.DrawRect(paneLayout.DetailsRect, detailsColor);
        }

        private void DrawDetails(HfHubModelHubItem item)
        {
            var headerRect = GUILayoutUtility.GetRect(0f, DetailsHeaderHeight, GUILayout.ExpandWidth(true));
            var titleRect = new Rect(
                headerRect.x + DetailsContentPadding,
                headerRect.y + 2f,
                headerRect.width - StatusBadgeWidth - ListItemPadding - DetailsContentPadding,
                headerRect.height - 4f);
            var badgeRect = new Rect(headerRect.xMax - StatusBadgeWidth, headerRect.y + 8f, StatusBadgeWidth, 18f);
            GUI.Label(titleRect, GetRepositoryDisplayName(item.Repository), _detailsHeaderTitleStyle);
            DrawStatusBadge(item, badgeRect);

            EditorGUILayout.Space(10f);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(_locateContent, GUILayout.Width(90f)))
                {
                    LocateManifest(item);
                }

                if (item.State == HfHubModelHubItemState.Ready)
                {
                    var manageRect = GUILayoutUtility.GetRect(_manageContent, _manageButtonStyle, GUILayout.Width(ManageButtonWidth));
                    if (GUI.Button(manageRect, _manageContent, _manageButtonStyle))
                    {
                        ShowManageMenu(item, manageRect);
                    }

                    var arrowRect = new Rect(manageRect.xMax - 14f, manageRect.y + 1f, 10f, manageRect.height - 2f);
                    GUI.Label(arrowRect, "▾", _manageDropdownArrowStyle);
                }
                else
                {
                    var isSyncRunning = HfHubBackgroundSyncManager.IsRunning;
                    var actionContent = GetImportActionContent(item, isSyncRunning);
                    using (new EditorGUI.DisabledScope(isSyncRunning))
                    {
                        if (GUILayout.Button(actionContent, GUILayout.Width(80f)))
                        {
                            HfHubEditorOperations.StartBackgroundSync(item);
                            Refresh();
                        }
                    }
                }
            }

            EditorGUILayout.Space();
            DrawDetailsTabs(item);
        }

        private void DrawDetailsTabs(HfHubModelHubItem item)
        {
            var availableTabs = GetAvailableTabs(item);
            if (!availableTabs.Contains(_selectedDetailTab))
            {
                _selectedDetailTab = availableTabs[0];
            }

            DrawDetailsTabStrip(availableTabs, item);

            var topPaddingRect = GUILayoutUtility.GetRect(0f, DetailsContentPadding + 6f, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                var contentBackgroundRect = new Rect(
                    0f,
                    topPaddingRect.y,
                    Mathf.Max(0f, position.width),
                    Mathf.Max(0f, position.height - topPaddingRect.y));
                EditorGUI.DrawRect(contentBackgroundRect, GetDetailsContentBackgroundColor());
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(DetailsContentLeftPadding);
                using (new EditorGUILayout.VerticalScope())
                {
                    switch (_selectedDetailTab)
                    {
                        case DetailTab.Summary:
                            DrawSummarySection(item);
                            break;
                        case DetailTab.Dependents:
                            DrawDependentsSection(item);
                            break;
                        case DetailTab.Files:
                            DrawFilesSection(item);
                            break;
                        case DetailTab.Diagnostics:
                            DrawDiagnosticsSection(item);
                            break;
                        case DetailTab.DiscoveryIssues:
                            DrawDiscoveryIssuesSection();
                            break;
                    }
                }
            }
        }

        private void DrawDetailsTabStrip(IReadOnlyList<DetailTab> availableTabs, HfHubModelHubItem item)
        {
            var tabAreaRect = GUILayoutUtility.GetRect(0f, DetailsTabHeight, GUILayout.ExpandWidth(true));
            var tabX = tabAreaRect.x;

            for (var index = 0; index < availableTabs.Count; index++)
            {
                var tab = availableTabs[index];
                var tabRect = new Rect(tabX, tabAreaRect.y, DetailsTabWidth, tabAreaRect.height);
                var isSelected = tab == _selectedDetailTab;
                var isEnabled = IsDetailTabEnabled(tab, item);
                var tooltip = GetDetailTabTooltip(tab, item);
                // Custom tab drawing keeps subtle text + underline styling. Stock IMGUI toolbar buttons
                // hover correctly, but their boxed visuals do not match Package Manager's detail tabs.
                if (GUI.Button(tabRect, new GUIContent(string.Empty, tooltip), GUIStyle.none) && isEnabled)
                {
                    _selectedDetailTab = tab;
                }

                var labelRect = new Rect(tabRect.x, tabRect.y, tabRect.width, tabRect.height - DetailsTabUnderlineHeight);
                var labelStyle = new GUIStyle(_detailsTabLabelStyle);
                labelStyle.normal.textColor = isEnabled ? GetDetailTabActiveTextColor() : GetDetailTabDisabledTextColor();
                labelStyle.hover.textColor = labelStyle.normal.textColor;
                labelStyle.active.textColor = labelStyle.normal.textColor;
                labelStyle.focused.textColor = labelStyle.normal.textColor;
                GUI.Label(labelRect, new GUIContent(GetDetailTabLabel(tab), tooltip), labelStyle);

                if (isSelected && isEnabled && Event.current.type == EventType.Repaint)
                {
                    var underlineRect = new Rect(
                        tabRect.x + 10f,
                        tabRect.yMax - DetailsTabUnderlineHeight,
                        Mathf.Max(0f, tabRect.width - 20f),
                        DetailsTabUnderlineHeight);
                    EditorGUI.DrawRect(underlineRect, GetDetailTabUnderlineColor());
                }

                tabX += DetailsTabWidth;
            }
        }

        private void DrawSummarySection(HfHubModelHubItem item)
        {
            DrawRepositoryLinkRow(item);
            EditorGUILayout.LabelField(Content.RequestedRevisionLabel, item.RequestedRevision);
            EditorGUILayout.LabelField(Content.ResolvedCommitLabel, string.IsNullOrWhiteSpace(item.ResolvedCommitHash) ? Content.UnresolvedCommitPlaceholder : item.ResolvedCommitHash);
            EditorGUILayout.LabelField(Content.ManifestOwnersLabel, item.ManifestOwnerCount.ToString());
            EditorGUILayout.LabelField(Content.GeneratedSentisFilesLabel, item.ImportedFileCount.ToString());
            var cacheSizeOnDisk = FormatByteSize(item.CacheSizeOnDiskBytes);
            var estimatedCacheSize = FormatByteSize(item.EstimatedCacheSizeBytes);
            var runtimeSizeOnDisk = FormatByteSize(item.RuntimeSizeOnDiskBytes);
            var estimatedRuntimeSize = FormatByteSize(item.EstimatedRuntimeSizeBytes);
            DrawOptionalValueRow(Content.CacheSizeOnDiskLabel, cacheSizeOnDisk);
            if (cacheSizeOnDisk == null || !string.Equals(cacheSizeOnDisk, estimatedCacheSize, StringComparison.Ordinal))
            {
                DrawOptionalValueRow(Content.EstimatedCacheDownloadSizeLabel, estimatedCacheSize);
            }
            DrawOptionalLinkRow(Content.RawSnapshotLabel, item.RawSnapshotRootPath, HfHubProjectSettingsLinks.RevealPath);
            DrawOptionalValueRow(Content.RuntimeAssetsSizeOnDiskLabel, runtimeSizeOnDisk);
            if (runtimeSizeOnDisk == null || !string.Equals(runtimeSizeOnDisk, estimatedRuntimeSize, StringComparison.Ordinal))
            {
                DrawOptionalValueRow(Content.EstimatedRuntimeAssetsSizeLabel, estimatedRuntimeSize);
            }
            DrawOptionalLinkRow(Content.RuntimeFolderLabel, item.RuntimeSnapshotAssetPath, HfHubProjectSettingsLinks.RevealPath);

            EditorGUILayout.Space();
        }

        private void DrawRepositoryLinkRow(HfHubModelHubItem item)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(Content.RepositoryLabel);
                if (GUILayout.Button(new GUIContent(item.Repository, item.Repository), _pathLinkStyle))
                {
                    HfHubProjectSettingsLinks.OpenSourceRepository(item.Dependents[0].Dependency);
                }
            }
        }

        private static void DrawOptionalValueRow(string label, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            EditorGUILayout.LabelField(label, value);
        }

        private void DrawDependentsSection(HfHubModelHubItem item)
        {
            foreach (var dependent in item.Dependents)
            {
                EditorGUILayout.LabelField(
                    $"{dependent.Dependency.PackageDisplayName} / {dependent.Dependency.ModelId}",
                    EditorStyles.boldLabel);
                EditorGUILayout.LabelField(Content.OwnerLabel, $"{dependent.Dependency.PackageId} {dependent.Dependency.PackageVersion}");
                EditorGUILayout.LabelField(Content.ModelIdLabel, dependent.Dependency.ModelId);
                DrawOptionalLinkRow(Content.ManifestLabel, dependent.Dependency.ManifestPath, HfHubProjectSettingsLinks.RevealPath);
                DrawOptionalLinkRow(Content.RuntimeRegistryLabel, dependent.ImportedMetadata?.RuntimeRegistryAssetPath, HfHubProjectSettingsLinks.PingAsset);
                EditorGUILayout.Space(8f);
            }

            EditorGUILayout.Space();
        }

        private void DrawFilesSection(HfHubModelHubItem item)
        {
            if (item.RuntimeFiles.Count == 0)
            {
                EditorGUILayout.HelpBox(Content.NoRuntimeFilesMessage, MessageType.Info);
                EditorGUILayout.Space();
                return;
            }

            foreach (var runtimeFile in item.RuntimeFiles)
            {
                EditorGUILayout.LabelField(runtimeFile.RepoPath, EditorStyles.boldLabel);
                DrawOptionalLinkRow(Content.RuntimeAssetLabel, runtimeFile.AssetPath, HfHubProjectSettingsLinks.PingAsset);
                EditorGUILayout.LabelField(Content.RuntimeRelativeLabel, runtimeFile.RelativePath);
                EditorGUILayout.Space(8f);
            }

            EditorGUILayout.Space();
        }

        private void DrawDiagnosticsSection(HfHubModelHubItem item)
        {
            if (item.Diagnostics.Count == 0)
            {
                EditorGUILayout.HelpBox(Content.NoDiagnosticsMessage, MessageType.None);
                EditorGUILayout.Space();
                return;
            }

            foreach (var diagnostic in item.Diagnostics)
            {
                EditorGUILayout.HelpBox(diagnostic, item.State == HfHubModelHubItemState.Error ? MessageType.Error : MessageType.Info);
            }

            EditorGUILayout.Space();
        }

        private void DrawDiscoveryIssuesSection()
        {
            if (_report.DiscoveryIssues.Count == 0)
            {
                EditorGUILayout.HelpBox("No discovery issues for this project.", MessageType.None);
                return;
            }

            foreach (var issue in _report.DiscoveryIssues)
            {
                EditorGUILayout.HelpBox($"{issue.PackageId}: {issue.Message}", MessageType.Warning);
            }
        }

        private IReadOnlyList<HfHubModelHubItem> GetFilteredItems()
        {
            IEnumerable<HfHubModelHubItem> items = _report.Items;

            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                items = items.Where(item =>
                    item.Repository.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.Dependents.Any(dependent =>
                        dependent.Dependency.PackageDisplayName.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        dependent.Dependency.ModelId.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0));
            }
            return new List<HfHubModelHubItem>(items);
        }

        private IReadOnlyList<HfHubListGroup> BuildListGroups(IReadOnlyList<HfHubModelHubItem> items)
        {
            return items
                .GroupBy(item => GetRepositoryOwner(item.Repository), StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => new HfHubListGroup(
                    group.Key,
                    group.OrderBy(item => GetRepositoryDisplayName(item.Repository), StringComparer.OrdinalIgnoreCase).ToArray()))
                .ToArray();
        }

        private bool GetIsSectionExpanded(string owner)
        {
            if (_sectionExpandedStates.TryGetValue(owner, out var expanded))
            {
                return expanded;
            }

            _sectionExpandedStates[owner] = true;
            return true;
        }

        private static int IndexOfItem(IReadOnlyList<HfHubModelHubItem> items, HfHubModelHubItem target)
        {
            for (var index = 0; index < items.Count; index++)
            {
                if (ReferenceEquals(items[index], target))
                {
                    return index;
                }
            }

            return -1;
        }

        private static string GetRepositoryOwner(string repository)
        {
            if (string.IsNullOrWhiteSpace(repository))
            {
                return "<unknown>";
            }

            var separatorIndex = repository.IndexOf('/');
            return separatorIndex > 0 ? repository.Substring(0, separatorIndex) : repository;
        }

        private static string GetRepositoryDisplayName(string repository)
        {
            if (string.IsNullOrWhiteSpace(repository))
            {
                return "<unknown>";
            }

            var separatorIndex = repository.IndexOf('/');
            return separatorIndex >= 0 && separatorIndex < repository.Length - 1
                ? repository.Substring(separatorIndex + 1)
                : repository;
        }

        private DetailTab[] GetAvailableTabs(HfHubModelHubItem item)
        {
            var tabs = new List<DetailTab>
            {
                DetailTab.Summary,
                DetailTab.Dependents,
                DetailTab.Files
            };

            if (item != null && item.Diagnostics.Count > 0)
            {
                tabs.Add(DetailTab.Diagnostics);
            }

            if (_report != null && _report.DiscoveryIssues.Count > 0)
            {
                tabs.Add(DetailTab.DiscoveryIssues);
            }

            return tabs.ToArray();
        }

        private static bool IsDetailTabEnabled(DetailTab tab, HfHubModelHubItem item)
        {
            return tab switch
            {
                DetailTab.Files => item != null && item.RuntimeFiles.Count > 0,
                _ => true
            };
        }

        private static string GetDetailTabLabel(DetailTab tab)
        {
            return tab switch
            {
                DetailTab.Summary => Content.SummarySection,
                DetailTab.Dependents => Content.DependentsSection,
                DetailTab.Files => Content.FilesSection,
                DetailTab.Diagnostics => Content.DiagnosticsSection,
                DetailTab.DiscoveryIssues => Content.DiscoveryIssuesSection,
                _ => tab.ToString()
            };
        }

        private static string GetDetailTabTooltip(DetailTab tab, HfHubModelHubItem item)
        {
            return tab switch
            {
                DetailTab.Files when item == null || item.RuntimeFiles.Count == 0 =>
                    "Files appear here after this repository has been downloaded and imported.",
                DetailTab.Files =>
                    "Shows generated runtime files for this repository.",
                _ => null
            };
        }

        private void Refresh()
        {
            _report = HfHubEditorOperations.CreateModelHubReport();
            _lastRefreshLocalTime = DateTime.Now;
            Repaint();
        }

        private void HandleSyncStateChanged()
        {
            Refresh();
        }

        private string GetLastRefreshLabel()
        {
            if (_lastRefreshLocalTime == default)
            {
                return string.Empty;
            }

            return string.Format(Content.LastRefreshFormat, _lastRefreshLocalTime.ToString("MMM d HH:mm"));
        }

        private static string GetStatusBadgeLabel(HfHubModelHubItem item)
        {
            if (item.State == HfHubModelHubItemState.Ready)
            {
                return Content.ImportedState;
            }

            var hasCacheSnapshot = !string.IsNullOrWhiteSpace(item.RawSnapshotRootPath);
            var hasRuntimeFiles = item.RuntimeFiles.Count > 0;

            if (!hasCacheSnapshot)
            {
                return Content.DownloadNeededState;
            }

            if (item.State == HfHubModelHubItemState.Error)
            {
                return hasRuntimeFiles ? Content.RepairNeededState : Content.ImportNeededState;
            }

            return hasRuntimeFiles ? Content.UpdateNeededState : Content.ImportNeededState;
        }

        private void DrawStatusBadge(HfHubModelHubItem item, Rect rect)
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

        private void DrawStateIcon(HfHubModelHubItemState state, Rect rect)
        {
            InitializeGuiContent();

            if (state == HfHubModelHubItemState.Ready)
            {
                GUI.Label(rect, Content.ReadyStateGlyph, _readyStateGlyphStyle);
                return;
            }

            var icon = state switch
            {
                HfHubModelHubItemState.ActionRequired => _actionRequiredStateIconContent,
                _ => _errorStateIconContent
            };

            if (icon != null)
            {
                GUI.Label(rect, icon);
            }
        }

        private static Color GetStatusBadgeColor(HfHubModelHubItem item)
        {
            if (item.State == HfHubModelHubItemState.Ready)
            {
                return new Color(0.34f, 0.55f, 0.36f, 1f);
            }

            var hasCacheSnapshot = !string.IsNullOrWhiteSpace(item.RawSnapshotRootPath);
            return hasCacheSnapshot
                ? new Color(0.76f, 0.60f, 0.24f, 1f)
                : new Color(0.68f, 0.30f, 0.28f, 1f);
        }

        private static string GetStatusBadgeTooltip(HfHubModelHubItem item)
        {
            var hasCacheSnapshot = !string.IsNullOrWhiteSpace(item.RawSnapshotRootPath);

            if (item.State == HfHubModelHubItemState.Ready)
            {
                return hasCacheSnapshot
                    ? "This model is downloaded and imported into this project."
                    : "This model is imported into this project. Raw cache was removed from this machine.";
            }

            var hasRuntimeFiles = item.RuntimeFiles.Count > 0;

            if (!hasCacheSnapshot)
            {
                return "This model has not been downloaded yet.";
            }

            if (item.State == HfHubModelHubItemState.Error)
            {
                return hasRuntimeFiles
                    ? "This model is in the project, but something is wrong and it needs to be fixed."
                    : "This model is downloaded, but it could not be imported into the project.";
            }

            return hasRuntimeFiles
                ? "This model is downloaded and in the project, but it needs to be updated."
                : "This model is downloaded in cache, but not yet imported into the project.";
        }

        private GUIContent GetImportActionContent(HfHubModelHubItem item, bool isSyncRunning = false)
        {
            var hasCacheSnapshot = !string.IsNullOrWhiteSpace(item?.RawSnapshotRootPath);
            if (isSyncRunning)
            {
                return hasCacheSnapshot
                    ? new GUIContent(Content.ImportLabel, Content.SyncInProgressTooltip)
                    : new GUIContent(Content.DownloadLabel, Content.SyncInProgressTooltip);
            }

            return hasCacheSnapshot
                ? _importContent
                : new GUIContent(Content.DownloadLabel, Content.DownloadTooltip);
        }

        private void DrawOptionalLinkRow(string label, string value, Action<string> action)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                // Long absolute paths force the details pane to grow horizontally, which can clip the
                // header badge. Show compact display values here and keep the full path in the tooltip.
                var displayValue = GetPathDisplayValue(label, value);
                var buttonContent = new GUIContent(displayValue, value);
                EditorGUILayout.PrefixLabel(label);
                if (GUILayout.Button(buttonContent, _pathLinkStyle))
                {
                    action(value);
                }
            }
        }

        private static string GetPathDisplayValue(string label, string value)
        {
            if (string.Equals(label, Content.ManifestLabel, StringComparison.Ordinal))
            {
                return GetManifestDisplayValue(value);
            }

            // For cache/runtime links, filename is usually enough to identify the target while avoiding
            // duplicate noise with the surrounding section labels.
            var fileName = Path.GetFileName(value);
            return string.IsNullOrWhiteSpace(fileName) ? value : fileName;
        }

        private static string GetManifestDisplayValue(string value)
        {
            var normalizedValue = value.Replace('\\', '/');

            // Manifest provenance matters, but full absolute paths are noisy. Show the path relative to
            // the Unity project root entry point so users can tell whether it came from Assets or Packages.
            var assetsIndex = normalizedValue.IndexOf("Assets/", StringComparison.OrdinalIgnoreCase);
            if (assetsIndex >= 0)
            {
                return $"../{normalizedValue.Substring(assetsIndex)}";
            }

            var packagesIndex = normalizedValue.IndexOf("Packages/", StringComparison.OrdinalIgnoreCase);
            if (packagesIndex >= 0)
            {
                return $"../{normalizedValue.Substring(packagesIndex)}";
            }

            var fileName = Path.GetFileName(normalizedValue);
            return string.IsNullOrWhiteSpace(fileName) ? normalizedValue : $"../{fileName}";
        }

        private static string FormatByteSize(long? bytes)
        {
            if (!bytes.HasValue || bytes.Value < 0)
            {
                return null;
            }

            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes.Value;
            var unitIndex = 0;
            while (size >= 1024d && unitIndex < units.Length - 1)
            {
                size /= 1024d;
                unitIndex++;
            }

            return unitIndex == 0
                ? $"{size:0} {units[unitIndex]}"
                : $"{size:0.0} {units[unitIndex]}";
        }

        private void InitializeGuiContent()
        {
            _refreshIconContent ??= EditorGUIUtility.IconContent("Refresh");
            _refreshIconContent.tooltip = Content.RefreshTooltip;
            _importAllContent ??= new GUIContent(Content.ImportAllLabel, Content.ImportAllTooltip);
            _importContent ??= new GUIContent(Content.ImportLabel, Content.ImportTooltip);
            // Keep the trigger itself tooltip-free so popup-entry tooltips are not obscured by a parent hint.
            _manageContent ??= new GUIContent(Content.ManageLabel);
            _locateContent ??= new GUIContent(Content.LocateLabel, Content.LocateTooltip);
            _actionRequiredStateIconContent ??= EditorGUIUtility.IconContent("info");
            _errorStateIconContent ??= EditorGUIUtility.IconContent("console.warnicon");
        }

        private static void LocateManifest(HfHubModelHubItem item)
        {
            var targets = HfHubProjectSettingsLinks.BuildManifestLocateTargets(item);
            if (targets.Count == 0)
            {
                return;
            }

            if (targets.Count == 1)
            {
                HfHubProjectSettingsLinks.LocateManifest(targets[0].ManifestPath);
                return;
            }

            var menu = new GenericMenu();
            foreach (var target in targets)
            {
                menu.AddItem(
                    new GUIContent(target.Label),
                    false,
                    () => HfHubProjectSettingsLinks.LocateManifest(target.ManifestPath));
            }

            menu.ShowAsContext();
        }

        private void ShowManageMenu(HfHubModelHubItem item, Rect buttonRect)
        {
            if (item == null)
            {
                return;
            }

            var hasCache = HasCacheForCurrentItem(item);
            var entries = HfHubManageMenuPopup.BuildEntries(
                hasCache,
                () =>
                {
                    HfHubEditorOperations.RemoveImportedArtifacts(item);
                    Refresh();
                },
                () =>
                {
                    HfHubEditorOperations.RemoveRawCache(item);
                    Refresh();
                },
                Content.RemoveLabel,
                Content.RemoveTooltip,
                Content.RemoveFromCacheLabel,
                hasCache ? Content.RemoveFromCacheTooltip : Content.RemoveFromCacheDisabledTooltip);

            PopupWindow.Show(
                buttonRect,
                new HfHubManageMenuPopup(entries));
        }

        private static bool HasCacheForCurrentItem(HfHubModelHubItem item)
        {
            if (item == null)
            {
                return false;
            }

            var revisionRefPath = HfHubCachePaths.GetRevisionRefPath(item.Repository, item.RequestedRevision);
            if (File.Exists(revisionRefPath))
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(item.RawSnapshotRootPath) &&
                Directory.Exists(item.RawSnapshotRootPath);
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
            _searchFieldStyle ??= new GUIStyle(GUI.skin.FindStyle(Content.SearchStyleName) ?? EditorStyles.toolbarSearchField)
            {
                fixedHeight = SearchFieldHeight,
                stretchHeight = false,
                alignment = TextAnchor.MiddleLeft
            };
        }

        private readonly struct PaneLayout
        {
            public PaneLayout(Rect listRect, Rect splitterRect, Rect detailsRect)
            {
                ListRect = listRect;
                SplitterRect = splitterRect;
                DetailsRect = detailsRect;
            }

            public Rect ListRect { get; }

            public Rect SplitterRect { get; }

            public Rect DetailsRect { get; }
        }

        private readonly struct HfHubListGroup
        {
            public HfHubListGroup(string owner, IReadOnlyList<HfHubModelHubItem> items)
            {
                Owner = owner;
                Items = items;
            }

            public string Owner { get; }

            public IReadOnlyList<HfHubModelHubItem> Items { get; }
        }
    }
}
