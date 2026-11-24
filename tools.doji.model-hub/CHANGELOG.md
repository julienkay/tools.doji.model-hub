# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- ONNX files convert with `tools.doji.onnx-model-loader`, in the Editor and in players, and the package depends on it. The loader quantizes each weight while it loads and writes the weights to the file one at a time, so a conversion never holds all weights in memory. A model quantized to float16 or uint8 dequantizes each weight right before the first layer that reads it. Before, Inference Engine's `ModelQuantizer` dequantized all weights at the start of the model, so the model ran with all its float32 weights on the device. Models convert again after the update, in the Editor and in players.
- `IOnnxModelConverter.Convert(request, sentisPath)` writes the converted model to a file and returns no `Model`.
- The package is now `tools.doji.model-hub`, and packages list their models in `model-hub-manifest.json`. The package is not affiliated with Hugging Face.
- Optional-file choices and changed import settings are stored in `ProjectSettings/Doji.AI.ModelHubSettings.asset`, next to the build delivery settings. The `AI Model Hub Optional File Selections` and `AI Model Hub Conversion Overrides` assets under `Assets/Doji/ModelHub/` are no longer read; choose those settings again in Model Hub.

### Added

- `-executeMethod Doji.AI.ModelHub.Editor.ModelHubBatch.SetModel -modelHubRepo <id> -modelHubEnabled true|false` enables or disables an optional model in batch mode, like `model_hub_set_model`. `-modelHubOwner <package-id>` picks the package. It exits with `0`, or `2` for an invalid argument, and can run with `-quit`.
- `-executeMethod Doji.AI.ModelHub.Editor.ModelHubBatch.Sync` and `ModelHubBatch.Status` run a sync or a status check in batch mode without `com.unity.pipeline`. They log the JSON result, write it to `-modelHubOutput <path>`, and exit with `0` (ready), `1` (a model failed or is not ready), or `2` (invalid argument). `-modelHubRepo <id>` limits them to one model. `Sync` refuses `-quit` with exit code `2`: with it, Unity waits without updating the Editor, and the sync hangs until Unity's timeout.
- `PretrainedRepository.OpenModelAsync` and `OpenModel` open a model's Sentis file as a `ModelFile` of `tools.doji.sentis-utils`, downloading or converting it first when needed. Its weights stay in the file until a worker needs them, so they are not in RAM. The package now depends on `tools.doji.sentis-utils`.
- Optional models: `"optional": true` on a manifest model entry declares a model that each project opts into. Until the project enables it, AI Model Hub does not download, convert, or bundle it; status is `NotEnabled`, sync skips it, and it takes no part in revision checks. Model Hub lists it under **Available** with its size and license and one **Enable** button; enabled optional models get **Disable**. The choice is stored per project in `ProjectSettings/Doji.AI.ModelHubSettings.asset`. The `model_hub_set_model --repo <id> --enabled true|false` CLI command does the same. Loading a file of a model that is not enabled asks in the Editor whether to enable and download it, in the setup prompt window (its button names what happens, such as Download & Convert, and counts only the files that are not in the cache yet). When the setup prompt opens for required models, it also lists the optional models that are not enabled, each with a checkbox that starts off, so they can be enabled and downloaded in the same step. In players and batch mode, loading such a model throws an error that says how to enable it.
- `Preferences > Doji > AI > AI Model Hub` chooses the download cache and the folder of converted models per user, for every project, and applies at once. It replaces the cache folder in Project Settings. Changing a folder offers to move its content (a rename on the same drive; a checked copy across drives that deletes the old folder only after you confirm), and can set `HF_HOME` for your user account so Python follows.
- The Model Hub window, Preferences, and Project Settings show both folders, what set each, and the free space on their drives. On Windows they warn when `HF_HOME`, `HF_HUB_CACHE`, or `DOJI_MODEL_HUB_DATA_ROOT` changed after Unity Hub started.
- A sync stops before it downloads or converts when the files do not fit on their drives, and names the space needed and free per drive.
- `model_hub_cache_info` Unity CLI command: the folders, their sources, free space, and stale environment variables.

- `Hub.ReadFileAsync`, `Hub.ReadTextAsync` and their synchronous forms, and `PretrainedRepository.ReadFileAsync`, `ReadTextAsync`, `DataFiles` and `HasFile`, to read selected files that are not models, such as configs and tokenizer files. Builds bundle or download these files like their model.
- A `weights.pb` file next to the only `.onnx` file of its folder counts as that model's external weights.
- Unity CLI commands, when the project has `com.unity.pipeline`: `model_hub_status`, `model_hub_sync`, `model_hub_sync_status`, `model_hub_set_file`, `model_hub_cancel`, and `model_hub_remove_cache`. Agents, scripts, and CI read each model's state and files, start a background sync and poll its progress and per-model result, and turn optional files on, without the Model Hub window and without dialogs. `unity run <project> --command model_hub_sync` waits until the sync ends. The commands live in `Doji.ModelHub.Editor.Cli`, which compiles only with `com.unity.pipeline`. The package ships a `model-hub-cli` agent skill for them.
- **Set Up All Models** says in the Console why it starts nothing, per model: ready, not found on Hugging Face, no optional file turned on, or requested at two revisions.
- Model Hub has a sidebar like Package Manager: **In Project** lists this project's models, and **Model Cache** lists the models downloaded on this machine. It shows the start of each model card. **Add to Project…** lists a cached model in the project manifest or in a package in development, with the cached files, so it needs no download. The cache map moved to its own window, **Model Cache Disk Usage**, opened from `Tools > Doji > AI Model Hub`, the Model Cache context menu, or a cached model's details.
- `.sentis` files on Hugging Face are used as downloaded, without an import step. The file extension decides the format of each file.
- ONNX external weight files (`.onnx_data`, `.onnx.data`) are selected with their `.onnx` file.
- Builds add models to StreamingAssets at build time. Project Settings chooses the default delivery, **Bundle in the build** or **Download at runtime**, and Model Hub overrides it per model.
- Players download models from Hugging Face at the commit tested in the Editor. Desktop players use the user's Hugging Face cache; other players use `persistentDataPath`.
- Players convert downloaded ONNX files.
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

### Fixed

- A player build bundles the real files of a model. Before, when the Hugging Face cache stored a bundled file as a symbolic link, the build copied the link. On Windows without the symbolic link privilege, the build then failed with `A required privilege is not held by the client`. This affected `.sentis` files from the Hub, and configs, tokenizers and other data files.
- A manifest edit that does not change a model no longer makes the manifest's models stale. Examples are other line endings from git (`core.autocrlf`), a byte order mark, formatting, or a change to another model. AI Model Hub compares only the model's repository, revision, selected files, and conversion settings.

## [1.0.0] - 2026-10-20

### Added

- Initial release.
- Package-owned `model-hub-manifest.json` model dependencies, plus project-owned manifests in Project Settings and ScriptableObject assets.
- Model Hub window: discover dependencies, download into a shared per-user cache, and import selected ONNX files as `.sentis` assets.
- Per-file conversion recipes and optional files, with ONNX input shapes read through `tools.doji.formats.onnx` without loading model weights.
- Runtime lookup by HuggingFace `repo_id` through `Hub.TryResolveRuntimeModel`, `Hub.FromPretrained`, and `Hub.LoadRuntimeModel`.
- Background sync can be cancelled from the Background Tasks window; a cancelled sync does not resume.
