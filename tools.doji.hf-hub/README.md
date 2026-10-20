# tools.doji.hf-hub

Reference and download AI models from HuggingFace for Unity packages.

Shared Hub logic should live in `Runtime` whenever possible so same package can later support downloading files from HuggingFace at runtime. Keep only UnityEditor-specific integration, Package Manager scanning, prompts, and asset import workflows in `Editor`.

Current runtime contract:

- editor sync generates runtime-loadable `.sentis` payloads directly under repo-scoped snapshot paths in `Assets/StreamingAssets/hf-hub/`
- editor sync downloads only files required by each dependency scope into shared cache and converts selected ONNX files to `.sentis` for runtime
- generated runtime assets use human-readable `Assets/StreamingAssets/hf-hub/<owner>/<model>/...` paths
- sync writes per-package runtime locator registries under `Assets/StreamingAssets/hf-hub/registries/`
- sync stores editor-only shared import and owner binding metadata under `Library/Doji/HfHub/imports/`
- projects can declare dependencies either with `ProjectSettings/Packages/tools.doji.hf-hub/hf-hub-manifest.json` or with `ProjectManifestAsset` ScriptableObject assets, while packages stay on plain `hf-hub-manifest.json`
- editor conversion uses Unity Inference ONNX conversion directly, without temporary ONNX asset imports
- ONNX conversion now hangs off shared `IOnnxModelConverter`; runtime placeholder exists for future custom runtime converter package
- runtime code resolves models by HuggingFace `repo_id` through `Hub.TryResolveRuntimeModel("owner/model")`
- owner-specific runtime lookup also uses canonical `repo_id` through `Hub.TryResolveRuntimeModel("package-or-project-owner", "owner/model")`
- runtime code resolves repo snapshots by HuggingFace `repo_id` through `Hub.TryResolveRuntimeModel("owner/model")`
- runtime code can wrap one synchronized repo snapshot in `from_pretrained(...)` style API through `Hub.FromPretrained("owner/model")`
- runtime code loads one generated Sentis payload by repo-relative file path through `Hub.LoadRuntimeModel("owner/model", "onnx/model.onnx")`
- default runtime workflow is offline-first: editor sync prepares payloads, runtime resolves local files, runtime download comes later

Manifest sources:

- Project Settings manifest: `ProjectSettings/Packages/tools.doji.hf-hub/hf-hub-manifest.json`, edited from `Project Settings > Doji > AI > AI Model Hub`
- package manifest: `hf-hub-manifest.json` at local package root, edited from the custom manifest Inspector when package source is writable
- ScriptableObject project manifest asset: create from `Assets > Create > Doji > AI > HF Hub Project Manifest` for project-owned content that should live as a Unity asset

Companion project sample:

- `projects/HF Hub/Assets/HF Hub/RuntimeLocatorSample.cs` shows project-manifest lookup by canonical `repo_id`, then loads chosen generated `.sentis` by repo-relative ONNX path
