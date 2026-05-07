# Per-File Conversion Recipes

## Abstract

Clean mental model:

- repo cache answers what bits
- conversion recipe answers how this project bakes those bits

HF Hub should keep those identities separate. Raw cache stays keyed by upstream repo snapshot. Generated `.sentis` output becomes keyed by repo file plus effective recipe. That lets Model Hub expose per-file conversion control without breaking package-management guarantees, raw-cache reuse, or runtime lookup simplicity.

## Intent

HF Hub currently treats repository download and `.onnx` to `.sentis` conversion as one coarse repo-level step:

- one manifest entry declares one `repo_id`
- one raw cache snapshot stores source files for one resolved commit
- one import pass converts every `.onnx` file in that snapshot with fixed converter behavior

That is enough for first import, but not enough for real model-package workflows.

Model Hub needs file-level conversion control so one Unity project can choose how each ONNX file in one repo becomes runtime `.sentis` output, while still preserving package-manager-style ownership and validation.

Primary examples:

- quantization mode selection
- static dimension overrides for dynamic input shapes
- future converter options that materially change generated `.sentis`

## Problem

Current import identity is too coarse for configurable conversion.

Today shared import reuse is keyed by:

- `repository`
- `resolvedCommitHash`
- `SentisVersion`

See:

- [ProjectPaths.GetSharedImportFolder](../../../com.doji.hf-hub/Editor/Import/ProjectPaths.cs)
- [ProjectImportService.ImportAsync](../../../com.doji.hf-hub/Editor/Import/ProjectImportService.cs)
- [StatusService.IsSharedCurrent](../../../com.doji.hf-hub/Editor/Status/StatusService.cs)

That means:

- changing conversion settings for one ONNX file has no explicit identity in metadata
- stale detection cannot tell whether generated `.sentis` matches current recipe
- runtime metadata cannot explain which recipe produced current output
- future converted-cache reuse would be unsafe because two different recipes could claim same shared import key

Current Model Hub UI is also too late in pipeline:

- [ModelHubWindow](../../../com.doji.hf-hub/Editor/UI/ModelHubWindow.cs) `Files` tab shows generated runtime files only
- raw ONNX inventory and conversion controls are not first-class
- background sync still couples raw download and runtime materialization in one operation

Approved direction:

- optional package-authored files are selected before download
- that pre-download selection must not depend on `Files` tab visibility
- `Files` tab remains detailed source-plus-output inventory plus recipe editing once selection exists

## Core Rule

Split two concerns cleanly:

- source dependency answers `what repo snapshot this project consumes`
- conversion recipe answers `how one ONNX file in that snapshot becomes project runtime artifact`

HF Hub should keep raw cache identity separate from conversion identity.

## Identity Layers

### Raw Cache Identity

Raw cache remains keyed by upstream snapshot identity only:

- `repository`
- `resolvedCommitHash`

Recipe changes must not invalidate or duplicate raw cache entries.

### File Conversion Identity

Each generated `.sentis` file should have its own effective conversion identity:

- `repository`
- `resolvedCommitHash`
- `repoPath`
- `SentisVersion`
- `ConversionPipelineVersion`
- `RecipeHash`

`repoPath` is required because one repository can contain multiple ONNX variants.

`ConversionPipelineVersion` is required because a future bridge or converter implementation change may alter output even when recipe data is identical.

`RecipeHash` is required because user-visible settings such as quantization or static shape overrides materially affect generated runtime content.

## Scope Rules

### First Slice

First configurable slice should support:

- per-file recipe at `repo + repoPath`
- one effective recipe per file in one Unity project
- package-authored optional files inside one repo dependency
- project-owned enable or disable of those files before download
- project-level overrides from Model Hub UI
- future package-authored defaults, but not required in same phase

First slice should no longer keep current import behavior that every discovered `.onnx` file in snapshot is part of import set.

Instead:

- package manifest declares optional files
- project chooses enabled optional files
- repo materialization uses union of enabled ONNX files across owners
- per-file recipe editing applies only to files in effective selection

That means first slice still does not yet add:

- multiple `.sentis` variants for same `repoPath`
- recipe-specific runtime public API
- non-concrete pattern semantics in manifest truth

