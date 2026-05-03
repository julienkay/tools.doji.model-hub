using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Renders repository list pane, including search, owner grouping, and selection behavior.
    /// </summary>
    internal sealed partial class ModelHubWindow
    {
        /// <summary>
        /// Draws left navigation pane that mirrors Package Manager-style repository browsing.
        /// </summary>
        private void DrawListPane(Rect rect, IReadOnlyList<ModelHubItem> items)
        {
            GUILayout.BeginArea(rect);
            using (new EditorGUILayout.VerticalScope())
            {
                DrawListToolbar();
                _listScrollPosition = EditorGUILayout.BeginScrollView(_listScrollPosition, false, false);

                if (_report.DiscoveryIssues.Count > 0)
                {
                    DrawDiscoveryIssueListSection();
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
                // Filter changes can invalidate current selection immediately, so clear it instead of
                // trying to preserve an index into a newly filtered set.
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

        /// <summary>
        /// Draws one selectable repository row or owner-child row in list pane.
        /// </summary>
        private void DrawListItem(IReadOnlyList<ModelHubItem> items, int index, ModelHubItem item, bool isChildRow = false)
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
                if (!TryChangeSelectedItem(items, index))
                {
                    return;
                }

                _selectedIndex = index;
                _selectedIssueIndex = -1;
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
            DrawStateIcon(item, iconRect);
        }

        /// <summary>
        /// Draws one owner foldout and keeps selected child visible when collapsed.
        /// </summary>
        private void DrawListGroup(ListGroup group, IReadOnlyList<ModelHubItem> items)
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
                        DrawListItem(items, index, item, true);
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
                DrawListItem(items, selectedIndex, selectedItem, true);
            }
        }

        /// <summary>
        /// Draws the discovery issues section header and one selectable row per issue.
        /// </summary>
        private void DrawDiscoveryIssueListSection()
        {
            var headerRect = GUILayoutUtility.GetRect(0f, SectionHeaderHeight, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                EditorStyles.helpBox.Draw(headerRect, false, false, false, false);
            }

            var labelRect = new Rect(
                headerRect.x + ListItemPadding,
                headerRect.y + ((headerRect.height - EditorGUIUtility.singleLineHeight) * 0.5f),
                Mathf.Max(0f, headerRect.width - (ListItemPadding * 2f)),
                EditorGUIUtility.singleLineHeight);
            GUI.Label(labelRect, Content.DiscoveryIssuesSection, _sectionFoldoutStyle);

            for (var i = 0; i < _report.DiscoveryIssues.Count; i++)
            {
                DrawDiscoveryIssueListItem(i, _report.DiscoveryIssues[i]);
            }
        }

        /// <summary>
        /// Draws one selectable discovery issue row in list pane.
        /// </summary>
        private void DrawDiscoveryIssueListItem(int index, ManifestDiscoveryIssue issue)
        {
            var itemRect = GUILayoutUtility.GetRect(0f, ListItemHeight, GUILayout.ExpandWidth(true));
            var isSelected = _selectedIssueIndex == index;

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(itemRect, isSelected ? GetSelectionColor() : GetChildRowColor());
                if (isSelected)
                {
                    EditorGUI.DrawRect(new Rect(itemRect.x, itemRect.y, itemRect.width, 1f), GetSelectionBorderColor());
                    EditorGUI.DrawRect(new Rect(itemRect.x, itemRect.yMax - 1f, itemRect.width, 1f), GetSelectionBorderColor());
                }
            }

            if (GUI.Button(itemRect, GUIContent.none, GUIStyle.none))
            {
                _selectedIssueIndex = index;
                _selectedIndex = -1;
                GUI.FocusControl(null);
            }

            var contentRect = new Rect(
                itemRect.x + ListItemPadding + ChildRowIndent,
                itemRect.y,
                itemRect.width - (ListItemPadding * 2f) - ChildRowIndent - ListItemIconSize - 6f,
                itemRect.height);
            var lineHeight = EditorGUIUtility.singleLineHeight;
            var contentY = itemRect.y + Mathf.Max(0f, (itemRect.height - lineHeight) * 0.5f);
            var iconY = itemRect.y + Mathf.Max(0f, (itemRect.height - ListItemIconSize) * 0.5f);
            var titleRect = new Rect(contentRect.x, contentY, contentRect.width, lineHeight);
            var ownerLabel = ProjectManifestDiscovery.GetOwnerDisplayName(issue.PackageId);
            GUI.Label(titleRect, ownerLabel, _packageEntryTitleStyle);

            var iconRect = new Rect(itemRect.xMax - ListItemPadding - ListItemIconSize, iconY, ListItemIconSize, ListItemIconSize);
            GUI.Label(iconRect, _actionRequiredStateIconContent);
        }

        /// <summary>
        /// Groups repositories by owner segment so sidebar stays repository-centric but still scannable.
        /// </summary>
        private IReadOnlyList<ListGroup> BuildListGroups(IReadOnlyList<ModelHubItem> items)
        {
            return items
                .GroupBy(item => GetRepositoryOwner(item.Repository), StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => new ListGroup(
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

        /// <summary>
        /// Lightweight owner-group view model used only by list-pane rendering.
        /// </summary>
        private readonly struct ListGroup
        {
            public ListGroup(string owner, IReadOnlyList<ModelHubItem> items)
            {
                Owner = owner;
                Items = items;
            }

            public string Owner { get; }
            public IReadOnlyList<ModelHubItem> Items { get; }
        }
    }
}
