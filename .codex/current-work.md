# Current Work

## Focus
Finish verification for whole-repo vs selected-file manifests with per-file `optional` flags.

## Next Tasks
- [ ] Run updated Unity editor tests inside Unity and fix remaining failures.
- [ ] Manually verify package/project manifest file picker against a real multi-file HuggingFace repo.
- [ ] Manually verify Model Hub optional-file enabling before download/import.

## Blockers And Decisions Needed
- No CLI Unity compile/test runner is available; validation must happen inside Unity.

## Read Before Editing
- [docs/manual/getting-started.md](../docs/manual/getting-started.md)
- [docs/manual/manifest-reference.md](../docs/manual/manifest-reference.md)
- [docs/manual/editor-surfaces.md](../docs/manual/editor-surfaces.md)
- [com.doji.hf-hub/Editor/UI/ManifestModelListEditor.cs](../com.doji.hf-hub/Editor/UI/ManifestModelListEditor.cs)
- [com.doji.hf-hub/Editor/UI/ModelHubWindow.DetailsPane.cs](../com.doji.hf-hub/Editor/UI/ModelHubWindow.DetailsPane.cs)
