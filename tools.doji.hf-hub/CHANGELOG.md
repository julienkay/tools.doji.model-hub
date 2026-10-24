# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- `.sentis` files on Hugging Face are used as downloaded, without an import step. The file extension decides the format of each file.
- ONNX external weight files (`.onnx_data`, `.onnx.data`) are selected with their `.onnx` file.
- Builds add models to StreamingAssets at build time. Project Settings chooses the default delivery, **Bundle in the build** or **Download at runtime**, and Model Hub overrides it per model.
- Players download models from Hugging Face at the commit tested in the Editor. Desktop players use the user's Hugging Face cache; other players use `persistentDataPath`.
- Players convert downloaded ONNX files when `tools.doji.onnx-model-loader` is installed.
- Runtime API: `Hub.LoadModelAsync`, `Hub.LoadModel`, `Hub.EnsureAvailableAsync`, `Hub.FromPretrained`, `Hub.FromPretrainedAsync`, and `Hub.RegisterOnnxConverter`. Android and Web players read StreamingAssets with `UnityWebRequest`.
- **Pull into Project** copies a model's Sentis files into `Assets/StreamingAssets/hf-hub/models`.
- Model Hub and Add Model show the model format.

### Changed

- Downloads use the shared Hugging Face cache instead of a private `raw` folder. Existing downloads are moved on the first Editor start.
- Converted models live in a shared folder outside the project, keyed by source file, converter, and conversion settings. Nothing is written into `Assets`.
- A whole-repository dependency on a repository with both ONNX and Sentis files is invalid. List the files you use.
- **Remove Imported Files** is now **Remove from Project**.

### Removed

- `Hub.TryResolveRuntimeModel` and `Hub.LoadRuntimeModel`. Use `Hub.LoadModelAsync`.
- Generated files under `Assets/StreamingAssets/hf-hub/<owner>/` and `Assets/StreamingAssets/hf-hub/registries/`. HF Hub deletes them on update.

## [1.0.0] - 2026-10-20

### Added

- Initial release.
- Package-owned `hf-hub-manifest.json` model dependencies, plus project-owned manifests in Project Settings and ScriptableObject assets.
- Model Hub window: discover dependencies, download into a shared per-user cache, and import selected ONNX files as `.sentis` assets.
- Per-file conversion recipes and optional files, with ONNX input shapes read through `tools.doji.formats.onnx` without loading model weights.
- Runtime lookup by HuggingFace `repo_id` through `Hub.TryResolveRuntimeModel`, `Hub.FromPretrained`, and `Hub.LoadRuntimeModel`.
- Background sync can be cancelled from the Background Tasks window; a cancelled sync does not resume.
