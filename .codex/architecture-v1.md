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

## Package Boundary

Current prototype proved one important boundary: generic resumable download infrastructure should not stay embedded inside `com.doji.hf-hub`.

Target shape:

- one generic downloader package with no HuggingFace or Sentis knowledge
- one HuggingFace Hub package that consumes that downloader package

Why:

- resumable file transfer, persisted jobs, and recovery are useful beyond AI model imports
- those concerns must eventually work at runtime too
- editor-only UX such as prompts and Project Settings should not define core download architecture
- HuggingFace resolution and `.sentis` import are separate concerns from URL-to-file transfer

Recommended first extraction package:

- `com.doji.downloads`

Keep it in same repository first. Separate repository can wait until API shape proves stable.

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

## Download Reliability

The download path is the core workflow of this package and must be engineered as first-class infrastructure.

V1 architecture targets three explicit guarantees:

1. Downloads run in the background using Unity editor task/progress APIs.
2. Downloads are resumable instead of restarting from zero.
3. Active sync work is not lost on domain reload.

How V1 tackles each guarantee:

- Background execution:
  - sync must not block the editor thread with `.GetAwaiter().GetResult()` style orchestration
  - editor-triggered sync runs through a dedicated background sync manager
  - user-visible state is surfaced through Unity `Progress` API rather than modal blocking UI
  - menu, Project Settings, and prompts all start the same background sync path

- Resumable downloads:
  - raw target uses a sibling partial file such as `<file>.downloading`
  - if that partial file exists, the next download attempt resumes from its current byte length
  - resume is implemented with HTTP `Range` requests
  - if the server ignores `Range` and returns full content, the client detects that and restarts cleanly once to avoid file corruption
  - this behavior follows the same general strategy used by `huggingface_hub` in `file_download.py`

- Domain reload resilience:
  - active sync state is persisted under `Library/Doji/HfHub/sync-state.json`
  - persisted state stores which dependency is currently active and which declared dependencies remain pending
  - after domain reload or editor script reload, the background sync manager checks for that persisted state and resumes remaining work
  - raw download partial files remain on disk, so resumed sync can continue transfer from previous byte position where the server supports it

This means the architecture deliberately separates:

- shared raw cache state
- generated project artifact state
- active sync job state

Those are different concerns and must remain independently recoverable.

It also separates package responsibilities:

- downloader package owns transfer/job durability
- HF Hub package owns repository resolution, cache policy, and model import

### Downloader Package Responsibilities

Downloader package should own:

- runtime-safe download core with no `UnityEditor` dependency
- persisted job state
- partial file conventions such as `.downloading` or `.part`
- resume semantics using HTTP `Range`
- file locking and duplicate-work prevention
- transport abstraction
- progress and cancel contracts
- retry/backoff policy
- integrity checks once download completes

Optional editor-specific adapter layer may live there too, but only for:

- `Progress` integration
- assembly/domain reload hooks
- editor job restoration hooks

### HF Hub Responsibilities

`com.doji.hf-hub` should keep owning:

- package manifest discovery
- HuggingFace repo/revision/file resolution
- HuggingFace-specific cache layout and sidecar metadata
- package/user-facing prompts and status surfaces
- project import pipeline from raw ONNX to `.sentis`
- stale detection and generated asset metadata
- runtime model locator contract

### Migration Bias

Do not generalize too far too early.

First extraction should support only what current project needs:

- URL to local file
- resume
- persisted state
- cancel
- recovery after editor/domain reload
- editor/runtime-safe core contracts

Avoid designing giant universal downloader API before real second use case exists.

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

The expensive content step must still be non-blocking:

- approval is explicit
- execution is backgrounded
- progress is visible
- interruption should be recoverable

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

## Planned Extraction Order

1. Extract generic transport/result/progress contracts into downloader package.
2. Extract partial-file handling, locks, and persisted job-state model.
3. Extract resumable file runner with no HuggingFace references.
4. Replace HF Hub background sync internals with adapter over downloader package.
5. Re-test reload recovery, progress reporting, and resume behavior through HF Hub.
