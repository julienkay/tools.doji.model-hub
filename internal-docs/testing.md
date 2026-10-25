# Testing

## Purpose

This page maps durable behavior contracts to package-owned tests so maintainers can extend coverage without re-discovering fixture layout.

Use it as navigation aid, not as replacement for reading test code.

## Fixtures That Touch Shared State

Tests run inside the companion project, where a maintainer may have real models imported, and on a machine with a real Hugging Face cache. Fixtures must never wipe either.

- `TestCacheScope` points `HF_HUB_CACHE` and `DOJI_HF_HUB_DATA_ROOT` at a temporary folder, so tests never read or write the real cache, the index, or the converted store.
- `TestProjectStateScope` records the files under `Assets/StreamingAssets/hf-hub` (pulled copies) and `Library/Doji/HfHub/imports` (or other roots you pass) at `SetUp`, and at `TearDown` deletes only the files and folders the test added. Use fake repository ids such as `doji/test-model`, so test output never overwrites a real import.
- Pass project files that would change a test's result, such as the conversion overrides and optional-file selections assets, as isolated files. The scope moves them aside for the test and restores them, `.meta` and GUID included, afterwards.

Create both in `SetUp` and dispose them in `TearDown`. Do not delete those folders wholesale. The companion project's sandbox tests reference `Doji.ModelHub.Editor.Tests` to reuse the same scope.

Runtime tests never touch the network or StreamingAssets of the project:

- `RuntimeModels.RegistryOverride` replaces the catalog.
- `RuntimeDownloader.DownloadOverride` replaces the network: it gets the URL and writes the target file.
- `Hub.RegisterOnnxConverter` installs a fake converter. Reset it in `TearDown`.
- Test models are tiny `FunctionalGraph` models written with `ModelWriter`, so conversion and quantization run in milliseconds.

The build is covered end to end by the companion project's smoke test, `Assets/HF Hub/Build Smoke Test/Editor/BuildSmokeTestRunner.cs`. `BuildAndRun` builds a Windows player with an empty player cache, runs it, and checks that bundled and downloaded models load. `BuildPlayer("android" | "web", entries)` builds the same scene for Android (IL2CPP, ARM64) or an uncompressed Web player:

- Android: uninstall the app, `adb install` the APK, launch it, and read `adb logcat -s Unity` for `HFHUB-SMOKE` lines.
- Web: serve the `Web` folder from a new local port (a fresh origin has empty IndexedDB) with `Cache-Control: no-store`, and read the browser console. Keep the page visible: a hidden page runs at about one frame per second.

Run each player twice. The second run must load downloaded models without a request to Hugging Face. The smoke test downloads from Hugging Face, so it is not part of the test run. Use small models only.

## Coverage Map

Manifest and discovery:

- `tools.doji.hf-hub/Tests/Editor/Discovery/ModelFilesTests.cs`
  - format by extension, repository format labels
  - external weight files found next to an `.onnx` file and added to the selection
  - mixed whole-repository dependencies and selections without model files are rejected
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
  - blob etags come from the tree listing (`lfs.oid`, else `oid`), not the CDN `ETag`
  - downloads take the Hub's spelling of the repository id
- `tools.doji.hf-hub/Tests/Editor/Cache/CachePathsTests.cs`
  - a manifest id that differs in letter case finds the existing folder by name, also on Windows
- `tools.doji.downloads` `ResumableDownloaderTests.DownloadFileAsync_ResumesThroughRedirect`
  - resume state keeps the stable `/resolve`-style URL; Range and If-Range reach the redirect target
- `tools.doji.hf-hub/Tests/Editor/Cache/RawCacheTests.cs`
  - raw metadata persistence
  - repository inventory persistence
- `tools.doji.hf-hub/Tests/Editor/Cache/LegacyCacheMigrationTests.cs`
  - old `raw/` snapshots move into blobs, refs, and the index; the old folder is removed
  - large-file blobs are named by content sha256, not the old xet-hash etag
  - partial, unlisted, and wrong-size files are dropped; existing blobs and newer refs win
  - a repository with a locked blob stays in `raw/` and migrates on retry
- `tools.doji.hf-hub/Tests/Editor/Cache/CacheOverviewServiceTests.cs`
  - whole-cache scan: repository ids and types, refs, sizes, partial downloads, linked blob names
  - Remove from Cache deletes the repository, its locks, and its index, and deletes nothing while a blob is locked
  - deleting a partial download keeps complete files
- `tools.doji.hf-hub/Tests/Editor/Cache/RepositoryValidationCacheServiceTests.cs`
  - cached repo and revision validation state

Import and generated assets:

- `tools.doji.hf-hub/Tests/Editor/Import/ProjectImportServiceTests.cs`
  - ONNX converts into the shared converted store; nothing is written into `Assets`
  - `.sentis` files are used as downloaded
  - conversion settings are applied; converted models are reused across owners and after a broken record
  - a deleted converted model converts again
  - companion files are recorded with etags
  - selection subset, mixed-format and no-model-file failures, partial conversion warnings
- `tools.doji.hf-hub/Tests/Editor/Import/SentisCompatibilityTests.cs`
  - versions map to format generations; updates within one are compatible
  - tripwire: the installed `ModelWriter.version` is covered by the table
