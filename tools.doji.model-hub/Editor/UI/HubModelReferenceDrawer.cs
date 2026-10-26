using System;
using System.Collections.Generic;
using System.Linq;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// The model files a <see cref="HubModelReference"/> picker offers: every model file the package declares, and
    /// whether this project includes it.
    /// </summary>
    internal static class HubModelPicker
    {
        internal enum FileState
        {
            /// <summary>The project includes the file and it is set up.</summary>
            Included,

            /// <summary>An optional file the project did not turn on.</summary>
            NotTurnedOn,

            /// <summary>The project uses the file, but it is not downloaded or set up yet.</summary>
            NotSetUp
        }

        internal sealed class Entry
        {
            public string PackageId;
            public string OwnerName;
            public string RepoId;
            public string Revision;
            public string RepoPath;
            public string Label;
            public FileState State;
            public DiscoveredModelDependency Dependency;

            public string DisplayName => HubModelFile.GetDisplayName(Label, RepoPath);

            public bool Matches(string repoId, string repoPath)
            {
                return string.Equals(RepoId, repoId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(RepoPath, repoPath, StringComparison.Ordinal);
            }
        }

        private static Entry[] _entries;

        static HubModelPicker()
        {
            BackgroundSyncManager.SyncStateChanged += Invalidate;
            EditorApplication.projectChanged += Invalidate;
        }

        public static void Invalidate()
        {
            _entries = null;
        }

        /// <summary>
        /// Lists the model files of one package, or of every package when <paramref name="packageId"/> is null.
        /// </summary>
        public static IReadOnlyList<Entry> GetEntries(string packageId)
        {
            _entries ??= Build(EditorOperations.CreateStatusReport(), Hub.GetModelFiles(null));
            return _entries
                .Where(entry => packageId == null || string.Equals(entry.PackageId, packageId, StringComparison.Ordinal))
                .ToArray();
        }

        internal static Entry[] Build(StatusReport report, IReadOnlyList<HubModelFile> includedFiles)
        {
            var included = new HashSet<string>(
                (includedFiles ?? Array.Empty<HubModelFile>()).Select(file => Key(file.PackageId, file.RepoId, file.RepoPath)),
                StringComparer.OrdinalIgnoreCase);
            var entries = new List<Entry>();
            foreach (var status in report?.DependencyStatuses ?? Array.Empty<DependencyStatus>())
            {
                var dependency = status.Dependency;
                var selected = new HashSet<string>(dependency.SelectedFiles, StringComparer.Ordinal);
                var files = dependency.HasExplicitFileSelection
                    ? dependency.FileEntries.Where(file => ModelFiles.IsModelFile(file.Path)).ToArray()
                    : (status.ImportedMetadata?.RuntimeFiles ?? Array.Empty<ImportedRuntimeFileMetadata>())
                        .Select(file => new ModelDependencyFileDefinition(file.RepoPath))
                        .ToArray();
                foreach (var file in files)
                {
                    entries.Add(new Entry
                    {
                        PackageId = dependency.PackageId,
                        OwnerName = ModelStatus.GetOwnerDisplayName(dependency),
                        RepoId = dependency.Repository,
                        Revision = dependency.Revision,
                        RepoPath = file.Path,
                        Label = file.Label,
                        Dependency = dependency,
                        State = included.Contains(Key(dependency.PackageId, dependency.Repository, file.Path))
                            ? FileState.Included
                            : file.Optional && !selected.Contains(file.Path) ? FileState.NotTurnedOn : FileState.NotSetUp
                    });
                }
            }

            return entries.ToArray();
        }

        /// <summary>
        /// Turns on an optional file for this project and starts downloading it.
        /// </summary>
        public static void TurnOn(Entry entry)
        {
            var enabled = entry.Dependency.GetEnabledOptionalRepoPaths().ToList();
            if (!enabled.Contains(entry.RepoPath, StringComparer.Ordinal))
            {
                enabled.Add(entry.RepoPath);
            }

            new ProjectOptionalFileSelectionSource().SetEnabledOptionalFiles(entry.PackageId, entry.Dependency.ModelId, enabled);
            Invalidate();
            ModelHubWindow.RefreshOpenWindows();
            EditorOperations.StartBackgroundSync();
        }

        private static string Key(string packageId, string repoId, string repoPath)
        {
            return $"{packageId}\n{repoId}\n{repoPath}";
        }
    }

    /// <summary>
    /// Inspector picker for <see cref="HubModelReference"/> fields: a dropdown of the package's model files, plus a
    /// choice for a model asset of your own.
    /// </summary>
    [CustomPropertyDrawer(typeof(HubModelReference))]
    [CustomPropertyDrawer(typeof(HubModelAttribute))]
    internal sealed class HubModelReferenceDrawer : PropertyDrawer
    {
        internal const string NoneChoice = "None";
        internal const string AssetChoice = "Model asset of your own…";

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement();
            var packageId = (attribute as HubModelAttribute)?.PackageId;
            var packageProperty = property.FindPropertyRelative("_packageId");
            var repoProperty = property.FindPropertyRelative("_repoId");
            var pathProperty = property.FindPropertyRelative("_repoPath");
            var assetProperty = property.FindPropertyRelative("_modelAsset");
            if (packageProperty == null || repoProperty == null || pathProperty == null || assetProperty == null)
            {
                root.Add(new HelpBox("[HubModel] works only on HubModelReference fields.", HelpBoxMessageType.Error));
                return root;
            }

            var entries = HubModelPicker.GetEntries(packageId);
            var choices = BuildChoices(entries, packageId == null);
            var dropdown = new DropdownField(property.displayName, choices.Select(choice => choice.Text).ToList(), 0);
            dropdown.AddToClassList(BaseField<string>.alignedFieldUssClassName);
            var assetField = new ObjectField("Model Asset") { objectType = typeof(ModelAsset), allowSceneObjects = false };
            assetField.AddToClassList(BaseField<UnityEngine.Object>.alignedFieldUssClassName);
            assetField.BindProperty(assetProperty);
            var status = new VisualElement();
            root.Add(dropdown);
            root.Add(assetField);
            root.Add(status);

            void Refresh()
            {
                property.serializedObject.UpdateIfRequiredOrScript();
                var isAsset = assetProperty.objectReferenceValue != null || dropdown.value == AssetChoice;
                var current = isAsset ? null : entries.FirstOrDefault(entry => entry.Matches(repoProperty.stringValue, pathProperty.stringValue));
                string text;
                if (isAsset)
                {
                    text = AssetChoice;
                }
                else if (current != null)
                {
                    text = choices.First(choice => choice.Entry == current).Text;
                }
                else if (string.IsNullOrWhiteSpace(pathProperty.stringValue))
                {
                    text = NoneChoice;
                }
                else
                {
                    text = $"{HubModelFile.GetDisplayName(null, pathProperty.stringValue)} (not in the manifest)";
                    if (!dropdown.choices.Contains(text))
                    {
                        dropdown.choices = choices.Select(choice => choice.Text).Append(text).ToList();
                    }
                }

                dropdown.SetValueWithoutNotify(text);
                dropdown.tooltip = isAsset || string.IsNullOrWhiteSpace(pathProperty.stringValue)
                    ? null
                    : $"{repoProperty.stringValue}:{pathProperty.stringValue}";
                assetField.style.display = isAsset ? DisplayStyle.Flex : DisplayStyle.None;
                BuildStatus(status, isAsset, current, pathProperty.stringValue, Refresh);
            }

            dropdown.RegisterValueChangedCallback(evt =>
            {
                var choice = choices.FirstOrDefault(item => item.Text == evt.newValue);
                if (evt.newValue == AssetChoice)
                {
                    SetFile(property, null, null, null);
                }
                else if (choice != null)
                {
                    assetProperty.objectReferenceValue = null;
                    SetFile(property, choice.Entry?.PackageId, choice.Entry?.RepoId, choice.Entry?.RepoPath);
                }

                Refresh();
            });
            assetField.RegisterValueChangedCallback(_ => Refresh());
            Refresh();
            return root;
        }

        private sealed class Choice
        {
            public string Text;
            public HubModelPicker.Entry Entry;
        }

        private static List<Choice> BuildChoices(IReadOnlyList<HubModelPicker.Entry> entries, bool showPackage)
        {
            var showRepository = entries.Select(entry => entry.RepoId).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
            var choices = new List<Choice> { new() { Text = NoneChoice } };
            foreach (var entry in entries)
            {
                var parts = new List<string>();
                if (showPackage)
                {
                    parts.Add(entry.OwnerName);
                }

                if (showRepository)
                {
                    parts.Add(entry.RepoId.Substring(entry.RepoId.IndexOf('/') + 1));
                }

                parts.Add(entry.DisplayName);
                var text = string.Join(" · ", parts);

                if (entry.State == HubModelPicker.FileState.NotTurnedOn)
                {
                    text += " (not included)";
                }

                // Dropdown choices must be unique; two files with one label fall back to their paths.
                if (choices.Any(choice => choice.Text == text))
                {
                    text = $"{text} ({entry.RepoPath})";
                }

                choices.Add(new Choice { Text = text, Entry = entry });
            }

            choices.Add(new Choice { Text = AssetChoice });
            return choices;
        }

        private static void SetFile(SerializedProperty property, string packageId, string repoId, string repoPath)
        {
            property.FindPropertyRelative("_packageId").stringValue = packageId ?? string.Empty;
            property.FindPropertyRelative("_repoId").stringValue = repoId ?? string.Empty;
            property.FindPropertyRelative("_repoPath").stringValue = repoPath ?? string.Empty;
            property.serializedObject.ApplyModifiedProperties();
        }

        private static void BuildStatus(VisualElement status, bool isAsset, HubModelPicker.Entry entry, string repoPath, Action refresh)
        {
            status.Clear();
            if (isAsset)
            {
                return;
            }

            if (entry == null)
            {
                if (!string.IsNullOrWhiteSpace(repoPath))
                {
                    status.Add(new HelpBox($"No package manifest in this project declares '{repoPath}'.", HelpBoxMessageType.Warning));
                }

                return;
            }

            switch (entry.State)
            {
                case HubModelPicker.FileState.NotTurnedOn:
                    var help = new HelpBox("This optional file is not included in this project, so it does not load.", HelpBoxMessageType.Info);
                    help.Add(new Button(() =>
                    {
                        HubModelPicker.TurnOn(entry);
                        refresh();
                    }) { text = "Include" });
                    status.Add(help);
                    break;
                case HubModelPicker.FileState.NotSetUp:
                    var setup = new HelpBox("This file is not downloaded and set up yet.", HelpBoxMessageType.Info);
                    setup.Add(new Button(() => ModelHubWindow.OpenWindow(entry.RepoId, entry.Revision)) { text = "Open AI Model Hub" });
                    status.Add(setup);
                    break;
            }
        }
    }
}
