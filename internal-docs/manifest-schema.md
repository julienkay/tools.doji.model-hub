# Manifest Schema

## Current Schema

HF Hub uses a single JSON manifest stored inside each package that depends on AI model files.

Recommended location:

- `Packages/<package-id>/hf-hub-manifest.json`

Manifest stays on `version: 1` and supports:

- whole-repo dependencies
- required explicit-file dependencies
- package-authored optional exact file paths inside one repo dependency

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
        {
          "path": "onnx/model.onnx",
          "optional": false
        }
      ]
    },
    {
      "id": "julienkay/sentis-MiDaS",
      "revision": "main",
      "files": [
        {
          "path": "onnx/midas_v21_small_256.onnx",
          "optional": true
        },
        {
          "path": "onnx/dpt_swin2_tiny_256.onnx",
          "optional": true
        }
      ]
    }
  ]
}
```

Field intent:

- `version`: manifest format version
- `id`: canonical HuggingFace model id, also used as repo snapshot identity and runtime lookup key
- `revision`: branch, tag, or commit to resolve from HuggingFace
- `files`: optional list of exact repo-relative file objects. Omit or leave empty for whole-repository scope.
- `files[].path`: exact repo-relative file path selected by this dependency owner
- `files[].optional`: whether the path is project-selectable instead of always required; default/omitted means `false`

Canonical HuggingFace model id definition:

- exact upstream HuggingFace `repo_id` string, usually `owner/name`
- store it unchanged in manifests, metadata, and runtime registries
- only serialize or sanitize it when deriving local folder names or paths
- do not invent separate display ids, package-local aliases, or duplicate `repository` fields for same identity

Manifest meaning:

- one entry resolves one upstream repo revision to one concrete commit
- one entry owns one canonical repo id for one manifest owner
- duplicate `id` values inside one manifest are invalid
- unknown properties at root, model, or file level are invalid, and a non-boolean `optional` is invalid; a silently ignored key can widen a file-selective entry into a whole-repo download. The legacy `optionalFiles` key gets an error with a migration hint
- optional model variants must not be modeled as duplicate manifest rows with same `id`
- one entry may depend on whole repo, on explicit always-required files, or on required files plus optional files inside that repo
- the extension decides each selected file's format: `.onnx` is converted, `.sentis` is used as downloaded; see [import-and-generated-assets.md](import-and-generated-assets.md)
- a whole-repository dependency on a repository with both formats is invalid, so HF Hub never picks a format
- selecting an `.onnx` file adds its external weight files (`ModelFiles.GetCompanionFiles`)

File selection uses exact repo-relative paths first. Pattern-based authoring can layer on later if needed, but manifest truth is still concrete file selection.

## Optional Files

Optional files solve cases like MiDaS where one upstream repo contains several interchangeable ONNX variants and package authors do not want to force every project to download or import all of them.

Rules:

- optional file paths use same exact-path normalization as required file paths
- a path appears once in `files`; its `optional` flag carries required vs optional intent
- required file rows (`optional: false`) remain always-required and are not controlled by user toggles
- project-owned selection data records enabled optional file paths per owner dependency

## Effective Selection

Manifest no longer tells whole truth by itself once optional file rows exist.

Effective owner selection is:

1. all `files` entries where `optional` is false
2. plus every optional file enabled for that owner in current Unity project
3. or whole-repo inventory when `files` is omitted or empty

Project-owned selection overrides must stay outside package manifest so installed package manifests can remain immutable.

Selection storage rule:

- package manifest declares available selected files and which selected files are optional
- project-owned override data records enabled optional file paths per owner dependency
- status, download, import, and runtime registry logic all reason over effective selection, not raw manifest text alone

If the effective selection is empty because every model file is optional and none is enabled, status is `SelectionRequired` (**Choose files**), not an error, and HF Hub downloads nothing. A selection without any `.onnx` or `.sentis` file is `InvalidSelection`.

## Selection Boundary

Important constraint:

- manifest-owned file selection describes what one dependency owner requires from one repo
- it does not redefine the download cache identity
- it does not mean the cache index should only list selected files
- `files` is owner dependency scope, not cache inventory

Simple maintainer translation:

- manifest says what owner needs
- the download cache says what source files are already present locally
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
- `files` narrows dependency scope inside repo when needed, with `optional` carrying project-selectable exact repo paths
