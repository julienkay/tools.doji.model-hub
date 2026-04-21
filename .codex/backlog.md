# Backlog

Use this file for work that should stay discoverable across sessions but is not part of the active slice in `.codex/current-work.md`.

This is the home for:

- longer-scoped epics
- lower-priority work
- parked ideas that should not disappear
- open questions that matter beyond the current task

## Open Questions

- What cleanup or eviction policy should the shared cache use beyond the current fixed platform roots?
- Is the current project-open plus `projectChanged` prompt hook sufficient for package import or update, or does HF Hub need a more targeted Package Manager trigger?
- Should `com.doji.downloads` eventually include its own editor adapter for `Progress`, or should `com.doji.hf-hub` keep editor UI orchestration and use downloader as pure core/runtime infrastructure?

## Near-Term Backlog

- Manifest v2: support filtered multi-file repo snapshots, including ONNX plus config, tokenizer, or preprocess companion files, following `huggingface_hub` `snapshot_download(...)` semantics.

## Architecture Risks From 2026-04-22 Review

- `Critical` Multi-revision same repo unsafe. [ProjectPaths.cs:89](../com.doji.hf-hub/Editor/Import/ProjectPaths.cs) ignores `resolvedCommitHash` for runtime folder. [ModelHubService.cs:159](../com.doji.hf-hub/Editor/Status/ModelHubService.cs) only warns on revision ambiguity. [StatusService.cs:402](../com.doji.hf-hub/Editor/Status/StatusService.cs) validates path/existence, not artifact content. [ProjectRemovalService.cs:129](../com.doji.hf-hub/Editor/Import/ProjectRemovalService.cs) can delete runtime folder for revision A while revision B still uses same path. Result: last-writer-wins, false `Ready`, cross-delete. Chosen fix: hard-block multiple requested revisions of same repo in one project instead of adding commit-key runtime folders.
- `High` Project manifest assets not real owners. [ProjectManifestDiscovery.cs:17](../com.doji.hf-hub/Editor/Discovery/ProjectManifestDiscovery.cs) forces all to owner id `project`. [ProjectPaths.cs:49](../com.doji.hf-hub/Editor/Import/ProjectPaths.cs) and [BackgroundSyncManager.cs:219](../com.doji.hf-hub/Editor/Sync/BackgroundSyncManager.cs) then collide bindings, registries, sync keys. Two project manifests with same repo/model will overwrite each other and lose independent stale/removal semantics. Fix: stable owner id per manifest asset GUID or path hash. UI label can still say `Project`.
- `High` Runtime side not actually runtime-safe. [RuntimeLocatorService.cs:99](../com.doji.hf-hub/Runtime/Locator/RuntimeLocatorService.cs) and [RuntimeLocatorService.cs:132](../com.doji.hf-hub/Runtime/Locator/RuntimeLocatorService.cs) use `File`/`Directory` under `Application.streamingAssetsPath`. [PretrainedRepository.cs:59](../com.doji.hf-hub/Runtime/Core/PretrainedRepository.cs) loads by absolute path. Inference from code + Unity platform behavior: likely desktop/editor-safe only; Android/WebGL will be shaky or broken. Fix: runtime provider for registry/file access via `UnityWebRequest` or byte streams, cache registry once in memory.
- `High` Download/import granularity too blunt for real HF repos. [HttpClient.cs:143](../com.doji.hf-hub/Editor/Cache/HttpClient.cs) trusts `siblings`, [RawCacheService.cs:52](../com.doji.hf-hub/Editor/Cache/RawCacheService.cs) downloads every file, [ProjectImportService.cs:88](../com.doji.hf-hub/Editor/Import/ProjectImportService.cs) converts every `.onnx`. Real repos often contain giant unused assets, duplicate variants, docs, training weights. No upstream-style `allow_patterns` or `list_repo_tree` fallback yet. Fix: manifest v2 soon with artifact filters, explicit primary model, companion files, then mirror `snapshot_download` semantics.
- `Medium` Raw snapshot metadata wrongly tracks requested revision. [RawCacheService.cs:63](../com.doji.hf-hub/Editor/Cache/RawCacheService.cs) stores `RequestedRevision` on commit-scoped metadata. [StatusService.cs:133](../com.doji.hf-hub/Editor/Status/StatusService.cs) treats mismatch as stale. Same commit via branch/tag/sha alias can become permanent false-stale. Fix: keep revision at ref layer only; snapshot metadata should key only by repo+commit.
- `Medium` Identity-to-path mapping lossy. [ModelHub.cs:132](../com.doji.hf-hub/Runtime/Core/ModelHub.cs), [ProjectPaths.cs:33](../com.doji.hf-hub/Editor/Import/ProjectPaths.cs), [ProjectPaths.cs:49](../com.doji.hf-hub/Editor/Import/ProjectPaths.cs), [CachePaths.cs:69](../com.doji.hf-hub/Editor/Cache/CachePaths.cs) replace separators with `-` or `--`. Distinct ids can collide, then cache/import state cross-talk. Fix: reversible encoding or hash+display suffix. Lossy sanitize fine for display, bad for identity.

## Review Priority

- 1. Fix identity rules first: strict same-repo single-revision policy per project, then project-manifest owner ids.
- 2. Fix runtime IO abstraction next. Mission says runtime-safe core; current path contract fights that.
- 3. Then do manifest v2 for filtered snapshot download. Without this, real HF repos get painful fast.

## Strict Revision Block Policy

- runtime `StreamingAssets` layout stays repo-scoped and human-readable
- one canonical `repo_id` may appear in many owners, but all owners in one project must request same revision
- conflicting revisions are unsupported project configuration, not warning-only state
- conflicting dependencies must not report `Ready`
- conflicting rows stay visible in Model Hub so users can inspect each requested revision and use `Locate`
- `Import`, `Import All`, `Remove From Project`, and `Remove From Cache` must not touch shared runtime payload for conflicting repo while conflict exists
- existing generated files may remain on disk during conflict, but HF Hub must treat them as non-authoritative until project resolves conflict

## Strict Revision Block Implementation Plan

- add dependency-level conflict reason such as `RevisionConflict`
- detect `repo_id -> requested revisions` conflicts once from discovery report before cache/import validation
- make `StatusService` short-circuit affected dependencies into conflict state before raw cache, shared import, or binding checks can claim `Ready`
- make `ModelHubService` surface conflict rows as `Error` with one shared diagnostic listing conflicting revisions and dependents
- keep `Locate`, disable import/manage actions for conflict rows in Model Hub UI
- make `Import All` and targeted import skip or fail conflicting repos without writing runtime files, bindings, or shared metadata
- keep repo-scoped runtime path layout unchanged
- add regression tests for status, Model Hub diagnostics/state, sync blocking, and removal gating

## Future Scope

- Add runtime-capable ONNX converter package behind `IHfHubOnnxModelConverter` when runtime conversion work starts.
- Extend `from_pretrained(...)` wrapper API only when concrete tokenizer/config/runtime helper needs appear.
- Add optional HuggingFace authentication support.
- Add gated model workflows.
- Later if package-owned revision conflicts stay painful, add explicit project-level revision override as opt-in escape hatch. Keep it loud in Model Hub and never silently choose one package winner.
- Define a stronger offline story for disconnected reuse and cache portability.
- Explore remote HuggingFace browsing in Model Hub window.
- Soften first-time model dependency notification and replace overly technical dialog copy.
- Surface license information during first import when model metadata makes it available.
