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
- prefer `download needed` or `import needed` states when visually testing revision ambiguity unless you deliberately want last-writer-wins imported metadata for the repeated repo
- use sandbox for visual verification of obscure Model Hub states such as `import needed`, `update needed`, `partial conversion`, and invalid metadata
- use repeated same-repo entries to test both multi-manifest shared dependencies and revision ambiguity warnings before reaching for fake embedded packages

## Shared Import Storage

Import storage now has two layers:

- owner binding metadata under `Library/Doji/HfHub/imports/bindings/...`
- shared repo import metadata under `Library/Doji/HfHub/imports/shared/...`

Model Hub should still think in repo rows, but maintainer-facing removal and stale reasoning must respect this split:

- owner binding mismatch means one manifest owner needs re-import
- shared import mismatch means generated repo artifact itself needs re-import
- removing one owner must not delete shared runtime artifacts while another binding still points at same shared import
