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
            var showsCache = _view == ModelHubView.Cache;
            if (item == null && issue == null && !showsCache)
            {
                _detailsPane.Add(BuildEmptyState());
                return;
            }

            _detailsScroll = new ScrollView(ScrollViewMode.Vertical) { userData = _selectedKey };
            _detailsScroll.AddToClassList("mh-details-scroll");
            _detailsPane.Add(_detailsScroll);

            if (showsCache)
            {
                BuildCachedModelDetails(_detailsScroll);
            }
            else if (issue != null)
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
            EnsureHubLookup(item);
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
            var repository = UiKit.Text(item.Repository);
            repository.selection.isSelectable = true;
            subtitle.Add(repository);
            subtitle.Add(UiKit.Tag(
                ManifestRevision.NormalizeForResolution(item.RequestedRevision),
                string.IsNullOrWhiteSpace(item.ResolvedCommitHash)
                    ? "Requested revision"
                    : $"Requested revision. Resolved commit: {item.ResolvedCommitHash}"));
            text.Add(subtitle);
            text.Add(UiKit.Links(
                ModelLinks.Build(
                    item.Repository,
                    string.IsNullOrWhiteSpace(item.ResolvedCommitHash) ? ManifestRevision.NormalizeForResolution(item.RequestedRevision) : item.ResolvedCommitHash,
                    item.ModelCard,
                    item.Files.Select(file => file.RepoPath)),
                "mh-header__links"));
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
                if ((status.Kind is ModelStatusKind.DownloadNeeded or ModelStatusKind.RepairNeeded or ModelStatusKind.NotEnabled) && !string.IsNullOrWhiteSpace(size))
                {
                    label = $"{label}  ·  {size}";
                }

                var primary = UiKit.PrimaryButton(label, () => RunPrimaryAction(item, status), "hf-button--first");
                if (status.Action is ModelActionKind.Sync or ModelActionKind.EnableModel)
                {
                    primary.SetEnabled(!isSyncRunning);
                    primary.tooltip = isSyncRunning
                        ? "Wait for the running setup to finish."
                        : status.Action == ModelActionKind.EnableModel
                            ? "Enable this optional model for this project and download it. The choice is saved in the project's settings."
                            : status.Description;
                }

                actions.Add(primary);
            }

            if (!item.IsNotEnabled && item.EnabledOptionalDependents.Count > 0)
            {
                var disable = UiKit.Button("Disable", () => ConfirmDisableModel(item));
                disable.SetEnabled(!isSyncRunning);
                disable.tooltip = isSyncRunning
                    ? "Wait for the running setup to finish."
                    : "Disable this optional model for this project. Builds no longer include it. Downloaded files stay in the cache.";
                if (status.Action == ModelActionKind.None)
                {
                    disable.AddToClassList("hf-button--first");
                }

                actions.Add(disable);
            }

            Button manifestButton = null;
            manifestButton = UiKit.Button("Show Manifest", () => LocateManifest(item, manifestButton.worldBound));
            manifestButton.tooltip = "Select the manifest that declares this model.";
            if (status.Action == ModelActionKind.None && !(item.EnabledOptionalDependents.Count > 0))
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

            if (status.Action == ModelActionKind.EnableModel)
            {
                EnableModel(item);
                return;
            }

            EditorOperations.StartBackgroundSync(item);
            Refresh();
        }

        /// <summary>
        /// Enables an optional model for every owner that declares it not enabled, then downloads and sets it up.
        /// </summary>
        private void EnableModel(ModelHubItem item)
        {
            EditorOperations.SetOptionalModelEnabled(item.NotEnabledDependents.Select(dependent => dependent.Dependency), true);
            BackgroundSyncManager.StartForRepository(item.Repository, item.RequestedRevision, SyncOperationMode.Full);
            Refresh();
        }

        private void ConfirmDisableModel(ModelHubItem item)
        {
            if (!EditorUtility.DisplayDialog(
                    "Disable Optional Model",
                    $"Disable {item.Repository} in this project?\n\n" +
                    "AI Model Hub stops setting it up, and builds no longer include it. Code that loads it gets an error that says how to enable it again. " +
                    "Downloaded and converted files stay in the shared caches, so enabling it again is fast.",
                    "Disable",
                    "Cancel"))
            {
                return;
            }

            EditorOperations.SetOptionalModelEnabled(item.EnabledOptionalDependents.Select(dependent => dependent.Dependency), false);
            Refresh();
        }

        private void ShowItemMenu(ModelHubItem item, Rect buttonRect)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Open on Hugging Face"), false, () => Application.OpenURL($"https://huggingface.co/{item.Repository}"));
            menu.AddItem(new GUIContent("Copy Model ID"), false, () => EditorGUIUtility.systemCopyBuffer = item.Repository);
            menu.AddSeparator(string.Empty);

            var canRemove = !item.HasRevisionConflict && !BackgroundSyncManager.IsRunning;
            if (PulledModels.HasCopies(item.Repository, item.RuntimeFiles))
            {
                menu.AddItem(new GUIContent("Remove Pulled Copies"), false, () =>
                {
                    PulledModels.Remove(item.Repository);
                    Refresh();
                });
            }
            else if (item.HasImportedArtifacts && item.State == ModelHubItemState.Ready)
            {
                menu.AddItem(new GUIContent("Pull into Project"), false, () => PullIntoProject(item));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Pull into Project"));
            }

            if (canRemove && item.HasImportedArtifacts)
            {
                menu.AddItem(new GUIContent("Remove from Project"), false, () => ConfirmRemoveImported(item));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Remove from Project"));
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

        private void PullIntoProject(ModelHubItem item)
        {
            try
            {
                var written = PulledModels.Pull(item.Repository, item.RuntimeFiles);
                Debug.Log($"AI Model Hub copied {written.Count} runtime models of {item.Repository} into {PulledModels.GetFolderAssetPath(item.Repository)}. Builds ship these copies.");
            }
            catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException)
            {
                EditorUtility.DisplayDialog("Pull into Project", exception.Message, "OK");
            }

            Refresh();
        }

        private void ConfirmRemoveImported(ModelHubItem item)
        {
            if (!EditorUtility.DisplayDialog(
                    "Remove from Project",
                    $"Remove {item.Repository} from this project's imports?\n\nCopies pulled into the project are deleted. Downloaded and converted files stay in the shared caches, so importing again is fast.",
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
            var size = UiKit.FormatBytes(item.CacheSizeOnDiskBytes);
            if (!EditorUtility.DisplayDialog(
                    "Remove from Cache",
                    $"Delete the downloaded files of {item.Repository}{(size == null ? string.Empty : $" ({size})")} from the shared Hugging Face cache on this machine?\n\n" +
                    "Imported files in this project stay usable. The cache is shared with other Unity projects and with Python tools that use huggingface_hub; they must download this model again.\n\n" +
                    "Files that another revision of this model still uses are kept.",
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
                        Debug.Log($"AI Model Hub copied the revision conflict details for '{item.Repository}' to the clipboard.");
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
                !string.IsNullOrWhiteSpace(item.RawSnapshotRootPath) || !string.IsNullOrWhiteSpace(item.RuntimeModelFolder))
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

            var selectedModelFiles = item.Files.Where(file => file.IsDependencySelected).Select(file => file.RepoPath).ToArray();
            if (selectedModelFiles.Any(ModelFiles.IsModelFile))
            {
                var format = ModelFiles.Describe(selectedModelFiles);
                card.Add(UiKit.KeyValue(
                    "Format",
                    format switch
                    {
                        RepositoryModelFormat.Sentis => "Sentis, used as downloaded",
                        RepositoryModelFormat.Onnx => "ONNX, converted to Sentis",
                        RepositoryModelFormat.Mixed => "ONNX and Sentis files",
                        _ => ModelFiles.GetLabel(format)
                    }));
            }

            card.Add(UiKit.KeyValue(
                "In this project",
                item.ImportedFileCount == 0
                    ? "Nothing yet"
                    : item.ImportedFileCount == 1 ? "1 Sentis model" : $"{item.ImportedFileCount} Sentis models"));
            card.Add(UiKit.KeyValue("In builds", BuildDeliveryField(item)));
            about.Add(card);
            content.Add(about);

            content.Add(BuildUsedBySection(item));
        }

        /// <summary>
        /// How builds deliver this model: the project default, bundled, or downloaded at runtime.
        /// </summary>
        private VisualElement BuildDeliveryField(ModelHubItem item)
        {
            var settings = ProjectSettings.instance;
            var choices = new List<string>
            {
                $"Project default ({DeliveryLabel(settings.DefaultDelivery)})",
                DeliveryLabel(ModelDelivery.Bundle),
                DeliveryLabel(ModelDelivery.Download)
            };
            var current = settings.GetDeliveryOverride(item.Repository);
            var field = new DropdownField(choices, current == null ? 0 : current == ModelDelivery.Bundle ? 1 : 2);
            field.tooltip = "Bundled models ship in StreamingAssets. Downloaded models are fetched from Hugging Face on first use, at the commit tested here.";
            field.RegisterValueChangedCallback(evt =>
            {
                var index = choices.IndexOf(evt.newValue);
                settings.SetDeliveryOverride(item.Repository, index == 0 ? null : index == 1 ? ModelDelivery.Bundle : ModelDelivery.Download);
                EditorRuntimeCatalog.Invalidate();
            });

            var hasOnnx = item.Files.Any(file => file.IsDependencySelected && ModelFiles.GetFormat(file.RepoPath) == ModelFileFormat.Onnx);
            if (!hasOnnx || ModelHubBuildProcessor.GetPlayerOnnxConverterId() != null)
            {
                return field;
            }

            var row = UiKit.Element("mh-delivery");
            row.Add(field);
            var hint = UiKit.Text("Download needs tools.doji.onnx-model-loader for ONNX files.", "hf-small", "hf-muted");
            UiKit.SetVisible(hint, settings.GetEffectiveDelivery(item.Repository) == ModelDelivery.Download);
            field.RegisterValueChangedCallback(_ => UiKit.SetVisible(hint, settings.GetEffectiveDelivery(item.Repository) == ModelDelivery.Download));
            row.Add(hint);
            return row;
        }

        internal static string DeliveryLabel(ModelDelivery delivery)
        {
            return delivery == ModelDelivery.Download ? "Download at runtime" : "Bundle in the build";
        }

        /// <summary>
        /// Asks the Hub once for what the details need and the caches lack: the file list, sizes, and the model
        /// card's license.
        /// </summary>
        private void EnsureHubLookup(ModelHubItem item)
        {
            if (item == null || (item.Files.Count > 0 && item.ModelCard != null && (item.CacheSizeOnDiskBytes.HasValue || item.EstimatedCacheSizeBytes.HasValue)))
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
            _optionalFileSelectionSource.SetEnabled(dependency, path, isEnabled);
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
                "AI Model Hub could not read this manifest, so its models are skipped.",
                issue.Message,
                UiKit.Button("Show Manifest", () => ProjectSettingsLinks.LocateManifest(issue.ManifestPath))));
            container.Add(status);
        }
    }
}
