using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Renders right-hand details pane for selected repository row.
    /// </summary>
    internal sealed partial class ModelHubWindow
    {
        /// <summary>
        /// Draws details pane and reserves bottom-bar space only when file editing actions are relevant.
        /// </summary>
        private void DrawDetailsPane(Rect rect, IReadOnlyList<ModelHubItem> items)
        {
            GUILayout.BeginArea(rect);
            using (new EditorGUILayout.VerticalScope())
            {
                if (items.Count == 0)
                {
                    _detailsScrollPosition = EditorGUILayout.BeginScrollView(_detailsScrollPosition);
                    EditorGUILayout.HelpBox(Content.EmptyStateMessage, MessageType.Info);
                    EditorGUILayout.EndScrollView();
                }
                else
                {
                    if (_selectedIndex < 0 || _selectedIndex >= items.Count)
                    {
                        _selectedIndex = 0;
                    }

                    var selectedItem = items[_selectedIndex];
                    var showFilesBottomBar = _selectedDetailTab == DetailTab.Files && selectedItem.Files.Any(file => file.SupportsImportRecipeEditing);
                    if (showFilesBottomBar)
                    {
                        _detailsScrollPosition = EditorGUILayout.BeginScrollView(_detailsScrollPosition, GUILayout.ExpandHeight(true));
                        DrawDetails(selectedItem);
                        EditorGUILayout.EndScrollView();
                        DrawFilesBottomBar(selectedItem);
                    }
                    else
                    {
                        _detailsScrollPosition = EditorGUILayout.BeginScrollView(_detailsScrollPosition);
                        DrawDetails(items[_selectedIndex]);
                        EditorGUILayout.EndScrollView();
                    }
                }
            }
            GUILayout.EndArea();
        }

        /// <summary>
        /// Draws selected repository header, status badge, and primary action row.
        /// </summary>
        private void DrawDetails(ModelHubItem item)
        {
            var statusBadgeWidth = GetStatusBadgeWidth(item);
            var headerRect = GUILayoutUtility.GetRect(0f, DetailsHeaderHeight, GUILayout.ExpandWidth(true));
            var titleRect = new Rect(
                headerRect.x + DetailsContentPadding,
                headerRect.y + 2f,
                headerRect.width - statusBadgeWidth - ListItemPadding - DetailsContentPadding,
                headerRect.height - 4f);
            var badgeRect = new Rect(headerRect.xMax - statusBadgeWidth, headerRect.y + 8f, statusBadgeWidth, 18f);
            GUI.Label(titleRect, GetRepositoryDisplayName(item.Repository), _detailsHeaderTitleStyle);
            DrawStatusBadge(item, badgeRect);

            EditorGUILayout.Space(10f);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(_locateContent, GUILayout.Width(76f)))
                {
                    LocateManifest(item);
                }

                if (item.State == ModelHubItemState.Ready)
                {
                    var manageRect = GUILayoutUtility.GetRect(_manageContent, _manageButtonStyle, GUILayout.Width(ManageButtonWidth));
                    if (GUI.Button(manageRect, _manageContent, _manageButtonStyle))
                    {
                        ShowManageMenu(item, manageRect);
                    }

                    var arrowRect = new Rect(manageRect.xMax - 14f, manageRect.y + 1f, 10f, manageRect.height - 2f);
                    GUI.Label(arrowRect, "\u25BE", _manageDropdownArrowStyle);
                }
                else
                {
                    var isSyncRunning = BackgroundSyncManager.IsRunning;
                    var actionContent = GetImportActionContent(item, isSyncRunning);
                    using (new EditorGUI.DisabledScope(isSyncRunning || item.HasRevisionConflict))
                    {
                        if (GUILayout.Button(actionContent, GUILayout.Width(80f)))
                        {
                            EditorOperations.StartBackgroundSync(item);
                            Refresh();
                        }
                    }
                }
            }

            EditorGUILayout.Space();
            DrawDetailsTabs(item);
        }

        /// <summary>
        /// Chooses active details section and paints shared content background below tab strip.
        /// </summary>
        private void DrawDetailsTabs(ModelHubItem item)
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
                var viewportBackgroundRect = new Rect(
                    0f,
                    topPaddingRect.y,
                    Mathf.Max(0f, position.width),
                    Mathf.Max(0f, position.height - topPaddingRect.y));
                EditorGUI.DrawRect(viewportBackgroundRect, GetDetailsContentBackgroundColor());
            }

            var contentContainerRect = EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                var overflowBackgroundRect = new Rect(
                    0f,
                    contentContainerRect.y,
                    Mathf.Max(0f, position.width),
                    contentContainerRect.height);
                EditorGUI.DrawRect(overflowBackgroundRect, GetDetailsContentBackgroundColor());
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
                        case DetailTab.Storage:
                            DrawStorageSection(item);
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

            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// Draws custom tabs so IMGUI surface stays visually aligned with Unity Package Manager.
        /// </summary>
        private void DrawDetailsTabStrip(IReadOnlyList<DetailTab> availableTabs, ModelHubItem item)
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
                // Custom tab drawing keeps subtle text + underline styling. Standard toolbar tabs look
                // heavier than surrounding Package Manager-inspired UI.
                if (Event.current.type == EventType.Repaint)
                {
                    var textColor = isSelected
                        ? GetDetailTabActiveTextColor()
                        : isEnabled
                            ? GUI.skin.label.normal.textColor
                            : GetDetailTabDisabledTextColor();
                    var previousColor = GUI.color;
                    GUI.color = textColor;
                    GUI.Label(tabRect, new GUIContent(GetDetailTabLabel(tab), tooltip), _detailsTabLabelStyle);
                    GUI.color = previousColor;

                    if (isSelected)
                    {
                        var underlineRect = new Rect(
                            tabRect.x + 10f,
                            tabRect.yMax - DetailsTabUnderlineHeight,
                            Mathf.Max(0f, tabRect.width - 20f),
                            DetailsTabUnderlineHeight);
                        EditorGUI.DrawRect(underlineRect, GetDetailTabUnderlineColor());
                    }
                }

                if (isEnabled && GUI.Button(tabRect, GUIContent.none, GUIStyle.none))
                {
                    TryChangeDetailTab(item, tab);
                    GUI.FocusControl(null);
                }

                tabX += DetailsTabWidth;
            }
        }

        /// <summary>
        /// Shows repository-level summary facts aggregated from cache, import, and manifest discovery.
        /// </summary>
        private void DrawSummarySection(ModelHubItem item)
        {
            EnsureCacheSizeEstimateLookup(item);
            EditorGUILayout.LabelField(Content.RepositoryLabel, item.Repository);
            DrawOptionalValueRow(
                Content.RequestedRevisionLabel,
                item.RequestedRevision,
                null,
                string.Format(
                    Content.RequestedRevisionTooltipFormat,
                    string.IsNullOrWhiteSpace(item.ResolvedCommitHash)
                        ? Content.UnresolvedCommitPlaceholder
                        : item.ResolvedCommitHash));
            DrawOptionalValueRow(Content.ManifestOwnersLabel, item.ManifestOwnerCount.ToString());
            if (item.RepositoryFileCount > 0)
            {
                DrawOptionalValueRow(Content.SelectedFilesLabel, $"{item.SelectedFileCount}/{item.RepositoryFileCount}");
                if (item.RepositoryFileCount > item.SelectedFileCount)
                {
                    DrawOptionalValueRow(Content.RepositoryFilesLabel, item.RepositoryFileCount.ToString());
                }
            }
            DrawOptionalValueRow(Content.GeneratedSentisFilesLabel, item.ImportedFileCount.ToString());
            DrawSourceSizeRow(item);
            if (item.RepositorySizeBytes.HasValue && item.RepositorySizeBytes != item.SourceSizeBytes)
            {
                DrawOptionalValueRow(Content.RepositorySizeLabel, FormatByteSize(item.RepositorySizeBytes));
            }
            DrawRepositoryLinkRow(item);

            if (item.HasRevisionConflict)
            {
                DrawRevisionConflictResolutionSection(item);
            }

            EditorGUILayout.Space();
        }

        private void DrawSourceSizeRow(ModelHubItem item)
        {
            var sourceSize = FormatByteSize(item?.SourceSizeBytes);
            if (string.IsNullOrWhiteSpace(sourceSize))
            {
                return;
            }

            var rowRect = EditorGUILayout.GetControlRect();
            var labelRect = EditorGUI.PrefixLabel(rowRect, new GUIContent(Content.SourceSizeLabel, Content.SourceSizeTooltip));
            var sizeContent = new GUIContent(sourceSize);
            var sizeWidth = EditorStyles.label.CalcSize(sizeContent).x;
            var sizeRect = new Rect(labelRect.x, labelRect.y, sizeWidth, labelRect.height);
            GUI.Label(sizeRect, sizeContent);
            if (HasStorageDetails(item))
            {
                var buttonRect = new Rect(sizeRect.xMax + 4f, labelRect.y, 22f, labelRect.height);
                if (GUI.Button(buttonRect, _jumpToStorageContent))
                {
                    _selectedDetailTab = DetailTab.Storage;
                    GUI.FocusControl(null);
                }
            }
        }

        private static bool HasStorageDetails(ModelHubItem item)
        {
            return item?.StorageTreemapTiles?.Count > 0;
        }

        /// <summary>
        /// Shows local storage footprint so users can distinguish remote source size from current disk usage.
        /// </summary>
        private void DrawStorageSection(ModelHubItem item)
        {
            EnsureCacheSizeEstimateLookup(item);
            DrawStorageTreemap(item);
            EditorGUILayout.Space();
        }

        private void DrawStorageTreemap(ModelHubItem item)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField(new GUIContent(Content.StorageTreemapLabel, Content.StorageTreemapTooltip), EditorStyles.boldLabel);
            if (item?.StorageTreemapTiles == null || item.StorageTreemapTiles.Count == 0)
            {
                EditorGUILayout.HelpBox(Content.StorageTreemapEmptyMessage, MessageType.Info);
                return;
            }

            var width = Mathf.Max(0f, EditorGUIUtility.currentViewWidth - 56f);
            var height = width > 0f
                ? width / StorageTreemapAspectRatio
                : 240f;
            var rect = GUILayoutUtility.GetRect(0f, height, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.MouseMove && rect.Contains(Event.current.mousePosition))
            {
                // Hover outlines/tints animate over time, so ask IMGUI for another pass while pointer is inside treemap.
                Repaint();
            }

            if (Event.current.type == EventType.MouseLeaveWindow && !string.IsNullOrWhiteSpace(_hoveredSplitTreemapRepoPath))
            {
                UpdateSplitTreemapHoverState(null);
            }

            if (Event.current.type == EventType.Repaint || Event.current.type == EventType.ContextClick)
            {
                switch (_storageTreemapMode)
                {
                    case StorageTreemapMode.SplitByStorage:
                    DrawSplitStorageTreemap(rect, item.Files);
                    break;
                default:
                    UpdateSplitTreemapHoverState(null);
                    DrawStorageTreemapTiles(rect, item.StorageTreemapTiles);
                    break;
            }
            }

            DrawStorageTreemapModeFooter();
        }

        private void DrawStorageTreemapModeFooter()
        {
            EditorGUILayout.Space(2f);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                DrawStorageTreemapModeSegmentedControl();
                GUILayout.Space(9f);
            }
        }

        private void DrawStorageTreemapModeSegmentedControl()
        {
            var controlRect = GUILayoutUtility.GetRect(160f, 22f, GUILayout.Width(160f), GUILayout.Height(22f));
            var combinedRect = new Rect(controlRect.x, controlRect.y, 90f, controlRect.height);
            var splitRect = new Rect(combinedRect.xMax, controlRect.y, 70f, controlRect.height);
            var selectedMode = _storageTreemapMode;

            DrawTreemapModeSegment(
                combinedRect,
                StorageTreemapMode.Combined,
                new GUIContent(Content.StorageTreemapCombinedModeLabel, Content.StorageTreemapCombinedModeTooltip),
                selectedMode);
            DrawTreemapModeSegment(
                splitRect,
                StorageTreemapMode.SplitByStorage,
                new GUIContent(Content.StorageTreemapSplitModeLabel, Content.StorageTreemapSplitModeTooltip),
                selectedMode);

            Handles.DrawSolidRectangleWithOutline(controlRect, Color.clear, GetTreemapModeBorderColor());
            EditorGUI.DrawRect(new Rect(combinedRect.xMax, controlRect.y + 1f, 1f, controlRect.height - 2f), GetTreemapModeBorderColor());
        }

        private void DrawTreemapModeSegment(
            Rect rect,
            StorageTreemapMode mode,
            GUIContent content,
            StorageTreemapMode selectedMode)
        {
            var isSelected = selectedMode == mode;
            EditorGUI.DrawRect(rect, isSelected ? GetTreemapModeSelectedTint() : GetTreemapModeUnselectedTint());

            if (Event.current.type == EventType.Repaint)
            {
                GUI.Label(rect, content, _treemapModeSegmentStyle);
            }

            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                _storageTreemapMode = mode;
                Event.current.Use();
                Repaint();
            }
        }

        private void DrawStorageTreemapTiles(Rect rect, IReadOnlyList<ModelHubStorageTreemapTile> tiles)
        {
            var totalBytes = tiles.Sum(tile => tile.TotalSizeOnDiskBytes);
            if (totalBytes <= 0)
            {
                return;
            }

            EditorGUI.DrawRect(rect, GetTreemapBackgroundColor());
            var paddedRect = GetTreemapContentRect(rect);
            const float labelHeight = 20f;
            var sectionLabelRect = new Rect(paddedRect.x + 6f, paddedRect.y + 4f, paddedRect.width - 12f, labelHeight);
            var cacheTotalBytes = tiles.Sum(tile => tile.RawSizeOnDiskBytes);
            var runtimeTotalBytes = tiles.Sum(tile => tile.RuntimeSizeOnDiskBytes);
            GUI.Label(
                sectionLabelRect,
                new GUIContent(
                    BuildTreemapSectionLabel(Content.StorageTreemapCombinedSectionLabel, totalBytes),
                    BuildCombinedTreemapSectionTooltip(cacheTotalBytes, runtimeTotalBytes)),
                EditorStyles.miniBoldLabel);
            var contentRect = new Rect(paddedRect.x, paddedRect.y + labelHeight + 4f, paddedRect.width, paddedRect.height - labelHeight - 4f);
            if (contentRect.width <= 0f || contentRect.height <= 0f)
            {
                return;
            }

            string ignoredHoveredRepoPath = null;
            DrawStorageTreemapGroup(contentRect, tiles, totalBytes, splitHorizontally: contentRect.width >= contentRect.height, fillColorOverride: null, hoverTileRects: null, hoveredRepoPath: ref ignoredHoveredRepoPath);
        }

        private void DrawSplitStorageTreemap(Rect rect, IReadOnlyList<ModelHubFileItem> files)
        {
            EditorGUI.DrawRect(rect, GetTreemapBackgroundColor());
            var paddedRect = GetTreemapContentRect(rect);
            var sectionGap = 8f;
            var sectionWidth = Mathf.Max(0f, (paddedRect.width - sectionGap) * 0.5f);
            var leftRect = new Rect(paddedRect.x, paddedRect.y, sectionWidth, paddedRect.height);
            var rightRect = new Rect(paddedRect.x + sectionWidth + sectionGap, paddedRect.y, sectionWidth, paddedRect.height);
            var hoverTileRects = new Dictionary<string, List<Rect>>(StringComparer.Ordinal);
            var hoveredRepoPath = _hoveredSplitTreemapRepoPath;

            var cacheTiles = (files ?? Array.Empty<ModelHubFileItem>())
                .Where(file => file.IsDependencySelected && file.RawSizeOnDiskBytes.GetValueOrDefault() > 0)
                .OrderByDescending(file => file.RawSizeOnDiskBytes.GetValueOrDefault())
                .ThenBy(file => file.RepoPath, StringComparer.Ordinal)
                .Select(file => new ModelHubStorageTreemapTile(
                    file.RepoPath,
                    file.RawSizeOnDiskBytes.GetValueOrDefault(),
                    0,
                    displayLabel: file.RepoPath,
                    revealPath: file.RawCachedFilePath,
                    rawDisplayPath: file.RawCachedFilePath))
                .ToArray();
            var runtimeTiles = (files ?? Array.Empty<ModelHubFileItem>())
                .Where(file => file.IsDependencySelected && file.RuntimeSizeOnDiskBytes.GetValueOrDefault() > 0)
                .OrderByDescending(file => file.RuntimeSizeOnDiskBytes.GetValueOrDefault())
                .ThenBy(file => file.RepoPath, StringComparer.Ordinal)
                .Select(file => new ModelHubStorageTreemapTile(
                    file.RepoPath,
                    0,
                    file.RuntimeSizeOnDiskBytes.GetValueOrDefault(),
                    displayLabel: GetRuntimeTreemapLabel(file),
                    revealPath: file.RuntimeFile?.AssetPath,
                    runtimeDisplayPath: file.RuntimeFile?.RelativePath ?? file.RuntimeFile?.AssetPath,
                    rawDisplayPath: file.RepoPath))
                .ToArray();

            DrawStorageTreemapSection(leftRect, Content.StorageTreemapCacheSectionLabel, cacheTiles, GetTreemapRawColor(), hoverTileRects, ref hoveredRepoPath);
            DrawStorageTreemapSection(rightRect, Content.StorageTreemapRuntimeSectionLabel, runtimeTiles, GetTreemapRuntimeColor(), hoverTileRects, ref hoveredRepoPath);
            UpdateSplitTreemapHoverState(hoveredRepoPath);
            DrawSplitTreemapHoverOutlines(hoverTileRects);
        }

        private static Rect GetTreemapContentRect(Rect rect)
        {
            const float leftInset = 1f;
            const float topInset = 1f;
            const float rightInset = 7f;
            const float bottomInset = 1f;
            return new Rect(
                rect.x + leftInset,
                rect.y + topInset,
                Mathf.Max(0f, rect.width - leftInset - rightInset),
                Mathf.Max(0f, rect.height - topInset - bottomInset));
        }

        private void DrawStorageTreemapSection(
            Rect rect,
            string label,
            IReadOnlyList<ModelHubStorageTreemapTile> tiles,
            Color sectionColor,
            Dictionary<string, List<Rect>> hoverTileRects,
            ref string hoveredRepoPath)
        {
            Handles.DrawSolidRectangleWithOutline(rect, Color.clear, GetTreemapBorderColor());
            var labelHeight = 20f;
            var sectionLabelRect = new Rect(rect.x + 6f, rect.y + 4f, rect.width - 12f, labelHeight);
            GUI.Label(sectionLabelRect, BuildTreemapSectionLabel(label, tiles?.Sum(tile => tile.TotalSizeOnDiskBytes)), EditorStyles.miniBoldLabel);

            var contentRect = new Rect(rect.x + 1f, rect.y + labelHeight + 4f, rect.width - 2f, rect.height - labelHeight - 5f);
            if (contentRect.width <= 0f || contentRect.height <= 0f)
            {
                return;
            }

            if (tiles == null || tiles.Count == 0)
            {
                EditorGUI.DrawRect(contentRect, GetTreemapBackgroundColor());
                var emptyRect = new Rect(contentRect.x + 6f, contentRect.y + 6f, contentRect.width - 12f, contentRect.height - 12f);
                GUI.Label(emptyRect, Content.StorageTreemapSectionEmptyMessage, EditorStyles.centeredGreyMiniLabel);
                return;
            }

            EditorGUI.DrawRect(contentRect, GetTreemapBackgroundColor());
            var totalBytes = tiles.Sum(tile => tile.TotalSizeOnDiskBytes);
            DrawStorageTreemapGroup(contentRect, tiles, totalBytes, splitHorizontally: contentRect.width >= contentRect.height, fillColorOverride: sectionColor, hoverTileRects: hoverTileRects, hoveredRepoPath: ref hoveredRepoPath);
        }

        private void DrawStorageTreemapGroup(
            Rect rect,
            IReadOnlyList<ModelHubStorageTreemapTile> tiles,
            long totalBytes,
            bool splitHorizontally,
            Color? fillColorOverride,
            Dictionary<string, List<Rect>> hoverTileRects,
            ref string hoveredRepoPath)
        {
            if (tiles == null || tiles.Count == 0 || rect.width <= 2f || rect.height <= 2f || totalBytes <= 0)
            {
                return;
            }

            if (tiles.Count == 1)
            {
                // Stop recursing once one tile owns full rect; hover/context handling is attached at tile draw time.
                DrawStorageTreemapTile(rect, tiles[0], fillColorOverride, hoverTileRects, ref hoveredRepoPath);
                return;
            }

            var leftCount = FindTreemapSplitIndex(tiles, totalBytes);
            var leftBytes = SumTreemapBytes(tiles, 0, leftCount);
            var rightBytes = totalBytes - leftBytes;
            if (leftBytes <= 0 || rightBytes <= 0)
            {
                DrawStorageTreemapTile(rect, tiles[0], fillColorOverride, hoverTileRects, ref hoveredRepoPath);
                DrawStorageTreemapGroup(rect, tiles.Skip(1).ToArray(), totalBytes - tiles[0].TotalSizeOnDiskBytes, !splitHorizontally, fillColorOverride, hoverTileRects, ref hoveredRepoPath);
                return;
            }

            if (splitHorizontally)
            {
                var splitWidth = Mathf.Clamp(rect.width * leftBytes / (float)totalBytes, 24f, rect.width - 24f);
                // Snap recursive treemap boundaries to device pixels so sibling tiles do not show 1px seams under editor scaling.
                var xMin = SnapToDevicePixel(rect.x);
                var splitX = SnapToDevicePixel(rect.x + splitWidth);
                var xMax = SnapToDevicePixel(rect.xMax);
                var leftRect = new Rect(xMin, rect.y, Mathf.Max(0f, splitX - xMin), rect.height);
                var rightRect = new Rect(splitX, rect.y, Mathf.Max(0f, xMax - splitX), rect.height);
                DrawStorageTreemapGroup(leftRect, tiles.Take(leftCount).ToArray(), leftBytes, splitHorizontally: false, fillColorOverride, hoverTileRects, ref hoveredRepoPath);
                DrawStorageTreemapGroup(rightRect, tiles.Skip(leftCount).ToArray(), rightBytes, splitHorizontally: false, fillColorOverride, hoverTileRects, ref hoveredRepoPath);
                return;
            }

            var splitHeight = Mathf.Clamp(rect.height * leftBytes / (float)totalBytes, 24f, rect.height - 24f);
            // Snap recursive treemap boundaries to device pixels so sibling tiles do not show 1px seams under editor scaling.
            var yMin = SnapToDevicePixel(rect.y);
            var splitY = SnapToDevicePixel(rect.y + splitHeight);
            var yMax = SnapToDevicePixel(rect.yMax);
            var topRect = new Rect(rect.x, yMin, rect.width, Mathf.Max(0f, splitY - yMin));
            var bottomRect = new Rect(rect.x, splitY, rect.width, Mathf.Max(0f, yMax - splitY));
            DrawStorageTreemapGroup(topRect, tiles.Take(leftCount).ToArray(), leftBytes, splitHorizontally: true, fillColorOverride, hoverTileRects, ref hoveredRepoPath);
            DrawStorageTreemapGroup(bottomRect, tiles.Skip(leftCount).ToArray(), rightBytes, splitHorizontally: true, fillColorOverride, hoverTileRects, ref hoveredRepoPath);
        }

        private static int FindTreemapSplitIndex(IReadOnlyList<ModelHubStorageTreemapTile> tiles, long totalBytes)
        {
            if (tiles == null || tiles.Count <= 1 || totalBytes <= 0)
            {
                return 1;
            }

            long runningBytes = 0;
            var bestIndex = 1;
            var bestDelta = long.MaxValue;
            var targetBytes = totalBytes / 2f;
            for (var index = 0; index < tiles.Count - 1; index++)
            {
                runningBytes += tiles[index].TotalSizeOnDiskBytes;
                var delta = Math.Abs(runningBytes - targetBytes);
                if (delta < bestDelta)
                {
                    bestDelta = (long)delta;
                    bestIndex = index + 1;
                }
            }

            return bestIndex;
        }

        private static long SumTreemapBytes(IReadOnlyList<ModelHubStorageTreemapTile> tiles, int startIndex, int count)
        {
            long total = 0;
            var endIndex = Math.Min(tiles.Count, startIndex + count);
            for (var index = startIndex; index < endIndex; index++)
            {
                total += tiles[index].TotalSizeOnDiskBytes;
            }

            return total;
        }

        private void DrawStorageTreemapTile(
            Rect rect,
            ModelHubStorageTreemapTile tile,
            Color? fillColorOverride,
            Dictionary<string, List<Rect>> hoverTileRects,
            ref string hoveredRepoPath)
        {
            var drawRect = rect;
            var innerRect = drawRect;
            if (innerRect.width <= 0f || innerRect.height <= 0f)
            {
                return;
            }

            // Register hover and context menu before paint so `ContextClick` and later overlay passes can reuse same hit areas.
            RegisterSplitTreemapHover(rect, tile, hoverTileRects, ref hoveredRepoPath);
            HandleStorageTreemapTileContextMenu(rect, tile);
            var hoverAlpha = GetSplitTreemapTileHoverAlpha(tile?.RepoPath);

            if (fillColorOverride.HasValue)
            {
                EditorGUI.DrawRect(innerRect, ApplySplitTreemapHoverToTileColor(fillColorOverride.Value, hoverAlpha));
                Handles.DrawSolidRectangleWithOutline(drawRect, Color.clear, GetTreemapBorderColor());
                DrawStorageTreemapTileLabel(innerRect, tile);
                return;
            }

            var rawWidth = tile.TotalSizeOnDiskBytes <= 0
                ? 0f
                : innerRect.width * tile.RawSizeOnDiskBytes / (float)tile.TotalSizeOnDiskBytes;
            // Snap split geometry to device pixels so scaled editor UI does not show a 1px seam between cache/runtime fills.
            var xMin = SnapToDevicePixel(innerRect.x);
            var splitX = SnapToDevicePixel(innerRect.x + rawWidth);
            var xMax = SnapToDevicePixel(innerRect.xMax);
            if (tile.HasRawBytes)
            {
                var leftWidth = Mathf.Max(0f, splitX - xMin);
                if (leftWidth > 0f)
                {
                    EditorGUI.DrawRect(new Rect(xMin, innerRect.y, leftWidth, innerRect.height), ApplySplitTreemapHoverToTileColor(GetTreemapRawColor(), hoverAlpha));
                }
            }

            if (tile.HasRuntimeBytes)
            {
                var rightWidth = Mathf.Max(0f, xMax - splitX);
                if (rightWidth > 0f)
                {
                    EditorGUI.DrawRect(new Rect(splitX, innerRect.y, rightWidth, innerRect.height), ApplySplitTreemapHoverToTileColor(GetTreemapRuntimeColor(), hoverAlpha));
                }
            }

            Handles.DrawSolidRectangleWithOutline(drawRect, Color.clear, GetTreemapBorderColor());
            DrawStorageTreemapTileLabel(innerRect, tile);
        }

        private void DrawStorageTreemapTileLabel(Rect innerRect, ModelHubStorageTreemapTile tile)
        {
            var labelRect = new Rect(innerRect.x + 6f, innerRect.y + 6f, innerRect.width - 12f, innerRect.height - 12f);
            var isLabelTooSmallToRender = labelRect.width < 56f || labelRect.height < 28f;
            var label = $"{tile.DisplayLabel}\n{FormatByteSize(tile.TotalSizeOnDiskBytes)}";
            var tooltip = BuildStorageTreemapTooltip(tile, labelRect, label, isLabelTooSmallToRender);
            if (!string.IsNullOrWhiteSpace(tooltip))
            {
                // Attach tooltip to tile hit area even when label is too small to render.
                GUI.Label(innerRect, new GUIContent(string.Empty, tooltip), GUIStyle.none);
            }

            if (isLabelTooSmallToRender)
            {
                return;
            }

            GUI.Label(labelRect, new GUIContent(label, tooltip), _treemapTileLabelStyle);
        }

        private static string GetRuntimeTreemapLabel(ModelHubFileItem file)
        {
            if (string.IsNullOrWhiteSpace(file?.RepoPath))
            {
                return file?.RuntimeFile?.RelativePath ?? file?.RuntimeFile?.AssetPath;
            }

            var repoRelativeRuntimePath = Path.ChangeExtension(file.RepoPath, ".sentis");
            return string.IsNullOrWhiteSpace(repoRelativeRuntimePath)
                ? file.RepoPath
                : repoRelativeRuntimePath.Replace('\\', '/');
        }

        private static float SnapToDevicePixel(float value)
        {
            var pixelsPerPoint = Mathf.Max(1f, EditorGUIUtility.pixelsPerPoint);
            return Mathf.Round(value * pixelsPerPoint) / pixelsPerPoint;
        }

        private static void RegisterSplitTreemapHover(
            Rect rect,
            ModelHubStorageTreemapTile tile,
            Dictionary<string, List<Rect>> hoverTileRects,
            ref string hoveredRepoPath)
        {
            if (tile == null || hoverTileRects == null || string.IsNullOrWhiteSpace(tile.RepoPath))
            {
                return;
            }

            if (!hoverTileRects.TryGetValue(tile.RepoPath, out var rects))
            {
                rects = new List<Rect>();
                hoverTileRects[tile.RepoPath] = rects;
            }

            rects.Add(rect);
            if (rect.Contains(Event.current.mousePosition))
            {
                hoveredRepoPath = tile.RepoPath;
            }
        }

        private void HandleStorageTreemapTileContextMenu(Rect rect, ModelHubStorageTreemapTile tile)
        {
            if (Event.current.type != EventType.ContextClick || !rect.Contains(Event.current.mousePosition))
            {
                return;
            }

            // Use explicit reveal path stored on treemap tiles because display labels/tooltips may be repo-relative or shortened.
            var revealPath = GetTreemapRevealPath(tile);
            if (string.IsNullOrWhiteSpace(revealPath))
            {
                return;
            }

            var menu = new GenericMenu();
            menu.AddItem(new GUIContent(GetRevealInFileBrowserLabel()), false, () => ProjectSettingsLinks.RevealPath(revealPath));
            if (!string.IsNullOrWhiteSpace(tile.RevealPath) &&
                tile.RevealPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                menu.AddItem(new GUIContent(Content.ShowInProjectLabel), false, () => ProjectSettingsLinks.PingAsset(tile.RevealPath));
            }

            menu.AddItem(new GUIContent(Content.CopyPathLabel), false, () => EditorGUIUtility.systemCopyBuffer = revealPath);

            menu.ShowAsContext();
            Event.current.Use();
        }

        private static string GetTreemapRevealPath(ModelHubStorageTreemapTile tile)
        {
            return tile?.RevealPath;
        }

        private static string GetRevealInFileBrowserLabel()
        {
            return Application.platform switch
            {
                RuntimePlatform.OSXEditor => Content.RevealInFileBrowserLabelMac,
                RuntimePlatform.WindowsEditor => Content.RevealInFileBrowserLabelWindows,
                _ => Content.RevealInFileBrowserLabelDefault
            };
        }

        private static string BuildTreemapSectionLabel(string label, long? totalBytes)
        {
            var formattedSize = FormatByteSize(totalBytes);
            return string.IsNullOrWhiteSpace(formattedSize)
                ? label
                : $"{label} ({formattedSize})";
        }

        private static string BuildCombinedTreemapSectionTooltip(long cacheTotalBytes, long runtimeTotalBytes)
        {
            return $"Cache: {FormatByteSize(cacheTotalBytes)} + Runtime: {FormatByteSize(runtimeTotalBytes)}";
        }

        private void UpdateSplitTreemapHoverState(string hoveredRepoPath)
        {
            if (string.Equals(_hoveredSplitTreemapRepoPath, hoveredRepoPath, StringComparison.Ordinal))
            {
                return;
            }

            // Keep old repo id around briefly so paired hover can crossfade instead of snapping between file pairs.
            _previousHoveredSplitTreemapRepoPath = _hoveredSplitTreemapRepoPath;
            _hoveredSplitTreemapRepoPath = hoveredRepoPath;
            _hoveredSplitTreemapChangedAtTime = EditorApplication.timeSinceStartup;
            Repaint();
        }

        private void DrawSplitTreemapHoverOutlines(IReadOnlyDictionary<string, List<Rect>> hoverTileRects)
        {
            if (hoverTileRects == null)
            {
                return;
            }

            var transitionProgress = Mathf.Clamp01((float)((EditorApplication.timeSinceStartup - _hoveredSplitTreemapChangedAtTime) / StorageTreemapHoverTransitionDuration));
            var currentAlpha = string.IsNullOrWhiteSpace(_hoveredSplitTreemapRepoPath) ? 0f : transitionProgress;
            var previousAlpha = string.IsNullOrWhiteSpace(_previousHoveredSplitTreemapRepoPath) ? 0f : 1f - transitionProgress;
            var directlyHoveredRepoPath = GetDirectlyHoveredSplitTreemapRepoPath(hoverTileRects);

            DrawDirectHoverOutline(hoverTileRects, directlyHoveredRepoPath, currentAlpha);

            if (transitionProgress < 1f && (!string.IsNullOrWhiteSpace(_hoveredSplitTreemapRepoPath) || !string.IsNullOrWhiteSpace(_previousHoveredSplitTreemapRepoPath)))
            {
                Repaint();
            }
            else if (transitionProgress >= 1f && !string.IsNullOrWhiteSpace(_previousHoveredSplitTreemapRepoPath))
            {
                _previousHoveredSplitTreemapRepoPath = null;
            }
        }

        private static void DrawDirectHoverOutline(
            IReadOnlyDictionary<string, List<Rect>> hoverTileRects,
            string directlyHoveredRepoPath,
            float alpha)
        {
            if (alpha <= 0f ||
                string.IsNullOrWhiteSpace(directlyHoveredRepoPath) ||
                !hoverTileRects.TryGetValue(directlyHoveredRepoPath, out var rects))
            {
                return;
            }

            var hoverOutlineColor = GetTreemapHoverOutlineColor(alpha);
            var mousePosition = Event.current.mousePosition;
            foreach (var rect in rects)
            {
                if (rect.Contains(mousePosition))
                {
                    Handles.DrawSolidRectangleWithOutline(rect, Color.clear, hoverOutlineColor);
                }
            }
        }

        private float GetSplitTreemapTileHoverAlpha(string repoPath)
        {
            if (string.IsNullOrWhiteSpace(repoPath))
            {
                return 0f;
            }

            // A tile can belong to both the incoming hover pair and the fading previous pair; take whichever alpha is stronger.
            var transitionProgress = Mathf.Clamp01((float)((EditorApplication.timeSinceStartup - _hoveredSplitTreemapChangedAtTime) / StorageTreemapHoverTransitionDuration));
            var currentAlpha = string.IsNullOrWhiteSpace(_hoveredSplitTreemapRepoPath) ? 0f : transitionProgress;
            var previousAlpha = string.IsNullOrWhiteSpace(_previousHoveredSplitTreemapRepoPath) ? 0f : 1f - transitionProgress;
            var alpha = 0f;
            if (string.Equals(repoPath, _hoveredSplitTreemapRepoPath, StringComparison.Ordinal))
            {
                alpha = Mathf.Max(alpha, currentAlpha);
            }

            if (string.Equals(repoPath, _previousHoveredSplitTreemapRepoPath, StringComparison.Ordinal))
            {
                alpha = Mathf.Max(alpha, previousAlpha);
            }

            return alpha;
        }

        private static Color ApplySplitTreemapHoverToTileColor(Color baseColor, float hoverAlpha)
        {
            return Color.Lerp(baseColor, Color.black, GetTreemapHoverDarkenStrength(hoverAlpha));
        }

        private static string GetDirectlyHoveredSplitTreemapRepoPath(IReadOnlyDictionary<string, List<Rect>> hoverTileRects)
        {
            if (hoverTileRects == null)
            {
                return null;
            }

            var mousePosition = Event.current.mousePosition;
            foreach (var pair in hoverTileRects)
            {
                foreach (var rect in pair.Value)
                {
                    if (rect.Contains(mousePosition))
                    {
                        return pair.Key;
                    }
                }
            }

            return null;
        }

        private string BuildStorageTreemapTooltip(ModelHubStorageTreemapTile tile, Rect labelRect, string label, bool forcePathTooltip = false)
        {
            if (tile == null)
            {
                return null;
            }

            var parts = new List<string>();
            var includePath = forcePathTooltip || IsTreemapLabelVisuallyClipped(labelRect, label);

            if (includePath && !string.IsNullOrWhiteSpace(tile.RepoPath))
            {
                parts.Add(tile.RepoPath);
            }

            if (_storageTreemapMode == StorageTreemapMode.Combined)
            {
                parts.Add($"Cache: {FormatByteSize(tile.RawSizeOnDiskBytes)} + Runtime: {FormatByteSize(tile.RuntimeSizeOnDiskBytes)}");
            }

            return parts.Count == 0
                ? null
                : string.Join("\n", parts);
        }

        private bool IsTreemapLabelVisuallyClipped(Rect labelRect, string label)
        {
            if (string.IsNullOrWhiteSpace(label) || labelRect.width <= 0f || labelRect.height <= 0f)
            {
                return false;
            }

            var requiredHeight = _treemapTileLabelStyle.CalcHeight(new GUIContent(label), labelRect.width);
            return requiredHeight > labelRect.height + 0.5f;
        }

        private static Color GetTreemapBackgroundColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.14f, 0.14f, 0.14f, 1f)
                : new Color(0.86f, 0.86f, 0.86f, 1f);
        }

        private static Color GetTreemapBorderColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.06f, 0.06f, 0.06f, 0.95f)
                : new Color(0.55f, 0.55f, 0.55f, 1f);
        }

        private static Color GetTreemapRawColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.19f, 0.49f, 0.73f, 1f)
                : new Color(0.41f, 0.67f, 0.88f, 1f);
        }

        private static Color GetTreemapRuntimeColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.18f, 0.63f, 0.38f, 1f)
                : new Color(0.47f, 0.77f, 0.55f, 1f);
        }

        private static Color GetTreemapHoverOutlineColor(float alpha = 0.95f)
        {
            return new Color(1f, 1f, 1f, alpha);
        }

        private static float GetTreemapHoverDarkenStrength(float alpha)
        {
            return EditorGUIUtility.isProSkin
                ? 0.16f * alpha
                : 0.10f * alpha;
        }

        private static Color GetTreemapModeSelectedTint()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.35f, 0.40f, 0.44f, 1f)
                : new Color(0.78f, 0.81f, 0.84f, 1f);
        }

        private static Color GetTreemapModeUnselectedTint()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.22f, 0.25f, 0.28f, 1f)
                : new Color(0.90f, 0.91f, 0.93f, 1f);
        }

        private static Color GetTreemapModeBorderColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.30f, 0.34f, 0.38f, 1f)
                : new Color(0.72f, 0.75f, 0.79f, 1f);
        }

        private void DrawCacheSizeRow(ModelHubItem item)
        {
            var displayBytes = item.CacheSizeOnDiskBytes ?? item.SourceSizeBytes;
            var tooltip = item.CacheSizeOnDiskBytes.HasValue
                ? Content.CacheSizeOnDiskTooltip
                : Content.CacheSizeEstimateTooltip;
            DrawOptionalValueRow(Content.CacheSizeOnDiskLabel, FormatByteSize(displayBytes), tooltip);
        }

        private void EnsureCacheSizeEstimateLookup(ModelHubItem item)
        {
            if (item == null ||
                item.CacheSizeOnDiskBytes.HasValue ||
                item.EstimatedCacheSizeBytes.HasValue)
            {
                return;
            }

            var dependency = item.Dependents.FirstOrDefault()?.Dependency;
            if (dependency == null)
            {
                return;
            }

            _remoteSnapshotSizeEstimateService.EnsureEstimateLookup(dependency, Refresh);
        }

        private static string GetStorageStateLabel(ModelHubItem item)
        {
            var hasCache = item?.CacheSizeOnDiskBytes.HasValue == true || !string.IsNullOrWhiteSpace(item?.RawSnapshotRootPath);
            var hasRuntime = item?.RuntimeSizeOnDiskBytes.HasValue == true || !string.IsNullOrWhiteSpace(item?.RuntimeSnapshotAssetPath);
            if (hasCache && hasRuntime)
            {
                return Content.StorageStateCachedAndImported;
            }

            if (hasCache)
            {
                return Content.StorageStateCached;
            }

            if (hasRuntime)
            {
                return Content.StorageStateImportedOnly;
            }

            return item?.SourceSizeBytes.HasValue == true
                ? Content.StorageStateRemoteOnly
                : Content.StorageStateUnknown;
        }

        private void DrawRepositoryLinkRow(ModelHubItem item)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(Content.RepositoryLabel);
                if (GUILayout.Button(item.Repository, _pathLinkStyle))
                {
                    Application.OpenURL($"https://huggingface.co/{item.Repository}");
                }
            }
        }

        private static void DrawOptionalValueRow(string label, string value, string labelTooltip = null, string valueTooltip = null)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            EditorGUILayout.LabelField(new GUIContent(label, labelTooltip), new GUIContent(value, valueTooltip));
        }

        /// <summary>
        /// Lists manifest dependents that caused this repository row to exist in project.
        /// </summary>
        private void DrawDependentsSection(ModelHubItem item)
        {
            foreach (var dependent in item.Dependents)
            {
                var resolutionInfo = item.HasRevisionConflict
                    ? ProjectSettingsLinks.GetManifestOwnerResolutionInfo(dependent.Dependency)
                    : default;
                var ownerLabel = ProjectManifestDiscovery.IsProjectManifestOwnerId(dependent.Dependency.PackageId)
                    ? ProjectManifestDiscovery.GetOwnerDisplayName(dependent.Dependency.PackageId, dependent.Dependency.PackageDisplayName)
                    : $"{dependent.Dependency.PackageId} {dependent.Dependency.PackageVersion}";
                EditorGUILayout.LabelField(
                    $"{dependent.Dependency.PackageDisplayName} / {dependent.Dependency.ModelId}",
                    EditorStyles.boldLabel);
                EditorGUILayout.LabelField(Content.OwnerLabel, ownerLabel);
                if (item.HasRevisionConflict)
                {
                    EditorGUILayout.LabelField(Content.OwnerSourceLabel, resolutionInfo.OwnerTypeLabel);
                    EditorGUILayout.LabelField(Content.ResolutionHintLabel, resolutionInfo.ResolutionHint);
                }

                EditorGUILayout.LabelField(Content.ModelIdLabel, dependent.Dependency.ModelId);
                DrawOptionalValueRow(
                    Content.DependencyScopeLabel,
                    dependent.Dependency.HasExplicitFileSelection
                        ? $"{dependent.Dependency.SelectedFiles.Count} selected files"
                        : "Whole repository");
                DrawOptionalLinkRow(Content.ManifestLabel, dependent.Dependency.ManifestPath, ProjectSettingsLinks.RevealPath);
                DrawOptionalLinkRow(Content.RuntimeRegistryLabel, dependent.ImportedMetadata?.RuntimeRegistryAssetPath, ProjectSettingsLinks.PingAsset);
                EditorGUILayout.Space(8f);
            }

            EditorGUILayout.Space();
        }

        /// <summary>
        /// Draws aggregated diagnostics for selected repository item.
        /// </summary>
        private void DrawDiagnosticsSection(ModelHubItem item)
        {
            if (item.Diagnostics.Count == 0)
            {
                EditorGUILayout.HelpBox(Content.NoDiagnosticsMessage, MessageType.None);
                EditorGUILayout.Space();
                return;
            }

            foreach (var diagnostic in item.Diagnostics)
            {
                EditorGUILayout.HelpBox(diagnostic.Message, GetDiagnosticMessageType(diagnostic));
            }

            EditorGUILayout.Space();
        }

        /// <summary>
        /// Surfaces revision-conflict resolution affordances when one repository is requested at many revisions.
        /// </summary>
        private void DrawRevisionConflictResolutionSection(ModelHubItem item)
        {
            EditorGUILayout.HelpBox(ProjectSettingsLinks.BuildRevisionConflictHelpMessage(item), MessageType.Error);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(
                        new GUIContent(Content.CopyConflictSummaryLabel, Content.CopyConflictSummaryTooltip),
                        GUILayout.Width(110f)))
                {
                    EditorGUIUtility.systemCopyBuffer = ProjectSettingsLinks.BuildRevisionConflictSummary(item);
                    UnityEngine.Debug.Log($"HF Hub copied revision conflict summary for '{item.Repository}' to clipboard.");
                }

                if (ProjectSettingsLinks.HasPackageManagerOwnedConflict(item) &&
                    GUILayout.Button(
                        new GUIContent(Content.OpenPackageManagerLabel, Content.OpenPackageManagerTooltip),
                        GUILayout.Width(160f)))
                {
                    ProjectSettingsLinks.OpenPackageManager();
                }
            }

            EditorGUILayout.Space(8f);
        }

        /// <summary>
        /// Draws project-wide manifest discovery issues not tied to one selected repository.
        /// </summary>
        private void DrawDiscoveryIssuesSection()
        {
            if (_report.DiscoveryIssues.Count == 0)
            {
                EditorGUILayout.HelpBox(Content.NoDiscoveryIssuesMessage, MessageType.None);
                return;
            }

            foreach (var issue in _report.DiscoveryIssues)
            {
                var ownerLabel = ProjectManifestDiscovery.GetOwnerDisplayName(issue.PackageId);
                EditorGUILayout.HelpBox($"{ownerLabel}: {issue.Message}", MessageType.Warning);
            }
        }
    }
}
