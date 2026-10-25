# Editor Model Hub UI

## Intent

All editor UI is UI Toolkit, built in C# with USS. There is no UXML: lists, tabs, and rows are dynamic, so code builds them and USS classes style them.

The editor UX has four surfaces with separate jobs:

- `Window > AI Model Hub`: the operational inventory. One row per repository, status, the next action, optional files, import settings, and storage.
- Setup prompt (`AutoPromptWindow`): a one-click "get the models your packages need" dialog.
- `Project Settings > Doji > AI > AI Model Hub`: project-scoped configuration only (project manifest, cache folder).
- Manifest Inspectors: authoring for package `hf-hub-manifest.json` and `ProjectManifestAsset`.

User-facing behavior is documented in `docs/manual/editor-surfaces.md`. This page covers structure and rules.

## Code Layout

All files live in `Editor/UI/`.

| File | Owns |
|---|---|
| `Styles/HfHub.uss` | Theme tokens (`.hf-dark`, `.hf-light`) and shared components: buttons, pills, dots, tags, callouts, cards, sections, key/value rows, search field, progress bar, empty state |
| `Styles/ModelHubWindow.uss`, `Styles/SetupPrompt.uss`, `Styles/Settings.uss`, `Styles/ManifestEditor.uss` | Layout for one surface each. `ManifestEditor.uss` also styles the Add Model popup and the file picker |
| `UiKit.cs` | Element factories that apply the shared classes, `ApplyTheme`, icon loading, `FormatBytes` |
| `ModelStatus.cs` | The single mapping from `ModelHubItem` to status label, description, tone, and next action |
| `ModelHubWindow.cs` | Window lifecycle, toolbar, sync progress banner, list, selection, keyboard navigation |
| `ModelHubWindow.Details.cs` | Header, actions, status callouts, tabs, Overview tab, manifest-error details, empty state |
| `ModelHubWindow.Files.cs` | Files tab, per-file import settings, apply bar, unsaved-settings prompt |
| `ModelHubWindow.Storage.cs` | Storage tab |
| `ModelHubWindow.Cache.cs` | Download cache row and view: legend, treemap, selection card, removal dialogs |
| `TreemapLayout.cs` | Squarified treemap layout (no UI) |
| `ModelHubFileEditing.cs` | Edit-session and recipe state models for the Files tab (no UI) |
| `AutoPromptWindow.cs` | Setup prompt and its view model (`BuildViewModel`) |
| `AddModelPopup.cs` | Add Model popup (project or local package target) |
| `FileSelectionPopup.cs` | Repository file picker with required/optional selection |
| `ManifestEditorElement.cs` | Shared manifest table used by Project Settings and both inspectors |
| `ManifestModelValidation.cs` | `ManifestModelEntry`, model id parsing, Hugging Face validation states and controller (no UI) |
| `ManifestDocumentStores.cs` | `IManifestDocumentStore` and the JSON, Project Settings, and asset stores |
| `ProjectSettingsProvider.cs`, `ModelManifestInspector.cs`, `ProjectManifestAssetInspector.cs` | Host the shared elements |

Maintainer rules:

- Style through USS classes. Do not set `element.style.*` except for data-driven values such as progress and size-bar widths.
- Use the `--hf-*` tokens for colors. Add a token to both `.hf-dark` and `.hf-light` instead of hard-coding a color in a surface sheet.
- Read status copy from `ModelStatus`. Do not re-derive "download vs import" wording in a surface.
- Keep logic that tests need (view models, parsing, status mapping) out of element-building code.
- Stylesheets load from `Packages/tools.doji.hf-hub/Editor/UI/Styles/` through `UiKit.ApplyTheme`.

## Surface Split

### Project Settings

Project Settings answers only:

- which models this project declares itself (`ProjectSettings/Packages/tools.doji.hf-hub/hf-hub-manifest.json`)
- where the shared cache lives, and which folder is in effect
- which Sentis version imports target

The default folder is the Hugging Face cache (`HF_HUB_CACHE`, `HF_HOME/hub`, `~/.cache/huggingface/hub`); the page names the environment variable when one decides it. A custom folder overrides it for the project.

`ProjectSettingsProvider.Refresh()` reloads the open page after other surfaces change the project manifest or cache. It never discards unapplied edits.

### Model Hub Window

Primary item key is `repository` plus requested `revision`. Each row aggregates all manifest dependents of that unit.

List sections, in order: `Manifest errors` (discovery issues), `Needs action` (errors, then choices, then routine work), `Ready`. The `Download cache` row is pinned below the list, outside the search filter, with key `cache`.

Details pane:

