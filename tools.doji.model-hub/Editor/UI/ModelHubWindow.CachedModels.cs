using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Model Cache view: the models downloaded on this machine, and Add to Project, which lists a cached model in the
    /// project manifest or in the manifest of a package in development. The cached files are used, so the model needs
    /// no download.
    /// </summary>
    internal sealed partial class ModelHubWindow
    {
        private const string ProjectTargetLabel = "This project";

        [SerializeField] private int _addTargetIndex;

        private string _addFolder;
        private string _addRevision;
        private string[] _addFiles = Array.Empty<string>();
        private bool _addFilesChosen;

        // ---------------------------------------------------------------- List

        /// <summary>
        /// Lists the cached models: first those this project does not use yet, then those it uses.
        /// </summary>
        private void RebuildCachedModelList()
        {
            var projectRepositories = GetProjectRepositories();
            var models = (_cacheOverview?.Repositories ?? Array.Empty<CacheRepositoryEntry>())
                .Where(repository => repository.IsModel && repository.SnapshotCount > 0)
                .Where(repository => string.IsNullOrWhiteSpace(_searchText) || Contains(repository.Repository, _searchText))
                .OrderBy(repository => repository.Repository, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var available = models.Where(repository => !projectRepositories.Contains(repository.Repository)).ToArray();
            var used = models.Where(repository => projectRepositories.Contains(repository.Repository)).ToArray();
            AddCachedModelSection("Not in this project", available, CacheTileKind.Unused);
            AddCachedModelSection("In this project", used, CacheTileKind.Used);

            if (_rowsByKey.Count == 0)
            {
                _list.Add(UiKit.Text(
                    string.IsNullOrWhiteSpace(_searchText) ? "No models are downloaded on this machine yet." : $"No cached models match “{_searchText}”.",
                    "mh-list-empty"));
            }
        }

        private void AddCachedModelSection(string title, IReadOnlyList<CacheRepositoryEntry> repositories, CacheTileKind kind)
        {
            if (repositories.Count == 0)
            {
                return;
            }

            _list.Add(BuildListSection(title, repositories.Count));
            foreach (var repository in repositories)
            {
                _list.Add(BuildCacheListRow(repository, kind));
            }
        }

        /// <summary>
        /// Keeps the cache selection on a listed row, or moves it to the first row.
        /// </summary>
        private void EnsureCacheSelection()
        {
            var key = BuildCacheRowKey(_selectedCacheFolder);
            if (key != null && _rowsByKey.ContainsKey(key))
            {
                return;
            }

            _selectedCacheFolder = GetCacheFolder(_rowsByKey.Keys.FirstOrDefault());
        }

        // ---------------------------------------------------------------- Details

        private void BuildCachedModelDetails(VisualElement content)
        {
            var repository = GetSelectedCacheRepository();
            if (repository == null)
            {
                content.Add(UiKit.EmptyState(
                    "Folder Icon",
                    "No cached models",
                    "Models that this machine downloaded, for any Unity project or Python tool, appear here. Add one to this project without downloading it again.",
                    UiKit.Button("Open Disk Usage", () => CacheDiskUsageWindow.Open(null))));
                return;
            }

            ResetAddFormIfNeeded(repository);
            var item = _report?.Items.FirstOrDefault(entry =>
                string.Equals(entry.Repository, repository.Repository, StringComparison.OrdinalIgnoreCase));

            var header = UiKit.Element("mh-header");
            var headerText = UiKit.Element("mh-header__text");
            headerText.Add(UiKit.Text(ModelStatus.GetModelName(repository.Repository), "hf-title"));
            var subtitle = UiKit.Element("mh-header__subtitle");
            var id = UiKit.Text(repository.Repository);
            id.selection.isSelectable = true;
            subtitle.Add(id);
            foreach (var reference in repository.Refs)
            {
                subtitle.Add(UiKit.Tag(reference));
            }

            headerText.Add(subtitle);
            headerText.Add(UiKit.Links(
                new[] { new ModelLink("Model card", $"https://huggingface.co/{repository.Repository}", "Open the model card on Hugging Face.") },
                "mh-header__links"));
            header.Add(headerText);
            header.Add(item != null
                ? UiKit.Pill(UiTone.Info, "In this project")
                : UiKit.Pill(UiTone.Neutral, "Not in this project"));
            content.Add(header);

            if (item != null)
            {
                var actions = UiKit.Element("mh-actions");
                actions.Add(UiKit.Button("Show in Project", () => ShowInProject(item.Repository, item.RequestedRevision), "hf-button--first"));
                content.Add(actions);
            }

            content.Add(BuildAddToProjectSection(repository, item != null));
            content.Add(BuildCachedModelStorageSection(repository, item != null));
        }

        private VisualElement BuildAddToProjectSection(CacheRepositoryEntry repository, bool isUsed)
        {
            var section = UiKit.Section(
                isUsed ? "Add to a Package" : "Add to Project",
                isUsed
                    ? "This project uses this model already. You can also list it in a package in development."
                    : "Lists the model in this project, or in a package in development. The cached files are used, so it needs no download.");
            var card = UiKit.Element("hf-card", "mh-add-card");

            var packages = LocalPackageManifestStore.GetProjectLocalPackages();
            var targets = new List<string> { ProjectTargetLabel };
            targets.AddRange(packages.Select(package => package.Label));
            _addTargetIndex = Mathf.Clamp(_addTargetIndex, 0, targets.Count - 1);
            var target = new DropdownField(targets, _addTargetIndex);
            target.AddToClassList("mh-add-card__field");
            target.tooltip = "The project manifest in ProjectSettings, or the manifest of a package in this project's Packages folder.";
            card.Add(UiKit.KeyValue("Add to", target));

            DropdownField revision = null;
            if (repository.Refs.Count > 0)
            {
                revision = new DropdownField(repository.Refs.ToList(), Math.Max(0, repository.Refs.ToList().IndexOf(_addRevision)));
                revision.AddToClassList("mh-add-card__field");
                revision.tooltip = "The cached revisions. The manifest pins this revision.";
                card.Add(UiKit.KeyValue("Revision", revision));
            }
            else
            {
                card.Add(UiKit.KeyValue("Revision", "No named revision in the cache"));
            }

            var files = UiKit.Element("mh-add-card__files");
            var filesSummary = UiKit.Text(DescribeAddFiles());
            filesSummary.tooltip = string.Join("\n", _addFiles);
            files.Add(filesSummary);
            Button choose = null;
            choose = UiKit.Button("Choose…", () => ChooseAddFiles(repository, choose.worldBound));
            choose.tooltip = "Pick the files from the model's file list on Hugging Face.";
            files.Add(choose);
            card.Add(UiKit.KeyValue("Files", files));

            var problem = repository.Refs.Count == 0
                ? "Download this model with a branch or tag name to add it from the cache."
                : _addFiles.Length == 0
                    ? "No .onnx or .sentis file of this revision is cached. Choose the files to use."
                    : null;
            if (problem != null)
            {
                card.Add(UiKit.Text(problem, "hf-small", "am-problem"));
            }

            var actions = UiKit.Element("mh-actions");
            var add = UiKit.PrimaryButton(BuildAddLabel(targets[_addTargetIndex]), () => AddCachedModel(repository, packages), "hf-button--first");
            add.SetEnabled(problem == null && !(isUsed && _addTargetIndex == 0));
            if (isUsed && _addTargetIndex == 0)
            {
                add.tooltip = "This project uses this model already.";
            }

            actions.Add(add);
            card.Add(actions);

            target.RegisterValueChangedCallback(_ =>
            {
                _addTargetIndex = target.index;
                RebuildDetails();
            });
            revision?.RegisterValueChangedCallback(evt =>
            {
                _addRevision = evt.newValue;
                _addFilesChosen = false;
                _addFiles = GetDefaultAddFiles(repository, _addRevision);
                RebuildDetails();
            });

            section.Add(card);
            return section;
        }

        private VisualElement BuildCachedModelStorageSection(CacheRepositoryEntry repository, bool isUsed)
        {
            var section = UiKit.Section("In the Cache");
            var card = UiKit.Element("hf-card");
            card.Add(UiKit.KeyValue("Size on disk", UiKit.FormatBytes(repository.SizeInBytes) ?? "—"));
            if (repository.HasPartialDownloads)
            {
                card.Add(UiKit.KeyValue("Partial downloads", UiKit.FormatBytes(repository.PartialSizeInBytes) ?? "—"));
            }

            card.Add(UiKit.KeyValue("Revisions", repository.Refs.Count > 0
                ? string.Join(", ", repository.Refs)
                : $"{repository.SnapshotCount} snapshot(s), no named revision"));
            var cachedModels = repository.Refs.Count > 0 ? GetDefaultAddFiles(repository, _addRevision) : Array.Empty<string>();
            if (cachedModels.Length > 0)
            {
                card.Add(UiKit.KeyValue("Format", ModelFiles.GetLabel(ModelFiles.Describe(cachedModels))));
            }

            var actions = UiKit.Element("mh-actions");
            actions.Add(UiKit.Button(UiKit.RevealInFileBrowserLabel, () => ProjectSettingsLinks.RevealPath(repository.FolderPath), "hf-button--first"));
            var diskUsage = UiKit.Button("Open Disk Usage", () => CacheDiskUsageWindow.Open(repository.FolderName));
            diskUsage.tooltip = "Open a map of the space every model in the cache takes.";
            actions.Add(diskUsage);
            actions.Add(UiKit.Spacer());
            var remove = UiKit.Button("Remove from Cache", () =>
            {
                if (CacheDiskUsageWindow.ConfirmRemoveRepository(repository, isUsed))
                {
                    Refresh();
                    CacheDiskUsageWindow.RefreshOpenWindows();
                }
            }, "hf-button--danger");
            remove.SetEnabled(!BackgroundSyncManager.IsRunning);
            remove.tooltip = BackgroundSyncManager.IsRunning ? "Wait until the setup finishes." : "Delete this model with all its revisions from the cache.";
            actions.Add(remove);
            card.Add(actions);
            section.Add(card);
            return section;
        }

        // ---------------------------------------------------------------- Add

        private void ResetAddFormIfNeeded(CacheRepositoryEntry repository)
        {
            if (string.Equals(_addFolder, repository.FolderName, StringComparison.Ordinal) &&
                (_addRevision == null || repository.Refs.Contains(_addRevision)))
            {
                return;
            }

            _addFolder = repository.FolderName;
            _addRevision = repository.Refs.Contains("main") ? "main" : repository.Refs.FirstOrDefault();
            _addFilesChosen = false;
            _addFiles = GetDefaultAddFiles(repository, _addRevision);
        }

        private static string[] GetDefaultAddFiles(CacheRepositoryEntry repository, string revision)
        {
            return CacheOverviewService.GetCachedModelFiles(
                CacheOverviewService.GetSnapshotFiles(repository.FolderPath, revision)).ToArray();
        }

        private string DescribeAddFiles()
        {
            var count = _addFiles.Length;
            var files = count == 1 ? "1 file" : $"{count} files";
            if (count == 0)
            {
                return "None";
            }

            return _addFilesChosen ? $"{files} chosen" : $"{files} · the cached model files";
        }

        private static string BuildAddLabel(string target)
        {
            return string.Equals(target, ProjectTargetLabel, StringComparison.Ordinal) ? "Add to Project" : $"Add to {target}";
        }

        private void ChooseAddFiles(CacheRepositoryEntry repository, Rect buttonRect)
        {
            UnityEditor.PopupWindow.Show(
                buttonRect,
                new FileSelectionPopup(
                    repository.Repository,
                    _addRevision,
                    _addFiles.Select(path => new ModelDependencyFileDefinition(path)).ToArray(),
                    allowOptional: false,
                    files =>
                    {
                        _addFiles = files?.Select(file => file.Path).ToArray() ?? Array.Empty<string>();
                        _addFilesChosen = true;
                        RebuildDetails();
                    }));
        }

        private void AddCachedModel(CacheRepositoryEntry repository, IReadOnlyList<LocalPackageManifestStore.LocalPackageOption> packages)
        {
            if (string.IsNullOrWhiteSpace(_addRevision) || _addFiles.Length == 0)
            {
                return;
            }

            string errorMessage;
            var added = _addTargetIndex <= 0
                ? ProjectSettingsModelManifestStore.TryAddModelId(repository.Repository, _addRevision, _addFiles, out errorMessage)
                : LocalPackageManifestStore.TryAddModelId(packages[_addTargetIndex - 1], repository.Repository, _addRevision, _addFiles, out errorMessage);
            if (!added)
            {
                EditorUtility.DisplayDialog("Could Not Add Model", errorMessage, "OK");
                return;
            }

            ProjectSettingsProvider.Refresh();
            ShowInProject(repository.Repository, _addRevision);
        }

        /// <summary>
        /// Switches to In Project, refreshed, and selects the model.
        /// </summary>
        private void ShowInProject(string repository, string revision)
        {
            Refresh();
            SwitchView(ModelHubView.Project);
            var item = _report?.Items.FirstOrDefault(entry =>
                string.Equals(entry.Repository, repository, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.RequestedRevision, revision, StringComparison.Ordinal))
                ?? _report?.Items.FirstOrDefault(entry => string.Equals(entry.Repository, repository, StringComparison.OrdinalIgnoreCase));
            if (item != null)
            {
                Select(BuildItemKey(item));
            }
        }
    }
}
