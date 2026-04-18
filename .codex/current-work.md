# Current Work

## Focus

Repo-level dependency architecture is now active: manifests declare one `repo_id`, cache downloads whole snapshots, import keeps raw repo files only in shared cache, runtime output uses human-readable `Assets/StreamingAssets/hf-hub/<owner>/<model>/...` paths, and runtime lookup resolves repo snapshots by `repo_id`.

Companion project includes empty `HfHubProjectManifestAsset` sample at `Assets/HF Hub/HF Hub Project Manifest.asset`, and editor discovery merges package JSON manifests with project manifest assets.

## Next Tasks

## Blockers And Decisions Needed

- No active blocker.

## Read Before Editing

- [Project Brief](./project-brief.md)
- [Runtime Locator](./architecture/runtime-locator.md)
- [Import And Generated Assets](./architecture/import-and-generated-assets.md)
- [Download And Cache](./architecture/download-and-cache.md)

## Exit Criteria

- Repo-level workflow is consistent across manifest, cache, import, runtime locator, and docs.
- Remaining follow-up is runtime helper polish, not per-file dependency plumbing.