- header: model name, repository link, revision tag (resolved commit in tooltip), status pill
- actions: primary action from `ModelStatus`, `Show Manifest`, and a `⋮` menu (open on Hugging Face, copy id, Pull into Project or Remove Pulled Copies, Remove from Project, Remove from Cache)
- Details card: `Format` from `ModelFiles.Describe` of the selected model files, and `In builds` (`BuildDeliveryField`): project default, bundle, or download, stored in `ProjectSettings` as a per-repository override. It invalidates `EditorRuntimeCatalog`. A hint appears when download is chosen for ONNX files without `tools.doji.onnx-model-loader`
- status callout: the status description; error diagnostics are added only for error states, because download/import/update diagnostics repeat the status
- tabs: `Overview` (optional files, details, used by), `Files` (only when inventory is known), `Storage` (only when something is on disk); the strip is hidden when only Overview exists

Size labels:

- `Download size` in Overview and the list size of pending rows is the remote size of the selected files (`SourceSizeBytes`)
- `Storage` describes one model's local disk usage; the `Download cache` view describes the whole cache

Download cache view:

- data comes from `CacheOverviewService.Scan`, which reads every `models--`, `datasets--`, and `spaces--` folder from disk, like `scan_cache_dir`. Each blob counts once; plain snapshot files (no symlinks) count separately; `.incomplete`, `.downloading`, and `.downloading.segment` (the body of the current request) files are partial downloads; deleting a partial download also deletes its resume state and segment. Tiles are named by the snapshot path that links to the blob, else by HF Hub's index, else by the etag
- "used by this project" means a model repository that the current report lists, compared without letter case
- the treemap lays out tiles with `TreemapLayout.Squarify` on `GeometryChangedEvent`, as absolutely positioned elements. The Files view nests file tiles in repository groups and merges files past the largest 120 into one tile
- `Remove from Cache` takes every blob lock of the repository first and deletes nothing if one is held, then deletes the repository folder, its `.locks` folder, and its index folder. It removes all revisions, unlike the per-model action, which removes one revision
- both removal actions are disabled while a sync runs

The window polls `BackgroundSyncManager.TryGetProgress` to show the progress banner and refreshes its report whenever a sync stops, in addition to `SyncStateChanged`.

Overview requests the remote inventory (`RemoteSnapshotSizeEstimateService.EnsureEstimateLookup`) whenever the item has no file list yet, so optional-file sizes show before any download.

### Primary Action

| Status (`ModelStatusKind`) | Condition | Button | Runs |
|---|---|---|---|
| `Conflict` | any dependent has `RevisionConflict` | none | n/a |
| `Ready` | item state `Ready` | none | n/a |
| `ChooseFiles` | any dependent has `SelectionRequired` | **Choose Files** | switches to Overview |
| `RepairNeeded` | any dependent has a `DependencyStatus.DamagedCacheCodes` code | **Repair** (with size) | `StartForRepository(..., Full)` |
| `DownloadNeeded` | no cache snapshot | **Download & Import** (with size) | same |
| `Error` | item state `Error` with cache | **Retry Import** | same |
| `UpdateNeeded` | `RawCacheFilesMissing`, or runtime files exist but are stale | **Update** | same |
| `ImportNeeded` | cache exists, nothing imported | **Import** | same |

All per-item actions operate on one repository only. Only **Import All** (toolbar, setup prompt, menu) runs a global sync. Nothing starts a download without a user action; see "Damaged Cache" in `download-and-cache.md`.

- `Remove from Project` deletes owner binding metadata and shared import records for this project. Converted models stay in the shared store. Pulled copies go only when no binding for the repository remains
- `Pull into Project` copies runtime models into `Assets/StreamingAssets/hf-hub/models/<owner>/<name>/` (`PulledModels`); builds then skip bundling them.
- `Remove from Cache` deletes this machine's download cache entry while leaving converted models intact.

That split matters because imported runtime files are project-owned artifacts, while raw snapshots are machine-local shared cache artifacts. Model Hub status must keep imported entries usable after cache removal as long as import metadata and runtime files still validate.

### Setup Prompt

`AutoPrompt` opens `AutoPromptWindow` once per session when `StatusReport.NeedsStartupPrompt` is true. `BuildViewModel` groups attention statuses by repository. A repository needs a download when any dependent has no the download cache or reports `RawCacheFilesMissing`. The primary label includes the total download size only when every pending size is known. The primary action starts a full sync and opens Model Hub, which shows progress.

## Repo Metadata File Filtering

`.gitattributes`, `.gitignore`, `README.md`, and localised `README_*.md` variants are **excluded** everywhere files are presented or selectable. They have no runtime value in Unity and would pollute the Files tab, Storage tab, size calculations, import pipeline, and file-selection picker.

The predicate is `ModelDependencyDefinition.IsRepoMetadataFile(string repoPath)` (Runtime assembly), shared by all call sites.

**All filter call sites — add new ones here when adding new file-facing surfaces:**

| Location | What it gates |
|---|---|
| `ModelHubService.GetRepositoryInventory` (raw metadata path) | Strips from `RepositoryFileMetadata` list fed into selected-path expansion, file-count fields, and size estimates |
| `ModelHubService.GetRepositoryInventory` (import metadata path) | Same, for imported metadata source |
| `ModelHubService.GetRepositoryInventory` (persisted estimate path) | Same, for pre-download remote estimate source |
| `ModelHubService.BuildFileItems` path union | Strips from Files tab and Storage rows |
| `FileSelectionPopup.LoadAsync` | Strips from the file picker |

