# Current Work

## Focus

Fix project-manifest owner ids now that strict same-repo revision conflicts are blocked in status, sync, editor UX, and tests.

## Next Tasks

- [ ] Give each project manifest asset a stable owner id so bindings, registries, and sync keys stop collapsing to `project`.
- [ ] Fix runtime IO abstraction next. Mission says runtime-safe core; current path contract fights that.
- [ ] Then do manifest v2 for filtered snapshot download. Without this, real HF repos get painful fast.

## Blockers And Decisions Needed

- No active blocker.

## Read Before Editing

- [Backlog](./backlog.md)
- [Import And Generated Assets](./architecture/import-and-generated-assets.md)
- [Editor Model Hub UI](./architecture/editor-model-hub-ui.md)
- [Runtime Locator](./architecture/runtime-locator.md)
