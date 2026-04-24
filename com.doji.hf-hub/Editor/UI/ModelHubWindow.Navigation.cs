using System;
using System.Collections.Generic;
using System.Linq;

namespace Doji.AI.ModelHub.Editor
{
    internal sealed partial class ModelHubWindow
    {
        private bool TryChangeSelectedItem(IReadOnlyList<ModelHubItem> items, int nextIndex)
        {
            if (nextIndex == _selectedIndex)
            {
                return true;
            }

            var currentItem = GetCurrentSelectedItem(items);
            return TryResolvePendingFileEdits(currentItem);
        }

        private void TryChangeDetailTab(ModelHubItem item, DetailTab nextTab)
        {
            if (nextTab == _selectedDetailTab)
            {
                return;
            }

            if (!TryResolvePendingFileEdits(item))
            {
                return;
            }

            _selectedDetailTab = nextTab;
        }

        private ModelHubItem GetCurrentSelectedItem(IReadOnlyList<ModelHubItem> items)
        {
            return _selectedIndex >= 0 && _selectedIndex < items.Count
                ? items[_selectedIndex]
                : null;
        }

        private IReadOnlyList<ModelHubItem> GetFilteredItems()
        {
            IEnumerable<ModelHubItem> items = _report.Items;

            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                items = items.Where(item =>
                    item.Repository.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.Dependents.Any(dependent =>
                        dependent.Dependency.PackageDisplayName.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        dependent.Dependency.ModelId.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            return new List<ModelHubItem>(items);
        }

        private static int IndexOfItem(IReadOnlyList<ModelHubItem> items, ModelHubItem target)
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
                return Content.UnknownRepositoryPlaceholder;
            }

            var separatorIndex = repository.IndexOf('/');
            return separatorIndex > 0 ? repository.Substring(0, separatorIndex) : repository;
        }

        private static string GetRepositoryDisplayName(string repository)
        {
            if (string.IsNullOrWhiteSpace(repository))
            {
                return Content.UnknownRepositoryPlaceholder;
            }

            var separatorIndex = repository.IndexOf('/');
            return separatorIndex >= 0 && separatorIndex < repository.Length - 1
                ? repository.Substring(separatorIndex + 1)
                : repository;
        }

        private DetailTab[] GetAvailableTabs(ModelHubItem item)
        {
            var tabs = new List<DetailTab>
            {
                DetailTab.Summary,
                DetailTab.Dependents,
                DetailTab.Files
            };

            if (item != null &&
                (!string.IsNullOrWhiteSpace(item.RawSnapshotRootPath) ||
                 item.CacheSizeOnDiskBytes.HasValue ||
                 !string.IsNullOrWhiteSpace(item.RuntimeSnapshotAssetPath) ||
                 item.RuntimeSizeOnDiskBytes.HasValue))
            {
                tabs.Add(DetailTab.Storage);
            }

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

        private static bool IsDetailTabEnabled(DetailTab tab, ModelHubItem item)
        {
            return tab switch
            {
                DetailTab.Files => item != null && item.Files.Count > 0,
                _ => true
            };
        }

        private static string GetDetailTabLabel(DetailTab tab)
        {
            return tab switch
            {
                DetailTab.Summary => Content.SummarySection,
                DetailTab.Storage => Content.StorageSection,
                DetailTab.Dependents => Content.DependentsSection,
                DetailTab.Files => Content.FilesSection,
                DetailTab.Diagnostics => Content.DiagnosticsSection,
                DetailTab.DiscoveryIssues => Content.DiscoveryIssuesSection,
                _ => tab.ToString()
            };
        }

        private static string GetDetailTabTooltip(DetailTab tab, ModelHubItem item)
        {
            return tab switch
            {
                DetailTab.Files when item == null || item.Files.Count == 0 =>
                    Content.NoFilesTabTooltip,
                DetailTab.Files =>
                    Content.FilesTabTooltip,
                DetailTab.Storage =>
                    Content.StorageTabTooltip,
                _ => null
            };
        }
    }
}