The download cache on disk is **not** affected — the shared cache stays a faithful mirror of the Hub repository. The filter is presentation-only.

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

Import records, the runtime registry, and bundled paths are repo-scoped and do not encode the commit.

That means one project-level rule is explicit:

- one canonical repository may appear in many owners, but all owners in one project must request same revision

If same repository is declared at multiple requested revisions:

- every affected dependency is conflict state, not `Ready`
- Model Hub still shows one row per `repository + requested revision` so users can see each conflicting request
- every conflicting row renders `Error`
- primary diagnostic should say repo is declared at multiple revisions and HF Hub supports only one revision per repo per project
- `Show Manifest` and the Used by rows stay available so users can jump to conflicting manifests
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

- the files were never downloaded
- the files are downloaded, but import metadata is missing
- import metadata exists, but converted models are missing

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

- the download cache stores repository snapshots
- import converts all ONNX files in a snapshot
- runtime output path is shared by repository, not by manifest owner

Showing one row per declared dependency would duplicate entries and hide the actual artifact boundary.

## Future Extensions

The current window is intentionally a local inventory surface first.

Future work can extend the same window with:

- a remote browse tab with thumbnails and richer HuggingFace metadata

Those features should reuse the repo-level item model instead of reintroducing dependency-level top rows.

## Manifest Editor

`ManifestEditorElement` is the shared manifest table for the package `hf-hub-manifest.json` Inspector (`ModelManifestInspector`, a `TextAsset` editor that falls back to Unity's text inspector for other files), the `ProjectManifestAsset` Inspector, and Project Settings.

- one `Model ID` field per row; `ManifestModelEntry.TryParseModelId` accepts `owner/name` or a Hugging Face URL
- revision field with a `main` placeholder; authored text is preserved (see Revision Semantics)
- `Files` opens `FileSelectionPopup`, enabled only when the row's model id and revision are known to exist
- `Apply` and `Revert`; inspectors ask about unapplied edits when they close (`ManifestInspectorUnsavedChanges`)
- read-only stores show labels, a `Read-only` tag, and an embed hint

Validation (`ManifestModelValidationController`):

- validation is editor-only and package-authoring focused; only editable stores validate
- rows are checked when they load or change and after `Apply` or `Revert`; each badge shows unknown, checking, valid, or invalid
- checks merge per row: a batch only replaces rows that are still `Checking`, so other rows keep their state and a newer edit is never overwritten by an older check
- results not in the persistent cache stay in memory until the row changes
- cached invalid results are promoted into shared dependency status, so Model Hub and sync skip repositories already known to be missing upstream

Shared invalid-repository flow:

1. `RepositoryValidationCacheService` stores repo-exists checks under `Library/.../hf-hub-repo-validation.json`
2. `ManifestEditorElement` writes successful yes/no checks into that shared cache when the store persists validation
3. `StatusService` upgrades cached negative results into `DependencyReasonCode.InvalidRepository`
4. `ModelHubService` filters those dependency statuses out instead of showing a broken row
5. `BackgroundSyncManager` excludes them when building pending sync queues

Maintainer rule:

- treat cached invalidity as an editor workflow hint, not manifest-schema validation
- if a new surface needs to know that a repo is invalid, consume `DependencyReasonCode.InvalidRepository` from shared status instead of reading the cache file directly
- keep sync and Model Hub policy aligned; do not let one surface hide invalid repos while another still tries to process them

Cache strategy:

- cache file lives under `Library/Doji/HfHub/model-manifest-validation/hf-hub-repo-validation.json`
- cache key is `modelId` (`owner/name`)
- entries expire by age only; anything older than 1 day is treated as a cache miss
- failed network checks do not overwrite the previous cached result
- overlapping validation requests for the same `modelId` share one in-flight task

### Add Model Popup

`AddModelPopup` adds one model to the project manifest or to a local package manifest (`LocalPackageManifestStore`).

- validation runs 400 ms after the last keystroke, through `ManifestModelValidationController.ValidateDependencyAsync(..., writePersistentCache: false)`
- the popup does not write `Library/.../hf-hub-repo-validation.json`; its result is transient
- `Add` stays disabled until the current id resolves as valid
- the popup is repo-id only and validates `main`; do not add revision-aware validation until it exposes a revision input

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

- `internal-docs/visual-scenario-sandbox.md`

## Shared Import Storage

Import storage now has two layers:

- owner binding metadata under `Library/Doji/HfHub/imports/bindings/...`
- shared repo import metadata under `Library/Doji/HfHub/imports/shared/...`

Model Hub should still think in repo rows, but maintainer-facing removal and stale reasoning must respect this split:

- owner binding mismatch means one manifest owner needs re-import
- shared import mismatch means generated repo artifact itself needs re-import
- removing one owner must not delete shared runtime artifacts while another binding still points at same shared import
