# Mission And Constraints

## Mission

`com.doji.hf-hub` should make HuggingFace-hosted ML models usable in Unity without shipping large binary model files inside UPM packages.

`com.doji.downloads` should provide reusable durable download infrastructure for that workflow, including resumable transfer, persisted state, and editor recovery primitives, while staying independent from HuggingFace and Sentis specifics.

## Baseline Versions

- Unity version: `6000.3.10f1`
- Sentis version: `2.6.1`

## Intended Workflow

1. Unity package maintainers declare remote model dependencies in a package-owned `hf-hub-manifest.json`.
2. The Hub detects those package manifests when packages are present in a Unity project.
3. The Editor prompts the user to download and import the required model dependencies, similar to a package manager flow for AI assets.
4. Source ONNX files are downloaded into a shared local cache so multiple Unity projects can reuse the same raw files.
5. The package converts those cached ONNX files into project-owned `.sentis` artifacts for actual use in editor and runtime.

## Architecture Constraints

- UPM packages are for code and lightweight assets, not large ONNX payloads.
- Model dependency manifests belong to packages, not to individual Unity projects.
- Shared Hub logic should live in `Runtime` whenever it does not require `UnityEditor` APIs, because package must eventually support downloading files from HuggingFace at runtime.
- Generic download infrastructure should not stay coupled to HuggingFace or model-import concerns. Treat resumable download/job persistence as separate package-owned concern.
- The download path is the bread-and-butter workflow of this package and must be treated as core infrastructure, not incidental tooling.
- Downloads must run in the background using Unity editor task/progress surfaces instead of blocking the editor thread.
- Downloads must be resumable so large model payloads do not restart from zero after interruption.
- Active sync/download work must survive domain reloads and continue from persisted local state.
- Direct references to large ONNX `ModelAsset` files are undesirable because Sentis import and Unity domain reload behavior make them expensive in editor workflows.
- `.sentis` is preferred for consumption because it avoids repeated ONNX import work and supports flatbuffer-based loading patterns.
- `.sentis` output may be tied to the installed Sentis version, so generated artifacts should be treated as version-sensitive.
- A future cache for converted artifacts should likely be keyed by both model identity and Sentis version.

## Reference Implementation

- Use `huggingface_hub` as the main behavioral reference for Hub API interactions, download semantics, cache layout ideas, and manifest shape.
- The local reference checkout lives at `references/huggingface_hub` and is git-ignored.
- Reuse the existing local clone before downloading it again in future sessions.
- Inspect `src/huggingface_hub/file_download.py`, `src/huggingface_hub/_snapshot_download.py`, `src/huggingface_hub/hf_api.py`, and `src/huggingface_hub/constants.py` first for cache and download behavior.

## Working Preference

- Prefer implementing package-owned functionality under `com.doji.hf-hub/`.
- Use `projects/HF Hub/Assets` for integration examples, tooling validation, and test scenes.
- Use embedded test package `projects/HF Hub/Packages/com.doji.hf-hub-test-models` for local manifest discovery and sync validation.
- Keep internal architecture notes under `.codex/`, with the current vertical-slice spec in `.codex/architecture-v1.md`.
- Keep maintainer-only external reference checkouts under the top-level `references/` folder.
- Unless the user says otherwise, treat the package as greenfield and optimize for the current architecture rather than preserving compatibility with earlier drafts or experiments.
- Every finished implementation phase must add or update tests that cover that phase before it is considered complete.
- When a finished change affects package users or package authors, update user-facing docs in `docs/` during same phase.
