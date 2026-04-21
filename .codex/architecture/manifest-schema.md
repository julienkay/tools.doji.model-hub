# Manifest Schema

## V1 Schema

V1 uses a single JSON manifest stored inside each package that depends on AI model files.

Recommended location:

- `Packages/<package-id>/hf-hub-manifest.json`

Initial schema:

```json
{
  "version": 1,
  "models": [
    {
      "id": "hf-internal-testing/tiny-random-bert",
      "revision": "main"
    }
  ]
}
```

Field intent:

- `version`: manifest format version
- `id`: canonical HuggingFace model id, also used as repo snapshot identity and runtime lookup key
- `revision`: branch, tag, or commit to resolve from HuggingFace

Canonical HuggingFace model id definition:

- exact upstream HuggingFace `repo_id` string, usually `owner/name`
- store it unchanged in manifests, metadata, and runtime registries
- only serialize or sanitize it when deriving local folder names or paths
- do not invent separate display ids, package-local aliases, or duplicate `repository` fields for same identity

V1 deliberately keeps manifest entry minimal. One entry resolves one remote repo snapshot, downloads full snapshot, then converts every ONNX file inside it. Multi-file filtering and richer import settings can come later.

## Important Next Extension

- support package dependencies that require multiple files from one repo, not only one ONNX payload
- this should cover companion files such as `config.json`, tokenizer files, vocab files, merges files, preprocess metadata, and similar artifacts

Reference behavior from `huggingface_hub`:

- `hf_hub_download(...)` handles one file
- `snapshot_download(...)` handles whole-repo or filtered multi-file snapshots
- `snapshot_download(...)` supports `allow_patterns` and `ignore_patterns` for partial repo download
- it resolves requested revision to concrete commit, stores `refs/<revision> -> <commit>`, and downloads files into `snapshots/<commit>/...`
- when sibling listing is unreliable, it falls back from repo info to `list_repo_tree(...)` before filtering files
