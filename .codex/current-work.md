# Current Work

## Focus

Finish the runtime locator contract, then prove it in a companion-project sample flow.

## Next Tasks

- [ ] Finish the runtime locator contract for generated `.sentis` assets and companion repository files.
- [ ] Generate package-owned runtime locator metadata for string-based runtime lookup and future multi-file model pipelines.
- [ ] Update runtime and editor code so packages stop depending on hard-coded `Assets/Doji/HfHub/Generated/...` paths.
- [ ] Build the first companion-project sample around sync, locator lookup, and runtime model load.

## Blockers And Decisions Needed

- Settle the registry shape for one model id resolving to a primary `.sentis` payload plus optional companion files.
- Settle the final `StreamingAssets` layout and uniqueness contract across packages and models.

## Read Before Editing

- [Project Brief](./project-brief.md)
- [Runtime Locator](./architecture/runtime-locator.md)
- [Import And Generated Assets](./architecture/import-and-generated-assets.md)
- [Download And Cache](./architecture/download-and-cache.md)

## Exit Criteria

- Runtime APIs resolve generated content by stable string id rather than project path conventions.
- Import generates runtime locator metadata together with project-owned artifacts.
- The companion project demonstrates sync, lookup, and runtime model load through the locator path.
