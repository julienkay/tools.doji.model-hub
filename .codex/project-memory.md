# Project Memory

## Mission

`com.doji.hf-hub` should make HuggingFace-hosted ML models usable in Unity without shipping large binary model files inside UPM packages.

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
- Keep internal architecture notes under `.codex/`, with the current vertical-slice spec in `.codex/architecture-v1.md`.
- Keep maintainer-only external reference checkouts under the top-level `references/` folder.
- Unless the user says otherwise, treat the package as greenfield and optimize for the current architecture rather than preserving compatibility with earlier drafts or experiments.
- Every finished implementation phase must add or update tests that cover that phase before it is considered complete.
- When a finished change affects package users or package authors, update user-facing docs in `docs/` during same phase.

## Current Implementation Plan

1. Implement the runtime consumption contract.
   Prefer a generated per-package registry or locator asset/API over hard-coded `Assets/...` paths.
2. Add a companion-project integration sample.
   Prove manifest discovery, prompt, download, conversion, and runtime `.sentis` loading end to end.
3. Add verification and failure-path tests for remaining phases.
   Prioritize Sentis-version invalidation, metadata recovery, duplicate-sync protection, and editor-UX-triggered sync paths.

## Current Phase Order

- Phase 1: manifest schema, discovery, normalized dependency model
- Phase 2: raw Hub client, shared cache, file metadata
- Phase 3: first project import layout and `.sentis` conversion
- Phase 4: stale detection, prompt/menu/project-settings UX
- Next phase: runtime locator, sample package flow

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
- Editor tests now cover raw-cache paths, sidecar metadata reads, and file-lock exclusivity.
- Editor tests now also cover sync success and sync failure aggregation.
- Editor tests now cover project import path sanitization and real `.onnx` -> `.sentis` conversion using local Unity Inference sample ONNX files.
- Editor tests now cover local status classification for missing, ready, stale, and failed cases plus auto-prompt decision logic.
- Editor tests now include explicit live-network integration coverage against `hf-internal-testing/huggingface-hub-dummy-repository` and `onnx-community/tiny-clip-test-ONNX`.

## Immediate Next Step

- Start runtime locator contract.
- Define how package code finds generated `.sentis` assets without hard-coded `Assets/...` paths.
- After that, build the first end-to-end companion-project sample around that runtime contract.

## Important Near-Term Gap

- Current manifest and sync flow support only one `sourceFile` per model entry.
- Important future support: package dependencies that need multiple files from one HuggingFace repo, including ONNX plus config/tokenizer/preprocess companion files.
- Reference direction from `huggingface_hub`: use snapshot-style repo download with optional filtering (`allow_patterns` / `ignore_patterns`) rather than only single-file download.

## V1 Scope Exclusions

- HuggingFace authentication is out of scope for v1.
- Gated model support is out of scope for v1.
- Offline-first reuse beyond the local raw model cache is out of scope for v1.

## Backlog

- Add optional HuggingFace authentication support.
- Add gated model workflows.
- Define a stronger offline story for disconnected reuse and cache portability.
- Future UI direction: browse HuggingFace models from that Project Settings surface.

## Open Questions

- Where should the shared cache live on each platform, and how should cleanup or eviction work?
- What is the best package-manifest discovery hook in the Editor for prompting after package import or update?
- What is the best runtime-facing contract for packages to locate their generated `.sentis` assets without hard-coded project paths?
- Which editor UX is primary beyond the initial prompt: project settings, window, inspector workflow, or a mix?
