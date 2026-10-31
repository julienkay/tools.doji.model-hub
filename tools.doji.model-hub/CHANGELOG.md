# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- The package is now `tools.doji.model-hub`, and packages list their models in `model-hub-manifest.json`. The define for the ONNX Model Loader is `MODEL_HUB_ONNX_MODEL_LOADER`. The package is not affiliated with Hugging Face.

### Added

- Unity CLI commands, when the project has `com.unity.pipeline`: `model_hub_status`, `model_hub_sync`, `model_hub_sync_status`, `model_hub_set_file`, `model_hub_cancel`, and `model_hub_remove_cache`. Agents, scripts, and CI read each model's state and files, start a background sync and poll its progress and per-model result, and turn optional files on, without the Model Hub window and without dialogs. `unity run <project> --command model_hub_sync` waits until the sync ends. The commands live in `Doji.ModelHub.Editor.Cli`, which compiles only with `com.unity.pipeline`. The package ships a `model-hub-cli` agent skill for them.
- **Set Up All Models** says in the Console why it starts nothing, per model: ready, not found on Hugging Face, no optional file turned on, or requested at two revisions.
- Model Hub has a sidebar like Package Manager: **In Project** lists this project's models, and **Model Cache** lists the models downloaded on this machine. It shows the start of each model card. **Add to Project…** lists a cached model in the project manifest or in a package in development, with the cached files, so it needs no download. The cache map moved to its own window, **Model Cache Disk Usage**, opened from `Tools > Doji > AI Model Hub`, the Model Cache context menu, or a cached model's details.
- `.sentis` files on Hugging Face are used as downloaded, without an import step. The file extension decides the format of each file.
- ONNX external weight files (`.onnx_data`, `.onnx.data`) are selected with their `.onnx` file.
- Builds add models to StreamingAssets at build time. Project Settings chooses the default delivery, **Bundle in the build** or **Download at runtime**, and Model Hub overrides it per model.
- Players download models from Hugging Face at the commit tested in the Editor. Desktop players use the user's Hugging Face cache; other players use `persistentDataPath`.
- Players convert downloaded ONNX files when `tools.doji.onnx-model-loader` is installed.
- Runtime API: `Hub.LoadModelAsync`, `Hub.LoadModel`, `Hub.EnsureAvailableAsync`, `Hub.FromPretrained`, `Hub.FromPretrainedAsync`, and `Hub.RegisterOnnxConverter`. Android and Web players read StreamingAssets with `UnityWebRequest`.
- **Pull into Project** copies a model's Sentis files into `Assets/StreamingAssets/model-hub/models`.
- Model Hub and Add Model show the model format.
- Converted models are reused across Inference Engine updates of the same format generation (`SentisCompatibility`).
- Manifest files can set a `label`, the name model pickers show. The file picker in the manifest Inspector edits it.
- `HubModelReference` and `[HubModel("package.id")]`: an Inspector dropdown of a package's model files, or a model asset of your own. A status line under it offers **Download**, **Convert**, or **Set up** when the chosen file does not load yet; downloading an optional file turns it on for the project, and shows progress while AI Model Hub works. `Hub.GetModelFiles(Async)` lists a package's model files at runtime, and `Hub.LoadModelFromFile` loads a `.sentis` or `.onnx` file of your own.
- `PretrainedModel<T>` and `[PretrainedModel(packageId, repoId, defaultRepoPath)]`: a base class that gives a package's model class `FromPretrained(Async)` for the default model, a manifest file, a file path, or a `HubModelReference`, plus `DefaultModel` and `Backend`. `HubModelLoader` does the same loading for classes that run several models. `[HubModel(typeof(T))]` takes the package id from the attribute.
- `HubStorage` lists and deletes converted models and old Android copies of bundled models. AI Model Hub never deletes them on its own.

### Changed

- Models are ready, need a download, or need a conversion; Model Hub no longer shows an import step. AI Model Hub sets up downloaded models by itself when their converted models exist, and asks before it downloads or converts. **Needs conversion** says why: a first conversion, changed import settings, or a Sentis update with a new model format. Builds convert downloaded models without asking and never download. `Tools > Doji > AI Model Hub > Import All` is now **Set Up All Models**.
- Downloads use the shared Hugging Face cache instead of a private `raw` folder. Existing downloads are moved on the first Editor start.
- Converted models live in a shared folder outside the project, keyed by source file, converter, and conversion settings. Nothing is written into `Assets`.
- A whole-repository dependency on a repository with both ONNX and Sentis files is invalid. List the files you use.
- A sync skips models that manifests request at two revisions, instead of reporting them as failed.
- **Remove Imported Files** is now **Remove from Project**.

### Removed

- `Hub.TryResolveRuntimeModel` and `Hub.LoadRuntimeModel`. Use `Hub.LoadModelAsync`.
- Generated files under `Assets/StreamingAssets/model-hub/<owner>/` and `Assets/StreamingAssets/model-hub/registries/`. AI Model Hub deletes them on update.

## [1.0.0] - 2026-10-20

### Added

- Initial release.
- Package-owned `model-hub-manifest.json` model dependencies, plus project-owned manifests in Project Settings and ScriptableObject assets.
- Model Hub window: discover dependencies, download into a shared per-user cache, and import selected ONNX files as `.sentis` assets.
- Per-file conversion recipes and optional files, with ONNX input shapes read through `tools.doji.formats.onnx` without loading model weights.
- Runtime lookup by HuggingFace `repo_id` through `Hub.TryResolveRuntimeModel`, `Hub.FromPretrained`, and `Hub.LoadRuntimeModel`.
- Background sync can be cancelled from the Background Tasks window; a cancelled sync does not resume.
