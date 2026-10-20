# Editor Model Hub UI

## Intent

The editor UX is split into two surfaces with different jobs:

- `Project Settings > Doji > AI > AI Model Hub` stores project-scoped configuration.
- `Window > AI Model Hub` is the operational inventory and diagnostics surface.

This keeps settings stable and sparse while giving the Hub window room to behave more like Unity Package Manager.

## Surface Split

### Project Settings

Project Settings should only answer:

- where shared cache lives for this project
- what effective cache path HF Hub is using
- what project-scoped model ids are declared for this project
- how to open the main Model Hub window

Current editable settings:

- cache location override
- project manifest entries stored at `ProjectSettings/Packages/tools.doji.hf-hub/hf-hub-manifest.json`

Keep environment-variable override support for tests and explicit maintainer workflows. Project settings apply only when `DOJI_HF_HUB_CACHE_ROOT` is not set.

### Model Hub Window

The Model Hub window is repository-centric, not dependency-centric.

Current implementation is split by editor task area instead of keeping one large IMGUI file:

- `Editor/UI/ModelHubWindow.cs`: lifecycle, top-level orchestration, shared state fields
- `Editor/UI/ModelHubWindow.Content.cs`: window-local labels and tooltips
- `Editor/UI/ModelHubWindow.Styles.cs`: lazy GUI content and style initialization
- `Editor/UI/ModelHubWindow.StatusRendering.cs`: row badges, icons, and color helpers
- `Editor/UI/ModelHubWindow.Layout.cs`: pane math and draggable splitter behavior
- `Editor/UI/ModelHubWindow.ListPane.cs`: sidebar search, grouping, and row rendering
- `Editor/UI/ModelHubWindow.DetailsPane.cs`: details header, tabs, details/storage sections, dependents, diagnostics
- `Editor/UI/ModelHubWindow.FilesTab.cs`: file recipe editing UI and apply/revert flow
- `Editor/UI/ModelHubWindow.Navigation.cs`: selection, filtering, and tab-availability helpers
- `Editor/UI/ModelHubWindow.Actions.cs`: refresh, locate, and manage actions
- `Editor/UI/ModelHubWindow.Formatting.cs`: path/size formatting helpers used by details UI
- `Editor/UI/ModelHubFileEditing.cs`: extracted per-file edit session and recipe state models

Maintainer rule:

- keep display-only helpers out of edit-state classes
- keep recipe-building logic out of generic list/details rendering partials
- add new UI work to existing task-area partials before creating new ones
- prefer new partials only when ownership boundary is stable enough to name clearly

Primary item key:

- `repository`
- requested `revision`

Each row aggregates all manifest dependents that resolve to the same repository dependency unit.

The `Details` tab answers:

- why this repo exists in the project
- which package or project manifests depend on it
- what source revision metadata HF Hub resolved
- how large the source model files are according to HuggingFace
- what diagnostics or stale reasons apply

The `Storage` tab answers:

- what local storage state this repo is currently in
- where the raw cache snapshot lives
- where generated runtime files live
- how much disk space the cache and generated runtime assets currently use

Maintainer rule for size labels:

- `Details > Size` means source model size reported by HuggingFace, not local disk usage
- `Storage` is the only place that should describe current local cache/runtime footprint

Ready-state actions are intentionally split by ownership boundary:

### Context-Sensitive Action Button

The primary action button in the details header changes label and mode based on the item's current cache and dependency state:

| Visible state | Button label | Mode |
|---|---|---|
| No cache snapshot exists | **Download** | `Full` |
| Cache exists but declared files not yet cached (`RawCacheFilesMissing`) | **Update + Import** | `Full` |
| Cache complete, import stale or missing | **Import** | `Full` |
| Item ready | **Manage** (dropdown) | n/a |

All per-item actions (Download, Update + Import, Import, Manage submenu) operate on that single repository only via `StartForRepository(repository, revision, mode)`.
Only **Import All** in the window header triggers a global sync across all dependencies.

`RawCacheFilesMissing` renders as `MessageType.Info` (not warning) in the Diagnostics tab because it is a normal expected state when file selection grows after an initial download — not a corruption.

- `Remove From Project` deletes owner binding metadata for this Unity project and garbage-collects shared runtime assets only when no bindings still reference them.
- `Remove From Cache` deletes this machine's raw cache entry while leaving generated project assets intact.

That split matters because imported runtime files are project-owned artifacts, while raw snapshots are machine-local shared cache artifacts. Model Hub status must keep imported entries usable after cache removal as long as import metadata and runtime files still validate.

## Repo Metadata File Filtering

`.gitattributes`, `.gitignore`, `README.md`, and localised `README_*.md` variants are **excluded** everywhere files are presented or selectable. They have no runtime value in Unity and would pollute the Files tab, Storage tab, size calculations, import pipeline, and file-selection picker.

