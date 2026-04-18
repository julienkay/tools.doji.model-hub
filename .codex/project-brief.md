# Project Brief

## Mission

`com.doji.hf-hub` makes HuggingFace-hosted ML models usable in Unity without shipping large binary model files inside UPM packages.

`com.doji.downloads` provides reusable durable download infrastructure for that workflow while staying independent from HuggingFace and Sentis specifics.

## Baseline Versions

- Unity version: `6000.3.10f1`
- Sentis version: `2.6.1`

## Intended Workflow

1. A Unity package declares remote model dependencies in a package-owned `hf-hub-manifest.json`.
2. HF Hub discovers those manifests when packages are present in a Unity project.
3. The Editor prompts the user to download and import the required model dependencies.
4. Whole HuggingFace repository snapshots are downloaded into shared local cache so multiple Unity projects can reuse same raw files.
5. The package keeps repo files only in shared cache and converts cached ONNX files into project-owned runtime `.sentis` artifacts.
6. Runtime code primarily resolves already-generated repo snapshots and loads individual model files by repo-relative path. Runtime download remains future scope, but default workflow assumes editor sync happened before build/runtime use.

## Architectural Boundaries

- Model dependency manifests belong to packages, not individual Unity projects.
- Shared Hub logic should live in `Runtime` whenever it does not require `UnityEditor` APIs.
- Generic download infrastructure is a separate package-owned concern and should not stay coupled to HuggingFace or model import logic.
- Editor-only UX, prompts, Project Settings integration, and Package Manager adapters stay in `Editor`.
- Direct long-lived references to ONNX `ModelAsset` files are undesirable because they are expensive during import and domain reload.
- `.sentis` is the preferred consumption format because it avoids repeated ONNX import work.
- Generated `.sentis` artifacts should be treated as version-sensitive to the installed Sentis version.
- Reuse raw downloads across projects whenever possible. If converted artifacts are cached later, key them by model identity and Sentis version.
- Compared with upstream `huggingface_hub`, prioritize stronger offline/runtime use: default package workflow assumes model payloads are pre-downloaded in editor and present at runtime, with runtime download as additive capability rather than primary path.

## Reference Implementation

- Use `huggingface_hub` as the primary behavioral reference for Hub API interactions, download semantics, cache layout ideas, and manifest shape.
- Reuse the local checkout at `references/huggingface_hub` when present instead of downloading it again.
- Inspect `file_download.py`, `_snapshot_download.py`, `hf_api.py`, and `constants.py` first when checking upstream behavior.

## Working Roots

- Repository root: `../..` from `projects/HF Hub`
- Package under development: `com.doji.hf-hub/`
- Companion project assets and scenes: `projects/HF Hub/Assets`
- Internal maintainer docs: `.codex/`
- Local reference checkouts: `references/`
