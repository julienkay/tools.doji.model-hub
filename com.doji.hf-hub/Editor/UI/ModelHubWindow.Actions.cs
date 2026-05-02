using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    internal sealed partial class ModelHubWindow
    {
        private void Refresh()
        {
            _report = EditorOperations.CreateModelHubReport();
            _lastRefreshLocalTime = DateTime.Now;
            Repaint();
        }

        private void HandleSyncStateChanged()
        {
            Refresh();
        }

        private void ResetFilters()
        {
            _searchText = string.Empty;
            _selectedIndex = -1;
            _selectedDetailTab = DetailTab.Summary;
        }

        private string GetLastRefreshLabel()
        {
            if (_lastRefreshLocalTime == default)
            {
                return string.Empty;
            }

            return string.Format(Content.LastRefreshFormat, _lastRefreshLocalTime.ToString("MMM d HH:mm"));
        }

        private GUIContent GetImportActionContent(ModelHubItem item, bool isSyncRunning = false)
        {
            if (item != null && item.HasRevisionConflict)
            {
                return new GUIContent(Content.ImportLabel, Content.RevisionConflictActionTooltip);
            }

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

        private static void LocateManifest(ModelHubItem item)
        {
            var targets = ProjectSettingsLinks.BuildManifestLocateTargets(item);
            if (targets.Count == 0)
            {
                return;
            }

            if (targets.Count == 1)
            {
                ProjectSettingsLinks.LocateManifest(targets[0].ManifestPath);
                return;
            }

            var menu = new GenericMenu();
            foreach (var target in targets)
            {
                menu.AddItem(
                    new GUIContent(target.Label),
                    false,
                    () => ProjectSettingsLinks.LocateManifest(target.ManifestPath));
            }

            menu.ShowAsContext();
        }

        private void ShowHeaderAddMenu(Rect buttonRect)
        {
            var localPackages = LocalPackageManifestStore.GetProjectLocalPackages();
            var menu = new GenericMenu();
            menu.AddItem(
                new GUIContent(Content.AddModelToProjectLabel, Content.AddModelToProjectTooltip),
                false,
                ShowAddProjectModelPopup);
            if (localPackages.Count > 0)
            {
                menu.AddItem(
                    new GUIContent(Content.AddModelToPackageLabel, Content.AddModelToPackageTooltip),
                    false,
                    ShowAddPackageModelPopup);
            }
            else
            {
                menu.AddDisabledItem(new GUIContent(Content.AddModelToPackageLabel, Content.AddModelToPackageDisabledTooltip));
            }
            menu.DropDown(buttonRect);
        }

        private void ShowHeaderMoreMenu(Rect buttonRect)
        {
            var menu = new GenericMenu();
            menu.AddItem(
                new GUIContent(Content.ImportAllLabel, Content.ImportAllTooltip),
                false,
                () =>
                {
                    EditorOperations.StartBackgroundSync();
                    Refresh();
                });
            menu.DropDown(buttonRect);
        }

        private void ShowManageMenu(ModelHubItem item, Rect buttonRect)
        {
            if (item == null)
            {
                return;
            }

            var hasCache = HasCacheForCurrentItem(item);
            var entries = ManageMenuPopup.BuildEntries(
                hasCache,
                () =>
                {
                    EditorOperations.RemoveImportedArtifacts(item);
                    Refresh();
                },
                () =>
                {
                    EditorOperations.RemoveRawCache(item);
                    Refresh();
                },
                Content.RemoveLabel,
                Content.RemoveTooltip,
                Content.RemoveFromCacheLabel,
                hasCache ? Content.RemoveFromCacheTooltip : Content.RemoveFromCacheDisabledTooltip);

            PopupWindow.Show(
                buttonRect,
                new ManageMenuPopup(entries));
        }

        private void ShowAddProjectModelPopup()
        {
            var buttonRect = _headerAddButtonRect;
            EditorApplication.delayCall += () =>
            {
                if (this == null)
                {
                    return;
                }

                PopupWindow.Show(
                    buttonRect,
                    new AddProjectModelPopup(AddModelToProjectManifest));
            };
        }

        private void ShowAddPackageModelPopup()
        {
            var packages = LocalPackageManifestStore.GetProjectLocalPackages();
            if (packages.Count == 0)
            {
                return;
            }

            var buttonRect = _headerAddButtonRect;
            EditorApplication.delayCall += () =>
            {
                if (this == null)
                {
                    return;
                }

                PopupWindow.Show(
                    buttonRect,
                    new AddPackageModelPopup(
                        packages.ToArray(),
                        AddModelToPackageManifest));
            };
        }

        private void AddModelToProjectManifest(string owner, string modelName)
        {
            if (ProjectSettingsModelManifestStore.TryAddModel(owner, modelName, out var errorMessage))
            {
                Refresh();
                ProjectSettingsProvider.Refresh();
                InternalEditorUtility.RepaintAllViews();
                return;
            }

            EditorUtility.DisplayDialog("HF Hub Project Manifest", errorMessage, "OK");
        }

        private void AddModelToPackageManifest(LocalPackageManifestStore.LocalPackageOption package, string owner, string modelName)
        {
            if (LocalPackageManifestStore.TryAddModel(package, owner, modelName, out var errorMessage))
            {
                Refresh();
                return;
            }

            EditorUtility.DisplayDialog("HF Hub Package Manifest", errorMessage, "OK");
        }

        private static bool HasCacheForCurrentItem(ModelHubItem item)
        {
            if (item == null)
            {
                return false;
            }

            var revisionRefPath = CachePaths.GetRevisionRefPath(item.Repository, item.RequestedRevision);
            if (File.Exists(revisionRefPath))
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(item.RawSnapshotRootPath) &&
                Directory.Exists(item.RawSnapshotRootPath);
        }
    }
}
