# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-10-20

### Added

- Initial release.
- Package-owned `hf-hub-manifest.json` model dependencies, plus project-owned manifests in Project Settings and ScriptableObject assets.
- Model Hub window: discover dependencies, download into a shared per-user cache, and import selected ONNX files as `.sentis` assets.
- Per-file conversion recipes and optional files, with ONNX input shapes read through `tools.doji.formats.onnx` without loading model weights.
- Runtime lookup by HuggingFace `repo_id` through `Hub.TryResolveRuntimeModel`, `Hub.FromPretrained`, and `Hub.LoadRuntimeModel`.
- Background sync can be cancelled from the Background Tasks window; a cancelled sync does not resume.
