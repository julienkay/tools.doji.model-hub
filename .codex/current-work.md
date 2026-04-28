# Current Work

## Focus
Verify editable Model manifest inspector behavior for writable package sources and confirm read-only behavior stays clear for installed package cache entries.

## Next Tasks
- [ ] Confirm `Apply` and `Revert` work for local `file:` package Model manifests without needing a manual refresh.
- [ ] Verify installed package cache manifests stay read-only with clear guidance.
- [ ] Decide whether inspector should grow structured field editing beyond raw JSON text editing.

## Blockers And Decisions Needed
- Unity package asset refresh behavior after editing package-backed JSON files may differ across embedded, local `file:`, and installed package sources.

## Read Before Editing
- [com.doji.hf-hub/Editor/UI/ModelManifestInspector.cs](../com.doji.hf-hub/Editor/UI/ModelManifestInspector.cs)
- [docs/manual/package-manifests-and-cache.md](../docs/manual/package-manifests-and-cache.md)
