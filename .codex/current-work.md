# Current Work

## Focus

Refactor runtime IO and path access so package-owned core code stays runtime-safe outside editor-only adapters.

## Next Tasks

- [ ] Replace editor-coupled file/path access in package-owned core flow with runtime-safe abstractions and keep UnityEditor adapters at the edges.
- [ ] Audit discovery, cache, and import code for editor-only path assumptions after the IO split so future runtime download work can reuse the same services.
- [ ] Then do manifest v2 for filtered snapshot download so large HuggingFace repos do not require full snapshot pulls.

## Blockers And Decisions Needed

- No active blocker.

## Read Before Editing

- [Import And Generated Assets](./architecture/import-and-generated-assets.md)
- [Runtime Locator](./architecture/runtime-locator.md)
