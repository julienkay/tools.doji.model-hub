using System;
using System.Collections.Generic;
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
                    var showFilesBottomBar = _selectedDetailTab == DetailTab.Files && selectedItem.Files.Count > 0;
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
            DrawOptionalValueRow(Content.GeneratedSentisFilesLabel, item.ImportedFileCount.ToString());
            DrawOptionalValueRow(Content.SourceSizeLabel, FormatByteSize(item.SourceSizeBytes), Content.SourceSizeTooltip);
            DrawRepositoryLinkRow(item);

            if (item.HasRevisionConflict)
            {
                DrawRevisionConflictResolutionSection(item);
            }

            EditorGUILayout.Space();
        }

        /// <summary>
        /// Shows local storage footprint so users can distinguish remote source size from current disk usage.
        /// </summary>
        private void DrawStorageSection(ModelHubItem item)
        {
            EnsureCacheSizeEstimateLookup(item);
            DrawOptionalValueRow(Content.StorageStateLabel, GetStorageStateLabel(item));
            DrawOptionalLinkRow(Content.RawSnapshotLabel, item.RawSnapshotRootPath, ProjectSettingsLinks.RevealPath);
            DrawCacheSizeRow(item);
            DrawOptionalLinkRow(Content.RuntimeFolderLabel, item.RuntimeSnapshotAssetPath, ProjectSettingsLinks.PingAsset);
            DrawOptionalValueRow(Content.RuntimeAssetsSizeOnDiskLabel, FormatByteSize(item.RuntimeSizeOnDiskBytes), Content.RuntimeAssetsSizeOnDiskTooltip);
            EditorGUILayout.Space();
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
