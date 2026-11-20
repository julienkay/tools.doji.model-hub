using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Main Model Hub window: a narrow view switcher on the left (In Project, Model Cache), a searchable list in the
    /// middle, and details for the selected entry on the right, like Package Manager.
    /// </summary>
    internal sealed partial class ModelHubWindow : EditorWindow
    {
        internal const string Title = "AI Model Hub";
        private const string DocumentationUrl = "https://docs.doji-tech.com/tools.doji.model-hub";
        private const string StyleSheet = "ModelHubWindow.uss";

        internal enum ModelHubView
        {
            /// <summary>
            /// The models this project's manifests list.
            /// </summary>
            Project,

            /// <summary>
            /// The models downloaded on this machine, to add to this project or to a package in development.
            /// </summary>
            Cache
        }

        private enum DetailTab
        {
            Overview,
            Files,
            Storage
        }

        [SerializeField] private string _selectedKey;
        [SerializeField] private string _searchText = string.Empty;
        [SerializeField] private DetailTab _selectedTab = DetailTab.Overview;
        [SerializeField] private ModelHubView _view = ModelHubView.Project;

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
        private VisualElement _navProject;
        private VisualElement _navCache;
        private Label _navProjectMeta;
        private Label _navCacheMeta;
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
            window.SwitchView(ModelHubView.Project);
            window.Select(BuildItemKey(repository, revision));
        }

        /// <summary>
        /// Opens the window in Model Cache with one cached repository selected, by its cache folder name.
        /// </summary>
        internal static void OpenCachedModel(string folderName)
        {
            OpenWindow();
            var window = GetWindow<ModelHubWindow>();
            window.SwitchView(ModelHubView.Cache);
            window.SelectCacheEntry(folderName);
        }

        /// <summary>
        /// Refreshes every open Model Hub window, for example after another window changed the cache.
        /// </summary>
        internal static void RefreshOpenWindows()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<ModelHubWindow>())
            {
                window.Refresh();
            }
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
            root.Add(BuildFoldersWarning());

            var body = UiKit.Element("mh-body");
            body.Add(BuildNav());
            var split = new TwoPaneSplitView(0, 300f, TwoPaneSplitViewOrientation.Horizontal)
            {
                viewDataKey = "model-hub-model-hub-split"
            };
            split.AddToClassList("mh-split");
            split.Add(BuildListPane());
            _detailsPane = UiKit.Element("mh-details-pane");
            split.Add(_detailsPane);
            body.Add(split);
            root.Add(body);

            root.schedule.Execute(UpdateSyncBanner).Every(250);
            Refresh();
        }

        // ---------------------------------------------------------------- Views

        private VisualElement BuildNav()
        {
            var nav = UiKit.Element("mh-nav");
            nav.Add(BuildListSection("Project", null));
            (_navProject, _navProjectMeta) = BuildNavItem(
                "Package Manager",
                "In Project",
                "The models this project's manifests list",
                ModelHubView.Project);
            nav.Add(_navProject);
            nav.Add(BuildListSection("This machine", null));
            (_navCache, _navCacheMeta) = BuildNavItem(
                "Folder Icon",
                "Model Cache",
                "Models downloaded on this machine, for any project or Python tool. Add them to this project without downloading them again.",
                ModelHubView.Cache);
            _navCache.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                evt.menu.AppendAction("Open Disk Usage", _ => CacheDiskUsageWindow.Open(null));
                evt.menu.AppendAction(UiKit.RevealInFileBrowserLabel, _ => ProjectSettingsLinks.RevealPath(_cacheOverview?.CacheRoot ?? CachePaths.GetCacheRoot()));
            }));
            nav.Add(_navCache);
            nav.Add(UiKit.Spacer());
            nav.Add(BuildFoldersPanel());
            UpdateNav();
            return nav;
        }

        private (VisualElement Item, Label Meta) BuildNavItem(string icon, string label, string tooltip, ModelHubView view)
        {
            var item = UiKit.Element("mh-item", "mh-nav__item");
            item.tooltip = tooltip;
            item.Add(UiKit.Icon(icon, "mh-item__icon"));
            item.Add(UiKit.Text(label, "mh-item__name", "mh-nav__label"));
            var meta = UiKit.Text(string.Empty, "mh-item__meta");
            item.Add(meta);
            item.RegisterCallback<ClickEvent>(_ => SwitchView(view));
            return (item, meta);
        }

        private void UpdateNav()
        {
            _navProject?.EnableInClassList("mh-item--selected", _view == ModelHubView.Project);
            _navCache?.EnableInClassList("mh-item--selected", _view == ModelHubView.Cache);
            if (_navProjectMeta != null)
            {
                _navProjectMeta.text = _report == null ? string.Empty : _report.Items.Count.ToString();
            }

            if (_navCacheMeta != null)
            {
                _navCacheMeta.text = _cacheOverview == null
                    ? string.Empty
                    : _cacheOverview.Repositories.Count(repository => repository.IsModel && repository.SnapshotCount > 0).ToString();
            }
        }

        /// <summary>
        /// Shows the project's models or the shared cache in the list and details.
        /// </summary>
        internal void SwitchView(ModelHubView view)
        {
            if (_view == view)
            {
                return;
            }

            if (_view == ModelHubView.Project && !TryResolvePendingFileEdits(GetSelectedItem()))
            {
                return;
            }

            _view = view;
            UpdateNav();
            RebuildList();
            RebuildDetails();
        }

        internal ModelHubView CurrentView => _view;

        private string CurrentSelectionKey => _view != ModelHubView.Project
            ? BuildCacheRowKey(_selectedCacheFolder)
            : _selectedKey;

        // ---------------------------------------------------------------- Toolbar

        private VisualElement BuildToolbar()
        {
            var toolbar = UiKit.Element("mh-toolbar");

            Button addButton = null;
            addButton = UiKit.Button("+  Add Model", () => ShowAddModelPopup(addButton.worldBound), "hf-button--first");
            addButton.tooltip = "Add a Hugging Face model to this project or to a local package.";
            toolbar.Add(addButton);

            var search = UiKit.SearchField("Search models", value =>
            {
                _searchText = value ?? string.Empty;
                RebuildList();
                RebuildDetails();
            });
            search.AddToClassList("mh-toolbar__search");
            search.SetValueWithoutNotify(_searchText);
            toolbar.Add(search);

            toolbar.Add(UiKit.Spacer());

            _importAllButton = UiKit.PrimaryButton("Set Up All", () =>
            {
                EditorOperations.StartBackgroundSync();
                Refresh();
            });
            _importAllButton.tooltip = "Download and convert every model that needs it. Converting takes CPU, memory, and time for large models.";
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
            menu.AddItem(new GUIContent("Folders (Preferences)"), false, CacheFolderPreferences.Open);
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

            var pending = _report.Items
                .Select(ModelStatus.From)
                .Where(status => status.Action == ModelActionKind.Sync)
                .ToArray();
            UiKit.SetVisible(_importAllButton, pending.Length > 0);
            _importAllButton.text = $"{ModelStatus.BuildBulkActionLabel(pending)} ({pending.Length})";
            _importAllButton.SetEnabled(!BackgroundSyncManager.IsRunning);
        }

        // ---------------------------------------------------------------- Sync banner

        private VisualElement BuildSyncBanner()
        {
            _syncBanner = UiKit.Element("mh-sync", "hf-hidden");
            var text = UiKit.Element("mh-sync__text");
            text.Add(UiKit.Text("Setting up models…", "mh-sync__title"));
            _syncDescription = UiKit.Text(string.Empty, "mh-sync__description");
            text.Add(_syncDescription);
            var track = UiKit.Element("hf-progress");
            _syncProgressFill = UiKit.Element("hf-progress__fill");
            track.Add(_syncProgressFill);
            text.Add(track);
            _syncBanner.Add(text);
            _syncBanner.Add(UiKit.Button("Cancel", () => BackgroundSyncManager.Cancel()));
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
            UpdateFolders();
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
            UpdateNav();
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
            if (_view == ModelHubView.Cache)
            {
                RebuildCachedModelList();
                EnsureCacheSelection();
                UpdateRowSelection();
                UpdateListFooter();
                return;
            }

            var items = GetFilteredItems();
            var issues = GetFilteredIssues();
            var attention = items
                .Where(item => ModelStatus.From(item).NeedsAction)
                .OrderBy(item => GetAttentionRank(ModelStatus.From(item)))
                .ToArray();
            var available = items.Where(item => item.IsNotEnabled).ToArray();
            var ready = items.Where(item => !item.IsNotEnabled && !ModelStatus.From(item).NeedsAction).ToArray();

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

            if (available.Length > 0)
            {
                // Optional models that the project did not enable: declared, but not downloaded until enabled.
                _list.Add(BuildListSection("Available", available.Length));
                foreach (var item in available)
                {
                    _list.Add(BuildItemRow(item));
                }
            }

            if (_rowsByKey.Count == 0 && !string.IsNullOrWhiteSpace(_searchText))
            {
                _list.Add(UiKit.Text($"No models match “{_searchText}”.", "mh-list-empty"));
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

        private static VisualElement BuildListSection(string title, int? count)
        {
            var header = UiKit.Element("mh-list-section");
            header.Add(UiKit.Text(title.ToUpperInvariant(), "hf-overline"));
            if (count.HasValue)
            {
                header.Add(UiKit.Text(count.Value.ToString(), "mh-list-section__count"));
            }

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

            if (_view == ModelHubView.Cache)
            {
                var models = _cacheOverview?.Repositories.Count(repository => repository.IsModel && repository.SnapshotCount > 0) ?? 0;
                _listFooter.text = $"{models} cached {(models == 1 ? "model" : "models")} · Updated {_lastRefreshTime:HH:mm}";
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
            var selectedKey = CurrentSelectionKey;
            foreach (var pair in _rowsByKey)
            {
                pair.Value.EnableInClassList("mh-item--selected", string.Equals(pair.Key, selectedKey, StringComparison.Ordinal));
            }
        }

        private void Select(string key)
        {
            if (_view != ModelHubView.Project)
            {
                SelectCacheEntry(GetCacheFolder(key));
                return;
            }

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

            var index = keys.IndexOf(CurrentSelectionKey);
            index = evt.keyCode == KeyCode.UpArrow
                ? Mathf.Max(0, index - 1)
                : Mathf.Min(keys.Count - 1, index + 1);
            Select(keys[index]);
            evt.StopPropagation();
        }

        private ModelHubItem GetSelectedItem()
        {
            if (_view != ModelHubView.Project)
            {
                return null;
            }

            return _report?.Items.FirstOrDefault(item =>
                string.Equals(BuildItemKey(item), _selectedKey, StringComparison.Ordinal));
        }

        private ManifestDiscoveryIssue GetSelectedIssue()
        {
            if (_view != ModelHubView.Project)
            {
                return null;
            }

            return _report?.DiscoveryIssues.FirstOrDefault(issue =>
                string.Equals(BuildIssueKey(issue), _selectedKey, StringComparison.Ordinal));
        }
    }
}