- `tools.doji.hf-hub/Tests/Editor/RuntimeLocator/HubStorageTests.cs`
  - converted models listed with records, deleted by predicate, skipped while being written
  - Android copies of other app versions deleted, the running version kept
- `tools.doji.hf-hub/Tests/Editor/Import/OnnxConversionSettingsTests.cs`
  - quantization modes (`int8` maps to `uint8`), fixed input shapes with mismatch warnings
  - quantized weights change data type
  - the conversion key depends on source, converter, and settings
- `tools.doji.hf-hub/Tests/Editor/Import/ProjectRemovalServiceTests.cs`
  - cache removal
  - shared import cleanup
  - revision-conflict removal blocking
- `tools.doji.hf-hub/Tests/Editor/Import/PulledModelsTests.cs`
  - pull copies to the build path; remove deletes them; a missing runtime model is explained
- `tools.doji.hf-hub/Tests/Editor/Import/LegacyProjectOutputCleanupTests.cs`
  - old generated folders and registries are deleted; everything else stays

Status and sync:

- `tools.doji.hf-hub/Tests/Editor/Status/StatusServiceTests.cs`
  - cache missing and invalid cases
  - `.sentis` dependencies are ready without conversion; mixed whole-repository dependencies fail
  - selected-file missing file detection
  - `SelectionRequired` state for optional-only ONNX dependencies
  - whole-repo no-ONNX failure
  - same-repo later-added broader selection marks only new owner as cache-update-needed
  - binding/shared stale detection
  - converted models stay ready after cache removal
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

Runtime loading and builds:

- `tools.doji.hf-hub/Tests/Editor/RuntimeLocator/RuntimeModelsTests.cs`
  - bundled models load from StreamingAssets; the Editor catalog uses local paths
  - download mode fetches a `.sentis` file once, into the cache spelling of the repository id
  - download mode fetches and converts an ONNX file once, keyed by the converter id
  - missing converter and synchronous-before-download errors point to the fix
  - `EnsureAvailableAsync` downloads every file
  - unknown models and package-scoped lookup
  - registry round trip drops Editor paths and refuses newer versions
- `tools.doji.hf-hub/Tests/Editor/RuntimeLocator/RuntimeLocatorPathTests.cs`
  - runtime path mapping matches the build layout
- `tools.doji.hf-hub/Tests/Editor/Build/RuntimeRegistryBuilderTests.cs`
  - bundled models at their lookup path, shared models bundled once, pulled copies skipped
  - download entries carry commit, etags, companions, and recipe
  - models a build cannot deliver are reported
  - delivery per repository, cache spelling of the repository id

Editor UI and manifest editing:

- `tools.doji.hf-hub/Tests/Editor/UI/AddModelPopupTests.cs`
  - mixed and model-less repositories need a file selection; an unknown format does not block adding
- `tools.doji.hf-hub/Tests/Editor/UI/ManifestModelEntryTests.cs`
  - file-selection button gating and incomplete-row detection
  - model id parsing from `owner/name` and Hugging Face URLs
- `tools.doji.hf-hub/Tests/Editor/Status/StatusServiceTests.cs` (partial conversion)
  - a recorded skipped ONNX file keeps the dependency ready
  - a selected ONNX file with neither output nor skip record stays stale
- `tools.doji.hf-hub/Tests/Editor/UI/CacheTilesTests.cs`
  - cache tile kinds (used ignoring letter case, unused, partial-only), file tiles, merged small files, which partial downloads the card deletes
- `tools.doji.hf-hub/Tests/Editor/UI/TreemapLayoutTests.cs`
  - tile areas match weights, stay in bounds, do not overlap, and stay close to square
- `tools.doji.hf-hub/Tests/Editor/UI/ModelStatusTests.cs`
  - status kind, tone, and primary action for every Model Hub state
- `tools.doji.hf-hub/Tests/Editor/Cache/RemoteSnapshotSizeEstimateServiceTests.cs`
  - pre-download inventory for manifests without a revision
  - refresh of estimates saved without a file list
- `tools.doji.hf-hub/Tests/Editor/Cache/CacheBlobsTests.cs`
  - the blob store shared by Editor sync and players: blob plus snapshot link, reuse across revisions, wrong-size blobs downloaded again, unknown size accepts existing blobs, files without an etag, lock wait and cancellation
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
- import converts all ONNX files of a single-format whole-repo entry when repository inventory is populated
- import converts only selected ONNX files for selected-file entries
- import fails clearly when dependency scope contains no model file, or a whole-repo entry has both formats
- status detects missing selected files
- status fails clearly when whole-repo inventory contains no model file
- when later owner broadens same-repo selection, existing owner can stay ready while new owner reports cache update needed
- Model Hub aggregates that state into one repo row with unioned selected files and imported subset still visible
- the runtime registry exposes only the owner's selected files

## Current Sharp Edge

Whole-repo import and status logic derive model file scope from repository inventory first, not from cached file blobs alone.

Relevant code paths:

- `Runtime/Manifest/ManifestDiscovery.cs` via `GetSelectedRepoPaths(...)`
- `Editor/Import/ProjectImportService.cs`
- `Editor/Status/StatusService.cs`

When changing raw metadata shape or downloader behavior, preserve tests that prove repository inventory stays populated for whole-repo dependencies.
