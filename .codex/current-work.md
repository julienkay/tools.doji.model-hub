# Current Work

## Focus

Runtime locator slice is at clean stop point. Next slice should unify identity around HuggingFace `repo_id` and finish follow-up contracts.

Companion project now includes empty `HfHubProjectManifestAsset` sample at `Assets/HF Hub/HF Hub Project Manifest.asset`, editor discovery now merges package JSON manifests with project manifest assets, and `Assets/HF Hub/HfHubRuntimeLocatorSample.cs` demonstrates upstream-style runtime lookup through `ModelHub.LoadRuntimeModel("<repo_id>")`.

## Next Tasks

- [ ] Remove package-local manifest `id` from public/runtime identity flow and make HuggingFace `repo_id` canonical.
- [ ] Extend runtime locator registry entries from primary `.sentis` payloads to multi-file companion file sets.
- [ ] Define ambiguity strategy when same `repo_id` appears in multiple manifest owners.
- [ ] Turn companion runtime sample script into a complete clickable scene or prefab flow if UX demo still needed.
- [ ] Add runtime-capable ONNX converter package behind `IHfHubOnnxModelConverter` when runtime conversion work starts.

## Blockers And Decisions Needed

- No active blocker for current sample-flow slice.

## Read Before Editing

- [Project Brief](./project-brief.md)
- [Runtime Locator](./architecture/runtime-locator.md)
- [Import And Generated Assets](./architecture/import-and-generated-assets.md)
- [Download And Cache](./architecture/download-and-cache.md)

## Exit Criteria

- Public runtime API uses HuggingFace-style `repo_id` lookup cleanly.
- Manifest/runtime identity no longer depends on package-local alias ids.