The predicate is `ModelDependencyDefinition.IsRepoMetadataFile(string repoPath)` (Runtime assembly), shared by all call sites.

**All filter call sites — add new ones here when adding new file-facing surfaces:**

| Location | What it gates |
|---|---|
| `ModelHubService.GetRepositoryInventory` (raw metadata path) | Strips from `RepositoryFileMetadata` list fed into selected-path expansion, file-count fields, and size estimates |
| `ModelHubService.GetRepositoryInventory` (import metadata path) | Same, for imported metadata source |
| `ModelHubService.GetRepositoryInventory` (persisted estimate path) | Same, for pre-download remote estimate source |
| `ModelHubService.BuildFileItems` path union | Strips from Files tab and Storage treemap rows |
| `ManifestFileSelectionPopup.ShouldIncludeFile` | Strips from manifest inspector file-picker (All / ONNX / Selected filter modes) |

Raw cache on disk is **not** affected — the shared cache stays a faithful mirror of the Hub repository. The filter is presentation-only.

## Aggregation Model

Existing status evaluation still works per discovered dependency because stale checks, import metadata ownership, and package-facing diagnostics are naturally attached to one manifest owner.

The Model Hub layer adds one editor-facing aggregation step:

1. build normal `HfHubStatusReport`
2. group dependency statuses by `repository + revision`
3. merge dependents, runtime files, and high-level diagnostics into one `HfHubModelHubItem`

UI diagnostic requirement:

- dependency-level status should expose structured `ReasonCode` plus optional maintainer-facing detail text
- Model Hub must map `ReasonCode` into user-helpful lifecycle copy before rendering it
- raw maintainer detail is fallback/debug text, not primary user copy
- prefer action-oriented wording such as `needs download`, `needs import`, `needs re-import`, or `is out of date`
- reserve error-severity rendering for true failed/invalid states, not normal `import needed` or `update needed` cases

Aggregate state precedence:

1. `Error` if any dependent failed
2. `ActionRequired` if any dependent is missing or stale
3. `Ready` otherwise

This keeps lower-level validation unchanged while giving the UI a Package-Manager-style inventory shape.

## Revision Conflict Policy

HF Hub keeps runtime output repo-scoped and human-readable instead of adding commit-key folders under `StreamingAssets`.

That means one project-level rule is explicit:

- one canonical repository may appear in many owners, but all owners in one project must request same revision

If same repository is declared at multiple requested revisions:

- every affected dependency is conflict state, not `Ready`
- Model Hub still shows one row per `repository + requested revision` so users can see each conflicting request
- every conflicting row renders `Error`
- primary diagnostic should say repo is declared at multiple revisions and HF Hub supports only one revision per repo per project
- `Locate` stays available so users can jump to conflicting manifests
- conflict summary UI should classify owners as project, embedded/local package, or installed package so user sees whether fix is direct manifest edit or package-version change
- conflict UX should offer clipboard summary and `Open Package Manager` when installed packages participate
- import and manage actions stay disabled for conflicting rows until project resolves conflict to one revision
- `Import All` must skip or fail those dependencies without touching shared runtime payload for that repo

Later if pain stays common:

- add explicit project-level revision override as opt-in escape hatch
- keep it loud and inspectable in Model Hub because it violates package-declared revision expectations
- never silently pick one package winner

## Future Status Granularity

Current dependency-level status is still backend-oriented:

- `Ready`
- `Missing`
- `Stale`
- `Failed`

That works for validation, but `Missing` currently covers several user-visible situations:

- raw cache was never downloaded
- raw cache exists, but import metadata is missing
- import metadata exists, but generated runtime files are missing

If the UI keeps needing direct lifecycle copy, a better future split would be:

- `DownloadNeeded`
- `ImportNeeded`
- `UpdateNeeded`
- `Ready`
- `Failed`

`RawCacheFilesMissing` is the current concrete code that approximates `UpdateNeeded` — the button and diagnostic severity already reflect this intent even before a formal status split.

That would let prompts and badges choose wording directly from status instead of inferring `download` versus `import` from raw-cache fields.

## Why Repo-Centric

Import, cache, and runtime output are already repo-level:

- raw cache stores repository snapshots
- import converts all ONNX files in a snapshot
- runtime output path is shared by repository, not by manifest owner

Showing one row per declared dependency would duplicate entries and hide the actual artifact boundary.

## Future Extensions

The current window is intentionally a local inventory surface first.

Future work can extend the same window with:

- a remote browse tab with thumbnails and richer HuggingFace metadata

Those features should reuse the repo-level item model instead of reintroducing dependency-level top rows.

## Model Manifest Inspector

`Editor/UI/ModelManifestInspector.cs` owns the custom inspector for `hf-hub-manifest.json` assets.

Shared JSON-backed manifest editing and validation UI now lives in:

