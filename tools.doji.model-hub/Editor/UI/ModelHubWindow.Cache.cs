using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Cache data for the Model Cache view: the scanned cache, row keys, the selected cached repository, and list rows.
    /// The disk usage map is <see cref="CacheDiskUsageWindow"/>.
    /// </summary>
    internal sealed partial class ModelHubWindow
    {
        private const string CacheRowKeyPrefix = "cache:";

        [SerializeField] private string _selectedCacheFolder;

        private CacheOverview _cacheOverview;

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
        }

        private static string BuildCacheRowKey(string folderName)
        {
            return string.IsNullOrEmpty(folderName) ? null : CacheRowKeyPrefix + folderName;
        }

        private static string GetCacheFolder(string key)
        {
            return key != null && key.StartsWith(CacheRowKeyPrefix, StringComparison.Ordinal)
                ? key.Substring(CacheRowKeyPrefix.Length)
                : null;
        }

        private HashSet<string> GetProjectRepositories()
        {
            return CacheTiles.CreateProjectRepositorySet((_report?.Items ?? Array.Empty<ModelHubItem>()).Select(item => item.Repository));
        }

        private CacheRepositoryEntry GetSelectedCacheRepository()
        {
            return _cacheOverview?.Repositories.FirstOrDefault(repository =>
                string.Equals(repository.FolderName, _selectedCacheFolder, StringComparison.Ordinal));
        }

        private VisualElement BuildCacheListRow(CacheRepositoryEntry repository, CacheTileKind kind)
        {
            var key = BuildCacheRowKey(repository.FolderName);
            var row = UiKit.Element("mh-item");
            row.tooltip = CacheDiskUsageWindow.BuildRepositoryTooltip(repository, kind);
            row.Add(UiKit.Element("mh-legend__swatch", "mh-item__swatch", CacheTiles.GetKindClass(kind)));
            var text = UiKit.Element("mh-item__text");
            text.Add(UiKit.Text(CacheDiskUsageWindow.GetDisplayName(repository), "mh-item__name"));
            var owner = repository.Repository?.Contains("/") == true
                ? repository.Repository.Substring(0, repository.Repository.IndexOf('/'))
                : null;
            var partial = repository.HasPartialDownloads ? "partial downloads" : null;
            text.Add(UiKit.Text(
                string.Join(" · ", new[] { owner, partial }.Where(part => !string.IsNullOrWhiteSpace(part))),
                "mh-item__subtitle"));
            row.Add(text);
            var size = UiKit.FormatBytes(repository.TotalSizeInBytes);
            if (!string.IsNullOrWhiteSpace(size))
            {
                row.Add(UiKit.Text(size, "mh-item__meta"));
            }

            row.RegisterCallback<ClickEvent>(_ => SelectCacheEntry(repository.FolderName));
            _rowsByKey[key] = row;
            return row;
        }

        private void SelectCacheEntry(string folderName)
        {
            _selectedCacheFolder = folderName;
            RebuildDetails();
            UpdateRowSelection();
            var key = BuildCacheRowKey(folderName);
            if (key != null && _rowsByKey.TryGetValue(key, out var row) && _list.contentContainer.Contains(row))
            {
                _list.ScrollTo(row);
            }
        }
    }
}
