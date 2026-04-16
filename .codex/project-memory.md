# Project Memory

## Mission

`com.doji.hf-hub` should make HuggingFace-hosted ML models usable in Unity without shipping large binary model files inside UPM packages.

`com.doji.downloads` should provide reusable durable download infrastructure for that workflow, including resumable transfer, persisted state, and editor recovery primitives, while staying independent from HuggingFace and Sentis specifics.

## Baseline Versions

- Unity version: `6000.3.10f1`
- Sentis version: `2.6.1`

## Intended Workflow

1. Unity package maintainers declare remote model dependencies in a package-owned `hf-hub-manifest.json`.
2. The Hub detects those package manifests when packages are present in a Unity project.
3. The Editor prompts the user to download and import the required model dependencies, similar to a package manager flow for AI assets.
4. Source ONNX files are downloaded into a shared local cache so multiple Unity projects can reuse the same raw files.
5. The package converts those cached ONNX files into project-owned `.sentis` artifacts for actual use in editor and runtime.

## Architecture Constraints

- UPM packages are for code and lightweight assets, not large ONNX payloads.
- Model dependency manifests belong to packages, not to individual Unity projects.
- Shared Hub logic should live in `Runtime` whenever it does not require `UnityEditor` APIs, because package must eventually support downloading files from HuggingFace at runtime.
- Generic download infrastructure should not stay coupled to HuggingFace or model-import concerns. Treat resumable download/job persistence as separate package-owned concern.
- The download path is the bread-and-butter workflow of this package and must be treated as core infrastructure, not incidental tooling.
- Downloads must run in the background using Unity editor task/progress surfaces instead of blocking the editor thread.
- Downloads must be resumable so large model payloads do not restart from zero after interruption.
- Active sync/download work must survive domain reloads and continue from persisted local state.
- Direct references to large ONNX `ModelAsset` files are undesirable because Sentis import and Unity domain reload behavior make them expensive in editor workflows.
- `.sentis` is preferred for consumption because it avoids repeated ONNX import work and supports flatbuffer-based loading patterns.
- `.sentis` output may be tied to the installed Sentis version, so generated artifacts should be treated as version-sensitive.
- A future cache for converted artifacts should likely be keyed by both model identity and Sentis version.

## Reference Implementation

- Use `huggingface_hub` as the main behavioral reference for Hub API interactions, download semantics, cache layout ideas, and manifest shape.
- The local reference checkout lives at `references/huggingface_hub` and is git-ignored.
- Reuse the existing local clone before downloading it again in future sessions.
- Inspect `src/huggingface_hub/file_download.py`, `src/huggingface_hub/_snapshot_download.py`, `src/huggingface_hub/hf_api.py`, and `src/huggingface_hub/constants.py` first for cache and download behavior.

## Working Preference

- Prefer implementing package-owned functionality under `com.doji.hf-hub/`.
- Use `projects/HF Hub/Assets` for integration examples, tooling validation, and test scenes.
- Use embedded test package `projects/HF Hub/Packages/com.doji.hf-hub-test-models` for local manifest discovery and sync validation.
- Keep internal architecture notes under `.codex/`, with the current vertical-slice spec in `.codex/architecture-v1.md`.
- Keep maintainer-only external reference checkouts under the top-level `references/` folder.
- Unless the user says otherwise, treat the package as greenfield and optimize for the current architecture rather than preserving compatibility with earlier drafts or experiments.
- Every finished implementation phase must add or update tests that cover that phase before it is considered complete.
- When a finished change affects package users or package authors, update user-facing docs in `docs/` during same phase.

## Current Phase Order

- Phase 1: manifest schema, discovery, normalized dependency model
- Phase 2: raw Hub client, shared cache, file metadata
- Phase 3: first project import layout and `.sentis` conversion
- Phase 4: stale detection, prompt/menu/project-settings UX
- Current phase: extract generic download package
- After that: runtime locator contract, then companion-project sample flow

## Implemented So Far

