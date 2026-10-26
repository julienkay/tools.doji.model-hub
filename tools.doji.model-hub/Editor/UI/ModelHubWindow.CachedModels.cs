using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Model Cache view: the models downloaded on this machine, with their model card, and Add to Project, which lists
    /// a cached model in the project manifest or in the manifest of a package in development.
    /// </summary>
    internal sealed partial class ModelHubWindow
    {
        private const int ModelCardPreviewLines = 8;
        private const int ModelCardPreviewCharacters = 480;
        private const int ModelCardMaxCharacters = 16000;

        [SerializeField] private bool _modelCardExpanded;

        private string _modelCardFolder;

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

            if (!string.Equals(_modelCardFolder, repository.FolderName, StringComparison.Ordinal))
            {
                _modelCardFolder = repository.FolderName;
                _modelCardExpanded = false;
            }

            var item = _report?.Items.FirstOrDefault(entry =>
                string.Equals(entry.Repository, repository.Repository, StringComparison.OrdinalIgnoreCase));
            var revision = repository.Refs.Contains("main") ? "main" : repository.Refs.FirstOrDefault();

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
                new[] { new ModelLink("Model card", ModelLinks.GetModelCardUrl(repository.Repository), "Open the model card on Hugging Face.") },
                "mh-header__links"));
            header.Add(headerText);
            header.Add(item != null
                ? UiKit.Pill(UiTone.Info, "In this project")
                : UiKit.Pill(UiTone.Neutral, "Not in this project"));
            content.Add(header);
            content.Add(BuildCachedModelActions(repository, item));
            content.Add(BuildModelCardSection(repository, revision));
            content.Add(BuildCachedModelStorageSection(repository, item != null, revision));
        }

        private VisualElement BuildCachedModelActions(CacheRepositoryEntry repository, ModelHubItem item)
        {
            var actions = UiKit.Element("mh-actions");
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
                AddAction(UiKit.Button("Show in Project", () => ShowInProject(item.Repository, item.RequestedRevision)));
            }

            var isUsed = item != null;
            if (AddCachedModelPopup.HasTarget(isUsed))
            {
                Button add = null;
                add = UiKit.Button(isUsed ? "Add to Package…" : "Add to Project…", () => UnityEditor.PopupWindow.Show(
                    add.worldBound,
                    new AddCachedModelPopup(repository, isUsed, revision => ShowInProject(repository.Repository, revision))));
                add.tooltip = isUsed
                    ? "List this model in a package in this project's Packages folder."
                    : "List this model in this project, or in a package in development. The cached files are used, so it needs no download.";
                AddAction(add);
            }

            return actions;
        }

        private VisualElement BuildModelCardSection(CacheRepositoryEntry repository, string revision)
        {
            var section = UiKit.Section("Model card");
            var card = UiKit.Element("hf-card", "mh-model-card");
            section.Add(card);

            var cached = revision == null ? null : ModelCardService.ReadCached(repository.FolderPath, revision);
            if (cached != null)
            {
                AddModelCardText(card, repository, cached);
                return section;
            }

            if (revision == null)
            {
                card.Add(UiKit.Text("No model card is cached.", "hf-muted"));
                return section;
            }

            var task = ModelCardService.FetchAsync(repository.Repository, revision);
            if (!task.IsCompleted)
            {
                card.Add(UiKit.Text("Loading the model card from Hugging Face…", "hf-muted"));
                var folder = repository.FolderName;
                task.ContinueWith(
                    _ => EditorApplication.delayCall += () =>
                    {
                        if (this != null && _view == ModelHubView.Cache && string.Equals(_selectedCacheFolder, folder, StringComparison.Ordinal))
                        {
                            RebuildDetails();
                        }
                    });
                return section;
            }

            if (task.IsFaulted)
            {
                card.Add(UiKit.Text("Could not load the model card from Hugging Face.", "hf-muted"));
            }
            else
            {
                AddModelCardText(card, repository, task.Result);
            }

            return section;
        }

        private void AddModelCardText(VisualElement card, CacheRepositoryEntry repository, string markdown)
        {
            var text = ModelCardText.ToPlainText(markdown);
            if (text.Length == 0)
            {
                card.Add(UiKit.Text("This model has no model card text.", "hf-muted"));
                return;
            }

            var isLong = text.Length > ModelCardMaxCharacters;
            var full = isLong ? text.Substring(0, ModelCardMaxCharacters).TrimEnd() + "…" : text;
            var preview = ModelCardText.GetPreview(full, ModelCardPreviewLines, ModelCardPreviewCharacters, out var isTruncated);
            var label = UiKit.Text(_modelCardExpanded ? full : preview, "hf-wrap", "mh-model-card__text");
            label.selection.isSelectable = true;
            card.Add(label);

            var footer = UiKit.Element("mh-model-card__footer");
            if (isTruncated)
            {
                footer.Add(UiKit.LinkButton(_modelCardExpanded ? "Show less" : "Show more", () =>
                {
                    _modelCardExpanded = !_modelCardExpanded;
                    RebuildDetails();
                }));
            }

            if (isLong && _modelCardExpanded)
            {
                footer.Add(UiKit.LinkButton("Read the rest on Hugging Face", () => Application.OpenURL(ModelLinks.GetModelCardUrl(repository.Repository))));
            }

            if (footer.childCount > 0)
            {
                card.Add(footer);
            }
        }

        private VisualElement BuildCachedModelStorageSection(CacheRepositoryEntry repository, bool isUsed, string revision)
        {
            var section = UiKit.Section("In the cache");
            var card = UiKit.Element("hf-card");
            card.Add(UiKit.KeyValue("Size on disk", UiKit.FormatBytes(repository.SizeInBytes) ?? "—"));
            if (repository.HasPartialDownloads)
            {
                card.Add(UiKit.KeyValue("Partial downloads", UiKit.FormatBytes(repository.PartialSizeInBytes) ?? "—"));
            }

            card.Add(UiKit.KeyValue("Revisions", repository.Refs.Count > 0
                ? string.Join(", ", repository.Refs)
                : $"{repository.SnapshotCount} snapshot(s), no named revision"));
            var cachedModels = revision == null
                ? Array.Empty<string>()
                : CacheOverviewService.GetCachedModelFiles(CacheOverviewService.GetSnapshotFiles(repository.FolderPath, revision));
            if (cachedModels.Count > 0)
            {
                card.Add(UiKit.KeyValue("Model files", $"{cachedModels.Count} · {ModelFiles.GetLabel(ModelFiles.Describe(cachedModels))}", string.Join("\n", cachedModels)));
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
