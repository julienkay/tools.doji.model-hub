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
    /// Cache overview: a treemap of the whole shared Hugging Face cache, per repository or per file, with the
    /// models this project uses, models it does not use, and partial downloads, plus Remove from Cache.
    /// </summary>
    internal sealed partial class ModelHubWindow
    {
        private const string CacheKey = "cache";
        private const float TileLabelMinWidth = 56f;
        private const float TileLabelMinHeight = 30f;
        private const float TileSizeLabelMinHeight = 44f;
        private const float GroupHeaderHeight = 16f;

        [SerializeField] private bool _cacheShowsFiles;
        [SerializeField] private string _selectedCacheFolder;
        [SerializeField] private string _selectedCacheFilePath;

        private CacheOverview _cacheOverview;
        private VisualElement _cacheRow;
        private Label _cacheRowMeta;
        private VisualElement _cacheTreemap;
        private VisualElement _cacheCard;

        // ---------------------------------------------------------------- Data

        private void RefreshCacheOverview()
        {
            try
            {
                _cacheOverview = CacheOverviewService.Scan();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Debug.LogWarning($"AI Model Hub could not read the download cache: {exception.Message}");
                _cacheOverview = new CacheOverview { CacheRoot = CachePaths.GetCacheRoot() };
            }

            if (_cacheRowMeta != null)
            {
                _cacheRowMeta.text = UiKit.FormatBytes(_cacheOverview.TotalSizeInBytes) ?? string.Empty;
            }
        }

        private HashSet<string> GetProjectRepositories()
        {
            return CacheTiles.CreateProjectRepositorySet((_report?.Items ?? Array.Empty<ModelHubItem>()).Select(item => item.Repository));
        }

        private static string GetDisplayName(CacheRepositoryEntry repository)
        {
            var name = ModelStatus.GetModelName(repository.Repository);
            return repository.IsModel ? name : $"{name} ({repository.RepoType})";
        }

        private CacheRepositoryEntry GetSelectedCacheRepository()
        {
            return _cacheOverview?.Repositories.FirstOrDefault(repository =>
                string.Equals(repository.FolderName, _selectedCacheFolder, StringComparison.Ordinal));
        }

        // ---------------------------------------------------------------- List row

        private VisualElement BuildCacheRow()
        {
            var container = UiKit.Element("mh-cache-row-container");
            _cacheRow = UiKit.Element("mh-item");
            _cacheRow.tooltip = "Everything in the Hugging Face cache on this machine";
            _cacheRow.Add(UiKit.Icon("Folder Icon", "mh-item__icon"));
            var text = UiKit.Element("mh-item__text");
            text.Add(UiKit.Text("Download cache", "mh-item__name"));
            text.Add(UiKit.Text("Shared with Python and other projects", "mh-item__subtitle"));
            _cacheRow.Add(text);
            _cacheRowMeta = UiKit.Text(UiKit.FormatBytes(_cacheOverview?.TotalSizeInBytes) ?? string.Empty, "mh-item__meta");
            _cacheRow.Add(_cacheRowMeta);
            _cacheRow.RegisterCallback<ClickEvent>(_ => Select(CacheKey));
            container.Add(_cacheRow);
            return container;
        }

        // ---------------------------------------------------------------- Details

        private void BuildCacheDetails(VisualElement content)
        {
            var overview = _cacheOverview ?? new CacheOverview { CacheRoot = CachePaths.GetCacheRoot() };
            var projectRepositories = GetProjectRepositories();

            var header = UiKit.Element("mh-header");
            var headerText = UiKit.Element("mh-header__text");
            headerText.Add(UiKit.Text("Download cache", "hf-title"));
            var subtitle = UiKit.Element("mh-header__subtitle");
            var count = overview.Repositories.Count;
            subtitle.Add(UiKit.Text($"{UiKit.FormatBytes(overview.TotalSizeInBytes)} · {count} {(count == 1 ? "repository" : "repositories")}"));
            subtitle.Add(UiKit.LinkButton(UiKit.RevealInFileBrowserLabel, () => ProjectSettingsLinks.RevealPath(overview.CacheRoot), overview.CacheRoot));
            headerText.Add(subtitle);
            header.Add(headerText);
            content.Add(header);
            content.Add(UiKit.Text(
                "The Hugging Face cache on this machine. Other Unity projects and Python tools that use huggingface_hub share these downloads.",
                "hf-section__hint", "mh-cache-hint"));

            if (count == 0)
            {
                content.Add(UiKit.EmptyState("Folder Icon", "The cache is empty", "Models appear here once they are downloaded."));
                return;
            }

            content.Add(BuildCacheLegend(overview, projectRepositories));

            var segment = UiKit.Element("mh-segment");
            segment.Add(BuildSegmentButton("Models", !_cacheShowsFiles, false, "mh-segment__button--first"));
            segment.Add(BuildSegmentButton("Files", _cacheShowsFiles, true, "mh-segment__button--last"));
            var section = UiKit.Section("Map", trailing: segment);
            _cacheTreemap = UiKit.Element("mh-treemap");
            _cacheTreemap.RegisterCallback<GeometryChangedEvent>(_ => LayoutCacheTreemap());
            section.Add(_cacheTreemap);
            content.Add(section);

            _cacheCard = UiKit.Element("hf-card", "mh-cache-card");
            content.Add(_cacheCard);
            RebuildCacheCard();
        }

        private Button BuildSegmentButton(string text, bool isActive, bool showsFiles, string edgeClass)
        {
            var button = new Button(() =>
            {
                if (_cacheShowsFiles == showsFiles)
                {
                    return;
                }

                _cacheShowsFiles = showsFiles;
                RebuildDetails();
            })
            {
                text = text,
                tooltip = showsFiles ? "One tile per file, grouped by repository" : "One tile per repository"
            };
            button.AddToClassList("mh-segment__button");
            button.AddToClassList(edgeClass);
            button.EnableInClassList("mh-segment__button--active", isActive);
            return button;
        }

        private static VisualElement BuildCacheLegend(CacheOverview overview, HashSet<string> projectRepositories)
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

        private void LayoutCacheTreemap()
        {
            if (_cacheTreemap == null || _cacheOverview == null)
            {
                return;
            }

            _cacheTreemap.Clear();
            var bounds = _cacheTreemap.contentRect;
            if (bounds.width <= 0f || bounds.height <= 0f)
            {
                return;
            }

            var projectRepositories = GetProjectRepositories();
            var repositories = _cacheOverview.Repositories.Where(repository => repository.TotalSizeInBytes > 0).ToArray();
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
                _cacheTreemap.Add(_cacheShowsFiles
                    ? BuildRepositoryGroup(repositories[index], kind, rects[index])
                    : BuildRepositoryTile(repositories[index], kind, rects[index]));
            }
        }

        private VisualElement BuildRepositoryTile(CacheRepositoryEntry repository, CacheTileKind kind, Rect rect)
        {
            var tile = BuildTile(rect, CacheTiles.GetKindClass(kind));
            tile.EnableInClassList("mh-tile--selected",
                string.Equals(repository.FolderName, _selectedCacheFolder, StringComparison.Ordinal));
            tile.tooltip = BuildRepositoryTooltip(repository, kind);
            if (repository.HasPartialDownloads && kind != CacheTileKind.Partial)
            {
                var strip = UiKit.Element("mh-tile__partial");
                strip.style.height = Math.Max(3f, rect.height * repository.PartialSizeInBytes / repository.TotalSizeInBytes);
                tile.Add(strip);
            }

            AddTileLabels(tile, rect, GetDisplayName(repository), CacheTiles.BuildSizeText(repository));
            tile.RegisterCallback<ClickEvent>(_ => SelectCacheEntry(repository.FolderName, null));
            return tile;
        }

        private VisualElement BuildRepositoryGroup(CacheRepositoryEntry repository, CacheTileKind kind, Rect rect)
        {
            var group = UiKit.Element("mh-tile-group");
            Position(group, rect);
            group.EnableInClassList("mh-tile-group--selected",
                string.Equals(repository.FolderName, _selectedCacheFolder, StringComparison.Ordinal) && _selectedCacheFilePath == null);
            group.tooltip = BuildRepositoryTooltip(repository, kind);

            var headerHeight = rect.height >= 40f && rect.width >= TileLabelMinWidth ? GroupHeaderHeight : 0f;
            if (headerHeight > 0f)
            {
                group.Add(UiKit.Text(GetDisplayName(repository), "mh-tile-group__name"));
            }

            group.RegisterCallback<ClickEvent>(_ => SelectCacheEntry(repository.FolderName, null));

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
                tile.EnableInClassList("mh-tile--selected", string.Equals(file.Path, _selectedCacheFilePath, StringComparison.Ordinal));
                tile.tooltip = $"{repository.Repository}\n{file.Name}{(file.IsPartial ? " (partial download)" : string.Empty)}\n{UiKit.FormatBytes(file.SizeInBytes)}";
                AddTileLabels(tile, rects[index], Path.GetFileName(file.Name), UiKit.FormatBytes(file.SizeInBytes));
                tile.RegisterCallback<ClickEvent>(evt =>
                {
                    evt.StopPropagation();
                    SelectCacheEntry(repository.FolderName, file.Path);
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

        private static string BuildRepositoryTooltip(CacheRepositoryEntry repository, CacheTileKind kind)
        {
            return $"{repository.Repository}\n{CacheTiles.BuildSizeText(repository)}\n{CacheTiles.GetStatusLabel(kind)}";
        }

        private void SelectCacheEntry(string folderName, string filePath)
        {
            _selectedCacheFolder = folderName;
            _selectedCacheFilePath = filePath;
            LayoutCacheTreemap();
            RebuildCacheCard();
        }

        // ---------------------------------------------------------------- Selection card

        private void RebuildCacheCard()
        {
            if (_cacheCard == null)
            {
                return;
            }

            _cacheCard.Clear();
            var repository = GetSelectedCacheRepository();
            if (repository == null)
            {
                _cacheCard.Add(UiKit.Text("Select a tile to see what it is and to remove it from the cache.", "hf-muted", "hf-wrap"));
                return;
            }

            var kind = CacheTiles.Classify(repository, GetProjectRepositories());
            var file = repository.Files.FirstOrDefault(entry => string.Equals(entry.Path, _selectedCacheFilePath, StringComparison.Ordinal));

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
            _cacheCard.Add(header);

            if (file != null)
            {
                _cacheCard.Add(UiKit.KeyValue("File", file.Name, file.Path));
                _cacheCard.Add(UiKit.KeyValue("Size", (UiKit.FormatBytes(file.SizeInBytes) ?? "—") + (file.IsPartial ? " · partial download" : string.Empty)));
            }
            else
            {
                _cacheCard.Add(UiKit.KeyValue(repository.HasPartialDownloads ? "Complete files" : "Size on disk", UiKit.FormatBytes(repository.SizeInBytes) ?? "—"));
                if (repository.HasPartialDownloads)
                {
                    var partialCount = repository.Files.Count(entry => entry.IsPartial);
                    _cacheCard.Add(UiKit.KeyValue("Partial downloads", $"{partialCount} · {UiKit.FormatBytes(repository.PartialSizeInBytes)}"));
                }

                _cacheCard.Add(UiKit.KeyValue("Revisions", repository.Refs.Count > 0 ? string.Join(", ", repository.Refs) : $"{repository.SnapshotCount} snapshot(s), no named revision"));
                if (!repository.IsModel)
                {
                    _cacheCard.Add(UiKit.KeyValue("Type", repository.RepoType));
                }
            }

            var actions = UiKit.Element("mh-actions");
            var item = kind == CacheTileKind.Used
                ? _report?.Items.FirstOrDefault(entry => string.Equals(entry.Repository, repository.Repository, StringComparison.OrdinalIgnoreCase))
                : null;
            var isFirst = true;
            void AddAction(Button button)
            {
                if (isFirst)
                {
                    button.AddToClassList("hf-button--first");
                    isFirst = false;
                }

                actions.Add(button);
            }

            if (item != null)
            {
                AddAction(UiKit.Button("Show in Model Hub", () => Select(BuildItemKey(item))));
            }

            AddAction(UiKit.Button(UiKit.RevealInFileBrowserLabel, () => ProjectSettingsLinks.RevealPath(file?.Path ?? repository.FolderPath)));
            actions.Add(UiKit.Spacer());

            var isSyncing = BackgroundSyncManager.IsRunning;
            var partials = CacheTiles.GetPartialsToDelete(repository, file);
            if (partials.Count > 0)
            {
                var deletePartials = UiKit.Button(
                    file != null ? "Delete Partial Download" : "Delete Partial Downloads",
                    () => ConfirmDeletePartials(repository, partials));
                deletePartials.SetEnabled(!isSyncing);
                actions.Add(deletePartials);
            }

            var remove = UiKit.Button("Remove from Cache", () => ConfirmRemoveCacheRepository(repository, item != null), "hf-button--danger");
            remove.SetEnabled(!isSyncing);
            remove.tooltip = isSyncing ? "Wait until the setup finishes." : "Delete this repository with all its revisions from the cache.";
            actions.Add(remove);
            _cacheCard.Add(actions);
        }

        private void ConfirmRemoveCacheRepository(CacheRepositoryEntry repository, bool isUsedByProject)
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
                return;
            }

            try
            {
                CacheOverviewService.RemoveRepository(repository);
                _selectedCacheFolder = null;
                _selectedCacheFilePath = null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                EditorUtility.DisplayDialog("Remove from Cache", exception.Message, "OK");
            }

            Refresh();
        }

        private void ConfirmDeletePartials(CacheRepositoryEntry repository, IReadOnlyList<CacheFileEntry> files)
        {
            var size = UiKit.FormatBytes(files.Sum(file => file.SizeInBytes));
            var what = files.Count == 1 ? $"the partial download of {files[0].Name}" : $"{files.Count} partial downloads";
            if (!EditorUtility.DisplayDialog(
                    "Delete Partial Downloads",
                    $"Delete {what} of {repository.Repository} ({size})?\n\nThese files are left over from interrupted downloads. A later download starts them from the beginning.",
                    "Delete",
                    "Cancel"))
            {
                return;
            }

            try
            {
                foreach (var file in files)
                {
                    CacheOverviewService.DeletePartialFile(repository, file);
                }

                _selectedCacheFilePath = null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                EditorUtility.DisplayDialog("Delete Partial Downloads", exception.Message, "OK");
            }

            Refresh();
        }
    }
}
