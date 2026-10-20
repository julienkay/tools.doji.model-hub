# Architecture Milestones

Use this file for sparse historical context about completed architectural changes and decisions that still shape how future work should be done.

Do not use it for active TODOs, backlog items, detailed edit history, or internal docs process changes. Keep active next steps in `.codex/current-work.md`.

## Phase Progression

- Phase 1: manifest schema, discovery, normalized dependency model
- Phase 2: raw Hub client, shared cache, file metadata
- Phase 3: first project import layout and `.sentis` conversion
- Phase 4: stale detection, prompt, menu, and Project Settings UX
- Completed recent phase: extract generic download package

Why it matters:

- The package already covers the main editor-side vertical slice from package manifest discovery through generated `.sentis` output and local stale-status UX.
- Remaining work should extend that foundation rather than revisiting already-settled earlier phases.

Related docs:

- [Architecture Overview](./architecture/overview.md)
- [Manifest Schema](./architecture/manifest-schema.md)
- [Download And Cache](./architecture/download-and-cache.md)
- [Import And Generated Assets](./architecture/import-and-generated-assets.md)
- [Runtime Locator](./architecture/runtime-locator.md)

## Phase 1: Package-Owned Manifest Workflow And Discovery

- Model dependencies are declared by UPM packages in `hf-hub-manifest.json`, not by individual Unity projects.
- `tools.doji.hf-hub` now defines the core runtime-side manifest and dependency model, including `ModelHub`, `HfHubPackageManifest`, `HfHubModelDependencyDefinition`, manifest parsing, and shared discovery and reporting types.
- Editor discovery is intentionally narrow at this layer: installed package enumeration plus status/reporting over discovered manifest data.
- The package root includes `hf-hub-manifest.template.json` as a copyable starting point for package authors.

Why it matters:

- Future work should preserve the package-owned dependency model rather than drifting toward project-local model declarations.
- Discovery, normalization, and authoring ergonomics are already treated as package contracts, not incidental editor tooling.

Related docs:

- [Architecture Overview](./architecture/overview.md)
- [Manifest Schema](./architecture/manifest-schema.md)

## Phase 2: Shared Raw Cache And Hub File Resolution

- The editor gained first raw-cache infrastructure through `HfHubCachePaths`, `HfHubHttpClient`, `HfHubRawCacheMetadata`, and `HfHubRawCacheService`.
- Raw downloads moved to a shared per-user cache outside the Unity project, using HuggingFace-style `raw/<serialized-repo>/refs/<revision>` and `raw/<serialized-repo>/snapshots/<commit>/<sourceFile>` layout.
- Cached raw files keep sidecar `.hfhub.json` metadata so local status and stale checks can reason about cache state without requiring network access.

Why it matters:

- Raw downloads are reusable machine-local infrastructure, not project-local temp files.
- Future cache and resolution work should extend the Hub-style cache model instead of inventing a second local storage scheme.

Related docs:

- [Download And Cache](./architecture/download-and-cache.md)

## Phase 3: Project-Owned `.sentis` Generation

- The package gained project import infrastructure through `HfHubProjectPaths`, `HfHubImportedModelMetadata`, and `HfHubProjectImportService`.
- Generated project-owned artifacts currently live under `Assets/Doji/HfHub/Generated/<package-id>/<model-id>/`.
- Conversion works by copying cached ONNX into a temporary project asset path, letting Unity import it, then serializing `.sentis` with `ModelWriter.Save(...)`.
- Generated outputs include `<source-file-name>.sentis` plus `hf-hub-import.json` metadata, and temporary ONNX import assets under `Assets/Doji/HfHub/TempImport/...` are deleted after conversion.
- `HfHubSyncService` and `Tools/Doji/HF Hub/Sync Models` established the first end-to-end sync path that downloads raw files and generates project-owned `.sentis` artifacts.

Why it matters:

- Future runtime and editor workflows should treat generated `.sentis` artifacts as the main consumption format rather than raw ONNX.
- Import provenance and regeneration behavior already depend on generated metadata, so new work should extend that model instead of bypassing it.

Related docs:

- [Import And Generated Assets](./architecture/import-and-generated-assets.md)
- [Architecture Overview](./architecture/overview.md)

## Phase 4: Local Status, Stale Detection, And Non-Blocking Editor UX

- The editor gained `HfHubStatusService`, `HfHubStatusReport`, and per-dependency state classification as `Ready`, `Missing`, `Stale`, or `Failed`.
- Stale detection compares raw cache metadata, revision refs, generated import metadata, manifest hash, and current Sentis version without requiring network access.
- Status is surfaced through `Tools/Doji/HF Hub/Show Status`, the Project Settings page at `Doji -> AI -> AI Model Hub`, and an automatic once-per-session sync prompt.
- Download and sync moved away from blocking modal flow.
- `HfHubBackgroundSyncManager` now starts sync through Unity's `Progress` API, persists pending dependency keys under `Library/Doji/HfHub/sync-state.json`, and resumes after domain reload.
- Raw file download transport supports partial `.downloading` files plus HTTP `Range` resume attempts.

Why it matters:

- Sync is now designed as recoverable background infrastructure rather than a one-shot blocking editor command.
- Future UX and workflow changes should preserve local stale detection and interruption recovery as core behavior, not optional polish.

Related docs:

- [Download And Cache](./architecture/download-and-cache.md)
- [Import And Generated Assets](./architecture/import-and-generated-assets.md)

## Downloader Extraction Into `tools.doji.downloads`

- Generic resumable transfer, persisted job state, recovery primitives, and queue mechanics were extracted out of `tools.doji.hf-hub` into `tools.doji.downloads`.
- `tools.doji.hf-hub` kept ownership of HuggingFace-specific resolution, cache policy, prompts, status surfaces, and model import orchestration.
- The extraction is established enough that downloader behavior now has package-owned tests in `tools.doji.downloads`.

Why it matters:

- HF Hub should not re-accumulate generic downloader concerns.
- Future runtime-safe transfer work should extend the downloader package instead of rebuilding those mechanics inside HF Hub.

Related docs:

- [Architecture Overview](./architecture/overview.md)
- [Download And Cache](./architecture/download-and-cache.md)

## Confidence Milestones In Test Coverage

- Editor tests cover raw-cache paths, sidecar metadata reads, and file-lock exclusivity.
- Editor tests cover sync success and sync failure aggregation.
- Editor tests cover project import path sanitization and real `.onnx` to `.sentis` conversion using local Unity Inference sample ONNX files.
- Editor tests cover local status classification for missing, ready, stale, and failed cases plus auto-prompt decision logic.
- Editor tests include explicit live-network integration coverage against `hf-internal-testing/huggingface-hub-dummy-repository` and `onnx-community/tiny-clip-test-ONNX`.

Why it matters:

- These architectural slices are no longer just design notes; they already have regression coverage around the most failure-prone editor workflows.
- Future refactors should treat these areas as established behavior that must preserve test-backed contracts.

Related docs:

- [Architecture Overview](./architecture/overview.md)
- [Download And Cache](./architecture/download-and-cache.md)
- [Import And Generated Assets](./architecture/import-and-generated-assets.md)
