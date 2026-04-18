# com.doji.hf-hub

Reference and download AI models from HuggingFace for Unity packages.

Shared Hub logic should live in `Runtime` whenever possible so same package can later support downloading files from HuggingFace at runtime. Keep only UnityEditor-specific integration, Package Manager scanning, prompts, and asset import workflows in `Editor`.

Current runtime contract:

- editor sync generates runtime-loadable `.sentis` payloads directly under repo-scoped snapshot paths in `Assets/StreamingAssets/hf-hub/`
- sync writes per-package runtime locator registries under `Assets/StreamingAssets/hf-hub/registries/`
- sync stores editor-only import provenance under `Library/Doji/HfHub/import-state/`
- editor conversion uses Unity Inference ONNX conversion directly, without temporary ONNX asset imports
- ONNX conversion now hangs off shared `IHfHubOnnxModelConverter`; runtime placeholder exists for future custom runtime converter package
- runtime code resolves models by package id + model id through `ModelHub.TryResolveRuntimeModel(...)`
- default runtime workflow is offline-first: editor sync prepares payloads, runtime resolves local files, runtime download comes later
