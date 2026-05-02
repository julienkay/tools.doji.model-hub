# Manifest Schema

## Current Schema

HF Hub uses a single JSON manifest stored inside each package that depends on AI model files.

Recommended location:

- `Packages/<package-id>/hf-hub-manifest.json`

Manifest stays on `version: 1` and supports both whole-repo and selected-file dependencies.

```json
{
  "version": 1,
  "models": [
    {
      "id": "hf-internal-testing/tiny-random-bert",
      "revision": "main"
    },
    {
      "id": "onnx-community/tiny-clip-test-ONNX",
      "revision": "main",
      "files": [
        "onnx/model.onnx"
      ]
    }
  ]
}
```

Field intent:

- `version`: manifest format version
- `id`: canonical HuggingFace model id, also used as repo snapshot identity and runtime lookup key
- `revision`: branch, tag, or commit to resolve from HuggingFace
- `files`: optional list of exact repo-relative file paths required by this dependency owner; when omitted, dependency scope is whole repo

Canonical HuggingFace model id definition:

- exact upstream HuggingFace `repo_id` string, usually `owner/name`
- store it unchanged in manifests, metadata, and runtime registries
- only serialize or sanitize it when deriving local folder names or paths
- do not invent separate display ids, package-local aliases, or duplicate `repository` fields for same identity

Manifest meaning:

- one entry resolves one upstream repo revision to one concrete commit
- one entry either depends on whole repo or on explicit repo-relative file paths inside that repo
- ONNX conversion and runtime materialization operate only on selected `.onnx` files for that dependency scope

File selection uses exact repo-relative paths first. Pattern-based authoring can layer on later if needed, but manifest truth is still concrete file selection.

## Selection Boundary

Important constraint:

- manifest-owned file selection describes what one dependency owner requires from one repo
- it does not redefine raw cache identity
- it does not mean raw cache metadata should only list selected files
- `files` is owner dependency scope, not cache inventory

Simple maintainer translation:

- manifest says what owner needs
- raw cache says what source files are already present locally
- selected-file dependency does not force HF Hub to pretend repo snapshot only contains selected files

## Reference Alignment

This schema matches upstream HuggingFace concepts:

- `hf_hub_download(...)` handles one file
- `snapshot_download(...)` handles whole-repo or filtered multi-file snapshots
- `snapshot_download(...)` supports `allow_patterns` and `ignore_patterns` for partial repo download
- it resolves requested revision to concrete commit, stores `refs/<revision> -> <commit>`, and downloads files into `snapshots/<commit>/...`
- when sibling listing is unreliable, it falls back from repo info to `list_repo_tree(...)` before filtering files

HF Hub keeps manifest identity close to upstream:

- `id` is repo id
- `revision` is resolution input
- `files` narrows dependency scope inside repo when needed
