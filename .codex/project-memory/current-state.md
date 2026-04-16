# Current State

## Current Phase Order

- Phase 1: manifest schema, discovery, normalized dependency model
- Phase 2: raw Hub client, shared cache, file metadata
- Phase 3: first project import layout and `.sentis` conversion
- Phase 4: stale detection, prompt/menu/project-settings UX
- Completed recent phase: extract generic download package
- Current phase: runtime locator contract, then companion-project sample flow

## Implemented So Far

- Phase 1 is implemented in `com.doji.hf-hub/`.
- Runtime now defines `ModelHub`, `HfHubPackageManifest`, `HfHubModelDependencyDefinition`, manifest parsing, and shared manifest discovery/reporting types.
- Editor now limits itself to installed-package enumeration plus a basic `Tools/Doji/HF Hub/Show Status` menu over runtime discovery results.
- The package root includes `hf-hub-manifest.template.json` as a copyable starting point for package authors.
- Phase 2 is implemented in editor assembly.
- Editor now includes first raw-cache infrastructure: `HfHubCachePaths`, `HfHubHttpClient`, `HfHubRawCacheMetadata`, `HfHubRawCacheService`, and downloader-backed transfer primitives.
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
- Editor tests now cover raw-cache paths, sidecar metadata reads, and file-lock exclusivity.
- Editor tests now also cover sync success and sync failure aggregation.
- Editor tests now cover project import path sanitization and real `.onnx` -> `.sentis` conversion using local Unity Inference sample ONNX files.
- Editor tests now cover local status classification for missing, ready, stale, and failed cases plus auto-prompt decision logic.
- Editor tests now include explicit live-network integration coverage against `hf-internal-testing/huggingface-hub-dummy-repository` and `onnx-community/tiny-clip-test-ONNX`.

## Current State Summary

- Manifest discovery, raw cache, ONNX -> `.sentis` import, stale detection, status UI, prompt flow, and background sync all exist in `com.doji.hf-hub`.
- The generic downloader extraction is in place and has package-owned tests in `com.doji.downloads`.
- Runtime locator contract work has started with runtime path constants, locator DTOs, and import metadata fields for planned runtime output.
- Internal maintainer docs are now split under `.codex/project-memory/` and `.codex/architecture/`, with `.codex/project-memory.md` and `.codex/architecture-v1.md` kept as thin entry points.
- Package code and tests are now grouped by task area instead of living in flat `Editor`, `Runtime`, and `Tests/Editor` folders.
- Main architectural focus now is finishing the runtime locator contract and then proving it in a companion-project sample flow.

## Immediate Next Step

1. Finish the runtime locator contract for generated `.sentis` assets and companion repo files.
2. Generate internal locator metadata suitable for string-based runtime lookup and future multi-file model pipelines.
3. Update runtime/editor code so packages stop depending on hard-coded `Assets/Doji/HfHub/Generated/...` paths.
4. Build the first companion-project sample around sync, locator lookup, and runtime model load.
