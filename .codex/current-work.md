# Current Work

## Focus

Build first companion-project sample around sync, runtime locator lookup, and runtime model load.

## Next Tasks

- [ ] Build the first companion-project sample around sync, locator lookup, and runtime model load.
- [ ] Extend runtime locator registry entries from primary `.sentis` payloads to multi-file companion file sets.
- [ ] Decide whether public runtime lookup should stay `package id + model id` or move closer to HuggingFace source ids for package-facing APIs.
- [ ] Add runtime-capable ONNX converter package behind `IHfHubOnnxModelConverter` when runtime conversion work starts.

## Blockers And Decisions Needed

- No active blocker for current sample-flow slice.

## Read Before Editing

- [Project Brief](./project-brief.md)
- [Runtime Locator](./architecture/runtime-locator.md)
- [Import And Generated Assets](./architecture/import-and-generated-assets.md)
- [Download And Cache](./architecture/download-and-cache.md)

## Exit Criteria

- The companion project demonstrates sync, lookup, and runtime model load through the locator path.
