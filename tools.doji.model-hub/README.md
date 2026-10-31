# tools.doji.model-hub

Reference AI models on Hugging Face from Unity packages, and load them as Inference Engine models in the Editor and in players.

Hub logic that does not need `UnityEditor` lives in `Runtime`, so players use the same cache, download, and registry code as the Editor. `Editor` holds Package Manager scanning, prompts, import, the build step, and the UI.

How it works:

- packages and projects list models in `model-hub-manifest.json`, the Project Settings manifest, or `ProjectManifestAsset` assets
- the Editor downloads the selected files into the shared Hugging Face cache, the same one the `huggingface_hub` Python library uses
- `.onnx` files are converted to Sentis models in a shared folder outside the project; `.sentis` files are used as downloaded
- nothing is written into `Assets`; import records live in `Library/Doji/ModelHub/imports/`
- builds bundle each model's Sentis file, or let the player download it at runtime, as Project Settings and per-model overrides say
- runtime code loads models by repository id and file path: `await Hub.LoadModelAsync("owner/model", "onnx/model.onnx")`
- players that download ONNX files convert them with the optional `tools.doji.onnx-model-loader` package

Manifest sources:

- Project Settings manifest: `ProjectSettings/Packages/tools.doji.model-hub/model-hub-manifest.json`, edited from `Project Settings > Doji > AI > AI Model Hub`
- package manifest: `model-hub-manifest.json` at the package root, edited from the manifest Inspector when the package source is writable
- ScriptableObject project manifest asset: `Assets > Create > Doji > AI > AI Model Hub Project Manifest`

Agent skills: `Skills~/model-hub-dependency/SKILL.md` for packages that declare models, and `Skills~/model-hub-cli/SKILL.md` for setting up a project's models with the Unity CLI. Unity does not import `Skills~`. Copy the folder into your agent's skill folder, for example `.claude/skills/`, or point your agent at it.

Companion project samples:

- `projects/AI Model Hub/Assets/AI Model Hub/RuntimeLocatorSample.cs` loads a model by repository id and file path
- `projects/AI Model Hub/Assets/AI Model Hub/Build Smoke Test/` builds a Windows player and checks bundled and downloaded models
