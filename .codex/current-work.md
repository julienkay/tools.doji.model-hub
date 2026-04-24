# Current Work

## Focus

Model Hub window information architecture is being tightened so `Details` answers source-model questions and `Storage` answers local footprint questions.

## Next Tasks

- [ ] Re-check `Storage` tab wording and row set after visual use with remote-only, cached-only, and imported-only model states.
- [ ] Decide whether cache-size sidecar lookups should ever be refreshed after first success/failure or remain strictly one-shot per repository revision.
- [ ] Extend ONNX inspection/signature surfacing beyond current dynamic-dimension UI so file rows can show richer input shape context.

## Blockers And Decisions Needed

- Decide whether `Storage` should later show combined total footprint row in addition to cache/runtime breakdown.
- Decide future static-dims UX once input signature inspection exists.

## Read Before Editing

- [.codex/architecture/editor-model-hub-ui.md](./architecture/editor-model-hub-ui.md)
- [com.doji.hf-hub/Editor/UI/ModelHubWindow.DetailsPane.cs](../com.doji.hf-hub/Editor/UI/ModelHubWindow.DetailsPane.cs)
- [com.doji.hf-hub/Editor/Cache/RemoteSnapshotSizeEstimateService.cs](../com.doji.hf-hub/Editor/Cache/RemoteSnapshotSizeEstimateService.cs)
