using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Doji.Downloads;
using Newtonsoft.Json;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Why a sync leaves one dependency out.
    /// </summary>
    internal enum SyncSkipReason
    {
        /// <summary>
        /// The sync works on the dependency.
        /// </summary>
        None,

        /// <summary>
        /// Every selected file is downloaded and set up for this project.
        /// </summary>
        Ready,

        /// <summary>
        /// The model was not found on Hugging Face.
        /// </summary>
        InvalidRepository,

        /// <summary>
        /// The manifest lists only optional model files and none is turned on.
        /// </summary>
        SelectionRequired,

        /// <summary>
        /// The project requests the model at more than one revision.
        /// </summary>
        RevisionConflict,

        /// <summary>
        /// Another sync is running. Only one sync runs at a time.
        /// </summary>
        AlreadyRunning
    }

    /// <summary>
    /// What a sync does with one dependency: the mode it runs, or why it leaves the dependency out.
    /// </summary>
    internal sealed class SyncPlanEntry
    {
        public SyncPlanEntry(DependencyStatus status, SyncOperationMode mode, SyncSkipReason skipReason, string message)
        {
            Status = status;
            Mode = mode;
            SkipReason = skipReason;
            Message = message;
        }

        public DependencyStatus Status { get; }
        public DiscoveredModelDependency Dependency => Status.Dependency;
        public SyncOperationMode Mode { get; }
        public SyncSkipReason SkipReason { get; }
        public string Message { get; }
        public bool IsPending => SkipReason == SyncSkipReason.None;
    }

    /// <summary>
    /// The answer to a sync request: whether a sync started, and the plan for every dependency in scope.
    /// </summary>
    internal sealed class SyncStartResult
    {
        private readonly SyncPlanEntry[] _entries;

        public SyncStartResult(bool started, bool alreadyRunning, bool resumed, IReadOnlyList<SyncPlanEntry> entries, string message)
        {
            Started = started;
            AlreadyRunning = alreadyRunning;
            Resumed = resumed;
            _entries = entries?.ToArray() ?? Array.Empty<SyncPlanEntry>();
            Message = message;
        }

        public bool Started { get; }
        public bool AlreadyRunning { get; }

        /// <summary>
        /// The sync continues the queue an interrupted sync saved, not a new plan.
        /// </summary>
        public bool Resumed { get; }

        public IReadOnlyList<SyncPlanEntry> Entries => _entries;
        public string Message { get; }
    }

    /// <summary>
    /// The state of one sync run, as <see cref="SyncRunRecord.State"/> stores it.
    /// </summary>
    internal static class SyncRunStates
    {
        public const string Running = "running";
        public const string Succeeded = "succeeded";
        public const string Failed = "failed";
        public const string Canceled = "canceled";

        /// <summary>
        /// The Editor stopped during the run and no saved queue is left to resume it.
        /// </summary>
        public const string Interrupted = "interrupted";
    }

    /// <summary>
    /// One sync run and its result. The last run is saved under <c>Library/</c>, so tools can read it after a domain
    /// reload or an Editor restart.
    /// </summary>
    internal sealed class SyncRunRecord
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("state")] public string State { get; set; }
        [JsonProperty("mode")] public string Mode { get; set; }

        /// <summary>
        /// The repository the run was limited to, or null for every model.
        /// </summary>
        [JsonProperty("repository")] public string Repository { get; set; }

        [JsonProperty("startedAtUtc")] public string StartedAtUtc { get; set; }
        [JsonProperty("finishedAtUtc")] public string FinishedAtUtc { get; set; }

        /// <summary>
        /// How often the run continued after a domain reload.
        /// </summary>
        [JsonProperty("resumeCount")] public int ResumeCount { get; set; }

        [JsonProperty("message")] public string Message { get; set; }
        [JsonProperty("planned")] public SyncRunPlannedItem[] Planned { get; set; } = Array.Empty<SyncRunPlannedItem>();
        [JsonProperty("results")] public SyncRunResultItem[] Results { get; set; } = Array.Empty<SyncRunResultItem>();
        [JsonProperty("discoveryIssues")] public string[] DiscoveryIssues { get; set; } = Array.Empty<string>();

        [JsonIgnore] public bool IsFinished => !string.Equals(State, SyncRunStates.Running, StringComparison.Ordinal);
    }

    /// <summary>
    /// One dependency a run works on.
    /// </summary>
    internal sealed class SyncRunPlannedItem
    {
        [JsonProperty("owner")] public string Owner { get; set; }
        [JsonProperty("repository")] public string Repository { get; set; }
        [JsonProperty("revision")] public string Revision { get; set; }
        [JsonProperty("mode")] public string Mode { get; set; }
    }

    /// <summary>
    /// The outcome of one dependency in a finished run.
    /// </summary>
    internal sealed class SyncRunResultItem
    {
        [JsonProperty("owner")] public string Owner { get; set; }
        [JsonProperty("repository")] public string Repository { get; set; }
        [JsonProperty("revision")] public string Revision { get; set; }
        [JsonProperty("mode")] public string Mode { get; set; }
        [JsonProperty("ok")] public bool Ok { get; set; }
        [JsonProperty("message")] public string Message { get; set; }
        [JsonProperty("resolvedCommit")] public string ResolvedCommit { get; set; }
        [JsonProperty("runtimeFileCount")] public int RuntimeFileCount { get; set; }
    }

    /// <summary>
    /// What the running sync does right now. Immutable, so any thread can publish a new one.
    /// </summary>
    internal sealed class SyncProgressSnapshot
    {
        public SyncProgressSnapshot(
            string phase,
            string repository,
            string dependencyLabel,
            int dependencyIndex,
            int dependencyCount,
            string currentFile,
            int fileIndex,
            int fileCount,
            long downloadedBytes,
            long? totalBytes,
            float progress)
        {
            Phase = phase;
            Repository = repository;
            DependencyLabel = dependencyLabel;
            DependencyIndex = dependencyIndex;
            DependencyCount = dependencyCount;
            CurrentFile = currentFile;
            FileIndex = fileIndex;
            FileCount = fileCount;
            DownloadedBytes = downloadedBytes;
            TotalBytes = totalBytes;
            Progress = progress;
        }

        /// <summary>
        /// <c>preparing</c>, <c>downloading</c>, <c>importing</c>, or <c>blocked</c>.
        /// </summary>
        public string Phase { get; }

        public string Repository { get; }
        public string DependencyLabel { get; }
        public int DependencyIndex { get; }
        public int DependencyCount { get; }

        /// <summary>
        /// The repository path of the file being downloaded, or null between files.
        /// </summary>
        public string CurrentFile { get; }

        public int FileIndex { get; }
        public int FileCount { get; }

        /// <summary>
        /// Bytes of <see cref="CurrentFile"/> on disk, including bytes a resumed download kept.
        /// </summary>
        public long DownloadedBytes { get; }

        public long? TotalBytes { get; }

        /// <summary>
        /// Progress of the whole run, from 0 to 1.
        /// </summary>
        public float Progress { get; }

        public SyncProgressSnapshot With(
            string phase = null,
            string currentFile = null,
            int? fileIndex = null,
            int? fileCount = null,
            long? downloadedBytes = null,
            long? totalBytes = null,
            float? progress = null,
            bool clearFile = false)
        {
            return new SyncProgressSnapshot(
                phase ?? Phase,
                Repository,
                DependencyLabel,
                DependencyIndex,
                DependencyCount,
                clearFile ? null : currentFile ?? CurrentFile,
                clearFile ? 0 : fileIndex ?? FileIndex,
                clearFile ? 0 : fileCount ?? FileCount,
                clearFile ? 0 : downloadedBytes ?? DownloadedBytes,
                clearFile ? null : totalBytes ?? TotalBytes,
                progress ?? Progress);
        }
    }

    /// <summary>
    /// A download progress sink that also hears when each file starts. <see cref="RawCacheService"/> calls
    /// <see cref="OnFileStarted"/> before it downloads a file.
    /// </summary>
    internal interface IFileDownloadProgress : IProgress<DownloadProgress>
    {
        void OnFileStarted(string repoPath, long? sizeInBytes, int fileIndex, int fileCount);
    }

    /// <summary>
    /// Forwards byte progress to an inner sink and file starts and bytes to callbacks.
    /// </summary>
    internal sealed class FileDownloadProgress : IFileDownloadProgress
    {
        private readonly IProgress<DownloadProgress> _inner;
        private readonly Action<string, long?, int, int> _onFileStarted;
        private readonly Action<DownloadProgress> _onBytes;

        public FileDownloadProgress(
            IProgress<DownloadProgress> inner,
            Action<string, long?, int, int> onFileStarted,
            Action<DownloadProgress> onBytes)
        {
            _inner = inner;
            _onFileStarted = onFileStarted;
            _onBytes = onBytes;
        }

        public void OnFileStarted(string repoPath, long? sizeInBytes, int fileIndex, int fileCount)
        {
            _onFileStarted?.Invoke(repoPath, sizeInBytes, fileIndex, fileCount);
        }

        public void Report(DownloadProgress value)
        {
            _onBytes?.Invoke(value);
            _inner?.Report(value);
        }
    }

    /// <summary>
    /// Reads and writes the last <see cref="SyncRunRecord"/> under <c>Library/Doji/ModelHub</c>.
    /// </summary>
    internal static class SyncRunStore
    {
        private const string FileName = "last-sync.json";

        /// <summary>
        /// The record file. Tests point it at a temporary folder.
        /// </summary>
        internal static string PathOverride { get; set; }

        public static string GetPath()
        {
            return PathOverride ?? Path.Combine(
                Path.GetDirectoryName(BackgroundSyncManager.GetPersistedStatePath()) ?? string.Empty,
                FileName);
        }

        public static SyncRunRecord Load()
        {
            var path = GetPath();
            try
            {
                return File.Exists(path)
                    ? JsonConvert.DeserializeObject<SyncRunRecord>(File.ReadAllText(path))
                    : null;
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        public static void Save(SyncRunRecord record)
        {
            if (record == null)
            {
                return;
            }

            var path = GetPath();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
                File.WriteAllText(path, JsonConvert.SerializeObject(record, Formatting.Indented));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                UnityEngine.Debug.LogWarning($"AI Model Hub could not save the sync result to {path}: {exception.Message}");
            }
        }

        /// <summary>
        /// Builds the per-dependency outcomes of a sync report.
        /// </summary>
        public static SyncRunResultItem[] ToResultItems(SyncReport report)
        {
            return (report?.ModelResults ?? Array.Empty<ModelSyncResult>())
                .Select(result => new SyncRunResultItem
                {
                    Owner = result.Dependency?.PackageId,
                    Repository = result.Dependency?.Repository,
                    Revision = result.Dependency?.Revision,
                    Mode = SyncModes.ToName(result.Mode),
                    Ok = result.Success,
                    Message = result.Success ? null : result.ErrorMessage,
                    ResolvedCommit = result.CachedFile?.Metadata?.ResolvedCommitHash,
                    RuntimeFileCount = result.ImportedArtifact?.RuntimeFiles.Count ?? 0
                })
                .ToArray();
        }
    }

    /// <summary>
    /// Names of <see cref="SyncOperationMode"/> values in tool output and arguments.
    /// </summary>
    internal static class SyncModes
    {
        public const string Full = "full";
        public const string Download = "download";
        public const string Import = "import";

        public static string ToName(SyncOperationMode mode)
        {
            return mode switch
            {
                SyncOperationMode.DownloadOnly => Download,
                SyncOperationMode.MaterializeOnly => Import,
                _ => Full
            };
        }

        /// <summary>
        /// Reads a mode argument: <c>full</c> (download and import) or <c>download</c>. Null or empty means
        /// <c>full</c>.
        /// </summary>
        public static bool TryParse(string value, out SyncOperationMode mode)
        {
            switch (value?.Trim().ToLowerInvariant())
            {
                case null:
                case "":
                case Full:
                    mode = SyncOperationMode.Full;
                    return true;
                case Download:
                    mode = SyncOperationMode.DownloadOnly;
                    return true;
                default:
                    mode = SyncOperationMode.Full;
                    return false;
            }
        }
    }
}
