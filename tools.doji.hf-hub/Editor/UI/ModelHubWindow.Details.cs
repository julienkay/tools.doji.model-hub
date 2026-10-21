using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Details pane: header, status, tabs, overview content, manifest errors, and the empty state.
    /// </summary>
    internal sealed partial class ModelHubWindow
    {
        private ScrollView _detailsScroll;

        private void RebuildDetails()
        {
            if (_detailsPane == null)
            {
                return;
            }

            var previousScrollOffset = _detailsScroll?.scrollOffset ?? Vector2.zero;
            var previousKey = _detailsScroll?.userData as string;
            _detailsPane.Clear();
            _detailsScroll = null;
            _applyBar = null;

            var item = GetSelectedItem();
            var issue = item == null ? GetSelectedIssue() : null;
            if (item == null && issue == null)
            {
                _detailsPane.Add(BuildEmptyState());
                return;
            }

            _detailsScroll = new ScrollView(ScrollViewMode.Vertical) { userData = _selectedKey };
            _detailsScroll.AddToClassList("mh-details-scroll");
            _detailsPane.Add(_detailsScroll);

            if (issue != null)
            {
                BuildIssueDetails(_detailsScroll, issue);
            }
            else
            {
                BuildItemDetails(_detailsScroll, item);
            }

            if (string.Equals(previousKey, _selectedKey, StringComparison.Ordinal))
            {
                var scroll = _detailsScroll;
                scroll.schedule.Execute(() => scroll.scrollOffset = previousScrollOffset);
            }
        }

        private VisualElement BuildEmptyState()
        {
            if (_report != null && (_report.Items.Count > 0 || _report.DiscoveryIssues.Count > 0))
            {
                return UiKit.EmptyState("Search Icon", "No matches", "Try a different search.");
            }

            Button addButton = null;
            addButton = UiKit.PrimaryButton("Add Model", () => ShowAddModelPopup(addButton.worldBound));
            return UiKit.EmptyState(
                "Package Manager",
                "No AI models yet",
                "Packages list the AI models they need in a manifest, and the models show up here. You can also add a Hugging Face model to this project yourself.",
                addButton,
                UiKit.Button("Learn More", () => Application.OpenURL(DocumentationUrl)));
        }

        // ---------------------------------------------------------------- Model details

        private void BuildItemDetails(VisualElement container, ModelHubItem item)
        {
            var status = ModelStatus.From(item);
            container.Add(BuildHeader(item, status));
            container.Add(BuildActions(item, status));

            var statusContainer = UiKit.Element("mh-status");
            foreach (var callout in BuildStatusCallouts(item, status))
            {
                statusContainer.Add(callout);
            }

            if (statusContainer.childCount > 0)
            {
                container.Add(statusContainer);
            }

            var tabs = GetAvailableTabs(item);
            if (!tabs.Contains(_selectedTab))
            {
                _selectedTab = DetailTab.Overview;
            }

            if (tabs.Count > 1)
            {
                container.Add(BuildTabStrip(item, tabs));
            }

            var content = UiKit.Element("mh-tab-content");
            container.Add(content);
            switch (_selectedTab)
            {
                case DetailTab.Files:
                    BuildFilesTab(content, item);
                    break;
                case DetailTab.Storage:
                    BuildStorageTab(content, item);
                    break;
                default:
                    BuildOverviewTab(content, item);
                    break;
            }
        }

        private VisualElement BuildHeader(ModelHubItem item, ModelStatus status)
        {
            var header = UiKit.Element("mh-header");
            var text = UiKit.Element("mh-header__text");
            text.Add(UiKit.Text(ModelStatus.GetModelName(item.Repository), "hf-title"));

            var subtitle = UiKit.Element("mh-header__subtitle");
            subtitle.Add(UiKit.LinkButton(
                item.Repository,
                () => Application.OpenURL($"https://huggingface.co/{item.Repository}"),
                "Open on Hugging Face"));
            subtitle.Add(UiKit.Tag(
                ManifestRevision.NormalizeForResolution(item.RequestedRevision),
                string.IsNullOrWhiteSpace(item.ResolvedCommitHash)
                    ? "Requested revision"
                    : $"Requested revision. Resolved commit: {item.ResolvedCommitHash}"));
            text.Add(subtitle);
            header.Add(text);
            header.Add(UiKit.Pill(status.Tone, status.Label, status.Description));
            return header;
        }

        private VisualElement BuildActions(ModelHubItem item, ModelStatus status)
        {
            var actions = UiKit.Element("mh-actions");
            var isSyncRunning = BackgroundSyncManager.IsRunning;

            if (status.Action != ModelActionKind.None)
            {
                var label = status.ActionLabel;
                var size = UiKit.FormatBytes(item.SourceSizeBytes);
                if ((status.Kind is ModelStatusKind.DownloadNeeded or ModelStatusKind.RepairNeeded) && !string.IsNullOrWhiteSpace(size))
                {
                    label = $"{label}  ·  {size}";
                }

                var primary = UiKit.PrimaryButton(label, () => RunPrimaryAction(item, status), "hf-button--first");
                if (status.Action == ModelActionKind.Sync)
                {
                    primary.SetEnabled(!isSyncRunning);
                    primary.tooltip = isSyncRunning ? "Wait for the running import to finish." : status.Description;
                }

                actions.Add(primary);
            }

            Button manifestButton = null;
            manifestButton = UiKit.Button("Show Manifest", () => LocateManifest(item, manifestButton.worldBound));
            manifestButton.tooltip = "Select the manifest that declares this model.";
            if (status.Action == ModelActionKind.None)
            {
                manifestButton.AddToClassList("hf-button--first");
            }

            actions.Add(manifestButton);

            Button moreButton = null;
            moreButton = UiKit.IconButton("_Menu", "More actions", () => ShowItemMenu(item, moreButton.worldBound));
            actions.Add(moreButton);
            return actions;
        }

        private void RunPrimaryAction(ModelHubItem item, ModelStatus status)
        {
            if (status.Action == ModelActionKind.ChooseFiles)
            {
                ChangeTab(item, DetailTab.Overview);
                return;
            }

            EditorOperations.StartBackgroundSync(item);
            Refresh();
        }

        private void ShowItemMenu(ModelHubItem item, Rect buttonRect)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Open on Hugging Face"), false, () => Application.OpenURL($"https://huggingface.co/{item.Repository}"));
            menu.AddItem(new GUIContent("Copy Model ID"), false, () => EditorGUIUtility.systemCopyBuffer = item.Repository);
            menu.AddSeparator(string.Empty);

            var canRemove = !item.HasRevisionConflict && !BackgroundSyncManager.IsRunning;
            if (canRemove && item.HasImportedArtifacts)
            {
                menu.AddItem(new GUIContent("Remove Imported Files"), false, () => ConfirmRemoveImported(item));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Remove Imported Files"));
            }

            if (canRemove && HasCache(item))
            {
                menu.AddItem(new GUIContent("Remove from Cache"), false, () => ConfirmRemoveCache(item));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Remove from Cache"));
            }

            menu.DropDown(buttonRect);
        }

        private void ConfirmRemoveImported(ModelHubItem item)
        {
            if (!EditorUtility.DisplayDialog(
                    "Remove Imported Files",
                    $"Remove the imported files of {item.Repository} from this project?\n\nThe downloaded files stay in the cache, so you can import the model again without downloading it.",
                    "Remove",
                    "Cancel"))
            {
                return;
            }

            EditorOperations.RemoveImportedArtifacts(item);
            Refresh();
        }

        private void ConfirmRemoveCache(ModelHubItem item)
        {
            if (!EditorUtility.DisplayDialog(
                    "Remove from Cache",
                    $"Delete the downloaded files of {item.Repository} from the cache on this machine?\n\nImported files in this project stay usable. Other projects that use this model must download it again.",
                    "Delete",
                    "Cancel"))
            {
                return;
            }

            EditorOperations.RemoveRawCache(item);
            Refresh();
        }

        private static bool HasCache(ModelHubItem item)
        {
            if (item == null)
            {
                return false;
            }

            return System.IO.File.Exists(CachePaths.GetRevisionRefPath(item.Repository, item.RequestedRevision)) ||
                (!string.IsNullOrWhiteSpace(item.RawSnapshotRootPath) && System.IO.Directory.Exists(item.RawSnapshotRootPath));
        }

        private static void LocateManifest(ModelHubItem item, Rect buttonRect)
        {
            var targets = ProjectSettingsLinks.BuildManifestLocateTargets(item);
            if (targets.Count == 1)
            {
                ProjectSettingsLinks.LocateManifest(targets[0]);
                return;
            }

            var menu = new GenericMenu();
            foreach (var target in targets)
            {
                menu.AddItem(new GUIContent(target.Label), false, () => ProjectSettingsLinks.LocateManifest(target));
            }

            menu.DropDown(buttonRect);
        }

        private static IEnumerable<VisualElement> BuildStatusCallouts(ModelHubItem item, ModelStatus status)
        {
            if (status.Kind == ModelStatusKind.Conflict)
            {
                var actions = new List<Button>
                {
                    UiKit.Button("Copy Details", () =>
                    {
                        EditorGUIUtility.systemCopyBuffer = ProjectSettingsLinks.BuildRevisionConflictSummary(item);
                        Debug.Log($"HF Hub copied the revision conflict details for '{item.Repository}' to the clipboard.");
                    })
                };
                if (ProjectSettingsLinks.HasPackageManagerOwnedConflict(item))
                {
                    actions.Add(UiKit.Button("Open Package Manager", ProjectSettingsLinks.OpenPackageManager));
                }

                yield return UiKit.Callout(
                    UiTone.Error,
                    status.Description,
                    ProjectSettingsLinks.BuildRevisionConflictHelpMessage(item),
                    actions.ToArray());
                yield break;
            }

            // Download, import, and update states already explain themselves; only errors add detail.
            var problems = item.Diagnostics
                .Where(diagnostic => diagnostic.Severity == "error" || status.Tone == UiTone.Error)
                .Select(diagnostic => diagnostic.Message)
                .Distinct()
                .ToArray();

            if (status.NeedsAction)
            {
                yield return UiKit.Callout(
                    status.Tone == UiTone.Error ? UiTone.Error : status.Tone == UiTone.Warning ? UiTone.Warning : UiTone.Info,
                    status.Description,
                    problems.Length > 0 ? string.Join("\n", problems) : null);
                yield break;
            }

            var skipped = item.SkippedOnnxRepoPaths;
            if (skipped.Count > 0)
            {
                var attempted = item.ImportedFileCount + skipped.Count;
                yield return UiKit.Callout(
                    UiTone.Warning,
                    skipped.Count == 1
                        ? $"1 of {attempted} model files could not be converted to Sentis."
                        : $"{skipped.Count} of {attempted} model files could not be converted to Sentis.",
                    $"They use operators or data types that Sentis does not support, so they were skipped. The other files are imported.\n\n{string.Join("\n", skipped)}");
            }

            var warnings = item.Diagnostics
                .Where(diagnostic => diagnostic.Severity is "error" or "warning")
                .Select(diagnostic => diagnostic.Message)
                .Where(message => !skipped.Any(path => message?.Contains(path) == true))
                .Distinct()
                .ToArray();
            if (warnings.Length > 0)
            {
                yield return UiKit.Callout(
                    UiTone.Warning,
                    warnings.Length == 1 ? "Imported with a warning." : $"Imported with {warnings.Length} warnings.",
                    string.Join("\n", warnings));
            }
        }

        // ---------------------------------------------------------------- Tabs

        private static IReadOnlyList<DetailTab> GetAvailableTabs(ModelHubItem item)
        {
            var tabs = new List<DetailTab> { DetailTab.Overview };
            if (item.Files.Count > 0)
            {
                tabs.Add(DetailTab.Files);
            }

            if (item.CacheSizeOnDiskBytes.HasValue || item.RuntimeSizeOnDiskBytes.HasValue ||
                !string.IsNullOrWhiteSpace(item.RawSnapshotRootPath) || !string.IsNullOrWhiteSpace(item.RuntimeSnapshotAssetPath))
            {
                tabs.Add(DetailTab.Storage);
            }

            return tabs;
        }

        private VisualElement BuildTabStrip(ModelHubItem item, IReadOnlyList<DetailTab> tabs)
        {
            var strip = UiKit.Element("mh-tabs");
            foreach (var tab in tabs)
            {
                var label = tab switch
                {
                    DetailTab.Files => item.SelectedFileCount > 0 ? $"Files  {item.SelectedFileCount}" : "Files",
                    DetailTab.Storage => "Storage",
                    _ => "Overview"
                };
                var button = new Button(() => ChangeTab(item, tab)) { text = label };
                button.AddToClassList("mh-tab");
                button.EnableInClassList("mh-tab--active", tab == _selectedTab);
                strip.Add(button);
            }

            return strip;
        }

        private void ChangeTab(ModelHubItem item, DetailTab tab)
        {
            if (tab == _selectedTab || !TryResolvePendingFileEdits(item))
            {
                return;
            }

            _selectedTab = tab;
            RebuildDetails();
        }

        // ---------------------------------------------------------------- Overview

        private void BuildOverviewTab(VisualElement content, ModelHubItem item)
        {
            EnsureSizeEstimate(item);

            var optionalSection = BuildOptionalFilesSection(item);
            if (optionalSection != null)
            {
                content.Add(optionalSection);
            }

            var about = UiKit.Section("Details");
            var card = UiKit.Element("hf-card");
            card.Add(UiKit.KeyValue("Model ID", UiKit.LinkButton(
                item.Repository,
                () => Application.OpenURL($"https://huggingface.co/{item.Repository}"),
                "Open on Hugging Face")));
            card.Add(UiKit.KeyValue(
                "Revision",
                string.IsNullOrWhiteSpace(item.ResolvedCommitHash)
                    ? ManifestRevision.NormalizeForResolution(item.RequestedRevision)
                    : $"{ManifestRevision.NormalizeForResolution(item.RequestedRevision)}  ·  {item.ResolvedCommitHash.Substring(0, Math.Min(7, item.ResolvedCommitHash.Length))}",
                item.ResolvedCommitHash));
            if (item.RepositoryFileCount > 0)
            {
                card.Add(UiKit.KeyValue(
                    "Files",
                    item.SelectedFileCount == item.RepositoryFileCount
                        ? $"All {item.RepositoryFileCount} files"
                        : $"{item.SelectedFileCount} of {item.RepositoryFileCount} files"));
            }

            var downloadSize = UiKit.FormatBytes(item.SourceSizeBytes);
            if (!string.IsNullOrWhiteSpace(downloadSize))
            {
                card.Add(UiKit.KeyValue("Download size", downloadSize, "Size of the selected files on Hugging Face."));
            }

            card.Add(UiKit.KeyValue(
                "Imported",
                item.ImportedFileCount == 0
                    ? "Nothing yet"
                    : item.ImportedFileCount == 1 ? "1 Sentis model" : $"{item.ImportedFileCount} Sentis models"));
            about.Add(card);
            content.Add(about);

            content.Add(BuildUsedBySection(item));
        }

        private void EnsureSizeEstimate(ModelHubItem item)
        {
            if (item == null || (item.Files.Count > 0 && (item.CacheSizeOnDiskBytes.HasValue || item.EstimatedCacheSizeBytes.HasValue)))
            {
                return;
            }

            var dependency = item.Dependents.FirstOrDefault()?.Dependency;
            if (dependency != null)
            {
                _remoteSnapshotSizeEstimateService.EnsureEstimateLookup(dependency, RefreshReport);
            }
        }

        private VisualElement BuildOptionalFilesSection(ModelHubItem item)
        {
            var owners = item.Dependents
                .Where(dependent => dependent.Dependency.HasOptionalFiles)
                .OrderBy(dependent => ModelStatus.GetOwnerDisplayName(dependent.Dependency), StringComparer.Ordinal)
                .ToArray();
            if (owners.Length == 0)
            {
                return null;
            }

            var section = UiKit.Section(
                "Optional files",
                "Choose which optional files to import. Large variants can be added later.");
            var card = UiKit.Element("hf-card");
            var sizesByPath = item.Files
                .GroupBy(file => file.RepoPath, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().RawSizeInBytes, StringComparer.Ordinal);

            foreach (var owner in owners)
            {
                var dependency = owner.Dependency;
                if (owners.Length > 1)
                {
                    card.Add(UiKit.Text(ModelStatus.GetOwnerDisplayName(dependency).ToUpperInvariant(), "hf-overline", "mh-variant__owner"));
                }

                var enabled = new HashSet<string>(dependency.GetEnabledOptionalRepoPaths(), StringComparer.Ordinal);
                foreach (var path in dependency.GetOptionalRepoPaths())
                {
                    var row = UiKit.Element("mh-variant");
                    var toggle = new Toggle { value = enabled.Contains(path) };
                    var filePath = path;
                    toggle.RegisterValueChangedCallback(evt => SetOptionalFileEnabled(dependency, filePath, evt.newValue));
                    row.Add(toggle);
                    var name = UiKit.Text(filePath, "mh-variant__name");
                    name.tooltip = filePath;
                    row.Add(name);
                    if (sizesByPath.TryGetValue(filePath, out var size) && size.HasValue)
                    {
                        row.Add(UiKit.Text(UiKit.FormatBytes(size), "hf-muted", "hf-small"));
                    }

                    row.RegisterCallback<ClickEvent>(evt =>
                    {
                        if (evt.target != toggle && !toggle.Contains(evt.target as VisualElement))
                        {
                            toggle.value = !toggle.value;
                        }
                    });
                    card.Add(row);
                }
            }

            section.Add(card);
            return section;
        }

        private void SetOptionalFileEnabled(DiscoveredModelDependency dependency, string path, bool isEnabled)
        {
            var enabledFiles = dependency.GetEnabledOptionalRepoPaths().ToList();
            enabledFiles.RemoveAll(existing => string.Equals(existing, path, StringComparison.Ordinal));
            if (isEnabled)
            {
                enabledFiles.Add(path);
            }

            _optionalFileSelectionSource.SetEnabledOptionalFiles(dependency.PackageId, dependency.ModelId, enabledFiles);
            RefreshReport();
        }

        private static VisualElement BuildUsedBySection(ModelHubItem item)
        {
            var section = UiKit.Section("Used by");
            var hasConflict = item.HasRevisionConflict;
            foreach (var dependent in item.Dependents)
            {
                var dependency = dependent.Dependency;
                var isProjectOwner = ProjectManifestDiscovery.IsProjectManifestOwnerId(dependency.PackageId);
                var row = UiKit.Element("mh-owner");
                row.Add(UiKit.Icon(isProjectOwner ? "SceneAsset Icon" : "Package Manager", "mh-owner__icon"));

                var text = UiKit.Element("mh-owner__text");
                text.Add(UiKit.Text(ModelStatus.GetOwnerDisplayName(dependency), "mh-owner__name"));
                var details = new List<string>
                {
                    isProjectOwner
                        ? "This project"
                        : string.IsNullOrWhiteSpace(dependency.PackageVersion)
                            ? dependency.PackageId
                            : $"{dependency.PackageId} {dependency.PackageVersion}",
                    DescribeScope(dependency),
                    $"revision {ManifestRevision.NormalizeForResolution(dependency.Revision)}"
                };
                if (hasConflict)
                {
                    details.Add(ProjectSettingsLinks.GetManifestOwnerResolutionInfo(dependency).ResolutionHint);
                }

                text.Add(UiKit.Text(string.Join("  ·  ", details.Where(part => !string.IsNullOrWhiteSpace(part))), "mh-owner__detail"));
                row.Add(text);

                var manifestPath = dependency.ManifestPath;
                row.Add(UiKit.Button("Manifest", () => ProjectSettingsLinks.LocateManifest(manifestPath), "hf-button--small"));
                section.Add(row);
            }

            return section;
        }

        private static string DescribeScope(DiscoveredModelDependency dependency)
        {
            if (!dependency.HasExplicitFileSelection)
            {
                return "All files";
            }

            var optionalCount = dependency.GetOptionalRepoPaths().Count;
            var requiredCount = dependency.FileEntries.Count - optionalCount;
            var enabledCount = dependency.GetEnabledOptionalRepoPaths().Count;
            var parts = new List<string>();
            if (requiredCount > 0)
            {
                parts.Add(requiredCount == 1 ? "1 file" : $"{requiredCount} files");
            }

            if (optionalCount > 0)
            {
                parts.Add($"{enabledCount} of {optionalCount} optional");
            }

            return string.Join(", ", parts);
        }

        // ---------------------------------------------------------------- Manifest errors

        private static void BuildIssueDetails(VisualElement container, ManifestDiscoveryIssue issue)
        {
            var header = UiKit.Element("mh-header");
            var text = UiKit.Element("mh-header__text");
            text.Add(UiKit.Text(ProjectManifestDiscovery.GetOwnerDisplayName(issue.PackageId), "hf-title"));
            var subtitle = UiKit.Element("mh-header__subtitle");
            subtitle.Add(UiKit.Text(issue.ManifestPath, "hf-small"));
            text.Add(subtitle);
            header.Add(text);
            header.Add(UiKit.Pill(UiTone.Error, "Manifest error"));
            container.Add(header);

            var status = UiKit.Element("mh-status");
            status.Add(UiKit.Callout(
                UiTone.Error,
                "HF Hub could not read this manifest, so its models are skipped.",
                issue.Message,
                UiKit.Button("Show Manifest", () => ProjectSettingsLinks.LocateManifest(issue.ManifestPath))));
            container.Add(status);
        }
    }
}
