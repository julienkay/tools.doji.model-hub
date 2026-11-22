# Command-Line Automation

AI Model Hub exposes its Editor workflows to the Unity CLI (`com.unity.pipeline`), so agents, scripts, and CI
can read status, run a sync, and change file selections without the window. User docs:
[docs/manual/command-line.md](../docs/manual/command-line.md). Agent skill:
[Skills~/model-hub-cli](../tools.doji.model-hub/Skills~/model-hub-cli/SKILL.md).

## Layers

| Layer | Where | Job |
|---|---|---|
| `ModelHubCliCommands` | `Editor/Cli`, assembly `Doji.ModelHub.Editor.Cli` | `[CliCommand]` wrappers: argument names, batch-mode wait. No logic. |
| `ModelHubAutomation` | `Editor/Automation`, assembly `Doji.ModelHub.Editor` | The tool surface: status DTOs, argument checks, refusals. Calls the services below. |
| `BackgroundSyncManager` | `Editor/Sync` | Sync plan, start, run record, progress, wait. The window, the setup prompt, the menu, and the CLI all start syncs here. |
| `StatusService`, `ModelHubService` | `Editor/Status` | Local status and file rows, as the window shows them. |
| `OptionalFileSelection.SetEnabled`, `EditorOperations.ApplyProjectOverrides` | `Editor/Discovery`, `Editor/UI` | The Files tab and Details tab edits. The window and the CLI share them. |

`ModelHubBatch` (`Editor/Automation`) is the one public type: `-executeMethod` entries `Sync` and `Status` for CI
without Pipeline. They call `ModelHubAutomation`, log and optionally write the same JSON, and call
`EditorApplication.Exit` with 0 (ready), 1 (failed or not ready), or 2 (argument error). `ModelHubAutomation` and
the DTOs stay internal: the JSON is the contract, so the C# shapes can change. Only `Sync` and `Status` are batch
entries; the other commands change state that a CI job sets in the manifest or project settings instead.

`Doji.ModelHub.Editor.Cli` compiles only when `com.unity.pipeline` is installed: a `versionDefines` entry sets
`MODEL_HUB_PIPELINE` and `defineConstraints` requires it. It references only `Unity.Pipeline.Attributes`, the
assembly with `[CliCommand]` and `[CliArg]`. `Doji.ModelHub.Editor` has no Pipeline dependency, and the tests
cover `ModelHubAutomation` directly, so tests also run without Pipeline.

## Rules

- **Never block the main thread.** The Pipeline server runs `MainThreadRequired` commands on the main thread with
  a short budget, and a modal dialog blocks it until a person answers, so every later command times out. No
  automation path calls `EditorUtility.DisplayDialog`. The dialogs in `Editor/UI` (remove from cache, unapplied
  settings, manifest errors) stay in the UI code; the automation calls the service behind each one.
- **Refuse what needs a person.** A step that the window confirms with a dialog becomes a
  `ModelHubAutomationException` that names the flag to pass, for example `--confirm true` for
  `model_hub_remove_cache`. The Pipeline server returns an `ArgumentException` as a failed request with the message.
- **Never return silently.** `BackgroundSyncManager.Start` returns a `SyncStartResult` with a `SyncPlanEntry` per
  dependency in scope: the mode it runs, or a `SyncSkipReason` (`Ready`, `InvalidRepository`, `SelectionRequired`,
  `RevisionConflict`, `AlreadyRunning`). The menu and window paths log the same explanation to the Console when
  nothing starts. A domain-reload resume logs nothing.
- **Long work runs in the background.** `model_hub_sync` returns once the runner started. In batch mode
  (`Application.isBatchMode`), or with `--wait true`, the command returns a `Task` that completes from
  `BackgroundSyncManager.WaitForRunToEndAsync`; the Pipeline server awaits it off the main thread, so the Editor
  loop keeps running the sync. `unity run --command` exits the Editor after the command, so it must wait.

## Sync plan

`BuildSyncPlan` replaces the old pending-item filter. Revision conflicts are now skipped at plan time, with the
conflict message, instead of running and failing in the report. `RunAsync` still blocks a conflict it finds in a
resumed queue.

## Run record and progress

`BackgroundSyncManager` keeps one `SyncRunRecord` for the running or last run and saves it to
`Library/Doji/ModelHub/last-sync.json` at start and end, so `model_hub_sync_status` answers after a domain reload
or an Editor restart.

| State | Set by |
|---|---|
| `running` | `BeginRun`. A resumed run keeps its record and increments `resumeCount`. |
| `succeeded` / `failed` | `Complete` (failed when a model failed), `Fail` (the run stopped at a failed model and saved its queue), the runner's unhandled-exception callback |
| `canceled` | `OnCanceled` |
| `interrupted` | `LastRun` when a `running` record has no active run, no runner, and no saved queue: the Editor closed during the run |

`model_hub_sync_status` adds `resumable` when a saved queue waits for the next domain reload or sync.

Live progress is a `SyncProgressSnapshot`, immutable and swapped through a `volatile` field, because download
progress can arrive on any thread. `RawCacheService` calls `IFileDownloadProgress.OnFileStarted` before each
file, so the snapshot names the current file and its size; the bytes come from the same `DownloadProgress` that
feeds the Background Tasks entry.

## File edits

`model_hub_set_file` refuses to run while a sync runs. `--enabled` changes the optional selection of every owner
whose manifest marks the path optional, through `OptionalFileSelection.SetEnabled`; it refuses to turn off a
required file and names the listed files when the path is not listed. `--quantization` builds a
`FileRecipeEditState` from the file row, as the Files tab does, and writes a project override through
`EditorOperations.ApplyProjectOverrides` only when the recipe changed. Neither starts a sync, unlike the Files
tab's **Apply & Convert**: an agent should ask before a download starts.

## Testing

`ModelHubAutomation.StatusReportOverride` replaces the installed manifests, and `SyncRunStore.PathOverride`
moves the run record. See [testing.md](testing.md).
