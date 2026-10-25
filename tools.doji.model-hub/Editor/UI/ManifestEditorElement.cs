using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine.UIElements;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Host-specific options for <see cref="ManifestEditorElement"/>.
    /// </summary>
    internal sealed class ManifestEditorOptions
    {
        public string Title { get; set; } = "Models";
        public string Description { get; set; }
        public string EmptyMessage { get; set; } = "No models yet. Add the Hugging Face models this manifest needs.";
        public bool ShowOpenModelHubButton { get; set; }
        public Action AfterApply { get; set; }
    }

    /// <summary>
    /// Edits one manifest's model list: model id, revision, file selection, and Hugging Face validation.
    /// Used by Project Settings and by the manifest inspectors.
    /// </summary>
    internal sealed class ManifestEditorElement : VisualElement
    {
        internal const string StyleSheet = "ManifestEditor.uss";
        private const string RevisionPlaceholder = "main";

        private readonly IManifestDocumentStore _store;
        private readonly ManifestEditorOptions _options;
        private readonly ManifestModelValidationController _validation;
        private readonly Label _count;
        private readonly VisualElement _body;
        private readonly Button _revertButton;
        private readonly Button _applyButton;
        private readonly List<(ManifestModelEntry Entry, VisualElement Status, Button Files)> _rows = new();

        private int _version = 1;
        private string _loadedStateKey = string.Empty;
        private string _loadErrorMessage;
        private List<ManifestModelEntry> _models = new();

        public ManifestEditorElement(IManifestDocumentStore store, ManifestEditorOptions options = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _options = options ?? new ManifestEditorOptions();
            _validation = new ManifestModelValidationController(
                _store.ShouldPersistValidationCache,
                canValidate: () => _store.IsEditable,
                requestRepaint: UpdateRowStatuses);

            AddToClassList("me");
            var header = UiKit.Element("me-header");
            header.Add(UiKit.Text(_options.Title, "hf-heading"));
            _count = UiKit.Text(string.Empty, "me-header__count");
            header.Add(_count);
            header.Add(UiKit.Spacer());
            if (!_store.IsEditable)
            {
                header.Add(UiKit.Tag("Read-only", "Embed the package to edit its manifest."));
            }

            if (_options.ShowOpenModelHubButton)
            {
                header.Add(UiKit.LinkButton("Open Model Hub", () => ModelHubWindow.OpenWindow()));
            }

            Add(header);
            if (!string.IsNullOrWhiteSpace(_options.Description))
            {
                Add(UiKit.Text(_options.Description, "hf-section__hint"));
            }

            _body = new VisualElement();
            Add(_body);

            var footer = UiKit.Element("me-footer");
            if (_store.IsEditable)
            {
                footer.Add(UiKit.Button("+  Add Model", AddRow, "hf-button--first"));
            }

            footer.Add(UiKit.Spacer());
            _revertButton = UiKit.Button("Revert", () =>
            {
                Reload();
                QueueValidation();
            });
            _applyButton = UiKit.PrimaryButton("Apply", () => Apply());
            if (_store.IsEditable)
            {
                footer.Add(_revertButton);
                footer.Add(_applyButton);
            }

            Add(footer);

            if (!_store.IsEditable)
            {
                var note = UiKit.Callout(
                    UiTone.Neutral,
                    null,
                    "This manifest belongs to an installed package and cannot be changed here. To edit it, embed the package in this project.");
                note.AddToClassList("me-note");
                Add(note);
            }

            Reload();
        }

        public bool HasPendingChanges =>
            !string.Equals(ModelManifestInspectorUtility.BuildStateKey(_version, _models.ToArray()), _loadedStateKey, StringComparison.Ordinal);

        public string DisplayName => _store.DisplayName;

        /// <summary>
        /// Reloads the manifest from disk and discards unapplied edits.
        /// </summary>
        public void Reload()
        {
            _version = 1;
            _models = new List<ManifestModelEntry>();
            _loadErrorMessage = null;
            if (_store.TryLoad(out var document, out var errorMessage))
            {
                if (document != null)
                {
                    _version = document.version;
                    _models = (document.models ?? Array.Empty<ModelManifestInspectorUtility.ManifestModelDocument>())
                        .Select(ManifestModelEntry.FromDocumentModel)
                        .ToList();
                }
            }
            else
            {
                _loadErrorMessage = errorMessage;
            }

            _loadedStateKey = ModelManifestInspectorUtility.BuildStateKey(_version, _models.ToArray());
            _validation.RefreshFromCache(GetValidationTargets());
            _validation.QueueUnknownValidationIfNeeded();
            RebuildRows();
        }

        /// <summary>
        /// Asks whether to apply or discard unapplied edits. Returns false when the user cancels.
        /// </summary>
        public bool PromptForUnsavedChanges()
        {
            if (!HasPendingChanges)
            {
                return true;
            }

            var choice = EditorUtility.DisplayDialogComplex(
                "Unapplied Manifest Changes",
                $"You changed the models in {_store.DisplayName}. Apply the changes?",
                "Apply",
                "Discard",
                "Cancel");
            switch (choice)
            {
                case 0:
                    return Apply();
                case 1:
                    Reload();
                    return true;
                default:
                    return false;
            }
        }

        private void RebuildRows()
        {
            _body.Clear();
            _rows.Clear();
            _count.text = _models.Count > 0 ? _models.Count.ToString() : string.Empty;

            if (!string.IsNullOrWhiteSpace(_loadErrorMessage))
            {
                _body.Add(UiKit.Callout(UiTone.Error, "The manifest could not be read.", _loadErrorMessage));
                UpdateButtons();
                return;
            }

            var table = UiKit.Element("me-table");
            var columns = UiKit.Element("me-columns");
            columns.Add(UiKit.Text("MODEL ID", "me-col-id"));
            columns.Add(UiKit.Text("REVISION", "me-col-revision"));
            columns.Add(UiKit.Text("FILES", "me-col-files"));
            columns.Add(UiKit.Element("me-col-status"));
            if (_store.IsEditable)
            {
                columns.Add(UiKit.Element("me-col-remove"));
            }

            table.Add(columns);

            if (_models.Count == 0)
            {
                table.Add(UiKit.Text(_options.EmptyMessage, "me-empty"));
            }

            for (var index = 0; index < _models.Count; index++)
            {
                var row = BuildRow(_models[index]);
                row.EnableInClassList("me-row--last", index == _models.Count - 1);
                table.Add(row);
            }

            _body.Add(table);
            UpdateRowStatuses();
            UpdateButtons();
        }

        private VisualElement BuildRow(ManifestModelEntry entry)
        {
            var row = UiKit.Element("me-row");
            Button filesButton = null;

            if (_store.IsEditable)
            {
                var idField = new TextField { value = entry.HasAnyModelIdContent ? entry.ModelId : string.Empty, isDelayed = true };
                idField.AddToClassList("me-col-id");
                idField.textEdition.placeholder = "owner/name or Hugging Face URL";
                idField.tooltip = "Hugging Face model id, for example julienkay/sentis-MiDaS. You can paste a model URL.";
                idField.RegisterValueChangedCallback(evt =>
                {
                    ManifestModelEntry.TryParseModelId(evt.newValue, out var owner, out var name);
                    entry.Owner = owner;
                    entry.Name = name;
                    var normalized = entry.HasAnyModelIdContent ? entry.ModelId : string.Empty;
                    if (!string.Equals(normalized, evt.newValue, StringComparison.Ordinal))
                    {
                        idField.SetValueWithoutNotify(normalized);
                    }

                    HandleEdited();
                });
                row.Add(idField);

                var revisionField = new TextField { value = entry.Revision ?? string.Empty, isDelayed = true };
                revisionField.AddToClassList("me-col-revision");
                revisionField.textEdition.placeholder = RevisionPlaceholder;
                revisionField.tooltip = "Branch, tag, or commit. Leave empty to use main.";
                revisionField.RegisterValueChangedCallback(evt =>
                {
                    entry.Revision = evt.newValue?.Trim() ?? string.Empty;
                    HandleEdited();
                });
                row.Add(revisionField);

                filesButton = new Button { text = entry.FileSelectionSummary };
                filesButton.AddToClassList("hf-button");
                filesButton.AddToClassList("me-col-files");
                filesButton.clicked += () => OpenFilePicker(entry, filesButton);
                row.Add(filesButton);
            }
            else
            {
                row.Add(UiKit.Text(entry.ModelId, "me-col-id"));
                row.Add(UiKit.Text(string.IsNullOrWhiteSpace(entry.Revision) ? RevisionPlaceholder : entry.Revision, "me-col-revision", "hf-muted"));
                row.Add(UiKit.Text(entry.FileSelectionSummary, "me-col-files", "hf-muted"));
            }

            var statusColumn = UiKit.Element("me-col-status");
            row.Add(statusColumn);

            if (_store.IsEditable)
            {
                var remove = UiKit.IconButton("Toolbar Minus", "Remove model", () =>
                {
                    _models.Remove(entry);
                    HandleEdited();
                    RebuildRows();
                });
                remove.AddToClassList("me-col-remove");
                row.Add(remove);
            }

            _rows.Add((entry, statusColumn, filesButton));
            return row;
        }

        private void OpenFilePicker(ManifestModelEntry entry, Button anchor)
        {
            UnityEditor.PopupWindow.Show(
                anchor.worldBound,
                new FileSelectionPopup(
                    entry.ModelId,
                    string.IsNullOrWhiteSpace(entry.Revision) ? RevisionPlaceholder : entry.Revision,
                    entry.FileEntries,
                    allowOptional: true,
                    files =>
                    {
                        entry.FileEntries = ModelDependencyDefinition.NormalizeFileEntries(files);
                        anchor.text = entry.FileSelectionSummary;
                        HandleEdited();
                    }));
        }

        private void AddRow()
        {
            _models.Add(new ManifestModelEntry(string.Empty, string.Empty, string.Empty));
            RebuildRows();
            var field = _body.Query<TextField>(className: "me-col-id").Last();
            field?.schedule.Execute(() => field.Focus());
            UpdateButtons();
        }

        private void HandleEdited()
        {
            _validation.RefreshFromCacheIfNeeded(GetValidationTargets());
            _validation.QueueUnknownValidationIfNeeded();
            _count.text = _models.Count > 0 ? _models.Count.ToString() : string.Empty;
            UpdateRowStatuses();
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            var hasChanges = HasPendingChanges;
            _revertButton.SetEnabled(hasChanges);
            _applyButton.SetEnabled(hasChanges && string.IsNullOrWhiteSpace(_loadErrorMessage));
        }

        private void UpdateRowStatuses()
        {
            foreach (var (entry, statusColumn, filesButton) in _rows)
            {
                statusColumn.Clear();
                var state = entry.HasCompleteModelId ? _validation.GetState(entry.ModelId, entry.Revision) : null;
                if (state != null)
                {
                    statusColumn.Add(BuildCheckBadge(state));
                }

                if (filesButton != null)
                {
                    var canOpen = ManifestModelEntry.CanOpenFileSelection(entry, state, hasValidationProvider: true);
                    filesButton.SetEnabled(canOpen);
                    filesButton.tooltip = canOpen
                        ? "Choose which files to download. Leave empty to use the whole repository."
                        : state?.Status == ManifestModelValidationStatus.Invalid
                            ? state.Message ?? "This model was not found on Hugging Face."
                            : "Enter a model id that exists on Hugging Face first.";
                }
            }
        }

        /// <summary>
        /// Builds the small round badge that shows whether a model id exists on Hugging Face.
        /// </summary>
        internal static VisualElement BuildCheckBadge(ManifestModelValidationState state)
        {
            var (text, className, tooltip) = state.Status switch
            {
                ManifestModelValidationStatus.Checking => ("…", "me-check--checking", "Checking Hugging Face…"),
                ManifestModelValidationStatus.Valid => ("✓", "me-check--valid", $"Found on Hugging Face ({state.Revision})."),
                ManifestModelValidationStatus.Invalid => ("!", "me-check--invalid", state.Message ?? "Not found on Hugging Face."),
                _ => ("?", "me-check--unknown", "Not checked yet.")
            };
            var badge = UiKit.Text(text, "me-check", className);
            badge.tooltip = tooltip;
            return badge;
        }

        private bool Apply()
        {
            var incompleteIndex = _models.FindIndex(ManifestModelEntry.IsIncomplete);
            if (incompleteIndex >= 0)
            {
                EditorUtility.DisplayDialog(
                    "Incomplete Model ID",
                    $"Row {incompleteIndex + 1} needs a model id in the form owner/name.",
                    "OK");
                return false;
            }

            if (!_store.TrySave(_version, _models.ToArray(), out var errorMessage))
            {
                EditorUtility.DisplayDialog("Could Not Save Manifest", errorMessage, "OK");
                return false;
            }

            _loadedStateKey = ModelManifestInspectorUtility.BuildStateKey(_version, _models.ToArray());
            _loadErrorMessage = null;
            QueueValidation();
            UpdateButtons();
            _options.AfterApply?.Invoke();
            return true;
        }

        private void QueueValidation()
        {
            _validation.QueueValidation(GetValidationTargets());
            UpdateRowStatuses();
        }

        private ManifestModelValidationController.ValidationTarget[] GetValidationTargets()
        {
            return _models
                .Where(model => model?.HasCompleteModelId == true)
                .Select(model => new ManifestModelValidationController.ValidationTarget(model.ModelId, model.Revision))
                .ToArray();
        }
    }
}
