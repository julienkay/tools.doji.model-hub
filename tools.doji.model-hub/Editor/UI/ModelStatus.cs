using System.Linq;

namespace Doji.AI.ModelHub.Editor
{
    internal enum ModelStatusKind
    {
        Ready,
        DownloadNeeded,
        ConversionNeeded,
        Preparing,
        UpdateNeeded,
        RepairNeeded,
        ChooseFiles,
        Error,
        Conflict,
        NotEnabled
    }

    internal enum ModelActionKind
    {
        None,
        Sync,
        ChooseFiles,
        EnableModel
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
        public bool NeedsAction => Kind != ModelStatusKind.Ready && Kind != ModelStatusKind.NotEnabled;

        public static ModelStatus From(ModelHubItem item)
        {
            if (item == null)
            {
                return new ModelStatus(ModelStatusKind.Error, "Unknown", "No status is available.", UiTone.Neutral, ModelActionKind.None, null);
            }

            var hasCache = !string.IsNullOrWhiteSpace(item.RawSnapshotRootPath);

            if (item.IsNotEnabled)
            {
                return new ModelStatus(
                    ModelStatusKind.NotEnabled,
                    "Not enabled",
                    "Optional model. It is not enabled in this project, so it is not downloaded, converted, or included in builds. Enable it to use it.",
                    UiTone.Neutral,
                    ModelActionKind.EnableModel,
                    "Enable");
            }

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
                        ? "Ready to use."
                        : "Ready to use. The downloaded files were removed from the cache on this machine.",
                    UiTone.Success,
                    ModelActionKind.None,
                    null);
            }

            if (item.HasSelectionRequired)
            {
                return new ModelStatus(
                    ModelStatusKind.ChooseFiles,
                    "Choose files",
                    "This model has optional files. Choose at least one model file to use.",
                    UiTone.Warning,
                    ModelActionKind.ChooseFiles,
                    "Choose Files");
            }

            if (item.HasDamagedCache)
            {
                return new ModelStatus(
                    ModelStatusKind.RepairNeeded,
                    "Needs repair",
                    "The downloaded files are damaged or incomplete. Download them again to repair the model.",
                    UiTone.Warning,
                    ModelActionKind.Sync,
                    "Repair");
            }

            if (!hasCache)
            {
                return new ModelStatus(
                    ModelStatusKind.DownloadNeeded,
                    "Not downloaded",
                    "Download the model from Hugging Face to use it in this project.",
                    UiTone.Info,
                    ModelActionKind.Sync,
                    "Download");
            }

            if (item.HasUnknownFileList)
            {
                return new ModelStatus(
                    ModelStatusKind.UpdateNeeded,
                    "Update needed",
                    "The files are downloaded, but AI Model Hub does not know this model's file list on this machine, so it cannot tell whether every file is there. Update to check and download what is missing.",
                    UiTone.Info,
                    ModelActionKind.Sync,
                    "Update");
            }

            if (item.HasMissingCacheFiles)
            {
                return new ModelStatus(
                    ModelStatusKind.UpdateNeeded,
                    "Update needed",
                    "More files are selected than were downloaded. Download the missing files to use them.",
                    UiTone.Info,
                    ModelActionKind.Sync,
                    "Update");
            }

            if (item.State == ModelHubItemState.Error)
            {
                return new ModelStatus(
                    ModelStatusKind.Error,
                    "Error",
                    "The model is downloaded, but setting it up for this project failed.",
                    UiTone.Error,
                    ModelActionKind.Sync,
                    "Try Again");
            }

            if (item.NeedsConversion)
            {
                return new ModelStatus(
                    ModelStatusKind.ConversionNeeded,
                    "Needs conversion",
                    BuildConversionDescription(item),
                    UiTone.Info,
                    ModelActionKind.Sync,
                    "Convert");
            }

            if (item.IsImportPending)
            {
                return new ModelStatus(
                    ModelStatusKind.Preparing,
                    "Setting up",
                    "The model is downloaded. AI Model Hub sets it up for this project automatically.",
                    UiTone.Info,
                    ModelActionKind.Sync,
                    "Finish");
            }

            return new ModelStatus(
                ModelStatusKind.UpdateNeeded,
                "Update needed",
                "The model is out of date in this project. Update it to use it.",
                UiTone.Info,
                ModelActionKind.Sync,
                "Update");
        }

        /// <summary>
        /// Names the toolbar action that runs every pending model: downloads win over conversions, which win over setup.
        /// </summary>
        public static string BuildBulkActionLabel(System.Collections.Generic.IReadOnlyCollection<ModelStatus> statuses)
        {
            if (statuses.Any(status => status.Kind is ModelStatusKind.DownloadNeeded or ModelStatusKind.UpdateNeeded or ModelStatusKind.RepairNeeded))
            {
                return "Download All";
            }

            return statuses.Any(status => status.Kind is ModelStatusKind.ConversionNeeded or ModelStatusKind.Error)
                ? "Convert All"
                : "Set Up All";
        }

        /// <summary>
        /// Explains why ONNX files of a downloaded model need converting, and what converting costs.
        /// </summary>
        public static string BuildConversionDescription(ModelHubItem item)
        {
            var count = item?.PendingConversions.Count ?? 0;
            var files = count == 1 ? "1 ONNX file needs" : $"{count} ONNX files need";
            const string cost = "Converting takes CPU, memory, and time for large models.";
            return (item?.ConversionCause ?? ConversionCause.None) switch
            {
                ConversionCause.FormatChanged =>
                    $"The model is downloaded. Sentis {ProjectImportService.GetSentisVersion()} uses a new model format, so {files} converting again. {cost}",
                ConversionCause.SettingsChanged =>
                    $"The model is downloaded. The import settings changed, so {files} converting again. {cost}",
                _ =>
                    $"The model is downloaded. {files} converting to Sentis before use. {cost}"
            };
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
