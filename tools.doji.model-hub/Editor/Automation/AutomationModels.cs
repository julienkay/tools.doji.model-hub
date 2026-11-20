using System;
using Newtonsoft.Json;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// A request a tool made that AI Model Hub refuses: an unknown model or file, a bad argument, or a step that needs
    /// the user's consent first. The message says what to change.
    /// </summary>
    internal sealed class ModelHubAutomationException : ArgumentException
    {
        public ModelHubAutomationException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// Every model dependency of the project, with its files and the sync state.
    /// </summary>
    internal sealed class AutomationStatusResult
    {
        [JsonProperty("dependencies")] public AutomationDependencyInfo[] Dependencies { get; set; } = Array.Empty<AutomationDependencyInfo>();
        [JsonProperty("discoveryIssues")] public AutomationIssueInfo[] DiscoveryIssues { get; set; } = Array.Empty<AutomationIssueInfo>();
        [JsonProperty("sync")] public AutomationSyncSummary Sync { get; set; }
    }

    /// <summary>
    /// One model one manifest owner declares, and its local state.
    /// </summary>
    internal sealed class AutomationDependencyInfo
    {
        [JsonProperty("owner")] public string Owner { get; set; }
        [JsonProperty("ownerName")] public string OwnerName { get; set; }
        [JsonProperty("ownerVersion")] public string OwnerVersion { get; set; }
        [JsonProperty("manifestPath")] public string ManifestPath { get; set; }
        [JsonProperty("repository")] public string Repository { get; set; }
        [JsonProperty("requestedRevision")] public string RequestedRevision { get; set; }

        /// <summary>
        /// The commit the revision resolved to at the last download, or null before the first download.
        /// </summary>
        [JsonProperty("resolvedCommit")] public string ResolvedCommit { get; set; }

        /// <summary>
        /// Whether the manifest marks the whole model optional: opt-in per project.
        /// </summary>
        [JsonProperty("optional")] public bool Optional { get; set; }

        /// <summary>
        /// Whether the project uses the model. Always true for a model that is not optional.
        /// </summary>
        [JsonProperty("enabled")] public bool Enabled { get; set; }

        /// <summary>
        /// Bytes of the selected files on Hugging Face (for an optional model that is not enabled: the files enabling
        /// it selects), or null when AI Model Hub does not know the file list yet.
        /// </summary>
        [JsonProperty("downloadSizeBytes")] public long? DownloadSizeBytes { get; set; }

        /// <summary>
        /// <c>Ready</c>, <c>Missing</c>, <c>Stale</c>, <c>Failed</c>, or <c>NotEnabled</c> (an optional model that the
        /// project did not enable; no action needed).
        /// </summary>
        [JsonProperty("state")] public string State { get; set; }

        [JsonProperty("reasonCode")] public string ReasonCode { get; set; }
        [JsonProperty("message")] public string Message { get; set; }

        /// <summary>
        /// Every selected file is cached; only this project's import is missing.
        /// </summary>
        [JsonProperty("importPending")] public bool ImportPending { get; set; }

        /// <summary>
        /// ONNX files the next full sync converts.
        /// </summary>
        [JsonProperty("pendingConversions")] public string[] PendingConversions { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Model card license fields, when the cache has them. A tool shows them to the user; it never accepts a
        /// license for the user.
        /// </summary>
        [JsonProperty("license")] public AutomationLicenseInfo License { get; set; }

        [JsonProperty("files")] public AutomationFileInfo[] Files { get; set; } = Array.Empty<AutomationFileInfo>();
    }

    /// <summary>
    /// One repository file a dependency lists or selects.
    /// </summary>
    internal sealed class AutomationFileInfo
    {
        [JsonProperty("path")] public string Path { get; set; }

        /// <summary>
        /// The manifest marks the file optional: the project turns it on or off.
        /// </summary>
        [JsonProperty("optional")] public bool Optional { get; set; }

        /// <summary>
        /// The dependency uses the file: required files always, optional files when turned on.
        /// </summary>
        [JsonProperty("enabled")] public bool Enabled { get; set; }

        /// <summary>
        /// The file is in the shared cache on this machine.
        /// </summary>
        [JsonProperty("cached")] public bool Cached { get; set; }

        /// <summary>
        /// The project has a runtime model for the file: a converted ONNX model, or the downloaded Sentis file.
        /// </summary>
        [JsonProperty("imported")] public bool Imported { get; set; }

        /// <summary>
        /// Size on Hugging Face, or null when the file list was not fetched yet.
        /// </summary>
        [JsonProperty("sizeBytes")] public long? SizeBytes { get; set; }

        /// <summary>
        /// Quantization of an ONNX file's conversion: <c>none</c>, <c>float16</c>, or <c>uint8</c>. Null for other
        /// files.
        /// </summary>
        [JsonProperty("quantization")] public string Quantization { get; set; }
    }

    /// <summary>
    /// The license a model card declares.
    /// </summary>
    internal sealed class AutomationLicenseInfo
    {
        [JsonProperty("license")] public string License { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("link")] public string Link { get; set; }
    }

    /// <summary>
    /// A manifest AI Model Hub could not read.
    /// </summary>
    internal sealed class AutomationIssueInfo
    {
        [JsonProperty("owner")] public string Owner { get; set; }
        [JsonProperty("manifestPath")] public string ManifestPath { get; set; }
        [JsonProperty("message")] public string Message { get; set; }
    }

    /// <summary>
    /// Whether a sync runs, in short.
    /// </summary>
    internal sealed class AutomationSyncSummary
    {
        [JsonProperty("running")] public bool Running { get; set; }

        /// <summary>
        /// An interrupted sync saved its queue; it resumes after the next domain reload or sync.
        /// </summary>
        [JsonProperty("resumable")] public bool Resumable { get; set; }

        [JsonProperty("lastRunState")] public string LastRunState { get; set; }
    }

    /// <summary>
    /// The answer to a sync request.
    /// </summary>
    internal sealed class AutomationSyncStartResult
    {
        [JsonProperty("started")] public bool Started { get; set; }
        [JsonProperty("alreadyRunning")] public bool AlreadyRunning { get; set; }
        [JsonProperty("resumed")] public bool Resumed { get; set; }
        [JsonProperty("mode")] public string Mode { get; set; }
        [JsonProperty("repository")] public string Repository { get; set; }
        [JsonProperty("message")] public string Message { get; set; }
        [JsonProperty("dependencies")] public AutomationSyncPlanItem[] Dependencies { get; set; } = Array.Empty<AutomationSyncPlanItem>();

        /// <summary>
        /// The run record when the request waited for the sync to end; null otherwise.
        /// </summary>
        [JsonProperty("run")] public SyncRunRecord Run { get; set; }

        [JsonProperty("next")] public string Next { get; set; }
    }

    /// <summary>
    /// What a sync does with one dependency, or why it leaves it out.
    /// </summary>
    internal sealed class AutomationSyncPlanItem
    {
        [JsonProperty("owner")] public string Owner { get; set; }
        [JsonProperty("repository")] public string Repository { get; set; }
        [JsonProperty("revision")] public string Revision { get; set; }

        /// <summary>
        /// <c>sync</c> when the sync works on the dependency, <c>skip</c> when it leaves it out.
        /// </summary>
        [JsonProperty("action")] public string Action { get; set; }

        /// <summary>
        /// <c>full</c>, <c>download</c>, or <c>import</c> for <c>sync</c>; null for <c>skip</c>.
        /// </summary>
        [JsonProperty("mode")] public string Mode { get; set; }

        /// <summary>
        /// Why the sync leaves the dependency out: <c>ready</c>, <c>invalid_repository</c>, <c>selection_required</c>,
        /// <c>revision_conflict</c>, or <c>already_running</c>. Null for <c>sync</c>.
        /// </summary>
        [JsonProperty("reason")] public string Reason { get; set; }

        [JsonProperty("message")] public string Message { get; set; }
    }

    /// <summary>
    /// The running or the last sync, and live progress.
    /// </summary>
    internal sealed class AutomationSyncStatusResult
    {
        /// <summary>
        /// <c>idle</c> (never ran), <c>running</c>, <c>resumable</c>, <c>succeeded</c>, <c>failed</c>,
        /// <c>canceled</c>, or <c>interrupted</c>.
        /// </summary>
        [JsonProperty("state")] public string State { get; set; }

        [JsonProperty("running")] public bool Running { get; set; }
        [JsonProperty("finished")] public bool Finished { get; set; }
        [JsonProperty("progress")] public AutomationSyncProgress Progress { get; set; }
        [JsonProperty("run")] public SyncRunRecord Run { get; set; }
        [JsonProperty("next")] public string Next { get; set; }
    }

    /// <summary>
    /// Live progress of the running sync.
    /// </summary>
    internal sealed class AutomationSyncProgress
    {
        /// <summary>
        /// <c>preparing</c>, <c>downloading</c>, <c>importing</c>, or <c>blocked</c>.
        /// </summary>
        [JsonProperty("phase")] public string Phase { get; set; }

        [JsonProperty("repository")] public string Repository { get; set; }
        [JsonProperty("dependency")] public string Dependency { get; set; }
        [JsonProperty("dependencyIndex")] public int DependencyIndex { get; set; }
        [JsonProperty("dependencyCount")] public int DependencyCount { get; set; }
        [JsonProperty("currentFile")] public string CurrentFile { get; set; }
        [JsonProperty("fileIndex")] public int FileIndex { get; set; }
        [JsonProperty("fileCount")] public int FileCount { get; set; }
        [JsonProperty("downloadedBytes")] public long DownloadedBytes { get; set; }
        [JsonProperty("totalBytes")] public long? TotalBytes { get; set; }

        /// <summary>
        /// Progress of the whole run, from 0 to 1.
        /// </summary>
        [JsonProperty("progress")] public float Progress { get; set; }
    }

    /// <summary>
    /// The answer to a file selection or import settings change.
    /// </summary>
    internal sealed class AutomationSetFileResult
    {
        [JsonProperty("repository")] public string Repository { get; set; }
        [JsonProperty("path")] public string Path { get; set; }
        [JsonProperty("changed")] public bool Changed { get; set; }
        [JsonProperty("changes")] public string[] Changes { get; set; } = Array.Empty<string>();
        [JsonProperty("dependencies")] public AutomationDependencyInfo[] Dependencies { get; set; } = Array.Empty<AutomationDependencyInfo>();
        [JsonProperty("next")] public string Next { get; set; }
    }

    /// <summary>
    /// The answer to a cancel request.
    /// </summary>
    internal sealed class AutomationSetModelResult
    {
        [JsonProperty("repository")] public string Repository { get; set; }
        [JsonProperty("enabled")] public bool Enabled { get; set; }
        [JsonProperty("changed")] public bool Changed { get; set; }
        [JsonProperty("changes")] public string[] Changes { get; set; } = Array.Empty<string>();
        [JsonProperty("dependencies")] public AutomationDependencyInfo[] Dependencies { get; set; } = Array.Empty<AutomationDependencyInfo>();
        [JsonProperty("next")] public string Next { get; set; }
    }

    internal sealed class AutomationCancelResult
    {
        [JsonProperty("canceled")] public bool Canceled { get; set; }
        [JsonProperty("message")] public string Message { get; set; }
    }

    /// <summary>
    /// The answer to a cache removal.
    /// </summary>
    internal sealed class AutomationRemoveCacheResult
    {
        [JsonProperty("repository")] public string Repository { get; set; }
        [JsonProperty("removedRevisions")] public string[] RemovedRevisions { get; set; } = Array.Empty<string>();
        [JsonProperty("message")] public string Message { get; set; }
    }

    /// <summary>
    /// The folders AI Model Hub uses, why, the free space on their drives, and stale environment variables.
    /// </summary>
    internal sealed class AutomationCacheInfoResult
    {
        /// <summary>
        /// The Hugging Face cache with the downloads, shared with Python's <c>huggingface_hub</c>.
        /// </summary>
        [JsonProperty("hubCache")] public AutomationFolderInfo HubCache { get; set; }

        /// <summary>
        /// AI Model Hub's data folder with the index and the converted models.
        /// </summary>
        [JsonProperty("dataRoot")] public AutomationFolderInfo DataRoot { get; set; }

        /// <summary>
        /// Folder variables that changed after Unity started (Windows only). Empty when none did.
        /// </summary>
        [JsonProperty("staleVariables")] public AutomationStaleVariableInfo[] StaleVariables { get; set; } = Array.Empty<AutomationStaleVariableInfo>();

        /// <summary>
        /// What to do about the stale variables, or null.
        /// </summary>
        [JsonProperty("warning")] public string Warning { get; set; }
    }

    /// <summary>
    /// One folder in use.
    /// </summary>
    internal sealed class AutomationFolderInfo
    {
        [JsonProperty("path")] public string Path { get; set; }

        /// <summary>
        /// <c>editor_setting</c>, an environment variable name such as <c>HF_HOME</c>, or <c>default</c>.
        /// </summary>
        [JsonProperty("source")] public string Source { get; set; }

        [JsonProperty("drive")] public string Drive { get; set; }
        [JsonProperty("freeBytes")] public long? FreeBytes { get; set; }
        [JsonProperty("totalBytes")] public long? TotalBytes { get; set; }
    }

    /// <summary>
    /// One environment variable whose value in the Editor differs from what newly started programs get.
    /// </summary>
    internal sealed class AutomationStaleVariableInfo
    {
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("editorValue")] public string EditorValue { get; set; }
        [JsonProperty("currentValue")] public string CurrentValue { get; set; }
    }
}
