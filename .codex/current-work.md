# Current Work

## Focus

Importer-style per-file recipe editing is now in Model Hub `Files` tab, with ONNX-driven dynamic-dimension inspection. Current open slice is tightening sync/status behavior around applied recipe changes and improving shape-edit UX.

## Next Tasks

- [ ] Extend ONNX inspection/signature surfacing beyond current dynamic-dimension UI so file rows can show richer input shape context.
- [ ] Re-check background sync and status behavior after `Apply`, especially repo refresh and any remaining `DownloadOnly` / `MaterializeOnly` resume paths.
- [ ] Decide whether to keep direct override-asset affordances or fully commit to importer-style editing in Model Hub only.

## Blockers And Decisions Needed

- Decide future static-dims UX once input signature inspection exists.
- Decide whether `Import All` should stay full sync or become two explicit bulk actions.

## Read Before Editing

- [.codex/architecture/per-file-conversion-recipes.md](./architecture/per-file-conversion-recipes.md)
- [com.doji.hf-hub/Editor/Import/ProjectModelRecipeSource.cs](../com.doji.hf-hub/Editor/Import/ProjectModelRecipeSource.cs)
- [com.doji.hf-hub/Editor/UI/ModelHubWindow.cs](../com.doji.hf-hub/Editor/UI/ModelHubWindow.cs)
- [com.doji.hf-hub/Editor/UI/EditorOperations.cs](../com.doji.hf-hub/Editor/UI/EditorOperations.cs)
- [com.doji.hf-hub/Editor/Sync/BackgroundSyncManager.cs](../com.doji.hf-hub/Editor/Sync/BackgroundSyncManager.cs)