- Phase 1 is implemented in `com.doji.hf-hub/`.
- Runtime now defines `ModelHub`, `HfHubPackageManifest`, `HfHubModelDependencyDefinition`, manifest parsing, and shared manifest discovery/reporting types.
- Editor now limits itself to installed-package enumeration plus a basic `Tools/Doji/HF Hub/Show Status` menu over runtime discovery results.
- The package root includes `hf-hub-manifest.template.json` as a copyable starting point for package authors.
- Phase 2 is implemented in editor assembly.
- Editor now includes first raw-cache infrastructure: `HfHubCachePaths`, `HfHubHttpClient`, `HfHubRawCacheMetadata`, `HfHubRawCacheService`, and `HfHubFileLock`.
- Raw cache currently uses per-user `raw/<serialized-repo>/refs/<revision>` plus `raw/<serialized-repo>/snapshots/<commit>/<sourceFile>` layout with sidecar `.hfhub.json` metadata.
- Phase 3 is started and first conversion slice is implemented.
- Editor now includes `HfHubProjectPaths`, `HfHubImportedModelMetadata`, and `HfHubProjectImportService`.
- Generated project-owned artifacts currently live under `Assets/Doji/HfHub/Generated/<package-id>/<model-id>/`.
- Conversion currently works by copying cached ONNX into temporary project asset path, letting Unity Inference import it, then serializing `.sentis` with `ModelWriter.Save(...)`.
- Generated artifacts currently include `<source-file-name>.sentis` plus `hf-hub-import.json` metadata.
- Temporary ONNX import assets currently use `Assets/Doji/HfHub/TempImport/...` and are deleted after conversion.
- Editor now includes first sync orchestration via `HfHubSyncService` and `Tools/Doji/HF Hub/Sync Models`, which now downloads raw files and generates project-owned `.sentis` artifacts.
- Phase 4 is started and first local-status/editor-UX slice is implemented.
- Editor now includes `HfHubStatusService`, `HfHubStatusReport`, and per-dependency state classification as `Ready`, `Missing`, `Stale`, or `Failed`.
- Stale detection currently compares raw cache metadata, revision refs, generated import metadata, manifest hash, and current Sentis version without needing network access.
- Editor now exposes that status through `Tools/Doji/HF Hub/Show Status`, a Project Settings page at `Doji -> AI -> AI Model Hub`, and an automatic once-per-session sync prompt.
- Download/sync no longer aims to run as a blocking modal operation.
- Editor now includes `HfHubBackgroundSyncManager`, which starts sync through Unity's Progress API, persists pending dependency keys to `Library/Doji/HfHub/sync-state.json`, and resumes after domain reload.
- Raw file download transport now supports partial `.downloading` files plus HTTP `Range` resume attempts.
- Repository now contains local package `com.doji.downloads/`, and extraction is actively in progress there.
- `com.doji.downloads` now owns shared `DownloadProgress`, `FileLock`, `UnityWebRequestResumableDownloader`, `PersistentItemQueueState`, `JsonStateStore`, `EditorPersistentOperationContext`, `EditorPersistentOperationRunner`, and `EditorPersistentItemQueueExecutor`.
- HF Hub background sync now uses downloader-owned persistence/progress/recovery runner primitives plus generic sequential item-queue execution, and mainly keeps dependency-key selection plus HF Hub-specific success/failure handling.
- HF Hub still owns HuggingFace-specific resolution, cache policy, dependency queue semantics, and model import orchestration.
- This download architecture is now an explicit project requirement:
  - background execution via Unity Progress API
  - resumable raw downloads via partial files + `Range`
  - persisted sync state so domain reload resumes work instead of losing it
- Editor tests now cover raw-cache paths, sidecar metadata reads, and file-lock exclusivity.
- Editor tests now also cover sync success and sync failure aggregation.
- Editor tests now cover project import path sanitization and real `.onnx` -> `.sentis` conversion using local Unity Inference sample ONNX files.
- Editor tests now cover local status classification for missing, ready, stale, and failed cases plus auto-prompt decision logic.
- Editor tests now include explicit live-network integration coverage against `hf-internal-testing/huggingface-hub-dummy-repository` and `onnx-community/tiny-clip-test-ONNX`.

## Current State Summary

- Manifest discovery, raw cache, ONNX -> `.sentis` import, stale detection, status UI, prompt flow, and background sync all exist in `com.doji.hf-hub`.
- Main architectural debt now: HF Hub still owns the actual per-dependency work body (`download raw -> import -> aggregate result`), and HuggingFace HTTP resolution still wraps downloader transport rather than consuming a more generic download job abstraction.

