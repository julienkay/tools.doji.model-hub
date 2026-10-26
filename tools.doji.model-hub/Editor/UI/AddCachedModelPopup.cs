using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Adds a cached model to the project manifest or to a local package manifest, with a cached revision and, by
    /// default, its cached model files, so the model needs no download.
    /// </summary>
    internal sealed class AddCachedModelPopup : PopupWindowContent
    {
        private const string ProjectTargetLabel = "This project";

        private readonly CacheRepositoryEntry _repository;
        private readonly bool _isUsedByProject;
        private readonly Action<string> _onAdded;
        private readonly IReadOnlyList<LocalPackageManifestStore.LocalPackageOption> _packages;

        private DropdownField _target;
        private Label _filesSummary;
        private Label _problem;
        private Button _filesButton;
        private Button _addButton;
        private string _revision;
        private string[] _files = Array.Empty<string>();
        private bool _filesChosen;

        /// <param name="onAdded">Called with the revision after the manifest is written.</param>
        public AddCachedModelPopup(CacheRepositoryEntry repository, bool isUsedByProject, Action<string> onAdded)
        {
            _repository = repository;
            _isUsedByProject = isUsedByProject;
            _onAdded = onAdded;
            _packages = LocalPackageManifestStore.GetProjectLocalPackages();
            _revision = repository.Refs.Contains("main") ? "main" : repository.Refs.FirstOrDefault();
            _files = GetDefaultFiles(_revision);
        }

        /// <summary>
        /// Whether there is any manifest to add to: the project manifest, unless the project uses the model already, or
        /// a local package.
        /// </summary>
        public static bool HasTarget(bool isUsedByProject)
        {
            return !isUsedByProject || LocalPackageManifestStore.GetProjectLocalPackages().Count > 0;
        }

        public override Vector2 GetWindowSize()
        {
            return new Vector2(380f, 250f);
        }

        public override void OnGUI(Rect rect)
        {
        }

        public override VisualElement CreateGUI()
        {
            var root = new VisualElement();
            UiKit.ApplyTheme(root, ManifestEditorElement.StyleSheet);
            root.AddToClassList("am");
            root.Add(UiKit.Text($"Add {ModelStatus.GetModelName(_repository.Repository)}", "hf-heading", "am-title"));

            root.Add(UiKit.Text("Add to", "am-label"));
            var targets = new List<string>();
            if (!_isUsedByProject)
            {
                targets.Add(ProjectTargetLabel);
            }

            targets.AddRange(_packages.Select(package => package.Label));
            _target = new DropdownField(targets, 0)
            {
                tooltip = "The project manifest in ProjectSettings, or the manifest of a package in this project's Packages folder."
            };
            _target.RegisterValueChangedCallback(_ => UpdateState());
            root.Add(_target);

            root.Add(UiKit.Text("Revision", "am-label"));
            if (_repository.Refs.Count > 0)
            {
                var revision = new DropdownField(_repository.Refs.ToList(), Math.Max(0, _repository.Refs.ToList().IndexOf(_revision)))
                {
                    tooltip = "The cached revisions. The manifest pins this revision."
                };
                revision.RegisterValueChangedCallback(evt =>
                {
                    _revision = evt.newValue;
                    _filesChosen = false;
                    _files = GetDefaultFiles(_revision);
                    UpdateState();
                });
                root.Add(revision);
            }
            else
            {
                root.Add(UiKit.Text("No named revision in the cache", "hf-muted"));
            }

            root.Add(UiKit.Text("Files", "am-label"));
            var files = UiKit.Element("am-files");
            _filesButton = UiKit.Button("Choose…", OpenFilePicker);
            _filesButton.tooltip = "Pick the files from the model's file list on Hugging Face. Files that are not cached are downloaded.";
            files.Add(_filesButton);
            _filesSummary = UiKit.Text(string.Empty);
            files.Add(_filesSummary);
            root.Add(files);

            _problem = UiKit.Text(string.Empty, "hf-small", "am-problem");
            root.Add(_problem);

            var footer = UiKit.Element("am-footer");
            footer.Add(UiKit.Button("Cancel", () => editorWindow?.Close()));
            _addButton = UiKit.PrimaryButton("Add", Add);
            footer.Add(_addButton);
            root.Add(footer);

            UpdateState();
            return root;
        }

        private string[] GetDefaultFiles(string revision)
        {
            return revision == null
                ? Array.Empty<string>()
                : CacheOverviewService.GetCachedModelFiles(CacheOverviewService.GetSnapshotFiles(_repository.FolderPath, revision)).ToArray();
        }

        private string GetProblem()
        {
            if (_revision == null)
            {
                return "Download this model with a branch or tag name to add it from the cache.";
            }

            return _files.Length == 0 ? "No .onnx or .sentis file of this revision is cached. Choose the files to use." : null;
        }

        private void UpdateState()
        {
            if (_filesSummary == null)
            {
                return;
            }

            var count = _files.Length;
            var files = count == 1 ? "1 file" : $"{count} files";
            _filesSummary.text = count == 0 ? "None" : _filesChosen ? $"{files} chosen" : $"{files} · the cached model files";
            _filesSummary.tooltip = string.Join("\n", _files);
            _filesButton.SetEnabled(_revision != null);
            var problem = GetProblem();
            _problem.text = problem ?? string.Empty;
            UiKit.SetVisible(_problem, problem != null);
            _addButton.text = string.Equals(_target.value, ProjectTargetLabel, StringComparison.Ordinal) ? "Add to Project" : "Add to Package";
            _addButton.SetEnabled(problem == null && _target.index >= 0);
        }

        private void OpenFilePicker()
        {
            UnityEditor.PopupWindow.Show(
                _filesButton.worldBound,
                new FileSelectionPopup(
                    _repository.Repository,
                    _revision,
                    _files.Select(path => new ModelDependencyFileDefinition(path)).ToArray(),
                    allowOptional: false,
                    selected =>
                    {
                        _files = selected?.Select(file => file.Path).ToArray() ?? Array.Empty<string>();
                        _filesChosen = true;
                        UpdateState();
                    }));
        }

        private void Add()
        {
            if (GetProblem() != null || _target.index < 0)
            {
                return;
            }

            string errorMessage;
            var packageIndex = _isUsedByProject ? _target.index : _target.index - 1;
            var added = packageIndex < 0
                ? ProjectSettingsModelManifestStore.TryAddModelId(_repository.Repository, _revision, _files, out errorMessage)
                : LocalPackageManifestStore.TryAddModelId(_packages[packageIndex], _repository.Repository, _revision, _files, out errorMessage);
            if (!added)
            {
                EditorUtility.DisplayDialog("Could Not Add Model", errorMessage, "OK");
                return;
            }

            editorWindow?.Close();
            ProjectSettingsProvider.Refresh();
            _onAdded?.Invoke(_revision);
        }
    }
}
