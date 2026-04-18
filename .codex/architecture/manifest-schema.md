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
      "id": "distilbert-base-uncased",
      "displayName": "Tiny Random BERT",
      "repository": "hf-internal-testing/tiny-random-bert",
      "revision": "main",
      "sourceFile": "model.onnx"
    }
  ]
}
```

Field intent:

- `version`: manifest format version
- `id`: stable package-local alias used for generated paths and package-facing lookup
- `repository`: HuggingFace model id / repo id used as upstream source identity
- `displayName`: user-facing name shown in download prompts
- `repository`: HuggingFace repo id
- `revision`: branch, tag, or commit to resolve from HuggingFace
- `sourceFile`: ONNX file path inside the repo

V1 deliberately supports one source file per model entry. Multi-file models and richer import settings can come later.

## Important Next Extension

- support package dependencies that require multiple files from one repo, not only one ONNX payload
- this should cover companion files such as `config.json`, tokenizer files, vocab files, merges files, preprocess metadata, and similar artifacts

Reference behavior from `huggingface_hub`:

- `hf_hub_download(...)` handles one file
- `snapshot_download(...)` handles whole-repo or filtered multi-file snapshots
- `snapshot_download(...)` supports `allow_patterns` and `ignore_patterns` for partial repo download
- it resolves requested revision to concrete commit, stores `refs/<revision> -> <commit>`, and downloads files into `snapshots/<commit>/...`
- when sibling listing is unreliable, it falls back from repo info to `list_repo_tree(...)` before filtering files
