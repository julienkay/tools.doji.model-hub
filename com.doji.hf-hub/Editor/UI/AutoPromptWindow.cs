using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Utility window version of the first-run auto prompt for discovered manifest dependencies.
    /// </summary>
    internal sealed class AutoPromptWindow : EditorWindow
    {
        private static class Content
        {
            public const string Title = "AI Model Hub";
            public const string SingleDependencyHeadline = "AI Model Required";
            public const string MultiDependencyHeadlineFormat = "{0} manifest dependencies need AI models.";
            public const string PackageLeadText = "Package";
            public const string RequiresText = "requires AI model";
            public const string SingleRepoBody = "AI Model Hub can download it into shared cache and import runtime files into this project.";
            public const string MultiRepoBody = "AI Model Hub can download them into shared cache and import runtime files into this project.";
            public const string SingleConflictBody = "AI Model Hub found conflicting requested revisions for this repository. Resolve manifests to one revision before importing runtime files.";
            public const string MultiConflictBody = "AI Model Hub found conflicting requested revisions for these repositories. Resolve each repository to one revision before importing runtime files.";
            public const string SinglePackageConflictBody = "AI Model Hub found conflicting requested revisions for this repository. Some owners come from installed packages, so resolve by updating package versions, embedding/localizing package, or removing one dependency.";
            public const string MultiPackageConflictBody = "AI Model Hub found conflicting requested revisions for these repositories. Some owners come from installed packages, so resolve by updating package versions, embedding/localizing package, or removing one dependency.";
            public const string DependencyListLabel = "Dependencies";
            public const string DetailsLineFormat = "Needs attention in {0} manifest dependencies.";
            public const string DetailsIssuesFormat = "{0} discovery issues also need attention.";
            public const string EmptyMessage = "AI Model Hub prompt opened without status data.";
            public const string LaterButton = "Later";
            public const string OpenButton = "Open Model Hub";
            public const string ResolveConflictButton = "Resolve Conflict";
            public const string ImportAllButton = "Import All";
            public const string ImportButton = "Import";
            public const string DownloadButton = "Download";
            public const string ResolveConflictTooltip = "Open Model Hub to inspect the conflicting manifests and resolve each repository to one requested revision.";
            public const string ImportAllTooltip = "Downloads any missing models and imports all unresolved models into this project.";
            public const string ImportTooltip = "Imports this model into the project using files already in the shared cache.";
            public const string DownloadTooltip = "Downloads this model into the shared cache and imports it into the project.";
        }

        private const float WindowWidth = 500f;
        private const float WindowHeight = 320f;
        private const float MaxInitialWindowHeight = 520f;
        private const float OuterPadding = 12f;
        private const float TopPadding = 18f;
        private const float SectionPadding = 10f;

        private PromptViewModel _viewModel;
        private Vector2 _contentScrollPosition;
        private GUIContent _headlineIconContent;
        private GUIStyle _manifestLinkStyle;
        private GUIStyle _headlineStyle;
        private GUIStyle _headlineRowStyle;
        private GUIStyle _bodyStyle;
        private GUIStyle _detailsStyle;
        private GUIStyle _sentenceStyle;
        private GUIStyle _sentenceLinkStyle;
        private GUIStyle _eyebrowStyle;
        private GUIStyle _modelNameStyle;
        private GUIStyle _repositoryStyle;
        private GUIStyle _dependencyRowStyle;
        private GUIStyle _sectionStyle;
        private GUIStyle _footerStyle;

        internal static void ShowWindow(StatusReport report)
        {
            var existingWindow = GetOpenWindow();
            if (existingWindow != null)
            {
                existingWindow.Close();
            }

            var window = CreateInstance<AutoPromptWindow>();
            window.titleContent = new GUIContent(Content.Title);
            window.minSize = new Vector2(WindowWidth, WindowHeight);
            window._viewModel = BuildViewModel(report);
            CenterInMainWindow(window);
            window.ShowUtility();
            window.Focus();
        }

        /// <summary>
        /// Indicates whether the utility prompt window already exists, even if Unity has not focused it yet.
        /// </summary>
        internal static bool IsOpen()
        {
            return GetOpenWindow() != null;
        }

        private void OnEnable()
        {
            EnsureViewModel();
        }

        private void OnGUI()
        {
            EnsureViewModel();
            InitializeStyles();

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(OuterPadding);
                using (new EditorGUILayout.VerticalScope())
                {
                    GUILayout.Space(TopPadding);
                    DrawHeadline();
                    GUILayout.Space(8f);
                    using (var scrollView = new EditorGUILayout.ScrollViewScope(
                               _contentScrollPosition,
                               GUILayout.ExpandHeight(true)))
                    {
                        _contentScrollPosition = scrollView.scrollPosition;
                        DrawContentSection();
                    }
                    GUILayout.Space(8f);
                    DrawFooterSection();
                }
                GUILayout.Space(OuterPadding);
            }
        }

        /// <summary>
        /// Rebuilds the prompt view from current manifest discovery after domain reload clears the cached window state.
        /// </summary>
        private void EnsureViewModel()
        {
            if (_viewModel != null)
            {
                return;
            }

            _viewModel = BuildViewModel(EditorOperations.CreateStatusReport());
        }

        private static AutoPromptWindow GetOpenWindow()
        {
            return Resources.FindObjectsOfTypeAll<AutoPromptWindow>().FirstOrDefault();
        }

        private static void CenterInMainWindow(AutoPromptWindow window)
        {
            var mainWindowPosition = EditorGUIUtility.GetMainWindowPosition();
            var height = GetInitialWindowHeight(window._viewModel);
            var position = new Rect(
                mainWindowPosition.x + ((mainWindowPosition.width - WindowWidth) * 0.5f),
                mainWindowPosition.y + ((mainWindowPosition.height - height) * 0.5f),
                WindowWidth,
                height);
            window.position = position;
        }

        private static float GetInitialWindowHeight(PromptViewModel viewModel)
        {
            var dependentCount = viewModel?.Dependents?.Count ?? 0;
            var extraHeight = dependentCount <= 1
                ? 0f
                : (dependentCount - 1) * 44f;
            return Mathf.Min(WindowHeight + extraHeight, MaxInitialWindowHeight);
        }

        internal static PromptViewModel BuildViewModel(StatusReport report)
        {
            if (report == null)
            {
                return new PromptViewModel(
                    Content.EmptyMessage,
                    string.Empty,
                    string.Empty,
                    new DependentPromptEntry[0],
                    Content.ImportAllButton,
                    Content.ImportAllTooltip,
                    PromptPrimaryAction.StartSync);
            }

            var attentionStatuses = report.DependencyStatuses
                .Where(status => status.NeedsAttention)
                .ToArray();
            var dependents = attentionStatuses
                .Select(status => new DependentPromptEntry(
                    BuildOwnerLabel(status.Dependency),
                    status.Dependency.PackageDisplayName,
                    status.Dependency.PackageId,
                    status.Dependency.ModelId,
                    status.Dependency.Repository,
                    status.Dependency.ManifestPath))
                .OrderBy(entry => entry.DisplayLabel, System.StringComparer.Ordinal)
                .ToArray();

            var headline = dependents.Length switch
            {
                0 => Content.EmptyMessage,
                1 => Content.SingleDependencyHeadline,
                _ => string.Format(Content.MultiDependencyHeadlineFormat, dependents.Length)
            };

            var hasAttentionStatuses = attentionStatuses.Length > 0;
            var allAttentionStatusesAreRevisionConflicts = hasAttentionStatuses &&
                attentionStatuses.All(status => status.ReasonCode == DependencyReasonCode.RevisionConflict);
            var hasPackageManagerOwnedConflicts = allAttentionStatusesAreRevisionConflicts &&
                attentionStatuses.Any(status => ProjectSettingsLinks
                    .GetManifestOwnerResolutionInfo(status.Dependency)
                    .SuggestsPackageManager);
            var body = allAttentionStatusesAreRevisionConflicts
                ? hasPackageManagerOwnedConflicts
                    ? dependents.Length <= 1
                        ? Content.SinglePackageConflictBody
                        : Content.MultiPackageConflictBody
                    : dependents.Length <= 1
                        ? Content.SingleConflictBody
                        : Content.MultiConflictBody
                : dependents.Length <= 1
                    ? Content.SingleRepoBody
                    : Content.MultiRepoBody;

            var details = dependents.Length > 1
                ? string.Format(Content.DetailsLineFormat, dependents.Length)
                : string.Empty;

            if (report.DiscoveryIssues.Count > 0)
            {
                details = string.IsNullOrWhiteSpace(details)
                    ? string.Format(Content.DetailsIssuesFormat, report.DiscoveryIssues.Count)
                    : $"{details} {string.Format(Content.DetailsIssuesFormat, report.DiscoveryIssues.Count)}";
            }

            var primaryAction = BuildPrimaryAction(attentionStatuses);

            return new PromptViewModel(
                headline,
                body,
                details,
                dependents,
                primaryAction.Label,
                primaryAction.Tooltip,
                primaryAction.Action);
        }

        internal enum PromptPrimaryAction
        {
            StartSync,
            OpenModelHub
        }

        internal sealed class PromptViewModel
        {
            internal PromptViewModel(
                string headline,
                string body,
                string details,
                IReadOnlyList<DependentPromptEntry> dependents,
                string primaryActionLabel,
                string primaryActionTooltip,
                PromptPrimaryAction primaryAction)
            {
                Headline = headline;
                Body = body;
                Details = details;
                Dependents = dependents?.ToArray() ?? new DependentPromptEntry[0];
                PrimaryActionLabel = primaryActionLabel;
                PrimaryActionTooltip = primaryActionTooltip;
                PrimaryAction = primaryAction;
            }

            public string Headline { get; }

            public string Body { get; }

            public string Details { get; }

            public IReadOnlyList<DependentPromptEntry> Dependents { get; }

            public string PrimaryActionLabel { get; }

            public string PrimaryActionTooltip { get; }

            internal PromptPrimaryAction PrimaryAction { get; }
        }

        internal sealed class DependentPromptEntry
        {
            public DependentPromptEntry(
                string displayLabel,
                string packageDisplayName,
                string packageId,
                string modelId,
                string repository,
                string manifestPath)
            {
                DisplayLabel = displayLabel;
                PackageDisplayName = packageDisplayName;
                PackageId = packageId;
                ModelId = modelId;
                Repository = repository;
                ManifestPath = manifestPath;
            }

            public string DisplayLabel { get; }

            public string PackageDisplayName { get; }

            public string PackageId { get; }

            public string ModelId { get; }

            public string Repository { get; }

            public string ManifestPath { get; }
        }

        private void InitializeStyles()
        {
            _headlineIconContent ??= EditorGUIUtility.IconContent("console.infoicon");
            _manifestLinkStyle ??= new GUIStyle(EditorStyles.linkLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
                stretchWidth = true,
                clipping = TextClipping.Clip
            };
            _headlineStyle ??= new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 15,
                wordWrap = true,
                stretchWidth = true
            };
            _headlineRowStyle ??= new GUIStyle
            {
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };
            _bodyStyle ??= new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                wordWrap = true,
                stretchWidth = true
            };
            _detailsStyle ??= new GUIStyle(EditorStyles.wordWrappedMiniLabel)
            {
                wordWrap = true,
                stretchWidth = true
            };
            _sentenceStyle ??= new GUIStyle(EditorStyles.label)
            {
                wordWrap = true,
                stretchWidth = true,
                clipping = TextClipping.Clip,
                fontStyle = FontStyle.Normal,
                fontSize = 12,
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(0, 0, 0, 0)
            };
            _sentenceLinkStyle ??= new GUIStyle(_sentenceStyle)
            {
                normal =
                {
                    textColor = _manifestLinkStyle.normal.textColor
                },
                hover =
                {
                    textColor = _manifestLinkStyle.hover.textColor
                },
                active =
                {
                    textColor = _manifestLinkStyle.active.textColor
                },
                focused =
                {
                    textColor = _manifestLinkStyle.focused.textColor
                }
            };
            _eyebrowStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                fontStyle = FontStyle.Bold,
                wordWrap = false,
                stretchWidth = true
            };
            _modelNameStyle ??= new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                wordWrap = false,
                stretchWidth = true,
                clipping = TextClipping.Clip
            };
            _repositoryStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = false,
                stretchWidth = true,
                clipping = TextClipping.Clip,
                alignment = TextAnchor.MiddleLeft
            };
            _dependencyRowStyle ??= new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(8, 8, 6, 6),
                margin = new RectOffset(0, 0, 0, 4),
                stretchWidth = true
            };
            _sectionStyle ??= new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset((int)SectionPadding, (int)SectionPadding, (int)SectionPadding, (int)SectionPadding),
                margin = new RectOffset(0, 0, 0, 0),
                stretchWidth = true
            };
            _footerStyle ??= new GUIStyle(_sectionStyle)
            {
                padding = new RectOffset((int)SectionPadding, (int)SectionPadding, 8, 8)
            };
        }

        private static string BuildOwnerLabel(DiscoveredModelDependency dependency)
        {
            if (dependency == null)
            {
                return string.Empty;
            }

            return ProjectManifestDiscovery.IsProjectManifestOwnerId(dependency.PackageId)
                ? ProjectManifestDiscovery.GetOwnerDisplayName(dependency.PackageId, dependency.PackageDisplayName)
                : dependency.PackageId;
        }

        private static string GetManifestOwnerTooltip(DependentPromptEntry dependent)
        {
            if (dependent == null)
            {
                return null;
            }

            return string.IsNullOrWhiteSpace(dependent.PackageDisplayName) ||
                string.Equals(dependent.PackageDisplayName, dependent.DisplayLabel, System.StringComparison.Ordinal)
                ? dependent.ManifestPath
                : $"{dependent.PackageDisplayName}\n{dependent.ManifestPath}";
        }

        private void DrawSingleDependentSentence(DependentPromptEntry dependent)
        {
            if (dependent == null)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(GetSingleDependencyLeadText(dependent), _sentenceStyle, GUILayout.ExpandWidth(false));
                    DrawSentenceLink(dependent);
                }

                EditorGUILayout.LabelField(
                    $"{Content.RequiresText} {dependent.Repository}.",
                    _sentenceStyle);
            }
        }

        private void DrawContentSection()
        {
            using (new EditorGUILayout.VerticalScope(_sectionStyle))
            {
                if (_viewModel == null || _viewModel.Dependents.Count == 0)
                {
                    EditorGUILayout.LabelField(_viewModel?.Body ?? Content.EmptyMessage, _bodyStyle);
                }
                else if (_viewModel.Dependents.Count == 1)
                {
                    DrawSingleContent(_viewModel.Dependents[0]);
                }
                else
                {
                    DrawMultiContent(_viewModel);
                }

                if (!string.IsNullOrWhiteSpace(_viewModel?.Details))
                {
                    GUILayout.Space(10f);
                    EditorGUILayout.LabelField(_viewModel.Details, _detailsStyle);
                }
            }
        }

        private void DrawSingleContent(DependentPromptEntry dependent)
        {
            DrawSingleDependentSentence(dependent);
            GUILayout.Space(10f);
            EditorGUILayout.LabelField(_viewModel.Body, _bodyStyle);
        }

        private void DrawMultiContent(PromptViewModel viewModel)
        {
            EditorGUILayout.LabelField(viewModel.Body, _bodyStyle);
            GUILayout.Space(10f);
            EditorGUILayout.LabelField(Content.DependencyListLabel, EditorStyles.boldLabel);
            GUILayout.Space(4f);

            foreach (var dependent in viewModel.Dependents)
            {
                using (new EditorGUILayout.VerticalScope(_dependencyRowStyle))
                {
                    EditorGUILayout.LabelField(GetLeadText(dependent), _eyebrowStyle);
                    if (GUILayout.Button(
                            new GUIContent(dependent.DisplayLabel, GetManifestOwnerTooltip(dependent)),
                            _manifestLinkStyle))
                    {
                        ProjectSettingsLinks.LocateManifest(dependent.ManifestPath);
                    }

                    GUILayout.Space(2f);
                    GUILayout.Label(
                        dependent.Repository,
                        _modelNameStyle);
                }
            }
        }

        private void DrawFooterSection()
        {
            using (new EditorGUILayout.VerticalScope(_footerStyle))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button(Content.LaterButton, GUILayout.Width(90f)))
                    {
                        Close();
                    }

                    if (GUILayout.Button(Content.OpenButton, GUILayout.Width(110f)))
                    {
                        ModelHubWindow.OpenWindow();
                        Close();
                    }

                    var previousBackgroundColor = GUI.backgroundColor;
                    GUI.backgroundColor = EditorGUIUtility.isProSkin
                        ? new Color(0.30f, 0.48f, 0.72f, 1f)
                        : new Color(0.42f, 0.62f, 0.86f, 1f);
                    var primaryActionContent = new GUIContent(
                        _viewModel?.PrimaryActionLabel ?? Content.ImportAllButton,
                        _viewModel?.PrimaryActionTooltip ?? Content.ImportAllTooltip);
                    var primaryButtonWidth = _viewModel?.PrimaryAction == PromptPrimaryAction.OpenModelHub
                        ? 120f
                        : 90f;
                    if (GUILayout.Button(primaryActionContent, GUILayout.Width(primaryButtonWidth)))
                    {
                        GUI.backgroundColor = previousBackgroundColor;
                        if (_viewModel?.PrimaryAction == PromptPrimaryAction.OpenModelHub)
                        {
                            ModelHubWindow.OpenWindow();
                        }
                        else
                        {
                            EditorOperations.StartBackgroundSync();
                            ProjectSettingsProvider.Refresh();
                        }

                        Close();
                    }
                    GUI.backgroundColor = previousBackgroundColor;
                }
            }
        }

        private static string GetLeadText(DependentPromptEntry dependent)
        {
            if (dependent == null)
            {
                return string.Empty;
            }

            return ProjectManifestDiscovery.IsProjectManifestOwnerId(dependent.PackageId)
                ? GetProjectManifestLeadText(dependent.PackageId)
                : Content.PackageLeadText;
        }

        private static (string Label, string Tooltip, PromptPrimaryAction Action) BuildPrimaryAction(
            IReadOnlyList<DependencyStatus> attentionStatuses)
        {
            if (attentionStatuses == null || attentionStatuses.Count == 0)
            {
                return (Content.ImportAllButton, Content.ImportAllTooltip, PromptPrimaryAction.StartSync);
            }

            var hasNonConflictAttention = attentionStatuses.Any(status => status.ReasonCode != DependencyReasonCode.RevisionConflict);
            if (!hasNonConflictAttention)
            {
                return (Content.ResolveConflictButton, Content.ResolveConflictTooltip, PromptPrimaryAction.OpenModelHub);
            }

            if (attentionStatuses.Count != 1)
            {
                return (Content.ImportAllButton, Content.ImportAllTooltip, PromptPrimaryAction.StartSync);
            }

            var unresolvedStatus = attentionStatuses[0];
            return HasRawCache(unresolvedStatus)
                ? (Content.ImportButton, Content.ImportTooltip, PromptPrimaryAction.StartSync)
                : (Content.DownloadButton, Content.DownloadTooltip, PromptPrimaryAction.StartSync);
        }

        private static bool HasRawCache(DependencyStatus status)
        {
            // "Missing" can still mean cache exists but import metadata/runtime files are incomplete,
            // so prompt copy must infer download-vs-import from the raw cache fields instead.
            return status != null &&
                (!string.IsNullOrWhiteSpace(status.RevisionRefPath) ||
                    !string.IsNullOrWhiteSpace(status.RawFilePath) ||
                    !string.IsNullOrWhiteSpace(status.RawMetadataPath) ||
                    status.RawMetadata != null);
        }

        private static string GetSingleDependencyLeadText(DependentPromptEntry dependent)
        {
            if (dependent == null)
            {
                return string.Empty;
            }

            return ProjectManifestDiscovery.IsProjectManifestOwnerId(dependent.PackageId)
                ? $"The {GetProjectManifestLinkText(dependent.PackageId)} "
                : "Package ";
        }

        private void DrawSentenceLink(DependentPromptEntry dependent)
        {
            var ownerText = ProjectManifestDiscovery.IsProjectManifestOwnerId(dependent.PackageId)
                ? GetProjectManifestLinkText(dependent.PackageId)
                : dependent.DisplayLabel;

            GUILayout.Label(
                new GUIContent(ownerText, GetManifestOwnerTooltip(dependent)),
                _sentenceLinkStyle,
                GUILayout.ExpandWidth(false));

            var linkRect = GUILayoutUtility.GetLastRect();
            EditorGUIUtility.AddCursorRect(linkRect, MouseCursor.Link);
            if (Event.current.type == EventType.MouseUp &&
                Event.current.button == 0 &&
                linkRect.Contains(Event.current.mousePosition))
            {
                ProjectSettingsLinks.LocateManifest(dependent.ManifestPath);
                Event.current.Use();
            }
        }

        private static string GetProjectManifestLeadText(string ownerId)
        {
            return ProjectManifestDiscovery.IsProjectSettingsManifestOwnerId(ownerId)
                ? "Project Settings manifest"
                : "Project manifest asset";
        }

        private static string GetProjectManifestLinkText(string ownerId)
        {
            return ProjectManifestDiscovery.IsProjectSettingsManifestOwnerId(ownerId)
                ? "Project Settings manifest"
                : "project manifest asset";
        }

        private void DrawHeadline()
        {
            using (new EditorGUILayout.HorizontalScope(_headlineRowStyle))
            {
                if (_headlineIconContent?.image != null)
                {
                    GUILayout.Label(_headlineIconContent, GUILayout.Width(20f), GUILayout.Height(20f));
                    GUILayout.Space(4f);
                }

                EditorGUILayout.LabelField(_viewModel?.Headline ?? Content.EmptyMessage, _headlineStyle);
            }
        }
    }
}
