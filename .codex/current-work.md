# Current Work

## Focus
Verify streamed ONNX signature inspection in Model Hub after replacing the previous full-file Unity parser path.

## Next Tasks
- [ ] Run HF Hub editor tests inside Unity, especially `OnnxModelInspectorTests`.
- [ ] Run ONNX Model Loader editor tests inside Unity, especially `OnnxModelSignatureReaderTests`.
- [ ] Manually verify Model Hub Files tab against cached ONNX files with dynamic dimensions and static-only input shapes.

## Blockers And Decisions Needed
- No CLI Unity compile/test runner is available; validation must happen inside Unity.

## Read Before Editing
- [.codex/architecture/per-file-conversion-recipes.md](architecture/per-file-conversion-recipes.md)
- [com.doji.hf-hub/Editor/Import/OnnxModelInspector.cs](../com.doji.hf-hub/Editor/Import/OnnxModelInspector.cs)
- [com.doji.hf-hub/Editor/UI/ModelHubFileEditing.cs](../com.doji.hf-hub/Editor/UI/ModelHubFileEditing.cs)
- [../com.doji.onnx-model-loader/com.doji.onnx-model-loader/Runtime/InferenceEngine/Onnx/OnnxModelSignatureReader.cs](../../com.doji.onnx-model-loader/com.doji.onnx-model-loader/Runtime/InferenceEngine/Onnx/OnnxModelSignatureReader.cs)
