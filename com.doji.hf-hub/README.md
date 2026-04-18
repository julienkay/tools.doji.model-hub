# com.doji.hf-hub

Reference and download AI models from HuggingFace for Unity packages.

Shared Hub logic should live in `Runtime` whenever possible so same package can later support downloading files from HuggingFace at runtime. Keep only UnityEditor-specific integration, Package Manager scanning, prompts, and asset import workflows in `Editor`.

Current runtime contract:

- editor sync generates runtime-loadable `.sentis` payloads directly under repo-scoped snapshot paths in `Assets/StreamingAssets/hf-hub/`
- editor sync downloads full declared repository snapshot into shared cache and converts every ONNX file to `.sentis` for runtime
- generated runtime assets use human-readable `Assets/StreamingAssets/hf-hub/<owner>/<model>/...` paths
- sync writes per-package runtime locator registries under `Assets/StreamingAssets/hf-hub/registries/`
- sync stores editor-only import provenance under `Library/Doji/HfHub/import-state/`
- projects can declare dependencies with `HfHubProjectManifestAsset` from `Assets > Create > Doji > AI > HF Hub Project Manifest`, while packages stay on plain `hf-hub-manifest.json`
- editor conversion uses Unity Inference ONNX conversion directly, without temporary ONNX asset imports
- ONNX conversion now hangs off shared `IHfHubOnnxModelConverter`; runtime placeholder exists for future custom runtime converter package
- runtime code resolves models by HuggingFace `repo_id` through `ModelHub.TryResolveRuntimeModel("owner/model")`
- owner-specific runtime lookup also uses canonical `repo_id` through `ModelHub.TryResolveRuntimeModel("package-or-project-owner", "owner/model")`
- runtime code resolves repo snapshots by HuggingFace `repo_id` through `ModelHub.TryResolveRuntimeModel("owner/model")`
- runtime code can wrap one synchronized repo snapshot in `from_pretrained(...)` style API through `ModelHub.FromPretrained("owner/model")`
- runtime code loads one generated Sentis payload by repo-relative file path through `ModelHub.LoadRuntimeModel("owner/model", "onnx/model.onnx")`
- default runtime workflow is offline-first: editor sync prepares payloads, runtime resolves local files, runtime download comes later

Companion project sample:

- `projects/HF Hub/Assets/HF Hub/HfHubRuntimeLocatorSample.cs` shows project-manifest lookup by canonical `repo_id`, then loads chosen generated `.sentis` by repo-relative ONNX path
