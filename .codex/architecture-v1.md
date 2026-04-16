# Architecture Note: Vertical Slice V1

## Scope

This first slice targets Unity `6000.3.10f1` with Sentis `2.6.1`.

The goal is a narrow end-to-end path:

1. Let a UPM package declare one or more HuggingFace ONNX model dependencies in a package-owned manifest.
2. Detect those manifests when packages are present in a Unity project.
3. Prompt the user in the Unity Editor to download and import the required model files.
4. Download the raw ONNX files into a shared machine-local cache.
5. Convert them into project-owned `.sentis` assets for use by the package inside the current project.

Out of scope for v1:

- HuggingFace authentication
- gated models
- offline workflows beyond reusing already downloaded raw cache data

## Manifest Schema

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
- `id`: stable package-local identifier used for generated paths and metadata
- `displayName`: user-facing name shown in download prompts
- `repository`: HuggingFace repo id
- `revision`: branch, tag, or commit to resolve from HuggingFace
- `sourceFile`: ONNX file path inside the repo

V1 deliberately supports one source file per model entry. Multi-file models and richer import settings can come later.

Important next extension:

- support package dependencies that require multiple files from one repo, not only one ONNX payload
- this should cover companion files such as `config.json`, tokenizer files, vocab files, merges files, preprocess metadata, and similar artifacts

Reference behavior from `huggingface_hub`:

- `hf_hub_download(...)` handles one file
- `snapshot_download(...)` handles whole-repo or filtered multi-file snapshots
- `snapshot_download(...)` supports `allow_patterns` and `ignore_patterns` for partial repo download
- it resolves requested revision to concrete commit, stores `refs/<revision> -> <commit>`, and downloads files into `snapshots/<commit>/...`
- when sibling listing is unreliable, it falls back from repo info to `list_repo_tree(...)` before filtering files

Generated asset paths are not authored in the manifest. They are derived by the Hub:

```text
Assets/Doji/HfHub/Generated/<package-id>/<model-id>/
```

This keeps package manifests declarative and avoids package authors hard-coding project-specific paths.

## Cache Location

V1 uses a shared per-user cache outside the Unity project so raw downloads can be reused across projects.

Cache root:

- macOS: `~/Library/Application Support/Doji/HfHub`
- Windows: `%LocalAppData%/Doji/HfHub`
- Linux: `~/.cache/doji/hf-hub`

Raw ONNX path shape:

```text
<cache-root>/raw/<repository>/<revision>/<sourceFile>
```

Downloaded files should only be keyed by Hub identity, not by project.

## Conversion Trigger

V1 conversion is editor-driven and package-discovery-based.

Primary trigger:

- package manifests are discovered when the Editor detects package changes or opens a project

Behavior:

1. Scan installed packages for `hf-hub-manifest.json`.
2. Collect unresolved or stale model dependencies declared by those package manifests.
3. Show the user a prompt listing the package and model dependencies that need attention.
4. If the user approves, resolve each model entry to a cached raw ONNX file, downloading it if missing.
5. Convert missing or stale models using the currently installed Sentis version.
6. Write a small metadata file next to each generated asset so later scans can detect staleness cheaply.

The key behavior is automatic prompting, not silent automatic import. The Hub should behave more like Unity Package Manager: package dependencies are discovered automatically, but the expensive content step is made explicit to the user.

Fallback trigger:

- `Tools/Doji/HF Hub/Sync Models`

The menu command remains available to rescan package manifests and retry downloads or conversion after a user initially postpones the prompt.

## Sentis Version Invalidation

Each generated model gets a sidecar metadata file in its derived output folder.

Minimum metadata:

```json
{
  "manifestVersion": 1,
  "packageId": "com.example.ai-feature",
  "modelId": "distilbert-base-uncased",
  "repository": "hf-internal-testing/tiny-random-bert",
  "revision": "main",
  "sourceFile": "model.onnx",
  "manifestHash": "<computed-hash>",
  "sentisVersion": "2.6.1",
  "unityVersion": "6000.3.10f1"
}
```

During `Sync Models`, a generated `.sentis` asset is stale and must be rebuilt if any of these change:

- package manifest content hash
- installed Sentis version
- generated asset or metadata file missing

The manifest hash covers dependency identity changes such as `repository`, `revision`, or `sourceFile` without maintaining a larger invalidation matrix.

Unity version is recorded for diagnostics, but Sentis version is the primary rebuild key beyond manifest changes.

## First Implementation Bias

Keep the first implementation simple:

- one manifest file per package
- package discovery plus one confirmation prompt
- one fallback sync command
- JSON metadata, not a database
- project-owned generated `.sentis` assets
- no silent background conversion
- no auth flow
