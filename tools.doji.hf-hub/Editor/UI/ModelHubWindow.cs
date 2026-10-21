using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Main Model Hub window: a searchable model list on the left and details for the selected model on the right.
    /// </summary>
    internal sealed partial class ModelHubWindow : EditorWindow
    {
        internal const string Title = "AI Model Hub";
        private const string DocumentationUrl = "https://docs.doji-tech.com/tools.doji.hf-hub";
        private const string StyleSheet = "ModelHubWindow.uss";

        private enum DetailTab
        {
            Overview,
            Files,
            Storage
        }

        [SerializeField] private string _selectedKey;
        [SerializeField] private string _searchText = string.Empty;
        [SerializeField] private DetailTab _selectedTab = DetailTab.Overview;

        private ModelHubReport _report;
        private DateTime _lastRefreshTime;
        private bool _wasSyncRunning;

        private Button _importAllButton;
        private VisualElement _syncBanner;
        private Label _syncDescription;
        private VisualElement _syncProgressFill;
        private ScrollView _list;
        private Label _listFooter;
        private VisualElement _detailsPane;
        private readonly Dictionary<string, VisualElement> _rowsByKey = new(StringComparer.Ordinal);

        [MenuItem("Window/AI Model Hub")]
        private static void OpenFromWindowMenu()
        {
            OpenWindow();
        }

        public static void OpenWindow(bool resetFilters = false)
        {
            var window = GetWindow<ModelHubWindow>();
            window.titleContent = new GUIContent(Title, UiKit.LoadIcon("Package Manager"));
            window.minSize = new Vector2(760f, 460f);
            if (resetFilters)
            {
                window.ResetFilters();
            }

            window.Refresh();
            window.Show();
        }

        /// <summary>
        /// Opens the window with one repository selected.
        /// </summary>
        public static void OpenWindow(string repository, string revision)
        {
            OpenWindow();
            var window = GetWindow<ModelHubWindow>();
            window.Select(BuildItemKey(repository, revision));
        }

        private void OnEnable()
        {
            BackgroundSyncManager.SyncStateChanged += HandleSyncStateChanged;
        }

        private void OnDisable()
        {
            BackgroundSyncManager.SyncStateChanged -= HandleSyncStateChanged;
        }

        private void CreateGUI()
        {
            titleContent = new GUIContent(Title, UiKit.LoadIcon("Package Manager"));
            var root = rootVisualElement;
            UiKit.ApplyTheme(root, StyleSheet);

            root.Add(BuildToolbar());
            root.Add(BuildSyncBanner());

            var split = new TwoPaneSplitView(0, 300f, TwoPaneSplitViewOrientation.Horizontal)
            {
                viewDataKey = "hf-hub-model-hub-split"
            };
            split.AddToClassList("mh-split");
            split.Add(BuildListPane());
            _detailsPane = UiKit.Element("mh-details-pane");
            split.Add(_detailsPane);
            root.Add(split);

            root.schedule.Execute(UpdateSyncBanner).Every(250);
            Refresh();
        }

        // ---------------------------------------------------------------- Toolbar

        private VisualElement BuildToolbar()
        {
            var toolbar = UiKit.Element("mh-toolbar");

            Button addButton = null;
            addButton = UiKit.Button("+  Add Model", () => ShowAddModelPopup(addButton.worldBound), "hf-button--first");
            addButton.tooltip = "Add a Hugging Face model to this project or to a local package.";
            toolbar.Add(addButton);

            var search = UiKit.SearchField("Search models and packages", value =>
            {
                _searchText = value ?? string.Empty;
                RebuildList();
                RebuildDetails();
            });
            search.AddToClassList("mh-toolbar__search");
            search.SetValueWithoutNotify(_searchText);
            toolbar.Add(search);

            toolbar.Add(UiKit.Spacer());

            _importAllButton = UiKit.PrimaryButton("Import All", () =>
            {
                EditorOperations.StartBackgroundSync();
                Refresh();
            });
            _importAllButton.tooltip = "Download and import every model that needs it.";
            toolbar.Add(_importAllButton);

            toolbar.Add(UiKit.IconButton("Refresh", "Refresh", Refresh));

            Button moreButton = null;
            moreButton = UiKit.IconButton("_Menu", "More", () => ShowMoreMenu(moreButton.worldBound));
            toolbar.Add(moreButton);
            return toolbar;
        }

        private void ShowMoreMenu(Rect buttonRect)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Project Settings"), false, () => SettingsService.OpenProjectSettings(EditorOperations.ProjectSettingsPath));
            menu.AddItem(new GUIContent("Open Cache Folder"), false, () => ProjectSettingsLinks.RevealPath(CachePaths.GetCacheRoot()));
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Documentation"), false, () => Application.OpenURL(DocumentationUrl));
            menu.DropDown(buttonRect);
        }

        private void ShowAddModelPopup(Rect buttonRect)
        {
            UnityEditor.PopupWindow.Show(buttonRect, new AddModelPopup(Refresh));
        }

        private void UpdateToolbar()
        {
            if (_importAllButton == null || _report == null)
            {
                return;
            }

            var pendingCount = _report.Items.Count(item => ModelStatus.From(item).Action == ModelActionKind.Sync);
            UiKit.SetVisible(_importAllButton, pendingCount > 0);
            _importAllButton.text = $"Import All ({pendingCount})";
            _importAllButton.SetEnabled(!BackgroundSyncManager.IsRunning);
        }

        // ---------------------------------------------------------------- Sync banner

        private VisualElement BuildSyncBanner()
        {
            _syncBanner = UiKit.Element("mh-sync", "hf-hidden");
            var text = UiKit.Element("mh-sync__text");
            text.Add(UiKit.Text("Importing models…", "mh-sync__title"));
            _syncDescription = UiKit.Text(string.Empty, "mh-sync__description");
            text.Add(_syncDescription);
            var track = UiKit.Element("hf-progress");
            _syncProgressFill = UiKit.Element("hf-progress__fill");
            track.Add(_syncProgressFill);
            text.Add(track);
            _syncBanner.Add(text);
            _syncBanner.Add(UiKit.Button("Cancel", BackgroundSyncManager.Cancel));
            return _syncBanner;
        }

        private void UpdateSyncBanner()
        {
            if (_syncBanner == null)
            {
                return;
            }

            var isRunning = BackgroundSyncManager.TryGetProgress(out var progress, out var description);
            UiKit.SetVisible(_syncBanner, isRunning);
            if (isRunning)
            {
                _syncDescription.text = string.IsNullOrWhiteSpace(description) ? "Preparing…" : description;
                _syncProgressFill.style.width = Length.Percent(Mathf.Clamp01(progress) * 100f);
            }

            if (isRunning != _wasSyncRunning)
            {
                _wasSyncRunning = isRunning;
                RefreshReport();
            }
        }

        // ---------------------------------------------------------------- Data

        private void Refresh()
        {
            RefreshReport();
        }

        private void RefreshReport()
        {
            _report = EditorOperations.CreateModelHubReport();
            RefreshCacheOverview();
            _lastRefreshTime = DateTime.Now;
            if (_list == null)
            {
                return;
            }

            UpdateToolbar();
            RebuildList();
            RebuildDetails();
        }

        private void HandleSyncStateChanged()
        {
            RefreshReport();
        }

        private void ResetFilters()
        {
            _searchText = string.Empty;
            _selectedKey = null;
            _selectedTab = DetailTab.Overview;
            var search = rootVisualElement.Q<TextField>(className: "mh-toolbar__search");
            search?.SetValueWithoutNotify(string.Empty);
        }

        private IReadOnlyList<ModelHubItem> GetFilteredItems()
        {
            IEnumerable<ModelHubItem> items = _report?.Items ?? Array.Empty<ModelHubItem>();
            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                items = items.Where(item =>
                    Contains(item.Repository, _searchText) ||
                    item.Dependents.Any(dependent =>
                        Contains(dependent.Dependency.PackageDisplayName, _searchText) ||
                        Contains(dependent.Dependency.PackageId, _searchText)));
            }

            return items
                .OrderBy(item => ModelStatus.GetModelName(item.Repository), StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private IReadOnlyList<ManifestDiscoveryIssue> GetFilteredIssues()
        {
            IEnumerable<ManifestDiscoveryIssue> issues = _report?.DiscoveryIssues ?? Array.Empty<ManifestDiscoveryIssue>();
            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                issues = issues.Where(issue =>
                    Contains(issue.PackageId, _searchText) ||
                    Contains(ProjectManifestDiscovery.GetOwnerDisplayName(issue.PackageId), _searchText));
            }

            return issues.ToArray();
        }

        private static bool Contains(string value, string search)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string BuildItemKey(string repository, string revision)
        {
            return $"model:{repository}@{revision}";
        }

        private static string BuildItemKey(ModelHubItem item)
        {
            return BuildItemKey(item?.Repository, item?.RequestedRevision);
        }

        private static string BuildIssueKey(ManifestDiscoveryIssue issue)
        {
            return $"issue:{issue?.PackageId}|{issue?.ManifestPath}";
        }

        // ---------------------------------------------------------------- List

        private VisualElement BuildListPane()
        {
            var pane = UiKit.Element("mh-list-pane");
            _list = new ScrollView(ScrollViewMode.Vertical);
            _list.AddToClassList("mh-list");
            _list.focusable = true;
            _list.RegisterCallback<KeyDownEvent>(HandleListKeyDown);
            pane.Add(_list);
            pane.Add(BuildCacheRow());

            var footer = UiKit.Element("mh-list-footer");
            _listFooter = UiKit.Text(string.Empty, "hf-small", "hf-faint");
            footer.Add(_listFooter);
            pane.Add(footer);
            return pane;
        }

        private void RebuildList()
        {
            if (_list == null)
            {
                return;
            }

            _list.Clear();
            _rowsByKey.Clear();

            var items = GetFilteredItems();
            var issues = GetFilteredIssues();
            var attention = items
                .Where(item => ModelStatus.From(item).NeedsAction)
                .OrderBy(item => GetAttentionRank(ModelStatus.From(item)))
                .ToArray();
            var ready = items.Where(item => !ModelStatus.From(item).NeedsAction).ToArray();

            if (issues.Count > 0)
            {
                _list.Add(BuildListSection("Manifest errors", issues.Count));
                foreach (var issue in issues)
                {
                    _list.Add(BuildIssueRow(issue));
                }
            }

            if (attention.Length > 0)
            {
                _list.Add(BuildListSection("Needs action", attention.Length));
                foreach (var item in attention)
                {
                    _list.Add(BuildItemRow(item));
                }
            }

            if (ready.Length > 0)
            {
                _list.Add(BuildListSection("Ready", ready.Length));
                foreach (var item in ready)
                {
                    _list.Add(BuildItemRow(item));
                }
            }

            if (_rowsByKey.Count == 0 && !string.IsNullOrWhiteSpace(_searchText))
            {
                _list.Add(UiKit.Text($"No models match “{_searchText}”.", "mh-list-empty"));
            }

            if (_cacheRow != null)
            {
                _rowsByKey[CacheKey] = _cacheRow;
            }

            EnsureSelection();
            UpdateRowSelection();
            UpdateListFooter();
        }

        /// <summary>
        /// Orders the "Needs action" section: problems first, then choices, then routine downloads and imports.
        /// </summary>
        private static int GetAttentionRank(ModelStatus status)
        {
            return status.Tone switch
            {
                UiTone.Error => 0,
                UiTone.Warning => 1,
                _ => 2
            };
        }

        private static VisualElement BuildListSection(string title, int count)
        {
            var header = UiKit.Element("mh-list-section");
            header.Add(UiKit.Text(title.ToUpperInvariant(), "hf-overline"));
            header.Add(UiKit.Text(count.ToString(), "mh-list-section__count"));
            return header;
        }

        private VisualElement BuildItemRow(ModelHubItem item)
        {
            var status = ModelStatus.From(item);
            var key = BuildItemKey(item);
            var row = UiKit.Element("mh-item");
            row.tooltip = status.Label;
            row.Add(UiKit.Dot(status.Tone, status.Label));

            var text = UiKit.Element("mh-item__text");
            text.Add(UiKit.Text(ModelStatus.GetModelName(item.Repository), "mh-item__name"));
            var usedBy = ModelStatus.BuildUsedBySummary(item);
            var owner = item.Repository?.Contains("/") == true ? item.Repository.Substring(0, item.Repository.IndexOf('/')) : null;
            var revision = status.Kind == ModelStatusKind.Conflict
                ? ManifestRevision.NormalizeForResolution(item.RequestedRevision)
                : null;
            text.Add(UiKit.Text(
                string.Join(" · ", new[] { owner, revision, usedBy }.Where(part => !string.IsNullOrWhiteSpace(part))),
                "mh-item__subtitle"));
            row.Add(text);

            var size = status.Kind == ModelStatusKind.Ready
                ? UiKit.FormatBytes(item.RuntimeSizeOnDiskBytes)
                : UiKit.FormatBytes(item.SourceSizeBytes);
            if (!string.IsNullOrWhiteSpace(size))
            {
                row.Add(UiKit.Text(size, "mh-item__meta"));
            }

            row.RegisterCallback<ClickEvent>(_ => Select(key));
            _rowsByKey[key] = row;
            return row;
        }

        private VisualElement BuildIssueRow(ManifestDiscoveryIssue issue)
        {
            var key = BuildIssueKey(issue);
            var row = UiKit.Element("mh-item");
            row.Add(UiKit.Dot(UiTone.Error, "Manifest error"));
            var text = UiKit.Element("mh-item__text");
            text.Add(UiKit.Text(ProjectManifestDiscovery.GetOwnerDisplayName(issue.PackageId), "mh-item__name"));
            text.Add(UiKit.Text("Manifest could not be read", "mh-item__subtitle"));
            row.Add(text);
            row.RegisterCallback<ClickEvent>(_ => Select(key));
            _rowsByKey[key] = row;
            return row;
        }

        private void UpdateListFooter()
        {
            if (_listFooter == null || _report == null)
            {
                return;
            }

            var count = _report.Items.Count;
            var modelsLabel = count == 1 ? "1 model" : $"{count} models";
            _listFooter.text = $"{modelsLabel} · Updated {_lastRefreshTime:HH:mm}";
        }

        private void EnsureSelection()
        {
            if (!string.IsNullOrWhiteSpace(_selectedKey) && _rowsByKey.ContainsKey(_selectedKey))
            {
                return;
            }

            _selectedKey = _rowsByKey.Keys.FirstOrDefault();
        }

        private void UpdateRowSelection()
        {
            foreach (var pair in _rowsByKey)
            {
                pair.Value.EnableInClassList("mh-item--selected", string.Equals(pair.Key, _selectedKey, StringComparison.Ordinal));
            }
        }

        private void Select(string key)
        {
            if (string.Equals(key, _selectedKey, StringComparison.Ordinal))
            {
                return;
            }

            if (!TryResolvePendingFileEdits(GetSelectedItem()))
            {
                return;
            }

            _selectedKey = key;
            UpdateRowSelection();
            RebuildDetails();
            if (key != null && _rowsByKey.TryGetValue(key, out var row) && _list.contentContainer.Contains(row))
            {
                _list.ScrollTo(row);
            }
        }

        private void HandleListKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.UpArrow && evt.keyCode != KeyCode.DownArrow)
            {
                return;
            }

            var keys = _rowsByKey.Keys.ToList();
            if (keys.Count == 0)
            {
                return;
            }

            var index = keys.IndexOf(_selectedKey);
            index = evt.keyCode == KeyCode.UpArrow
                ? Mathf.Max(0, index - 1)
                : Mathf.Min(keys.Count - 1, index + 1);
            Select(keys[index]);
            evt.StopPropagation();
        }

        private ModelHubItem GetSelectedItem()
        {
            return _report?.Items.FirstOrDefault(item =>
                string.Equals(BuildItemKey(item), _selectedKey, StringComparison.Ordinal));
        }

        private ManifestDiscoveryIssue GetSelectedIssue()
        {
            return _report?.DiscoveryIssues.FirstOrDefault(issue =>
                string.Equals(BuildIssueKey(issue), _selectedKey, StringComparison.Ordinal));
        }
    }
}