- `Editor/UI/ManifestDocumentEditor.cs`
- `Editor/UI/ManifestModelListEditor.cs`
- `Editor/UI/ModelIdPopupValidationController.cs`

Those helpers are reused by both:

- package `hf-hub-manifest.json` custom Inspector
- Project Settings project-manifest editor
- add-model popups for transient repo validation

Current validation behavior for the small repository-exists icon:

- validation is editor-only and package-authoring focused
- only writable package manifests trigger background validation
- checks run on explicit `Apply` or `Revert`, not on every keystroke
- rows show a temporary pending icon when cache has no result yet or a check is in flight
- cached invalid results are promoted into shared dependency status, so Model Hub and sync skip repositories already known to be missing upstream

Shared invalid-repository flow:

1. `RepositoryValidationCacheService` stores repo-exists checks under `Library/.../hf-hub-repo-validation.json`
2. `ModelManifestInspector` writes successful yes/no checks into that shared cache
3. `StatusService` upgrades cached negative results into `DependencyReasonCode.InvalidRepository`
4. `ModelHubService` filters those dependency statuses out instead of showing a broken row
5. `BackgroundSyncManager` excludes them when building pending sync queues

Maintainer rule:

- treat cached invalidity as an editor workflow hint, not manifest-schema validation
- if a new surface needs to know that a repo is invalid, consume `DependencyReasonCode.InvalidRepository` from shared status instead of reading the cache file directly
- keep sync and Model Hub policy aligned; do not let one surface hide invalid repos while another still tries to process them

Current cache strategy:

- cache file lives under `Library/Doji/HfHub/model-manifest-validation/hf-hub-repo-validation.json`
- cache key is `modelId` (`owner/name`)
- cache survives inspector redraws and script reloads, but is not project data
- entries expire by age only; anything older than 1 day is treated as a cache miss
- stale entries are ignored on read and replaced only when a later explicit validation succeeds
- failed network checks do not overwrite the previous cached result
- overlapping validation requests for the same `modelId` share one in-flight task

Maintainer rule:

- do not refresh validation state from cache every `OnInspectorGUI()` call
- preserve transient `Checking` / pending UI until the async validation pass completes

### Add-Model Popup Validation

`Editor/UI/AddProjectModelPopup.cs` reuses the same upstream repo-exists check shape, but not the same persistence policy.

Shared popup validation flow now lives in:

- `Editor/UI/ModelIdPopupValidationController.cs`

Current popup behavior:

- validation runs only when both `Owner` and `Model` fields are non-empty and neither field is focused
- popup uses `RepositoryValidationCacheService.GetOrStartRepositoryValidationAsync(...)` only for in-flight request dedupe
- popup does not read or write `Library/.../hf-hub-repo-validation.json`
- popup validation result is transient UI state for that popup instance only
- `Add` stays disabled until the current `owner/model` resolves as valid

Maintainer rule:

- keep persistent `Library` cache for manifest-inspector/package-authoring validation
- keep add-model popup validation ephemeral unless a stronger project-wide reason appears
- do not let short-lived project-manifest authoring UI silently expand `Library` cache churn
- keep add-model popups repo-id-only for now; do not add revision-aware validation until those popups expose an authored revision input and can validate the exact user-entered target instead of inventing hidden defaults

## Revision Semantics

Authored revision text and effective revision resolution are intentionally separate.

Central resolution helper:

- `Runtime/Manifest/ManifestRevision.cs`

Rules:

- editor surfaces preserve authored revision text exactly
- empty revision field stays empty in editor state and can serialize as omitted revision
- explicitly typed `main` stays explicitly typed `main` in editor state and JSON when user chose that
- downstream discovery/runtime/sync code should use `ModelDependencyDefinition.EffectiveRevision` or `ManifestRevision.NormalizeForResolution(...)`

Maintainer rule:

- do not add new ad hoc `string.IsNullOrWhiteSpace(revision) ? "main" : ...` logic in editor or runtime code
- treat `Revision` as authored/raw text
- treat `EffectiveRevision` as operational value for lookup, status, sync, cache refs, and runtime resolution

## Visual Sandbox

Companion project now includes maintainer-only visual sandbox under:

- `projects/HF Hub/Assets/HF Hub/Scenario Sandbox/`

Detailed maintainer behavior, constraints, and supported workflow live in:

- `.codex/architecture/visual-scenario-sandbox.md`

## Shared Import Storage

Import storage now has two layers:

- owner binding metadata under `Library/Doji/HfHub/imports/bindings/...`
- shared repo import metadata under `Library/Doji/HfHub/imports/shared/...`

Model Hub should still think in repo rows, but maintainer-facing removal and stale reasoning must respect this split:

- owner binding mismatch means one manifest owner needs re-import
- shared import mismatch means generated repo artifact itself needs re-import
- removing one owner must not delete shared runtime artifacts while another binding still points at same shared import
