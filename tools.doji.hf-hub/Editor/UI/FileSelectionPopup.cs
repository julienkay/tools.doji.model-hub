using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Popup that lists a Hugging Face repository's files so a manifest can select some of them.
    /// An empty selection means the whole repository.
    /// </summary>
    internal sealed class FileSelectionPopup : PopupWindowContent
    {
        private enum Filter
        {
            All,
            Onnx,
            Selected
        }

        private readonly string _repository;
        private readonly string _revision;
        private readonly bool _allowOptional;
        private readonly Action<ModelDependencyFileDefinition[]> _onApply;
        private readonly Dictionary<string, bool> _selection;
        private readonly IHttpClient _httpClient = new HttpClient();
        private readonly CancellationTokenSource _cancellation = new();

        private RepositoryFileMetadata[] _files = Array.Empty<RepositoryFileMetadata>();
        private string _search = string.Empty;
        private Filter _filter = Filter.All;
        private VisualElement _content;
        private Label _summary;
        private readonly List<Button> _filterButtons = new();

        public FileSelectionPopup(
            string repository,
            string revision,
            IReadOnlyList<ModelDependencyFileDefinition> selectedFiles,
            bool allowOptional,
            Action<ModelDependencyFileDefinition[]> onApply)
        {
            _repository = repository?.Trim();
            _revision = string.IsNullOrWhiteSpace(revision) ? "main" : revision.Trim();
            _allowOptional = allowOptional;
            _onApply = onApply ?? throw new ArgumentNullException(nameof(onApply));
            _selection = ModelDependencyDefinition.NormalizeFileEntries(selectedFiles)
                .ToDictionary(file => file.Path, file => file.Optional, StringComparer.Ordinal);
        }

        public override Vector2 GetWindowSize()
        {
            return new Vector2(560f, 460f);
        }

        public override void OnGUI(Rect rect)
        {
        }

        public override VisualElement CreateGUI()
        {
            var root = new VisualElement();
            UiKit.ApplyTheme(root, ManifestEditorElement.StyleSheet);
            root.AddToClassList("fp");

            var header = UiKit.Element("fp-header");
            header.Add(UiKit.Text("Choose files", "hf-heading"));
            root.Add(header);
            root.Add(UiKit.Text($"{_repository} · {_revision}", "fp-subtitle"));

            var controls = UiKit.Element("fp-controls");
            controls.Add(UiKit.SearchField("Filter files", value =>
            {
                _search = value ?? string.Empty;
                RebuildList();
            }));
            var filter = UiKit.Element("fp-filter");
            foreach (var mode in new[] { Filter.All, Filter.Onnx, Filter.Selected })
            {
                var button = new Button(() =>
                {
                    _filter = mode;
                    RebuildList();
                })
                {
                    text = mode == Filter.Onnx ? "ONNX" : mode.ToString(),
                    userData = mode
                };
                _filterButtons.Add(button);
                filter.Add(button);
            }

            controls.Add(filter);
            root.Add(controls);

            _content = new ScrollView(ScrollViewMode.Vertical);
            _content.AddToClassList("fp-list");
            root.Add(_content);

            var footer = UiKit.Element("fp-footer");
            _summary = UiKit.Text(string.Empty, "hf-muted", "hf-grow");
            footer.Add(_summary);
            var allFiles = UiKit.Button("Use All Files", () =>
            {
                _selection.Clear();
                RebuildList();
            });
            allFiles.tooltip = "Clear the selection so the manifest uses the whole repository.";
            footer.Add(allFiles);
            footer.Add(UiKit.Button("Cancel", () => editorWindow?.Close()));
            footer.Add(UiKit.PrimaryButton("Apply", Apply));
            root.Add(footer);

            ShowMessage("Loading files from Hugging Face…");
            _ = LoadAsync();
            return root;
        }

        public override void OnClose()
        {
            _cancellation.Cancel();
        }

        private async Task LoadAsync()
        {
            try
            {
                var inventory = await _httpClient.GetRepositoryInventoryAsync(_repository, _revision, _cancellation.Token);
                _files = inventory.Files
                    .Where(file => file != null &&
                        !string.IsNullOrWhiteSpace(file.RepoPath) &&
                        !ModelDependencyDefinition.IsRepoMetadataFile(file.RepoPath))
                    .OrderBy(file => file.RepoPath, StringComparer.Ordinal)
                    .ToArray();
                RebuildList();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                ShowMessage($"Could not load the file list: {exception.Message}");
            }
        }

        private void ShowMessage(string message)
        {
            _content?.Clear();
            _content?.Add(UiKit.Text(message, "fp-message"));
            UpdateSummary();
        }

        private void RebuildList()
        {
            if (_content == null)
            {
                return;
            }

            foreach (var button in _filterButtons)
            {
                button.EnableInClassList("fp-filter--active", (Filter)button.userData == _filter);
            }

            _content.Clear();
            var knownPaths = new HashSet<string>(_files.Select(file => file.RepoPath), StringComparer.Ordinal);
            var missing = _selection.Keys.Where(path => !knownPaths.Contains(path)).OrderBy(path => path).ToArray();
            if (missing.Length > 0 && _files.Length > 0)
            {
                _content.Add(UiKit.Callout(
                    UiTone.Warning,
                    "Some selected files do not exist at this revision.",
                    string.Join("\n", missing)));
            }

            var visible = _files.Where(IsVisible).ToArray();
            if (visible.Length == 0)
            {
                _content.Add(UiKit.Text(_files.Length == 0 ? "This repository has no files." : "No files match the filter.", "fp-message"));
            }

            foreach (var file in visible)
            {
                _content.Add(BuildRow(file));
            }

            UpdateSummary();
        }

        private bool IsVisible(RepositoryFileMetadata file)
        {
            if (!string.IsNullOrWhiteSpace(_search) &&
                file.RepoPath.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            return _filter switch
            {
                Filter.Onnx => ModelDependencyDefinition.IsOnnxFile(file.RepoPath),
                Filter.Selected => _selection.ContainsKey(file.RepoPath),
                _ => true
            };
        }

        private VisualElement BuildRow(RepositoryFileMetadata file)
        {
            var path = file.RepoPath;
            var isSelected = _selection.ContainsKey(path);
            var row = UiKit.Element("fp-row");
            row.EnableInClassList("fp-row--off", !isSelected);

            var toggle = new Toggle { value = isSelected };
            row.Add(toggle);
            var name = UiKit.Text(path, "fp-row__name");
            name.tooltip = path;
            row.Add(name);

            Toggle optional = null;
            if (_allowOptional)
            {
                optional = new Toggle { text = "Optional", value = isSelected && _selection[path] };
                optional.AddToClassList("fp-row__optional");
                optional.tooltip = "Optional files are off by default. Projects that use the package can turn them on in Model Hub.";
                optional.EnableInClassList("fp-invisible", !isSelected);
                optional.RegisterValueChangedCallback(evt =>
                {
                    if (_selection.ContainsKey(path))
                    {
                        _selection[path] = evt.newValue;
                        UpdateSummary();
                    }
                });
                row.Add(optional);
            }

            row.Add(UiKit.Text(UiKit.FormatBytes(file.SizeInBytes) ?? string.Empty, "fp-row__size"));

            toggle.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue)
                {
                    _selection[path] = false;
                }
                else
                {
                    _selection.Remove(path);
                }

                row.EnableInClassList("fp-row--off", !evt.newValue);
                if (optional != null)
                {
                    optional.SetValueWithoutNotify(false);
                    optional.EnableInClassList("fp-invisible", !evt.newValue);
                }

                UpdateSummary();
            });
            name.RegisterCallback<ClickEvent>(_ => toggle.value = !toggle.value);
            return row;
        }

        private void UpdateSummary()
        {
            if (_summary == null)
            {
                return;
            }

            if (_selection.Count == 0)
            {
                var total = _files.Sum(file => file.SizeInBytes ?? 0);
                _summary.text = total > 0
                    ? $"All files · {UiKit.FormatBytes(total)}"
                    : "All files";
                return;
            }

            var sizes = _files.ToDictionary(file => file.RepoPath, file => file.SizeInBytes ?? 0, StringComparer.Ordinal);
            var selectedBytes = _selection.Keys.Sum(path => sizes.TryGetValue(path, out var size) ? size : 0);
            var optionalCount = _selection.Count(pair => pair.Value);
            var parts = new List<string> { _selection.Count == 1 ? "1 file" : $"{_selection.Count} files" };
            if (optionalCount > 0)
            {
                parts.Add($"{optionalCount} optional");
            }

            if (selectedBytes > 0)
            {
                parts.Add(UiKit.FormatBytes(selectedBytes));
            }

            _summary.text = string.Join(" · ", parts);
        }

        private void Apply()
        {
            _onApply(_selection.Count > 0
                ? _selection
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => new ModelDependencyFileDefinition(pair.Key, pair.Value))
                    .ToArray()
                : null);
            editorWindow?.Close();
        }
    }
}
