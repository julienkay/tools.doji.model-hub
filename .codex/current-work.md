# Current Work

## Focus

Manifest schema now uses canonical Hugging Face model ids directly; active work is Model Hub status polish, including separate cache-versus-project lifecycle handling, plus cleanup of stale tests, docs, and comments.

## Next Tasks

- [ ] Finish the canonical-model-id cleanup across remaining stale tests, docs, generated docs, and editor/runtime comments.
- [ ] Add warning surfaces for revision ambiguity and partial ONNX conversion outcomes inside the Model Hub diagnostics flow.
- [ ] Decide whether `Remove From Project` and `Remove From Cache` need confirmation dialogs or stronger warning copy.
- [ ] Revisit whether dependency status needs a more granular lifecycle split for UI copy, especially `download needed` versus `import needed`.

## Blockers And Decisions Needed

- No active blocker.

## Read Before Editing

- [Project Brief](./project-brief.md)
- [Editor Model Hub UI](./architecture/editor-model-hub-ui.md)
- [Manifest Schema](./architecture/manifest-schema.md)
- [Runtime Locator](./architecture/runtime-locator.md)
- [Import And Generated Assets](./architecture/import-and-generated-assets.md)
- [Download And Cache](./architecture/download-and-cache.md)

## Exit Criteria

- Manifest schema uses canonical Hugging Face model ids without redundant display/repository fields.
- Project Settings stores real HF Hub settings instead of status lists.
- Model Hub window is the main local inventory surface for repo imports, dependents, and file links.
