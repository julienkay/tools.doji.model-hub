using System.Linq;

namespace Doji.AI.ModelHub.Editor
{
    internal enum ModelStatusKind
    {
        Ready,
        DownloadNeeded,
        ImportNeeded,
        UpdateNeeded,
        ChooseFiles,
        Error,
        Conflict
    }

    internal enum ModelActionKind
    {
        None,
        Sync,
        ChooseFiles
    }

    /// <summary>
    /// User-facing status of one Model Hub repository: label, explanation, tone, and the next action.
    /// Every surface (list, details, setup prompt) reads status copy from here so wording stays consistent.
    /// </summary>
    internal readonly struct ModelStatus
    {
        private ModelStatus(
            ModelStatusKind kind,
            string label,
            string description,
            UiTone tone,
            ModelActionKind action,
            string actionLabel)
        {
            Kind = kind;
            Label = label;
            Description = description;
            Tone = tone;
            Action = action;
            ActionLabel = actionLabel;
        }

        public ModelStatusKind Kind { get; }
        public string Label { get; }
        public string Description { get; }
        public UiTone Tone { get; }
        public ModelActionKind Action { get; }
        public string ActionLabel { get; }
        public bool NeedsAction => Kind != ModelStatusKind.Ready;

        public static ModelStatus From(ModelHubItem item)
        {
            if (item == null)
            {
                return new ModelStatus(ModelStatusKind.Error, "Unknown", "No status is available.", UiTone.Neutral, ModelActionKind.None, null);
            }

            var hasCache = !string.IsNullOrWhiteSpace(item.RawSnapshotRootPath);
            var hasRuntimeFiles = item.RuntimeFiles.Count > 0;

            if (item.HasRevisionConflict)
            {
                return new ModelStatus(
                    ModelStatusKind.Conflict,
                    "Conflict",
                    "Manifests request different revisions of this model. A project can use only one revision per model.",
                    UiTone.Error,
                    ModelActionKind.None,
                    null);
            }

            if (item.State == ModelHubItemState.Ready)
            {
                return new ModelStatus(
                    ModelStatusKind.Ready,
                    "Ready",
                    hasCache
                        ? "Downloaded and imported into this project."
                        : "Imported into this project. The downloaded files were removed from the cache on this machine.",
                    UiTone.Success,
                    ModelActionKind.None,
                    null);
            }

            if (item.HasSelectionRequired)
            {
                return new ModelStatus(
                    ModelStatusKind.ChooseFiles,
                    "Choose files",
                    "This model has optional files. Choose at least one model file to import.",
                    UiTone.Warning,
                    ModelActionKind.ChooseFiles,
                    "Choose Files");
            }

            if (!hasCache)
            {
                return new ModelStatus(
                    ModelStatusKind.DownloadNeeded,
                    "Not downloaded",
                    "Download the model from Hugging Face and import it into this project.",
                    UiTone.Info,
                    ModelActionKind.Sync,
                    "Download & Import");
            }

            if (item.State == ModelHubItemState.Error)
            {
                return new ModelStatus(
                    ModelStatusKind.Error,
                    "Error",
                    hasRuntimeFiles
                        ? "The imported files are broken or incomplete. Import the model again to repair them."
                        : "The model is downloaded, but the import failed.",
                    UiTone.Error,
                    ModelActionKind.Sync,
                    "Retry Import");
            }

            if (item.HasMissingCacheFiles)
            {
                return new ModelStatus(
                    ModelStatusKind.UpdateNeeded,
                    "Update needed",
                    "More files are selected than were downloaded. Download them and import the model again.",
                    UiTone.Info,
                    ModelActionKind.Sync,
                    "Update");
            }

            return hasRuntimeFiles
                ? new ModelStatus(
                    ModelStatusKind.UpdateNeeded,
                    "Update needed",
                    "The imported files are out of date. Import the model again to update them.",
                    UiTone.Info,
                    ModelActionKind.Sync,
                    "Update")
                : new ModelStatus(
                    ModelStatusKind.ImportNeeded,
                    "Not imported",
                    "The model is downloaded, but not imported into this project yet.",
                    UiTone.Info,
                    ModelActionKind.Sync,
                    "Import");
        }

        /// <summary>
        /// Maps one diagnostic severity string to a UI tone.
        /// </summary>
        public static UiTone ToneForSeverity(string severity)
        {
            return severity switch
            {
                "error" => UiTone.Error,
                "warning" => UiTone.Warning,
                _ => UiTone.Info
            };
        }

        /// <summary>
        /// Builds a short owner list such as <c>MiDaS</c> or <c>MiDaS and 2 more</c>.
        /// </summary>
        public static string BuildUsedBySummary(ModelHubItem item)
        {
            var names = (item?.Dependents ?? System.Array.Empty<DependencyStatus>())
                .Select(dependent => GetOwnerDisplayName(dependent.Dependency))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct()
                .ToArray();
            return names.Length switch
            {
                0 => string.Empty,
                1 => names[0],
                2 => $"{names[0]} and {names[1]}",
                _ => $"{names[0]} and {names.Length - 1} more"
            };
        }

        public static string GetOwnerDisplayName(DiscoveredModelDependency dependency)
        {
            if (dependency == null)
            {
                return string.Empty;
            }

            return ProjectManifestDiscovery.IsProjectManifestOwnerId(dependency.PackageId)
                ? ProjectManifestDiscovery.GetOwnerDisplayName(dependency.PackageId, dependency.PackageDisplayName)
                : string.IsNullOrWhiteSpace(dependency.PackageDisplayName)
                    ? dependency.PackageId
                    : dependency.PackageDisplayName;
        }

        /// <summary>
        /// Returns the repository part after <c>owner/</c>, or the full id when it has no owner.
        /// </summary>
        public static string GetModelName(string repository)
        {
            if (string.IsNullOrWhiteSpace(repository))
            {
                return "Unknown model";
            }

            var separatorIndex = repository.IndexOf('/');
            return separatorIndex >= 0 && separatorIndex < repository.Length - 1
                ? repository.Substring(separatorIndex + 1)
                : repository;
        }
    }
}