### Why One Effective Recipe Per File

Runtime API currently resolves files by:

- `repoId`
- repo-relative `repoPath`

See:

- [ModelHub](../../../com.doji.hf-hub/Runtime/Core/ModelHub.cs)
- [PretrainedRepository](../../../com.doji.hf-hub/Runtime/Core/PretrainedRepository.cs)
- [ResolvedRuntimeModel](../../../com.doji.hf-hub/Runtime/Locator/ResolvedRuntimeModel.cs)

Keeping one effective recipe per `repo + repoPath` preserves that runtime contract and avoids adding recipe identifiers to public runtime lookup before they are clearly needed.

## Recipe Sources

HF Hub should support layered recipe sources.

### Package Default

Future manifest schema should allow package authors to declare default conversion intent for a file or artifact group.

Package-authored optional bundle selection belongs in current additive `version: 1` extension.

Richer conversion defaults can still grow later.

Package defaults exist so package authors can express known-good import expectations when a repo contains multiple ONNX variants or requires specific static dimensions.

### Project Override

Model Hub `Files` tab should allow project maintainers to override package default recipe for one file.

This override belongs to project state, not package state and not machine-local cache state.

Why:

- package manifest may be immutable because it comes from installed UPM package
- project-specific deployment constraints may differ from package defaults
- override must travel with project source if team wants deterministic results

### Effective Recipe

Effective recipe for one file follows precedence:

1. project override
2. package default
3. built-in converter default

HF Hub should surface recipe source in UI so users can see whether current settings come from package, project, or fallback behavior.

## Storage Model

### Raw Cache Metadata

Raw cache metadata should stay recipe-agnostic.

It should continue to describe:

- repository
- resolved commit
- cached files
- download provenance

Raw cache metadata must not absorb project conversion state.

### Shared Import Metadata

Current shared import metadata is repo-wide. Configurable conversion requires file-granular metadata inside that shared record or a file-level shared record structure.

Minimum file metadata should include:

- `RepoPath`
- source ONNX identity hash or signature
- `RecipeHash`
- `ConversionPipelineVersion`
- `InputSignatureHash`
- generated runtime asset path
- generated runtime relative path
- runtime file size
- diagnostics specific to that file
- imported timestamp

Repo-level shared metadata can still exist as container, but freshness checks must reason per file instead of only repo-wide.

### Owner Binding Metadata

Owner binding metadata should continue to stay owner-specific.

It should additionally record:

- effective recipe fingerprint for each file the owner expects
- pointer to shared import metadata that satisfies those fingerprints

That lets multiple owners reuse one generated repo artifact only when they agree on effective recipe for same files.

## Project Override Asset

Project overrides should live in project-owned versioned data, not in `Library/` and not in machine cache.

Target shape:

- one project asset or JSON document under versioned project content
- keyed by `repository + repoPath`
- stores recipe fields plus provenance and update timestamp

Recommended location direction:

- dedicated HF Hub project asset under `Assets/Doji/HfHub/`
- or extend existing project manifest asset system with explicit override asset

Keep it separate from package manifest parsing so package-owned declaration and project-owned override remain distinct.

## Converter Contract

Current converter seam is too small:

- [IOnnxModelConverter.ConvertFromFile(string)](../../../com.doji.hf-hub/Runtime/Conversion/IOnnxModelConverter.cs)

Configurable conversion needs explicit request and result types.

Target direction:

- converter request carries source file path plus recipe
- converter result carries produced `Model` plus normalized diagnostics and optional discovered input signature

Minimum request fields:

- source ONNX file path
- recipe settings
- optional cached ONNX inspection data

Minimum result fields:

- converted `Model`
- normalized recipe actually applied
- input signature summary
- warning diagnostics

This keeps editor bridge and future runtime converter behind same contract.

## ONNX Inspection

Model Hub file configuration needs lightweight ONNX inspection before conversion.

HF Hub should extract and cache file-level inspection data such as:

- input names
- rank
- static versus dynamic dimensions
- dtype where available

Inspection data supports:

- file tab display
- validation of static-dimension overrides before conversion
- recipe hash normalization
- stale detection when source model signature changes across repo updates