## Immediate Next Step

1. Start runtime locator contract for generated `.sentis` assets.
   Remove package/runtime dependence on hard-coded `Assets/Doji/HfHub/Generated/...` paths.
2. Decide locator shape before implementation.
   Prefer generated per-package registry or locator asset/API over raw path constants.
3. Update HF Hub runtime/editor code to emit and consume that locator contract.
   `ModelHub.GeneratedAssetsRoot` and direct `Assets/...` assumptions are next cleanup targets.
4. After locator contract is stable, build first companion-project sample flow around it.
   Prove package discovery, sync, generated model lookup, and runtime load end to end.

## Planned Package Split

- New package should be independent from `Doji.AI.ModelHub` / HuggingFace concerns.
- Recommended working name for now: `com.doji.downloads`.
- Keep it in same repository as second local package first; avoid separate repo until package boundary proves stable.

Downloader package should own:

- persisted download-job state
- resumable URL-to-file transfer
- partial file conventions such as `.downloading` or `.part`
- file locks / duplicate-work prevention
- transport abstraction
- retry / cancel / progress contracts
- runtime-safe core API
- optional Unity editor adapter for `Progress` integration

HF Hub package should keep owning:

- package `hf-hub-manifest.json` discovery
- HuggingFace repo/revision/file resolution
- HuggingFace-specific cache policy and metadata
- user-facing model dependency UX
- raw-model-to-`.sentis` import pipeline
- generated asset metadata and stale detection
- package/runtime locator contract for generated models

Initial migration slices:

1. Extract transport/result/progress contracts.
2. Extract partial-file and lock helpers.
3. Extract persisted job-state model and runner.
4. Rebuild HF Hub background sync as adapter over downloader package.
5. Add downloader-package tests for resume/recovery independent of HuggingFace.

Current repository status for this split:

- `com.doji.downloads/` now contains runtime/editor implementation for shared progress, file locking, resumable UnityWebRequest downloads, persisted queue-state storage, generic editor recovery/progress runner primitives, and generic sequential item-queue execution.
- `projects/HF Hub/Packages/manifest.json` and `Packages/packages-lock.json` now reference `com.doji.downloads`.
- Generated Unity `.csproj` files have not been refreshed yet, so CLI `dotnet build` still does not see downloader assemblies until Unity reloads packages/scripts.

## Important Near-Term Gap

- Current manifest and sync flow support only one `sourceFile` per model entry.
- Important future support: package dependencies that need multiple files from one HuggingFace repo, including ONNX plus config/tokenizer/preprocess companion files.
- Reference direction from `huggingface_hub`: use snapshot-style repo download with optional filtering (`allow_patterns` / `ignore_patterns`) rather than only single-file download.

## V1 Scope Exclusions

- HuggingFace authentication is out of scope for v1.
- Gated model support is out of scope for v1.
- Offline-first re[AGENTS.md](../AGENTS.md)use beyond the local raw model cache is out of scope for v1.

## Backlog

- Add optional HuggingFace authentication support.
- Add gated model workflows.
- Define a stronger offline story for disconnected reuse and cache portability.
- Future UI direction: browse HuggingFace models from that Project Settings surface.
- Soften first-time model dependency notification shown after package import.
- Replace scary/technical info dialog copy with calmer user-facing language and simple acknowledge flow when package brings required models.
- Low-priority future UX: when first importing a model, surface license information when available so users can see the model's license before using it.

## Open Questions

- Where should the shared cache live on each platform, and how should cleanup or eviction work?
- What is the best package-manifest discovery hook in the Editor for prompting after package import or update?
- What exact trigger and copy should import-time model notification use so it informs users without sounding alarming or too technical?
- What is the best runtime-facing contract for packages to locate their generated `.sentis` assets without hard-coded project paths?
- Which editor UX is primary beyond the initial prompt: project settings, window, inspector workflow, or a mix?
- Should downloader package include its own editor adapter for `Progress`, or should `com.doji.hf-hub` keep all editor UI orchestration and use downloader package as pure core/runtime library?
