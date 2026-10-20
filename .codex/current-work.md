# Current Work

## Focus
Release prep for `tools.doji.hf-hub` 1.0.0 with `tools.doji.downloads` and `tools.doji.formats.onnx`.

## Next Tasks
- [ ] Open `projects/HF Hub` in Unity 6000.6.4f1; let Package Manager re-resolve `packages-lock.json`; fix compile errors.
- [ ] Run EditMode tests for `tools.doji.hf-hub`, `tools.doji.downloads`, `tools.doji.formats.onnx` (`OnnxModelSignatureReaderTests`).
- [ ] Publish `tools.doji.formats.onnx` and `tools.doji.downloads` before `tools.doji.hf-hub`; hf-hub depends on both at `1.0.0`.

## Blockers And Decisions Needed
- Repo folders `G:\Unity\com.doji.hf-hub` and `G:\Unity\com.doji.formats.onnx` still use old names; companion manifests expect `tools.doji.*` sibling folders.
- GitHub repos still use `com.doji.*` names.
- `com.doji.onnx-model-loader` and `com.doji.midas` are not migrated; they reference old package IDs.

## Read Before Editing
- [.codex/index.md](index.md)
- [per-file-conversion-recipes.md](architecture/per-file-conversion-recipes.md) for ONNX signature reading
