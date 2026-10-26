using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Model Cache Disk Usage window: a treemap of the whole shared Hugging Face cache, per repository or per file, with
    /// the models this project uses, models it does not use, and partial downloads, plus Remove from Cache.
    /// </summary>
    internal sealed class CacheDiskUsageWindow : EditorWindow
    {
        private const string StyleSheet = "ModelHubWindow.uss";
        private const float TileLabelMinWidth = 56f;
        private const float TileLabelMinHeight = 30f;
        private const float TileSizeLabelMinHeight = 44f;
        private const float GroupHeaderHeight = 16f;

        [SerializeField] private bool _showsFiles;
        [SerializeField] private string _selectedFolder;
        [SerializeField] private string _selectedFilePath;

        private CacheOverview _overview;
        private ModelHubReport _report;
        private VisualElement _content;
        private VisualElement _treemap;
        private VisualElement _card;

        [MenuItem("Tools/Doji/AI Model Hub/Model Cache Disk Usage")]
        public static void OpenWindow()
        {
            Open(null);
        }

        /// <summary>
        /// Opens the window and selects a cached repository by its cache folder name, if given.
        /// </summary>
        internal static void Open(string folderName)
        {
            var window = GetWindow<CacheDiskUsageWindow>();
            window.titleContent = new GUIContent("Model Cache Disk Usage", EditorGUIUtility.IconContent("Folder Icon").image);
            window.minSize = new Vector2(520f, 420f);
            if (!string.IsNullOrEmpty(folderName))
            {
                window._selectedFolder = folderName;
                window._selectedFilePath = null;
            }

            window.Show();
            window.Focus();
            window.Refresh();
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            UiKit.ApplyTheme(root, StyleSheet);
            root.AddToClassList("du-root");
            _content = UiKit.Element("du-content");
            root.Add(_content);
            Refresh();
        }

        private void OnFocus()
        {
            if (_content != null)
            {
                Refresh();
            }
        }

        /// <summary>
        /// Scans the cache and the project's models again, and redraws.
        /// </summary>
        internal void Refresh()
        {
            try
            {
                _overview = CacheOverviewService.Scan();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Debug.LogWarning($"AI Model Hub could not read the download cache: {exception.Message}");
                _overview = new CacheOverview { CacheRoot = CachePaths.GetCacheRoot() };
            }

            _report = EditorOperations.CreateModelHubReport();
            if (GetSelectedRepository() == null)
            {
                _selectedFolder = null;
                _selectedFilePath = null;
            }

            Rebuild();
        }

        private HashSet<string> GetProjectRepositories()
        {
            return CacheTiles.CreateProjectRepositorySet((_report?.Items ?? Array.Empty<ModelHubItem>()).Select(item => item.Repository));
        }

        private CacheRepositoryEntry GetSelectedRepository()
        {
            return _overview?.Repositories.FirstOrDefault(repository =>
                string.Equals(repository.FolderName, _selectedFolder, StringComparison.Ordinal));
        }

        // ---------------------------------------------------------------- Layout

        private void Rebuild()
        {
            if (_content == null)
            {
                return;
            }

            _content.Clear();
            _treemap = null;
            _card = null;
            var overview = _overview ?? new CacheOverview { CacheRoot = CachePaths.GetCacheRoot() };
            var projectRepositories = GetProjectRepositories();

            var header = UiKit.Element("mh-header");
            var headerText = UiKit.Element("mh-header__text");
            headerText.Add(UiKit.Text("Model Cache Disk Usage", "hf-title"));
            var subtitle = UiKit.Element("mh-header__subtitle");
            var count = overview.Repositories.Count;
            subtitle.Add(UiKit.Text($"{UiKit.FormatBytes(overview.TotalSizeInBytes) ?? "0 B"} · {CacheTiles.DescribeCount(overview)}"));
            subtitle.Add(UiKit.LinkButton(UiKit.RevealInFileBrowserLabel, () => ProjectSettingsLinks.RevealPath(overview.CacheRoot), overview.CacheRoot));
            headerText.Add(subtitle);
            header.Add(headerText);
            header.Add(UiKit.IconButton("Refresh", "Scan the cache again", Refresh));
            _content.Add(header);
            _content.Add(UiKit.Text(
                "The Hugging Face cache on this machine. Other Unity projects and Python tools that use huggingface_hub share these downloads.",
                "hf-section__hint", "mh-cache-hint"));

            if (count == 0)
            {
                _content.Add(UiKit.EmptyState("Folder Icon", "The cache is empty", "Models appear here once they are downloaded."));
                return;
            }

            _content.Add(BuildLegend(overview, projectRepositories));

            var segment = UiKit.Element("mh-segment");
            segment.Add(BuildSegmentButton("Models", !_showsFiles, false, "mh-segment__button--first"));
            segment.Add(BuildSegmentButton("Files", _showsFiles, true, "mh-segment__button--last"));
            var section = UiKit.Section("Map", trailing: segment);
            section.AddToClassList("du-map");
            _treemap = UiKit.Element("mh-treemap", "du-treemap");
            _treemap.RegisterCallback<GeometryChangedEvent>(_ => LayoutTreemap());
            section.Add(_treemap);
            _content.Add(section);

            _card = UiKit.Element("hf-card", "mh-cache-card");
            _content.Add(_card);
            RebuildCard();
        }

        private Button BuildSegmentButton(string text, bool isActive, bool showsFiles, string edgeClass)
        {
            var button = new Button(() =>
            {
                if (_showsFiles == showsFiles)
                {
                    return;
                }

                _showsFiles = showsFiles;
                Rebuild();
            })
            {
                text = text,
                tooltip = showsFiles ? "One tile per file, grouped by model" : "One tile per model"
            };
            button.AddToClassList("mh-segment__button");
            button.AddToClassList(edgeClass);
            button.EnableInClassList("mh-segment__button--active", isActive);
            return button;
        }

        private static VisualElement BuildLegend(CacheOverview overview, HashSet<string> projectRepositories)
        {
            var legend = UiKit.Element("mh-legend");
            var used = overview.Repositories.Where(repository => CacheTiles.Classify(repository, projectRepositories) == CacheTileKind.Used).ToArray();
            var unused = overview.Repositories.Where(repository => CacheTiles.Classify(repository, projectRepositories) == CacheTileKind.Unused).ToArray();
            if (used.Length > 0)
            {
                legend.Add(BuildLegendItem("mh-tile--used", "Used by this project", used.Length, used.Sum(repository => repository.SizeInBytes)));
            }

            if (unused.Length > 0)
            {
                legend.Add(BuildLegendItem("mh-tile--unused", "Not used by this project", unused.Length, unused.Sum(repository => repository.SizeInBytes)));
            }

            if (overview.PartialSizeInBytes > 0)
            {
                var partialCount = overview.Repositories.Sum(repository => repository.Files.Count(file => file.IsPartial));
                legend.Add(BuildLegendItem("mh-tile--partial", "Partial downloads", partialCount, overview.PartialSizeInBytes));
            }

            return legend;
        }

        private static VisualElement BuildLegendItem(string kindClass, string label, int count, long bytes)
        {
            var item = UiKit.Element("mh-legend__item");
            item.Add(UiKit.Element("mh-legend__swatch", kindClass));
            item.Add(UiKit.Text(label));
            item.Add(UiKit.Text($"{count} · {UiKit.FormatBytes(bytes)}", "mh-legend__count"));
            return item;
        }

        // ---------------------------------------------------------------- Treemap

        private void LayoutTreemap()
        {
            if (_treemap == null || _overview == null)
            {
                return;
            }

            _treemap.Clear();
            var bounds = _treemap.contentRect;
            if (bounds.width <= 0f || bounds.height <= 0f)
            {
                return;
            }

            var projectRepositories = GetProjectRepositories();
            var repositories = _overview.Repositories.Where(repository => repository.TotalSizeInBytes > 0).ToArray();
            var rects = TreemapLayout.Squarify(
                repositories.Select(repository => (double)repository.TotalSizeInBytes).ToArray(),
                new Rect(0f, 0f, bounds.width, bounds.height));
            for (var index = 0; index < repositories.Length; index++)
            {
                if (rects[index].width < 1f || rects[index].height < 1f)
                {
                    continue;
                }

                var kind = CacheTiles.Classify(repositories[index], projectRepositories);
                _treemap.Add(_showsFiles
                    ? BuildRepositoryGroup(repositories[index], kind, rects[index])
                    : BuildRepositoryTile(repositories[index], kind, rects[index]));
            }
        }

        private VisualElement BuildRepositoryTile(CacheRepositoryEntry repository, CacheTileKind kind, Rect rect)
        {
            var tile = BuildTile(rect, CacheTiles.GetKindClass(kind));
            tile.EnableInClassList("mh-tile--selected",
                string.Equals(repository.FolderName, _selectedFolder, StringComparison.Ordinal));
            tile.tooltip = BuildRepositoryTooltip(repository, kind);
            if (repository.HasPartialDownloads && kind != CacheTileKind.Partial)
            {
                var strip = UiKit.Element("mh-tile__partial");
                strip.style.height = Math.Max(3f, rect.height * repository.PartialSizeInBytes / repository.TotalSizeInBytes);
                tile.Add(strip);
            }

            AddTileLabels(tile, rect, GetDisplayName(repository), CacheTiles.BuildSizeText(repository));
            tile.RegisterCallback<ClickEvent>(_ => Select(repository.FolderName, null));
            return tile;
        }

        private VisualElement BuildRepositoryGroup(CacheRepositoryEntry repository, CacheTileKind kind, Rect rect)
        {
            var group = UiKit.Element("mh-tile-group");
            Position(group, rect);
            group.EnableInClassList("mh-tile-group--selected",
                string.Equals(repository.FolderName, _selectedFolder, StringComparison.Ordinal) && _selectedFilePath == null);
            group.tooltip = BuildRepositoryTooltip(repository, kind);

            var headerHeight = rect.height >= 40f && rect.width >= TileLabelMinWidth ? GroupHeaderHeight : 0f;
            if (headerHeight > 0f)
            {
                group.Add(UiKit.Text(GetDisplayName(repository), "mh-tile-group__name"));
            }

            group.RegisterCallback<ClickEvent>(_ => Select(repository.FolderName, null));

            var (files, restCount, restBytes) = CacheTiles.SplitFiles(repository);
            var weights = files.Select(file => (double)file.SizeInBytes).ToList();
            if (restBytes > 0)
            {
                weights.Add(restBytes);
            }

            var inner = new Rect(0f, headerHeight, Math.Max(0f, rect.width - 2f), Math.Max(0f, rect.height - headerHeight - 2f));
            var rects = TreemapLayout.Squarify(weights, inner);
            for (var index = 0; index < weights.Count; index++)
            {
                if (rects[index].width < 1f || rects[index].height < 1f)
                {
                    continue;
                }

                if (index >= files.Count)
                {
                    var rest = BuildTile(rects[index], CacheTiles.GetKindClass(kind));
                    rest.tooltip = $"{restCount} more files · {UiKit.FormatBytes(restBytes)}";
                    AddTileLabels(rest, rects[index], $"{restCount} more", UiKit.FormatBytes(restBytes));
                    group.Add(rest);
                    continue;
                }

                var file = files[index];
                var tile = BuildTile(rects[index], CacheTiles.GetKindClass(CacheTiles.ClassifyFile(file, kind)));
                tile.EnableInClassList("mh-tile--selected", string.Equals(file.Path, _selectedFilePath, StringComparison.Ordinal));
                tile.tooltip = $"{repository.Repository}\n{file.Name}{(file.IsPartial ? " (partial download)" : string.Empty)}\n{UiKit.FormatBytes(file.SizeInBytes)}";
                AddTileLabels(tile, rects[index], Path.GetFileName(file.Name), UiKit.FormatBytes(file.SizeInBytes));
                tile.RegisterCallback<ClickEvent>(evt =>
                {
                    evt.StopPropagation();
                    Select(repository.FolderName, file.Path);
                });
                group.Add(tile);
            }

            return group;
        }

        private static VisualElement BuildTile(Rect rect, string kindClass)
        {
            var tile = UiKit.Element("mh-tile", kindClass);
            Position(tile, rect);
            return tile;
        }

        private static void Position(VisualElement element, Rect rect)
        {
            element.style.left = rect.x;
            element.style.top = rect.y;
            element.style.width = rect.width;
            element.style.height = rect.height;
        }

        private static void AddTileLabels(VisualElement tile, Rect rect, string name, string size)
        {
            if (rect.width < TileLabelMinWidth || rect.height < TileLabelMinHeight)
            {
                return;
            }

            tile.Add(UiKit.Text(name, "mh-tile__name"));
            if (rect.height >= TileSizeLabelMinHeight && !string.IsNullOrWhiteSpace(size))
            {
                tile.Add(UiKit.Text(size, "mh-tile__size"));
            }
        }

        internal static string GetDisplayName(CacheRepositoryEntry repository)
        {
            var name = ModelStatus.GetModelName(repository.Repository);
            return repository.IsModel ? name : $"{name} ({repository.RepoType})";
        }

        internal static string BuildRepositoryTooltip(CacheRepositoryEntry repository, CacheTileKind kind)
        {
            return $"{repository.Repository}\n{CacheTiles.BuildSizeText(repository)}\n{CacheTiles.GetStatusLabel(kind)}";
        }

        private void Select(string folderName, string filePath)
        {
            _selectedFolder = folderName;
            _selectedFilePath = filePath;
            LayoutTreemap();
            RebuildCard();
        }

        // ---------------------------------------------------------------- Selection card

        private void RebuildCard()
        {
            if (_card == null)
            {
                return;
            }

            _card.Clear();
            var repository = GetSelectedRepository();
            if (repository == null)
            {
                _card.Add(UiKit.Text("Select a tile to see what it is and to remove it from the cache.", "hf-muted", "hf-wrap"));
                return;
            }

            var kind = CacheTiles.Classify(repository, GetProjectRepositories());
            var file = repository.Files.FirstOrDefault(entry => string.Equals(entry.Path, _selectedFilePath, StringComparison.Ordinal));

            var header = UiKit.Element("mh-cache-card__header");
            var title = UiKit.Text(repository.Repository, "hf-heading", "mh-cache-card__title");
            title.tooltip = repository.Repository;
            header.Add(title);
            var tone = kind switch
            {
                CacheTileKind.Used => UiTone.Info,
                CacheTileKind.Partial => UiTone.Warning,
                _ => UiTone.Neutral
            };
            header.Add(UiKit.Pill(tone, CacheTiles.GetStatusLabel(kind)));
            _card.Add(header);

            if (file != null)
            {
                _card.Add(UiKit.KeyValue("File", file.Name, file.Path));
                _card.Add(UiKit.KeyValue("Size", (UiKit.FormatBytes(file.SizeInBytes) ?? "—") + (file.IsPartial ? " · partial download" : string.Empty)));
            }
            else
            {
                _card.Add(UiKit.KeyValue(repository.HasPartialDownloads ? "Complete files" : "Size on disk", UiKit.FormatBytes(repository.SizeInBytes) ?? "—"));
                if (repository.HasPartialDownloads)
                {
                    var partialCount = repository.Files.Count(entry => entry.IsPartial);
                    _card.Add(UiKit.KeyValue("Partial downloads", $"{partialCount} · {UiKit.FormatBytes(repository.PartialSizeInBytes)}"));
                }

                _card.Add(UiKit.KeyValue("Revisions", repository.Refs.Count > 0 ? string.Join(", ", repository.Refs) : $"{repository.SnapshotCount} snapshot(s), no named revision"));
                if (!repository.IsModel)
                {
                    _card.Add(UiKit.KeyValue("Type", repository.RepoType));
                }
            }

            var actions = UiKit.Element("mh-actions");
            var item = kind == CacheTileKind.Used
                ? _report?.Items.FirstOrDefault(entry => string.Equals(entry.Repository, repository.Repository, StringComparison.OrdinalIgnoreCase))
                : null;
            var first = item != null
                ? UiKit.Button("Show in Model Hub", () => ModelHubWindow.OpenWindow(item.Repository, item.RequestedRevision))
                : repository.IsModel && repository.SnapshotCount > 0
                    ? UiKit.Button("Show in Model Cache", () => ModelHubWindow.OpenCachedModel(repository.FolderName))
                    : null;
            if (first != null)
            {
                first.AddToClassList("hf-button--first");
                actions.Add(first);
            }

            var reveal = UiKit.Button(UiKit.RevealInFileBrowserLabel, () => ProjectSettingsLinks.RevealPath(file?.Path ?? repository.FolderPath));
            if (first == null)
            {
                reveal.AddToClassList("hf-button--first");
            }

            actions.Add(reveal);
            actions.Add(UiKit.Spacer());

            var isSyncing = BackgroundSyncManager.IsRunning;
            var partials = CacheTiles.GetPartialsToDelete(repository, file);
            if (partials.Count > 0)
            {
                var deletePartials = UiKit.Button(
                    file != null ? "Delete Partial Download" : "Delete Partial Downloads",
                    () =>
                    {
                        if (ConfirmDeletePartials(repository, partials))
                        {
                            _selectedFilePath = null;
                            AfterCacheChange();
                        }
                    });
                deletePartials.SetEnabled(!isSyncing);
                actions.Add(deletePartials);
            }

            var remove = UiKit.Button("Remove from Cache", () =>
            {
                if (ConfirmRemoveRepository(repository, item != null))
                {
                    _selectedFolder = null;
                    _selectedFilePath = null;
                    AfterCacheChange();
                }
            }, "hf-button--danger");
            remove.SetEnabled(!isSyncing);
            remove.tooltip = isSyncing ? "Wait until the setup finishes." : "Delete this model with all its revisions from the cache.";
            actions.Add(remove);
            _card.Add(actions);
        }

        /// <summary>
        /// Refreshes the open Disk Usage window, for example after Model Hub changed the cache.
        /// </summary>
        internal static void RefreshOpenWindows()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<CacheDiskUsageWindow>())
            {
                window.Refresh();
            }
        }

        private void AfterCacheChange()
        {
            Refresh();
            ModelHubWindow.RefreshOpenWindows();
        }

        // ---------------------------------------------------------------- Removal

        /// <summary>
        /// Asks, then deletes a repository with all its revisions from the cache. Returns true when the cache changed.
        /// </summary>
        internal static bool ConfirmRemoveRepository(CacheRepositoryEntry repository, bool isUsedByProject)
        {
            var message =
                $"Delete {repository.Repository} ({UiKit.FormatBytes(repository.TotalSizeInBytes)}) from the shared Hugging Face cache on this machine?\n\n" +
                "All its revisions and partial downloads are deleted. Other Unity projects and Python tools that use this cache must download it again.";
            if (isUsedByProject)
            {
                message += "\n\nThis project uses this model. Its imported files stay usable; AI Model Hub asks to download the model again when it needs to update or convert it.";
            }

            if (!EditorUtility.DisplayDialog("Remove from Cache", message, "Delete", "Cancel"))
            {
                return false;
            }

            try
            {
                CacheOverviewService.RemoveRepository(repository);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                EditorUtility.DisplayDialog("Remove from Cache", exception.Message, "OK");
            }

            return true;
        }

        private static bool ConfirmDeletePartials(CacheRepositoryEntry repository, IReadOnlyList<CacheFileEntry> files)
        {
            var size = UiKit.FormatBytes(files.Sum(file => file.SizeInBytes));
            var what = files.Count == 1 ? $"the partial download of {files[0].Name}" : $"{files.Count} partial downloads";
            if (!EditorUtility.DisplayDialog(
                    "Delete Partial Downloads",
                    $"Delete {what} of {repository.Repository} ({size})?\n\nThese files are left over from interrupted downloads. A later download starts them from the beginning.",
                    "Delete",
                    "Cancel"))
            {
                return false;
            }

            try
            {
                foreach (var file in files)
                {
                    CacheOverviewService.DeletePartialFile(repository, file);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                EditorUtility.DisplayDialog("Delete Partial Downloads", exception.Message, "OK");
            }

            return true;
        }
    }
}
