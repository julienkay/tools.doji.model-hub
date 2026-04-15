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

## Current Implementation Plan

1. Implement the Hub and cache domain models needed beyond manifest discovery.
   Keep transport, cache, manifest, and download-state models runtime-safe by default. Add editor-only models only for Package Manager integration, prompting, and editor asset import concerns.
2. Implement the shared raw cache layout.
   Start with a simplified per-user cache using `raw/<serialized-repo>/refs/<revision>` and `raw/<serialized-repo>/snapshots/<commit>/<sourceFile>`.
   Skip blob-level deduplication in v1 unless it becomes necessary.
3. Implement the Hub HTTP layer.
   Support anonymous public model access, revision-to-commit resolution, single-file download, and remote metadata needed for staleness checks.
4. Implement raw download metadata and locking.
   Store sidecar metadata per cached file and protect downloads with file or snapshot-level locks.
5. Implement project import layout.
   Generate assets under `Assets/Doji/HfHub/Generated/<package-id>/<model-id>/`.
6. Implement the Sentis conversion pipeline.
   Convert cached ONNX files into project-owned `.sentis` assets and record package/model/revision/commit/Sentis/Unity metadata.
7. Implement stale detection and sync orchestration.
   Classify dependencies as missing, stale, ready, or failed and drive download plus conversion.
8. Implement the first editor UX beyond status inspection.
   Add automatic prompting on package/project changes plus `Tools/Doji/HF Hub/Sync Models`.
9. Implement the runtime consumption contract.
   Prefer a generated per-package registry or locator asset/API over hard-coded `Assets/...` paths.
10. Add a companion-project integration sample.
    Prove manifest discovery, prompt, download, conversion, and runtime `.sentis` loading end to end.
11. Add verification and failure-path tests.
    Prioritize revision cache behavior, Sentis-version invalidation, metadata recovery, and duplicate-sync protection.

## Current Phase Order

- Phase 1: manifest schema, discovery, normalized dependency model
- Next phase: raw Hub client, shared cache, file metadata
- Later phase: Sentis conversion, import metadata
- Later phase: sync orchestration, prompt/menu UX
- Later phase: runtime locator, sample package flow

## Implemented So Far

- Phase 1 is implemented in `com.doji.hf-hub/`.
- Runtime now defines `ModelHub`, `HfHubPackageManifest`, `HfHubModelDependencyDefinition`, manifest parsing, and shared manifest discovery/reporting types.
- Editor now limits itself to installed-package enumeration plus a basic `Tools/Doji/HF Hub/Show Status` menu over runtime discovery results.
- The package root includes `hf-hub-manifest.template.json` as a copyable starting point for package authors.

## Immediate Next Step

- Start Phase 2.
- Build runtime-first Hub HTTP and cache layers, with editor wrappers only where Unity editor APIs are required.
- Keep the cache simple: revision refs, resolved commit snapshots, sidecar metadata, and file-level locking.

## V1 Scope Exclusions

- HuggingFace authentication is out of scope for v1.
- Gated model support is out of scope for v1.
- Offline-first reuse beyond the local raw model cache is out of scope for v1.

## Backlog

- Add optional HuggingFace authentication support.
- Add gated model workflows.
- Define a stronger offline story for disconnected reuse and cache portability.

## Open Questions

- Where should the shared cache live on each platform, and how should cleanup or eviction work?
- What is the best package-manifest discovery hook in the Editor for prompting after package import or update?
- What is the best runtime-facing contract for packages to locate their generated `.sentis` assets without hard-coded project paths?
- Which editor UX is primary beyond the initial prompt: project settings, window, inspector workflow, or a mix?
