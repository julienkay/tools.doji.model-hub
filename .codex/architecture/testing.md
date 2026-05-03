# Testing

## Purpose

This page maps durable behavior contracts to package-owned tests so maintainers can extend coverage without re-discovering fixture layout.

Use it as navigation aid, not as replacement for reading test code.

## Coverage Map

Manifest and discovery:

- `com.doji.hf-hub/Tests/Editor/Discovery/PackageDiscoveryTests.cs`
  - manifest parse and normalization
  - duplicate id validation
  - selected-file preservation
  - project settings manifest serialization inputs

Cache and repository inventory:

- `com.doji.hf-hub/Tests/Editor/Cache/HttpClientTests.cs`
  - repository tree inventory
  - selected-file resolution against remote inventory
- `com.doji.hf-hub/Tests/Editor/Cache/RawCacheTests.cs`
  - raw metadata persistence
  - repository inventory persistence
- `com.doji.hf-hub/Tests/Editor/Cache/RepositoryValidationCacheServiceTests.cs`
  - cached repo and revision validation state

Import and generated assets:

- `com.doji.hf-hub/Tests/Editor/Import/ProjectImportServiceTests.cs`
  - whole-repo import from repository inventory
  - selected-file import subset
  - shared import reuse across owners
  - partial conversion warnings
  - no-ONNX selection failure
- `com.doji.hf-hub/Tests/Editor/Import/ProjectRemovalServiceTests.cs`
  - raw cache removal
  - shared import cleanup
  - revision-conflict removal blocking
- `com.doji.hf-hub/Tests/Editor/Import/OnnxModelConverterArchitectureTests.cs`
  - conversion pipeline assumptions

Status and sync:

- `com.doji.hf-hub/Tests/Editor/Status/StatusServiceTests.cs`
  - raw cache missing and invalid cases
  - selected-file missing file detection
  - whole-repo no-ONNX failure
  - same-repo later-added broader selection marks only new owner as cache-update-needed
  - binding/shared stale detection
  - imported-only ready fallback after raw cache removal
- `com.doji.hf-hub/Tests/Editor/Status/ModelHubServiceTests.cs`
  - status aggregation into Model Hub rows
  - same-repo multi-owner selection growth aggregates into one update-needed row
- `com.doji.hf-hub/Tests/Editor/Sync/SyncServiceTests.cs`
  - sync success and failure orchestration
- `com.doji.hf-hub/Tests/Editor/Sync/BackgroundSyncManagerTests.cs`
  - background gating and heal paths
- `com.doji.hf-hub/Tests/Editor/Sync/IntegrationTests.cs`
  - live-network maintainer coverage

Runtime locator:

- `com.doji.hf-hub/Tests/Editor/RuntimeLocator/RuntimeLocatorServiceTests.cs`
  - registry writes
  - owner-specific and global lookup
  - `FromPretrained(...)` and `LoadRuntimeModel(...)`
  - selected-file runtime subset exposure
- `com.doji.hf-hub/Tests/Editor/RuntimeLocator/RuntimeLocatorPathTests.cs`
  - runtime path mapping and layout assumptions

Editor UI and manifest editing:

- `com.doji.hf-hub/Tests/Editor/UI/ManifestModelListEditorTests.cs`
  - file-selection button gating
- `com.doji.hf-hub/Tests/Editor/UI/ModelHubWindowStatusRenderingTests.cs`
  - stable error/conflict icon asset selection
  - state-specific download/import/update icon asset selection
  - failed-row badge label and color mapping
- `com.doji.hf-hub/Tests/Editor/UI/ModelManifestInspectorUtilityTests.cs`
  - manifest dirty tracking
  - explicit `files` serialization
  - ScriptableObject manifest document load/save normalization
- `com.doji.hf-hub/Tests/Editor/UI/ProjectSettingsTests.cs`
  - project settings manifest workflow
- `com.doji.hf-hub/Tests/Editor/UI/EditorOperationsTests.cs`
  - UI command gating
- `com.doji.hf-hub/Tests/Editor/UI/AutoPromptTests.cs`
  - prompt summarization

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
