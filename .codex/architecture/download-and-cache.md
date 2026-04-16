# Download And Cache

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

## Downloader Package Responsibilities

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

## HF Hub Responsibilities

`com.doji.hf-hub` should keep owning:

- package manifest discovery
- HuggingFace repo/revision/file resolution
- HuggingFace-specific cache layout and sidecar metadata
- package/user-facing prompts and status surfaces
- project import pipeline from raw ONNX to `.sentis`
- stale detection and generated asset metadata
- runtime model locator contract

## Migration Bias

Do not generalize too far too early.

First extraction should support only what current project needs:

- URL to local file
- resume
- persisted state
- cancel
- recovery after editor/domain reload
- editor/runtime-safe core contracts

Avoid designing giant universal downloader API before a real second use case exists.