Inspection should be derived from raw cache file, not from imported Unity asset.

## Files Tab Role

Model Hub `Files` tab should become source-plus-output inventory, not output-only inventory.

For each ONNX file row, UI should show:

- repo path
- source file presence in raw cache
- discovered input signature summary
- effective recipe
- recipe source
- last conversion result
- generated `.sentis` path if present
- file-specific diagnostics

That tab should be editable when project override is allowed.

Non-ONNX files can remain visible later if manifest v2 adds artifact grouping, but first slice only needs actionable ONNX rows.

Selection should not depend on this tab being visible. Repos with optional variants must stay configurable before raw cache exists.

## Download Versus Materialize

Current background sync still does:

1. download raw snapshot
2. convert snapshot into `.sentis`

in one pass.

See [BackgroundSyncManager.RunAsync](../../../com.doji.hf-hub/Editor/Sync/BackgroundSyncManager.cs).

Per-file recipes make this split architecturally important:

- download fills raw source cache
- materialize applies current project recipe set to raw source files

Benefits:

- File tab can inspect raw ONNX before project commits to recipe
- recipe edits can trigger re-materialization without redownloading
- offline reconfiguration becomes possible when raw cache already exists

HF Hub can still offer one-click `Import` for convenience, but backend should treat download and materialize as separate operations.

## Validation

### Recipe Validation

HF Hub should validate recipe data before conversion:

- static dimensions only on known dynamic axes
- no conflicting overrides for same input axis
- quantization mode supported by current converter pipeline

Invalid recipe should produce file-level failure without corrupting raw cache state.

### Freshness Validation

One generated file is stale when any of these change:

- resolved commit for repository
- source ONNX inspection signature
- effective recipe hash
- converter pipeline version
- Sentis version
- generated runtime file missing

Repo item becomes action-required when any required file is stale or missing.

### Conflict Policy

Current strict revision conflict rule remains unchanged.

Additional future conflict rule:

- if multiple owners declare different package-default recipes for same `repo + repoPath`, HF Hub must not silently choose one

Preferred resolution:

- project override becomes explicit winner
- until then, row is conflict or failed-validation state

This mirrors revision-conflict philosophy: package management should not silently collapse incompatible declarations.

## Runtime Contract

Public runtime API should stay unchanged for first slice.

Runtime lookup still resolves:

- `repoId`
- repo-relative `repoPath`

to one `.sentis` file.

That remains safe as long as one Unity project publishes only one effective recipe for one `repo + repoPath`.

Runtime locator metadata may later expose recipe fingerprints for diagnostics, but runtime path lookup should not require them in first slice.

## Cache Direction

### Raw Cache

Raw cache remains shared across projects and recipe-agnostic.

### Converted Cache

Optional future optimization:

- machine-local converted cache keyed by file conversion identity

This should come after recipe-aware metadata and stale detection are stable.

Why later:

- first solve correctness and identity
- then add reuse of converted outputs across projects

Converted cache key should match file conversion identity exactly so copying into project runtime layout is deterministic and safe.

## Rollout

### Phase 1: Recipe Plumbing

- add file recipe types and hashing
- add ONNX inspection model
- replace converter request contract
- persist recipe-aware metadata
- add import and status tests for recipe stale detection

### Phase 2: File Tab Editing

- show raw ONNX file inventory in Model Hub
- add project override editing
- add explicit re-materialize action for selected repo or file
- split backend download from materialize

### Phase 3: Manifest v2 Defaults

- add package-authored file or artifact conversion defaults
- add owner-default conflict detection
- add docs and package-author guidance

### Phase 4: Converted Cache

- add optional machine-local converted artifact cache
- reuse converted outputs across projects when recipe identity matches

## Non-Goals For First Slice

- multiple runtime variants for same `repoPath`
- recipe-specific runtime public API
- turning project overrides into hidden machine-local preferences
- treating optional-bundle selection as machine-local cache preference instead of project-owned state

## Open Questions

- should project overrides live in one dedicated asset or inside expanded project manifest asset schema
- should conflict on package-default recipe mismatch be hard error immediately, or warning until manifest v2 ships
- how much ONNX inspection data can be extracted cheaply without triggering heavy import cost
