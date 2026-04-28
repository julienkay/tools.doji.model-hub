# Current Work

## Focus
Return to Model Hub `Storage` tab follow-up after unresolved-dependency reminder work finished.

## Next Tasks
- [ ] Visually verify treemap readability for remote-only, cached-only, imported-only, and cached-plus-imported scenarios in companion project sandbox.
- [ ] Decide whether `Storage` needs explicit legend or summary chips for cache/runtime split.
- [ ] Re-check `Storage` tab wording and row order after treemap pass.

## Blockers And Decisions Needed
- Decide whether `Storage` should add explicit combined-total row now that treemap shows mixed footprint.
- Decide whether cache-size sidecar lookups should ever refresh after first success/failure or remain one-shot per repository revision.

## Read Before Editing
- [.codex/architecture/editor-model-hub-ui.md](./architecture/editor-model-hub-ui.md)
- [com.doji.hf-hub/Editor/UI/ModelHubWindow.DetailsPane.cs](../com.doji.hf-hub/Editor/UI/ModelHubWindow.DetailsPane.cs)
- [com.doji.hf-hub/Editor/Status/ModelHubModels.cs](../com.doji.hf-hub/Editor/Status/ModelHubModels.cs)
