using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Setup prompt: lists the models that need a download or a new conversion, says what each costs, and starts the
    /// work on one click. Models that only need this project's records are set up without it, by <see cref="AutoImport"/>.
    /// It also asks, as a modal window, whether to enable one optional model that code loads (<see cref="ShowOptionalModel"/>).
    /// </summary>
    internal sealed class AutoPromptWindow : EditorWindow
    {
        private const string StyleSheet = "SetupPrompt.uss";
        private const float WindowWidth = 480f;

        private PromptViewModel _viewModel;
        private Action<OptionalModelChoice> _onOptionalChoice;
        private readonly HashSet<string> _checkedOptional = new(StringComparer.Ordinal);
        private Button _primaryButton;

        /// <summary>
        /// The optional section starts collapsed above this many models, so the required models stay in focus.
        /// </summary>
        private const int ExpandedOptionalLimit = 3;

        internal enum PromptPrimaryAction
        {
            StartSync,
            OpenModelHub,
            EnableOptionalModel
        }

        /// <summary>
        /// What the user chose in the optional-model prompt. Closing the window is <see cref="Cancel"/>.
        /// </summary>
        internal enum OptionalModelChoice
        {
            Cancel,
            Enable,
            OpenInModelHub
        }

        internal static void ShowWindow(StatusReport report)
        {
            GetOpenWindow()?.Close();

            var window = CreateInstance<AutoPromptWindow>();
            window.titleContent = new GUIContent(ModelHubWindow.Title);
            var items = GetHubItems();
            window._viewModel = BuildViewModel(report, GetDownloadSizes(items), GetLicenseLinks(items), GetOnnxSelections(items), GetOptionalModels(items));
            window.EnsureModelCards(report, items);
            var optionalCount = window._viewModel.OptionalModels.Count;
            var optionalHeight = optionalCount == 0 ? 0f : 28f + (optionalCount <= ExpandedOptionalLimit ? optionalCount * 64f : 0f);
            var height = Mathf.Min(220f + (window._viewModel.Models.Count * 64f) + optionalHeight, 560f);
            var main = EditorGUIUtility.GetMainWindowPosition();
            window.position = new Rect(
                main.x + ((main.width - WindowWidth) * 0.5f),
                main.y + ((main.height - height) * 0.5f),
                WindowWidth,
                height);
            window.minSize = new Vector2(WindowWidth, 220f);
            window.ShowUtility();
            window.Focus();
        }

        /// <summary>
        /// Shows the prompt for one optional model that is not enabled, with <paramref name="dependents"/> as its owners,
        /// and blocks until the user decides. The caller enables the model; this window only asks.
        /// </summary>
        internal static OptionalModelChoice ShowOptionalModel(ModelHubItem item, IReadOnlyList<DependencyStatus> dependents)
        {
            GetOpenWindow()?.Close();

            var choice = OptionalModelChoice.Cancel;
            var window = CreateInstance<AutoPromptWindow>();
            window.titleContent = new GUIContent(ModelHubWindow.Title);
            window._viewModel = BuildOptionalModelViewModel(item, dependents);
            window._onOptionalChoice = selected => choice = selected;
            window.EnsureModelCard(item, dependents);
            const float height = 300f;
            var main = EditorGUIUtility.GetMainWindowPosition();
            window.position = new Rect(
                main.x + ((main.width - WindowWidth) * 0.5f),
                main.y + ((main.height - height) * 0.5f),
                WindowWidth,
                height);
            window.minSize = new Vector2(WindowWidth, 220f);
            window.ShowModalUtility();
            return choice;
        }

        /// <summary>
        /// Whether the prompt window exists, even if Unity has not focused it yet.
        /// </summary>
        internal static bool IsOpen()
        {
            return GetOpenWindow() != null;
        }

        private static AutoPromptWindow GetOpenWindow()
        {
            return Resources.FindObjectsOfTypeAll<AutoPromptWindow>().FirstOrDefault();
        }

        private static IReadOnlyDictionary<string, ModelHubItem> GetHubItems()
        {
            try
            {
                return EditorOperations.CreateModelHubReport().Items
                    .GroupBy(item => item.Repository, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            }
            catch (Exception)
            {
                return new Dictionary<string, ModelHubItem>();
            }
        }

        private static IReadOnlyDictionary<string, long?> GetDownloadSizes(IReadOnlyDictionary<string, ModelHubItem> items)
        {
            return items.ToDictionary(pair => pair.Key, pair => pair.Value.SourceSizeBytes, StringComparer.Ordinal);
        }

        /// <summary>
        /// The optional models that no owner enabled, as unchecked cards the setup prompt can enable with the required ones.
        /// </summary>
        private static IReadOnlyList<PromptModelEntry> GetOptionalModels(IReadOnlyDictionary<string, ModelHubItem> items)
        {
            return items.Values
                .Where(item => item.IsNotEnabled)
                .Select(item => BuildOptionalEntry(item, item.NotEnabledDependents))
                .ToArray();
        }

        /// <summary>
        /// Whether each repository selects ONNX files, which convert after the download. Repositories whose file list is
        /// unknown are left out, and the prompt assumes they convert.
        /// </summary>
        private static IReadOnlyDictionary<string, bool> GetOnnxSelections(IReadOnlyDictionary<string, ModelHubItem> items)
        {
            return items
                .Where(pair => pair.Value.Files.Count > 0)
                .ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value.Files.Any(file => file.IsDependencySelected && ModelFiles.GetFormat(file.RepoPath) == ModelFileFormat.Onnx),
                    StringComparer.Ordinal);
        }

        private static IReadOnlyDictionary<string, ModelLink> GetLicenseLinks(IReadOnlyDictionary<string, ModelHubItem> items)
        {
            return items.ToDictionary(
                pair => pair.Key,
                pair => ModelLinks.GetLicenseLink(
                    pair.Key,
                    string.IsNullOrWhiteSpace(pair.Value.ResolvedCommitHash)
                        ? ManifestRevision.NormalizeForResolution(pair.Value.RequestedRevision)
                        : pair.Value.ResolvedCommitHash,
                    pair.Value.ModelCard,
                    pair.Value.Files.Select(file => file.RepoPath)),
                StringComparer.Ordinal);
        }

        /// <summary>
        /// Asks the Hub for the model cards the prompt does not know yet, then shows their licenses.
        /// </summary>
        private void EnsureModelCards(StatusReport report, IReadOnlyDictionary<string, ModelHubItem> items)
        {
            var service = new RemoteSnapshotSizeEstimateService();
            foreach (var status in (report?.DependencyStatuses ?? Array.Empty<DependencyStatus>()).Where(status => status.NeedsStartupPrompt))
            {
                if (items.TryGetValue(status.Dependency.Repository, out var item) && item.ModelCard != null)
                {
                    continue;
                }

                service.EnsureEstimateLookup(status.Dependency, () =>
                {
                    if (this == null)
                    {
                        return;
                    }

                    var refreshed = GetHubItems();
                    _viewModel = BuildViewModel(report, GetDownloadSizes(refreshed), GetLicenseLinks(refreshed), GetOnnxSelections(refreshed), GetOptionalModels(refreshed));
                    rootVisualElement.Clear();
                    CreateGUI();
                });
            }
        }

        /// <summary>
        /// Asks the Hub for the model card of the optional model when the prompt does not know it yet.
        /// </summary>
        private void EnsureModelCard(ModelHubItem item, IReadOnlyList<DependencyStatus> dependents)
        {
            var dependency = dependents?.FirstOrDefault()?.Dependency;
            if (item == null || item.ModelCard != null || dependency == null)
            {
                return;
            }

            new RemoteSnapshotSizeEstimateService().EnsureEstimateLookup(dependency, () =>
            {
                if (this == null)
                {
                    return;
                }

                var refreshed = GetHubItems();
                if (!refreshed.TryGetValue(item.Repository, out var refreshedItem))
                {
                    return;
                }

                _viewModel = BuildOptionalModelViewModel(refreshedItem, refreshedItem.NotEnabledDependents
                    .Where(status => dependents.Any(candidate => ReferenceEquals(candidate.Dependency, status.Dependency) ||
                        string.Equals(candidate.Dependency.PackageId, status.Dependency.PackageId, StringComparison.Ordinal)))
                    .ToArray());
                rootVisualElement.Clear();
                CreateGUI();
            });
        }

        private void OnDestroy()
        {
            Choose(OptionalModelChoice.Cancel);
        }

        /// <summary>
        /// Reports the first choice of the optional-model prompt; later calls, such as closing after a choice, do nothing.
        /// </summary>
        private void Choose(OptionalModelChoice choice)
        {
            var callback = _onOptionalChoice;
            _onOptionalChoice = null;
            callback?.Invoke(choice);
        }

        private void CreateGUI()
        {
            if (_viewModel == null)
            {
                var items = GetHubItems();
                _viewModel = BuildViewModel(EditorOperations.CreateStatusReport(), GetDownloadSizes(items), GetLicenseLinks(items), GetOnnxSelections(items), GetOptionalModels(items));
            }

            var root = rootVisualElement;
            UiKit.ApplyTheme(root, StyleSheet);
            root.AddToClassList("sp");

            var header = UiKit.Element("sp-header");
            header.Add(UiKit.Icon("Package Manager", "sp-header__icon"));
            var text = UiKit.Element("sp-header__text");
            text.Add(UiKit.Text(_viewModel.Headline, "sp-headline"));
            text.Add(UiKit.Text(_viewModel.Body, "sp-body"));
            header.Add(text);
            root.Add(header);

            if (_viewModel.Models.Count > 0)
            {
                var list = new ScrollView(ScrollViewMode.Vertical);
                list.AddToClassList("sp-list");
                for (var index = 0; index < _viewModel.Models.Count; index++)
                {
                    var row = BuildModelRow(_viewModel.Models[index]);
                    row.EnableInClassList("sp-model--last", index == _viewModel.Models.Count - 1);
                    list.Add(row);
                }

                if (_viewModel.OptionalModels.Count > 0)
                {
                    list.Add(BuildOptionalSection());
                }

                root.Add(list);
            }

            if (_viewModel.IssueCount > 0)
            {
                var issues = UiKit.Callout(
                    UiTone.Warning,
                    null,
                    _viewModel.IssueCount == 1
                        ? "1 model manifest has errors. Open Model Hub to see them."
                        : $"{_viewModel.IssueCount} model manifests have errors. Open Model Hub to see them.");
                issues.AddToClassList("sp-issues");
                root.Add(issues);
            }

            var footer = UiKit.Element("sp-footer");
            footer.Add(UiKit.Button(_viewModel.DismissLabel, Close, "hf-button--first"));
            footer.Add(UiKit.Spacer());
            if (_viewModel.PrimaryAction != PromptPrimaryAction.OpenModelHub)
            {
                footer.Add(UiKit.Button(_viewModel.OpenLabel, () =>
                {
                    var repository = _viewModel.OpenRepository;
                    var revision = _viewModel.OpenRevision;
                    if (string.IsNullOrWhiteSpace(repository))
                    {
                        ModelHubWindow.OpenWindow();
                        Close();
                        return;
                    }

                    // Close the modal prompt first, so Model Hub opens as a normal window.
                    Choose(OptionalModelChoice.OpenInModelHub);
                    Close();
                    EditorApplication.delayCall += () => ModelHubWindow.OpenWindow(repository, revision);
                }));
            }

            var primary = UiKit.PrimaryButton(_viewModel.PrimaryActionLabel, RunPrimaryAction);
            primary.tooltip = _viewModel.PrimaryActionTooltip;
            footer.Add(primary);
            _primaryButton = primary;
            UpdatePrimaryButton();
            root.Add(footer);
            root.schedule.Execute(() => primary.Focus());
        }

        /// <summary>
        /// The optional models that no owner enabled, each with a checkbox that starts off. Checked models are enabled
        /// and downloaded with the required ones.
        /// </summary>
        private VisualElement BuildOptionalSection()
        {
            var models = _viewModel.OptionalModels;
            var foldout = new Foldout
            {
                text = $"Optional models ({models.Count})",
                value = models.Count <= ExpandedOptionalLimit
            };
            foldout.AddToClassList("sp-optional");
            for (var index = 0; index < models.Count; index++)
            {
                var model = models[index];
                var toggle = new Toggle { value = _checkedOptional.Contains(model.Repository) };
                toggle.AddToClassList("sp-model__toggle");
                toggle.tooltip = "Enable this optional model in the project's settings and download it with the others.";
                toggle.RegisterValueChangedCallback(change =>
                {
                    if (change.newValue)
                    {
                        _checkedOptional.Add(model.Repository);
                    }
                    else
                    {
                        _checkedOptional.Remove(model.Repository);
                    }

                    UpdatePrimaryButton();
                });
                var row = BuildModelRow(model, toggle);
                row.EnableInClassList("sp-model--last", index == models.Count - 1);
                foldout.Add(row);
            }

            return foldout;
        }

        private void UpdatePrimaryButton()
        {
            if (_primaryButton == null || _viewModel == null)
            {
                return;
            }

            var (label, tooltip) = BuildPrimaryActionLabel(_viewModel, _checkedOptional);
            _primaryButton.text = label;
            _primaryButton.tooltip = tooltip;
        }

        private static VisualElement BuildModelRow(PromptModelEntry model, Toggle toggle = null)
        {
            var row = UiKit.Element("sp-model");
            if (toggle != null)
            {
                row.Add(toggle);
            }
            else
            {
                row.Add(UiKit.Dot(model.IsConflict ? UiTone.Error : UiTone.Info));
            }

            var text = UiKit.Element("sp-model__text");
            text.Add(UiKit.Text(ModelStatus.GetModelName(model.Repository), "sp-model__name"));
            var detail = UiKit.Text(
                model.IsOptional
                    ? $"{model.Repository} · optional, for {string.Join(", ", model.Owners)}"
                    : $"{model.Repository} · for {string.Join(", ", model.Owners)}",
                "sp-model__detail");
            detail.tooltip = string.Join("\n", model.ManifestPaths);
            text.Add(detail);
            text.Add(UiKit.Links(
                new[]
                {
                    new ModelLink("Model card", ModelLinks.GetModelCardUrl(model.Repository), "Open the model card on Hugging Face."),
                    model.LicenseLink
                },
                "sp-model__links"));
            row.Add(text);
            var size = UiKit.FormatBytes(model.DownloadSizeBytes);
            if (!string.IsNullOrWhiteSpace(size) && model.NeedsDownload)
            {
                row.Add(UiKit.Text(size, "sp-model__size"));
            }
            else if (!model.NeedsDownload && model.IsOptional)
            {
                var downloaded = UiKit.Text("Downloaded", "sp-model__size");
                downloaded.tooltip = "The files are already in the cache. Enabling the model does not download them again.";
                row.Add(downloaded);
            }
            else if (!model.NeedsDownload && model.WillConvert)
            {
                var convert = UiKit.Text("Convert", "sp-model__size");
                convert.tooltip = "The files are downloaded. They need converting to Sentis models.";
                row.Add(convert);
            }

            return row;
        }

        private void RunPrimaryAction()
        {
            if (_viewModel.PrimaryAction == PromptPrimaryAction.EnableOptionalModel)
            {
                // The caller enables the model and starts its sync once the modal window returns.
                Choose(OptionalModelChoice.Enable);
                Close();
                return;
            }

            if (_viewModel.PrimaryAction == PromptPrimaryAction.StartSync)
            {
                if (_checkedOptional.Count > 0)
                {
                    var items = GetHubItems();
                    EditorOperations.SetOptionalModelEnabled(
                        _checkedOptional
                            .Where(items.ContainsKey)
                            .SelectMany(repository => items[repository].NotEnabledDependents.Select(status => status.Dependency)),
                        true);
                }

                EditorOperations.StartBackgroundSync();
                ProjectSettingsProvider.Refresh();
            }

            ModelHubWindow.OpenWindow();
            Close();
        }

        // ---------------------------------------------------------------- View model

        internal static PromptViewModel BuildViewModel(
            StatusReport report,
            IReadOnlyDictionary<string, long?> downloadSizes = null,
            IReadOnlyDictionary<string, ModelLink> licenseLinks = null,
            IReadOnlyDictionary<string, bool> onnxSelections = null,
            IReadOnlyList<PromptModelEntry> optionalModels = null)
        {
            var attention = (report?.DependencyStatuses ?? Array.Empty<DependencyStatus>())
                .Where(status => status.NeedsStartupPrompt)
                .ToArray();
            var issueCount = report?.DiscoveryIssues.Count ?? 0;

            var models = attention
                .GroupBy(status => status.Dependency.Repository, StringComparer.Ordinal)
                .Select(group =>
                {
                    long? size = null;
                    downloadSizes?.TryGetValue(group.Key, out size);
                    var licenseLink = default(ModelLink);
                    if (licenseLinks == null || !licenseLinks.TryGetValue(group.Key, out licenseLink))
                    {
                        licenseLink = ModelLinks.GetLicenseLink(group.Key, null, null, null);
                    }

                    var needsDownload = group.Any(status => !status.IsImportPending && (!HasRawCache(status) || status.ReasonCode == DependencyReasonCode.RawCacheFilesMissing || status.HasDamagedCache));
                    var selectsOnnx = true;
                    onnxSelections?.TryGetValue(group.Key, out selectsOnnx);
                    var willConvert = needsDownload
                        ? onnxSelections == null || !onnxSelections.ContainsKey(group.Key) || selectsOnnx
                        : group.Any(status => status.NeedsConversion);
                    var cause = group
                        .Where(status => status.NeedsConversion)
                        .Select(status => status.ConversionCause)
                        .DefaultIfEmpty(ConversionCause.None)
                        .Max();

                    return new PromptModelEntry(
                        group.Key,
                        group.Select(status => ModelStatus.GetOwnerDisplayName(status.Dependency)).Distinct().OrderBy(name => name, StringComparer.Ordinal).ToArray(),
                        group.Select(status => status.Dependency.ManifestPath).Distinct().ToArray(),
                        group.Any(status => status.ReasonCode == DependencyReasonCode.RevisionConflict),
                        needsDownload,
                        size,
                        licenseLink,
                        willConvert,
                        cause);
                })
                .OrderBy(model => ModelStatus.GetModelName(model.Repository), StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var owners = models.SelectMany(model => model.Owners).Distinct().ToArray();
            var allConflicts = models.Length > 0 && models.All(model => model.IsConflict);
            var conflictHasInstalledPackage = allConflicts && attention.Any(status =>
                ProjectSettingsLinks.GetManifestOwnerResolutionInfo(status.Dependency).SuggestsPackageManager);

            string headline;
            string body;
            if (models.Length == 0)
            {
                headline = issueCount == 1 ? "A model manifest has errors" : "Model manifests have errors";
                body = "AI Model Hub skips manifests it cannot read. Open Model Hub to see what is wrong.";
            }
            else if (allConflicts)
            {
                headline = models.Length == 1 ? "Model revisions conflict" : $"{models.Length} models have conflicting revisions";
                body = conflictHasInstalledPackage
                    ? "Packages request different revisions of the same model. Update a package, embed it to edit its manifest, or remove the model from one manifest."
                    : "Manifests request different revisions of the same model. Change them to use one revision.";
            }
            else
            {
                var subject = owners.Length == 1 ? owners[0] : "This project";
                headline = models.Length == 1
                    ? $"{subject} needs an AI model"
                    : $"{subject} needs {models.Length} AI models";
                body = BuildBody(models.Where(model => !model.IsConflict).ToArray());
            }

            // Optional models ride along only when the prompt has required models to download or convert.
            var optional = models.Any(model => !model.IsConflict)
                ? (optionalModels ?? Array.Empty<PromptModelEntry>())
                    .Where(entry => entry != null && models.All(model => !string.Equals(model.Repository, entry.Repository, StringComparison.Ordinal)))
                    .OrderBy(entry => ModelStatus.GetModelName(entry.Repository), StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : Array.Empty<PromptModelEntry>();
            var (label, tooltip, action) = BuildPrimaryAction(models, optional.Length > 0);
            return new PromptViewModel(headline, body, models, issueCount, label, tooltip, action, optionalModels: optional);
        }

        /// <summary>
        /// The primary button of the setup prompt with <paramref name="checkedOptional"/> enabled too: their downloads
        /// add to the size, and the label counts the models.
        /// </summary>
        internal static (string Label, string Tooltip) BuildPrimaryActionLabel(
            PromptViewModel viewModel,
            IEnumerable<string> checkedOptional)
        {
            if (viewModel.PrimaryAction != PromptPrimaryAction.StartSync)
            {
                return (viewModel.PrimaryActionLabel, viewModel.PrimaryActionTooltip);
            }

            var chosen = new HashSet<string>(checkedOptional ?? Array.Empty<string>(), StringComparer.Ordinal);
            var models = viewModel.Models
                .Concat(viewModel.OptionalModels.Where(model => chosen.Contains(model.Repository)))
                .ToArray();
            var (label, tooltip, _) = BuildPrimaryAction(models, viewModel.OptionalModels.Count > 0);
            return (label, tooltip);
        }

        /// <summary>
        /// The prompt for one optional model that is not enabled: one card, a body from
        /// <see cref="OptionalModelPrompt.BuildMessage"/>, and Enable (and Download) as the primary action.
        /// </summary>
        internal static PromptViewModel BuildOptionalModelViewModel(ModelHubItem item, IReadOnlyList<DependencyStatus> dependents)
        {
            var entry = BuildOptionalEntry(item, dependents);
            return BuildOptionalModelViewModel(
                entry.Repository,
                item.RequestedRevision,
                entry.Owners,
                entry.ManifestPaths,
                item.PendingDownloadBytes,
                entry.WillConvert,
                entry.LicenseLink);
        }

        /// <summary>
        /// One card for an optional model that is not enabled: the bytes missing from the cache, whether it converts,
        /// and its license.
        /// </summary>
        private static PromptModelEntry BuildOptionalEntry(ModelHubItem item, IReadOnlyList<DependencyStatus> dependents)
        {
            var revision = string.IsNullOrWhiteSpace(item.ResolvedCommitHash)
                ? ManifestRevision.NormalizeForResolution(item.RequestedRevision)
                : item.ResolvedCommitHash;
            return BuildOptionalEntry(
                item.Repository,
                (dependents ?? Array.Empty<DependencyStatus>()).Select(status => ModelStatus.GetOwnerDisplayName(status.Dependency)).ToArray(),
                (dependents ?? Array.Empty<DependencyStatus>()).Select(status => status.Dependency.ManifestPath).ToArray(),
                item.PendingDownloadBytes,
                item.ConvertsWhenEnabled,
                ModelLinks.GetLicenseLink(item.Repository, revision, item.ModelCard, item.Files.Select(file => file.RepoPath)));
        }

        internal static PromptModelEntry BuildOptionalEntry(
            string repository,
            IReadOnlyList<string> owners,
            IReadOnlyList<string> manifestPaths,
            long? pendingDownloadBytes,
            bool willConvert,
            ModelLink licenseLink)
        {
            return new PromptModelEntry(
                repository,
                (owners ?? Array.Empty<string>()).Distinct().OrderBy(name => name, StringComparer.Ordinal).ToArray(),
                (manifestPaths ?? Array.Empty<string>()).Distinct().ToArray(),
                false,
                pendingDownloadBytes != 0,
                pendingDownloadBytes,
                licenseLink,
                willConvert,
                isOptional: true);
        }

        internal static PromptViewModel BuildOptionalModelViewModel(
            string repository,
            string requestedRevision,
            IReadOnlyList<string> owners,
            IReadOnlyList<string> manifestPaths,
            long? pendingDownloadBytes,
            bool willConvert,
            ModelLink licenseLink)
        {
            var model = BuildOptionalEntry(repository, owners, manifestPaths, pendingDownloadBytes, willConvert, licenseLink);
            // Name what enabling does, like the setup prompt does for required models. With nothing to download or
            // convert, enabling only adds the model to the project.
            var (label, tooltip, _) = model.NeedsDownload || model.WillConvert
                ? BuildPrimaryAction(new[] { model })
                : ("Enable", "Use the model in this project.", PromptPrimaryAction.EnableOptionalModel);
            return new PromptViewModel(
                "Use an optional AI model?",
                OptionalModelPrompt.BuildMessage(repository),
                new[] { model },
                0,
                label,
                tooltip,
                PromptPrimaryAction.EnableOptionalModel,
                "Cancel",
                "Open in AI Model Hub",
                repository,
                requestedRevision);
        }

        /// <param name="countModels">Name the number of models instead of "All", because the prompt also lists
        /// optional models that "All" might seem to include.</param>
        private static (string Label, string Tooltip, PromptPrimaryAction Action) BuildPrimaryAction(
            IReadOnlyList<PromptModelEntry> models,
            bool countModels = false)
        {
            var syncable = models.Where(model => !model.IsConflict).ToArray();
            if (syncable.Length == 0)
            {
                return ("Open Model Hub", "See the details in Model Hub.", PromptPrimaryAction.OpenModelHub);
            }

            var downloads = syncable.Where(model => model.NeedsDownload).ToArray();
            var converts = syncable.Any(model => model.WillConvert);
            var verb = downloads.Length == 0 ? "Convert" : converts ? "Download & Convert" : "Download";
            var label = syncable.Length == 1 ? verb : countModels ? $"{verb} {syncable.Length}" : $"{verb} All";
            if (downloads.Length > 0 && downloads.All(model => model.DownloadSizeBytes.HasValue))
            {
                label = $"{label}  ·  {UiKit.FormatBytes(downloads.Sum(model => model.DownloadSizeBytes.GetValueOrDefault()))}";
            }

            var tooltip = downloads.Length == 0
                ? $"Convert the downloaded files to Sentis models. {ConversionCost}"
                : converts
                    ? $"Download from Hugging Face into the shared cache, then convert the ONNX files to Sentis models. {ConversionCost}"
                    : "Download from Hugging Face into the shared cache.";
            return (label, tooltip, PromptPrimaryAction.StartSync);
        }

        private const string ConversionCost = "Converting takes CPU, memory, and time for large models.";

        /// <summary>
        /// Says what the primary action does: which models download, which convert, and why a downloaded model converts.
        /// </summary>
        private static string BuildBody(IReadOnlyList<PromptModelEntry> models)
        {
            var downloads = models.Count(model => model.NeedsDownload);
            var converts = models.Count(model => model.WillConvert);
            if (downloads == 0)
            {
                var causes = models.Select(model => model.ConversionCause).Distinct().ToArray();
                var subject = models.Count == 1 ? "The model is" : "The models are";
                var reason = causes.Length == 1 && causes[0] == ConversionCause.FormatChanged
                    ? $"Sentis {ProjectImportService.GetSentisVersion()} uses a new model format, so {(models.Count == 1 ? "it needs" : "they need")} converting again."
                    : causes.Length == 1 && causes[0] == ConversionCause.SettingsChanged
                        ? $"The import settings changed, so {(models.Count == 1 ? "it needs" : "they need")} converting again."
                        : $"{(models.Count == 1 ? "It needs" : "They need")} converting to Sentis models for this project.";
                return $"{subject} already downloaded on this machine. {reason} {ConversionCost}";
            }

            var download = models.Count == 1
                ? "AI Model Hub downloads it from Hugging Face once, into a cache that your Unity projects share."
                : "AI Model Hub downloads them from Hugging Face once, into a cache that your Unity projects share.";
            if (converts == 0)
            {
                return download;
            }

            var convert = converts == models.Count
                ? models.Count == 1 ? "Then it converts the model to Sentis." : "Then it converts the models to Sentis."
                : "Then it converts the ONNX models to Sentis.";
            return $"{download} {convert} {ConversionCost}";
        }

        private static bool HasRawCache(DependencyStatus status)
        {
            // Missing can still mean the cache exists and only the import is incomplete.
            return status != null &&
                (!string.IsNullOrWhiteSpace(status.RevisionRefPath) ||
                    !string.IsNullOrWhiteSpace(status.RawFilePath) ||
                    !string.IsNullOrWhiteSpace(status.RawMetadataPath) ||
                    status.RawMetadata != null);
        }

        internal sealed class PromptViewModel
        {
            internal PromptViewModel(
                string headline,
                string body,
                IReadOnlyList<PromptModelEntry> models,
                int issueCount,
                string primaryActionLabel,
                string primaryActionTooltip,
                PromptPrimaryAction primaryAction,
                string dismissLabel = "Later",
                string openLabel = "Open Model Hub",
                string openRepository = null,
                string openRevision = null,
                IReadOnlyList<PromptModelEntry> optionalModels = null)
            {
                OptionalModels = optionalModels?.ToArray() ?? Array.Empty<PromptModelEntry>();
                DismissLabel = dismissLabel;
                OpenLabel = openLabel;
                OpenRepository = openRepository;
                OpenRevision = openRevision;
                Headline = headline;
                Body = body;
                Models = models?.ToArray() ?? Array.Empty<PromptModelEntry>();
                IssueCount = issueCount;
                PrimaryActionLabel = primaryActionLabel;
                PrimaryActionTooltip = primaryActionTooltip;
                PrimaryAction = primaryAction;
            }

            public string Headline { get; }
            public string Body { get; }
            public IReadOnlyList<PromptModelEntry> Models { get; }
            public int IssueCount { get; }
            public string PrimaryActionLabel { get; }
            public string PrimaryActionTooltip { get; }
            public PromptPrimaryAction PrimaryAction { get; }

            /// <summary>
            /// The button that closes the prompt without acting.
            /// </summary>
            public string DismissLabel { get; }

            /// <summary>
            /// The button that opens Model Hub, with <see cref="OpenRepository"/> selected when it is set.
            /// </summary>
            public string OpenLabel { get; }

            public string OpenRepository { get; }

            /// <summary>
            /// Optional models that no owner enabled, listed below the required ones with a checkbox that starts off.
            /// Empty unless the prompt has required models to set up.
            /// </summary>
            public IReadOnlyList<PromptModelEntry> OptionalModels { get; }
            public string OpenRevision { get; }
        }

        internal sealed class PromptModelEntry
        {
            public PromptModelEntry(
                string repository,
                IReadOnlyList<string> owners,
                IReadOnlyList<string> manifestPaths,
                bool isConflict,
                bool needsDownload,
                long? downloadSizeBytes,
                ModelLink licenseLink,
                bool willConvert = false,
                ConversionCause conversionCause = ConversionCause.None,
                bool isOptional = false)
            {
                IsOptional = isOptional;
                LicenseLink = licenseLink;
                WillConvert = willConvert;
                ConversionCause = conversionCause;
                Repository = repository;
                Owners = owners ?? Array.Empty<string>();
                ManifestPaths = manifestPaths ?? Array.Empty<string>();
                IsConflict = isConflict;
                NeedsDownload = needsDownload;
                DownloadSizeBytes = downloadSizeBytes;
            }

            public string Repository { get; }
            public IReadOnlyList<string> Owners { get; }
            public IReadOnlyList<string> ManifestPaths { get; }
            public bool IsConflict { get; }

            /// <summary>
            /// An optional model that no owner enabled. Its download size counts only files missing from the cache.
            /// </summary>
            public bool IsOptional { get; }
            public bool NeedsDownload { get; }
            public long? DownloadSizeBytes { get; }

            /// <summary>
            /// The model converts ONNX files after this action: after the download, or because the converted store
            /// has no model for this project's settings and Sentis format.
            /// </summary>
            public bool WillConvert { get; }

            /// <summary>
            /// Why a downloaded model converts again. <see cref="ConversionCause.None"/> while it still downloads.
            /// </summary>
            public ConversionCause ConversionCause { get; }

            /// <summary>
            /// The license the model card declares, and where to read it.
            /// </summary>
            public ModelLink LicenseLink { get; }
        }
    }
}
