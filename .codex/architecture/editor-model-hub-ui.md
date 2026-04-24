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
- how to open the main Model Hub window

Current setting:

- cache location override

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
- `Editor/UI/ModelHubWindow.DetailsPane.cs`: details header, tabs, summary, dependents, diagnostics
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

The details pane answers:

- why this repo exists in the project
- which package or project manifests depend on it
- where the raw cache snapshot lives
- where generated runtime files live
- what diagnostics or stale reasons apply

Ready-state actions are intentionally split by ownership boundary:

- `Remove From Project` deletes owner binding metadata for this Unity project and garbage-collects shared runtime assets only when no bindings still reference them.
- `Remove From Cache` deletes this machine's raw cache entry while leaving generated project assets intact.

That split matters because imported runtime files are project-owned artifacts, while raw snapshots are machine-local shared cache artifacts. Model Hub status must keep imported entries usable after cache removal as long as import metadata and runtime files still validate.

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

## Visual Sandbox

Companion project now includes maintainer-only visual sandbox under:

- `projects/HF Hub/Assets/HF Hub/Scenario Sandbox/`

Use it to generate fake project manifest, cache, import metadata, and runtime files locally without downloading from HuggingFace.

Rules:

- keep sandbox code in companion project only, not package assemblies
- prefer unique `sandbox/...` repo ids so fake artifacts do not collide with real generated content
- sandbox writes one generated project manifest asset per scenario entry so repeated repo ids simulate multiple manifests depending on the same repo
- repeated repo ids intentionally still share one project import/runtime slot because real HF Hub imports are repo-scoped by repository
- prefer explicit revision-conflict scenarios when visually testing repeated repo ids at different requested revisions
- use sandbox for visual verification of obscure Model Hub states such as `import needed`, `update needed`, `partial conversion`, and invalid metadata
- use repeated same-repo entries to test both multi-manifest shared dependencies and blocked revision-conflict errors before reaching for fake embedded packages

## Shared Import Storage

Import storage now has two layers:

- owner binding metadata under `Library/Doji/HfHub/imports/bindings/...`
- shared repo import metadata under `Library/Doji/HfHub/imports/shared/...`

Model Hub should still think in repo rows, but maintainer-facing removal and stale reasoning must respect this split:

- owner binding mismatch means one manifest owner needs re-import
- shared import mismatch means generated repo artifact itself needs re-import
- removing one owner must not delete shared runtime artifacts while another binding still points at same shared import
