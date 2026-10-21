# Testing

## Purpose

This page maps durable behavior contracts to package-owned tests so maintainers can extend coverage without re-discovering fixture layout.

Use it as navigation aid, not as replacement for reading test code.

## Coverage Map

Manifest and discovery:

- `tools.doji.hf-hub/Tests/Editor/Discovery/PackageDiscoveryTests.cs`
  - manifest parse and normalization
  - duplicate id validation
  - selected-file and optional-file preservation
  - owner-scoped optional file override application
  - project settings manifest serialization inputs

Cache and repository inventory:

- `tools.doji.hf-hub/Tests/Editor/Cache/HttpClientTests.cs`
  - repository tree inventory
  - selected-file resolution against remote inventory
- `tools.doji.hf-hub/Tests/Editor/Cache/RawCacheTests.cs`
  - raw metadata persistence
  - repository inventory persistence
- `tools.doji.hf-hub/Tests/Editor/Cache/RepositoryValidationCacheServiceTests.cs`
  - cached repo and revision validation state

Import and generated assets:

- `tools.doji.hf-hub/Tests/Editor/Import/ProjectImportServiceTests.cs`
  - whole-repo import from repository inventory
  - selected-file import subset
  - optional-file selection asset persistence
  - generated `.sentis` prune on selection shrink
  - shared import reuse across owners
  - partial conversion warnings
  - no-ONNX selection failure
- `tools.doji.hf-hub/Tests/Editor/Import/ProjectRemovalServiceTests.cs`
  - raw cache removal
  - shared import cleanup
  - revision-conflict removal blocking
- `tools.doji.hf-hub/Tests/Editor/Import/OnnxModelConverterArchitectureTests.cs`
  - conversion pipeline assumptions

Status and sync:

- `tools.doji.hf-hub/Tests/Editor/Status/StatusServiceTests.cs`
  - raw cache missing and invalid cases
  - selected-file missing file detection
  - `SelectionRequired` state for optional-only ONNX dependencies
  - whole-repo no-ONNX failure
  - same-repo later-added broader selection marks only new owner as cache-update-needed
  - binding/shared stale detection
  - imported-only ready fallback after raw cache removal
- `tools.doji.hf-hub/Tests/Editor/Status/ModelHubServiceTests.cs`
  - status aggregation into Model Hub rows
  - selection-required diagnostics
  - same-repo multi-owner selection growth aggregates into one update-needed row
- `tools.doji.hf-hub/Tests/Editor/Sync/SyncServiceTests.cs`
  - sync success and failure orchestration
- `tools.doji.hf-hub/Tests/Editor/Sync/BackgroundSyncManagerTests.cs`
  - background gating and damaged-cache codes
  - status reads snapshots from disk; a missing or foreign index is ignored
- `tools.doji.hf-hub/Tests/Editor/Import/ProjectRemovalServiceTests.cs`
  - Remove from Cache deletes only blobs no remaining snapshot links to, and leaves partial downloads alone (needs symlink support)
  - case-only repository id differences are not treated as corrupt cache
- `tools.doji.hf-hub/Tests/Editor/Sync/IntegrationTests.cs`
  - live-network maintainer coverage

Runtime locator:

- `tools.doji.hf-hub/Tests/Editor/RuntimeLocator/RuntimeLocatorServiceTests.cs`
  - registry writes
  - owner-specific and global lookup
  - `FromPretrained(...)` and `LoadRuntimeModel(...)`
  - selected-file and optional-file runtime subset exposure
- `tools.doji.hf-hub/Tests/Editor/RuntimeLocator/RuntimeLocatorPathTests.cs`
  - runtime path mapping and layout assumptions

Editor UI and manifest editing:

- `tools.doji.hf-hub/Tests/Editor/UI/ManifestModelEntryTests.cs`
  - file-selection button gating and incomplete-row detection
  - model id parsing from `owner/name` and Hugging Face URLs
- `tools.doji.hf-hub/Tests/Editor/Status/StatusServiceTests.cs` (partial conversion)
  - a recorded skipped ONNX file keeps the dependency ready
  - a selected ONNX file with neither output nor skip record stays stale
- `tools.doji.hf-hub/Tests/Editor/UI/ModelStatusTests.cs`
  - status kind, tone, and primary action for every Model Hub state
- `tools.doji.hf-hub/Tests/Editor/Cache/RemoteSnapshotSizeEstimateServiceTests.cs`
  - pre-download inventory for manifests without a revision
  - refresh of estimates saved without a file list
- `tools.doji.hf-hub/Tests/Editor/Cache/BlobLockTests.cs`
  - blob lock exclusion, waiting, and cancellation; waits run on the thread pool because the main thread is blocked during a test
- `tools.doji.hf-hub/Tests/Editor/Discovery/ProjectOptionalFileSelectionsAssetTests.cs`
  - optional-file selections asset resolves its script, so choices survive asset refresh
- `tools.doji.hf-hub/Tests/Editor/UI/ModelHubFileEditingTests.cs`
  - inspected static ONNX input shapes become read-only Files tab display rows
- `tools.doji.hf-hub/Tests/Editor/UI/ModelHubFileIconTests.cs`
  - Files tab header icon classification by repository file extension
- `tools.doji.hf-hub/Tests/Editor/UI/ModelManifestInspectorUtilityTests.cs`
  - manifest dirty tracking
  - explicit `files` serialization with per-file `optional` flags
  - ScriptableObject manifest document load/save normalization
- `tools.doji.hf-hub/Tests/Editor/UI/ProjectSettingsTests.cs`
  - project settings manifest workflow
- `tools.doji.hf-hub/Tests/Editor/UI/EditorOperationsTests.cs`
  - UI command gating
- `tools.doji.hf-hub/Tests/Editor/UI/AutoPromptTests.cs`
  - prompt grouping by repository, headline, and primary action
  - download detection for missing cache files and total download size label

## Selected-File vs Whole-Repo Matrix

Current minimum regression set for dependency-scope behavior:

- discovery preserves explicit `files`
- manifest serialization omits `files` for whole-repo entries
- import converts all ONNX files for whole-repo entries when repository inventory is populated
- import converts only selected ONNX files for selected-file entries
- import fails clearly when dependency scope contains no ONNX file
- status detects missing selected files
- status fails clearly when whole-repo inventory contains no ONNX file
- when later owner broadens same-repo selection, existing owner can stay ready while new owner reports cache update needed
- Model Hub aggregates that state into one repo row with unioned selected files and imported subset still visible
- runtime locator exposes only imported ONNX subset for selected-file entries

## Current Sharp Edge

Whole-repo import and status logic derive ONNX scope from repository inventory first, not from raw cached file blobs alone.

Relevant code paths:

- `Runtime/Manifest/ManifestDiscovery.cs` via `GetSelectedRepoPaths(...)`
- `Editor/Import/ProjectImportService.cs`
- `Editor/Status/StatusService.cs`

When changing raw metadata shape or downloader behavior, preserve tests that prove repository inventory stays populated for whole-repo dependencies.
