# Internal Docs

Read these first for most sessions:

1. [Current Work](./current-work.md)
2. [Project Brief](./project-brief.md)
3. Only the architecture docs linked from the current task

Planning docs (on demand):
- [Backlog](./backlog.md) for longer-scoped work, parked epics,  open questions, and lower-priority items that are not active in `current-work.md`

Historical architecture context (on demand):
- [Architecture Milestones](./milestones.md) for completed architectural shifts and decisions that still matter to current implementation choices

Core architecture docs:

- [Overview](./architecture/overview.md)
- [Testing](./architecture/testing.md)
- [Manifest Schema](./architecture/manifest-schema.md)
- [Download And Cache](./architecture/download-and-cache.md)
- [Import And Generated Assets](./architecture/import-and-generated-assets.md)
- [Per-File Conversion Recipes](./architecture/per-file-conversion-recipes.md)
- [Runtime Locator](./architecture/runtime-locator.md)

Maintenance rules:

- Keep `current-work.md` short, task-focused, and free of completed work.
- Move implemented behavior into architecture docs instead of archiving it in session notes.
- Update `architecture/*.md` when implemented changes alter the current system design, package boundaries, data or API contracts, file or asset layout conventions, or runtime versus editor ownership described there.
- Keep backlog items and open questions out of the session resume path unless they are active now.
- Treat `backlog.md` as the discoverable home for longer-running epics and non-active work that should still survive across sessions.
- Update `milestones.md` only when a completed change materially alters the architecture, package boundaries, or durable implementation direction of the project.
