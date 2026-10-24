using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Adds one Hugging Face model to the project manifest or to a local package manifest.
    /// </summary>
    internal sealed class AddModelPopup : PopupWindowContent
    {
        private const string ProjectTargetLabel = "This project";
        private const long ValidationDelayMs = 400;

        private readonly Action _onAdded;
        private readonly IReadOnlyList<LocalPackageManifestStore.LocalPackageOption> _packages;

        private TextField _idField;
        private VisualElement _check;
        private DropdownField _target;
        private Button _filesButton;
        private Label _filesSummary;
        private Button _addButton;
        private IVisualElementScheduledItem _pendingValidation;
        private string[] _selectedFiles = Array.Empty<string>();
        private string _validatedModelId;
        private ManifestModelValidationStatus _status = ManifestModelValidationStatus.Unknown;
        private string _statusMessage;
        private int _validationVersion;

        public AddModelPopup(Action onAdded)
        {
            _onAdded = onAdded;
            _packages = LocalPackageManifestStore.GetProjectLocalPackages();
        }

        public override Vector2 GetWindowSize()
        {
            return new Vector2(380f, 262f);
        }

        public override void OnGUI(Rect rect)
        {
        }

        public override VisualElement CreateGUI()
        {
            var root = new VisualElement();
            UiKit.ApplyTheme(root, ManifestEditorElement.StyleSheet);
            root.AddToClassList("am");

            root.Add(UiKit.Text("Add a model", "hf-heading", "am-title"));

            root.Add(UiKit.Text("Hugging Face model", "am-label"));
            _idField = new TextField();
            _idField.textEdition.placeholder = "owner/name or model URL";
            _idField.RegisterValueChangedCallback(_ => ScheduleValidation());
            _idField.RegisterCallback<KeyDownEvent>(evt =>
            {
                if ((evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) && CanAdd)
                {
                    Add();
                }
            }, TrickleDown.TrickleDown);
            root.Add(_idField);
            _check = UiKit.Element("am-check");
            root.Add(_check);

            root.Add(UiKit.Text("Add to", "am-label"));
            var choices = new List<string> { ProjectTargetLabel };
            choices.AddRange(_packages.Select(package => package.Label));
            _target = new DropdownField(choices, 0);
            _target.tooltip = "The project manifest, or the manifest of a package in this project's Packages folder.";
            root.Add(_target);

            root.Add(UiKit.Text("Files", "am-label"));
            var files = UiKit.Element("am-files");
            _filesButton = UiKit.Button("Choose…", OpenFilePicker);
            files.Add(_filesButton);
            _filesSummary = UiKit.Text("All files");
            files.Add(_filesSummary);
            root.Add(files);

            var footer = UiKit.Element("am-footer");
            footer.Add(UiKit.Button("Cancel", () => editorWindow?.Close()));
            _addButton = UiKit.PrimaryButton("Add", Add);
            footer.Add(_addButton);
            root.Add(footer);

            UpdateState();
            root.schedule.Execute(() => _idField.Focus());
            return root;
        }

        private string CurrentModelId =>
            ManifestModelEntry.TryParseModelId(_idField?.value, out var owner, out var name) ? $"{owner}/{name}" : null;

        private RepositoryModelFormat? _format;

        private bool IsFound =>
            _status == ManifestModelValidationStatus.Valid &&
            string.Equals(_validatedModelId, CurrentModelId, StringComparison.Ordinal);

        private bool CanAdd => IsFound && GetFormatProblem(_format, _selectedFiles.Length) == null;

        /// <summary>
        /// Why the model cannot be added with the current file choice, or null. A repository with both ONNX and Sentis
        /// files needs a file list; one without model files cannot load. Unknown formats do not block adding.
        /// </summary>
        internal static string GetFormatProblem(RepositoryModelFormat? format, int selectedFileCount)
        {
            if (selectedFileCount > 0 || format == null)
            {
                return null;
            }

            return format switch
            {
                RepositoryModelFormat.Mixed => "This repository has ONNX and Sentis files. Choose the files you use.",
                RepositoryModelFormat.None => "This repository has no .onnx or .sentis files.",
                _ => null
            };
        }

        private void ScheduleValidation()
        {
            _validationVersion++;
            _status = ManifestModelValidationStatus.Unknown;
            _statusMessage = null;
            _format = null;
            _selectedFiles = Array.Empty<string>();
            _pendingValidation?.Pause();
            if (CurrentModelId != null)
            {
                _pendingValidation = _idField.schedule.Execute(Validate).StartingIn(ValidationDelayMs);
            }

            UpdateState();
        }

        private async void Validate()
        {
            var modelId = CurrentModelId;
            if (modelId == null)
            {
                return;
            }

            var version = _validationVersion;
            _status = ManifestModelValidationStatus.Checking;
            UpdateState();

            ManifestModelValidationState result;
            try
            {
                result = await ManifestModelValidationController.ValidateDependencyAsync(modelId, writePersistentCache: false);
            }
            catch (Exception exception)
            {
                result = new ManifestModelValidationState(modelId, null, ManifestModelValidationStatus.Invalid, exception.Message);
            }

            if (version != _validationVersion || _idField?.panel == null)
            {
                return;
            }

            _validatedModelId = modelId;
            _status = result.Status == ManifestModelValidationStatus.Valid
                ? ManifestModelValidationStatus.Valid
                : ManifestModelValidationStatus.Invalid;
            _statusMessage = result.Message;
            UpdateState();

            if (_status != ManifestModelValidationStatus.Valid)
            {
                return;
            }

            try
            {
                var inventory = await new HttpClient().GetRepositoryInventoryAsync(modelId, "main", System.Threading.CancellationToken.None);
                if (version == _validationVersion && _idField?.panel != null)
                {
                    _format = ModelFiles.Describe(inventory.Files.Select(file => file.RepoPath));
                    UpdateState();
                }
            }
            catch (Exception)
            {
                // The format is a hint; adding still works without it.
            }
        }

        private void UpdateState()
        {
            if (_check == null)
            {
                return;
            }

            _check.Clear();
            var hasId = CurrentModelId != null;
            if (hasId && _status != ManifestModelValidationStatus.Unknown)
            {
                _check.Add(ManifestEditorElement.BuildCheckBadge(
                    new ManifestModelValidationState(CurrentModelId, null, _status, _statusMessage)));
                _check.Add(UiKit.Text(_status switch
                {
                    ManifestModelValidationStatus.Checking => "Checking Hugging Face…",
                    ManifestModelValidationStatus.Valid => _format == null
                        ? "Found on Hugging Face."
                        : $"Found on Hugging Face · {ModelFiles.GetLabel(_format.Value)}",
                    _ => _statusMessage ?? "Not found on Hugging Face. Check the spelling, or sign in for gated models."
                }));
            }
            else if (!string.IsNullOrWhiteSpace(_idField?.value) && !hasId)
            {
                _check.Add(UiKit.Text("Use the form owner/name, for example julienkay/sentis-MiDaS.", "hf-muted"));
            }

            var formatProblem = IsFound ? GetFormatProblem(_format, _selectedFiles.Length) : null;
            if (formatProblem != null)
            {
                _check.Add(UiKit.Text(formatProblem, "hf-small", "am-problem"));
            }

            _filesButton?.SetEnabled(IsFound);
            if (_filesSummary != null)
            {
                _filesSummary.text = _selectedFiles.Length == 0
                    ? "All files"
                    : _selectedFiles.Length == 1 ? "1 file" : $"{_selectedFiles.Length} files";
            }

            _addButton?.SetEnabled(CanAdd);
        }

        private void OpenFilePicker()
        {
            UnityEditor.PopupWindow.Show(
                _filesButton.worldBound,
                new FileSelectionPopup(
                    CurrentModelId,
                    "main",
                    _selectedFiles.Select(path => new ModelDependencyFileDefinition(path)).ToArray(),
                    allowOptional: false,
                    files =>
                    {
                        _selectedFiles = files?.Select(file => file.Path).ToArray() ?? Array.Empty<string>();
                        UpdateState();
                    }));
        }

        private void Add()
        {
            if (!CanAdd || !ManifestModelEntry.TryParseModelId(_idField.value, out var owner, out var name))
            {
                return;
            }

            var files = _selectedFiles.Length > 0 ? _selectedFiles : null;
            string errorMessage;
            bool added;
            var targetIndex = _target.index;
            if (targetIndex <= 0)
            {
                added = ProjectSettingsModelManifestStore.TryAddModel(owner, name, files, out errorMessage);
            }
            else
            {
                added = LocalPackageManifestStore.TryAddModel(_packages[targetIndex - 1], owner, name, files, out errorMessage);
            }

            if (!added)
            {
                EditorUtility.DisplayDialog("Could Not Add Model", errorMessage, "OK");
                return;
            }

            editorWindow?.Close();
            ProjectSettingsProvider.Refresh();
            InternalEditorUtility.RepaintAllViews();
            _onAdded?.Invoke();
        }
    }
}
