# Download And Cache

## Cache Location

use shared per-user cache outside Unity project so raw HuggingFace repository snapshots can be reused across projects.

Cache root:

- macOS: `~/Library/Application Support/Doji/HfHub`
- Windows: `%LocalAppData%/Doji/HfHub`
- Linux: `~/.cache/doji/hf-hub`

Current raw-cache layout:

```text
<cache-root>/raw/<serialized-repo>/refs/<revision>
<cache-root>/raw/<serialized-repo>/snapshots/<commit>/<repo-file>
<cache-root>/raw/<serialized-repo>/snapshots/<commit>/snapshot.hfhub.json
```

Revision ref maps requested revision to resolved commit. Snapshot metadata records cached file inventory for that commit.

Project-local remote inventory metadata may also exist before any raw-cache download. That pre-download inventory is editor UX support for optional file selection and size estimates, not raw-cache identity.

Current implementation note:

- snapshot metadata currently also stores requested revision even though ref files are authoritative for revision-to-commit mapping; treat that revision field as implementation detail, not stable cache identity
- raw cache metadata is cache inventory for one `repository + resolved commit`, not owner dependency scope

Raw cache is not the same lifecycle as imported project assets. If a user removes cache from Model Hub, HF Hub may delete the selected revision ref and the snapshot only when no other revision ref still points at that resolved commit. Generated project assets can remain valid after cache removal and should not be treated as automatically missing.

## Selection Boundary

HF Hub keeps two different truths separate:

- raw cache metadata answers: which repo files are currently cached on this machine for this resolved commit
- dependency selection answers: which repo files one manifest owner requires

Those are not interchangeable.

Why:

- many owners can depend on same repo and same resolved commit while selecting different subsets
- raw cache may accumulate union of files requested across owners, reimports, or earlier sessions
- cached extra files do not mean current owner selected them
- uncached files do not mean current owner would never select them later

Design rule:

- raw cache identity stays `repository + resolved commit`
- raw cache metadata records cached file inventory for that snapshot
- owner selection must live in manifest/discovery and later owner-binding or import metadata, not in raw cache metadata
- stale detection and UI must never infer owner dependency scope by reading only snapshot cache metadata

## Download Model

Dependency declaration is repo-scoped, with optional file-level narrowing inside repo.

## Simple Rule

In simple terms:

- upstream `huggingface_hub` can download only one file, only selected files, or whole repo
- HF Hub ensures only dependency-required files are downloaded into cache for that request

Upstream mode depends on API used:

- `hf_hub_download(...)`: one requested repo file only
- `snapshot_download(...)` without filters: whole repo snapshot
- `snapshot_download(...)` with `allow_patterns` / `ignore_patterns`: only matched files

Important cache detail from upstream:

- even single-file downloads still live in repo-scoped cache
- upstream does not create separate cache identity for "selection"
- practical identity is still `repo + resolved commit + repo-relative path`

That means "what was requested by one caller" and "what files are now present in cache for this repo snapshot" are related but not same thing.

Download flow:

1. Resolve `repository + revision` through HuggingFace model revision API.
2. Enumerate repo files through repo info `siblings` or `list_repo_tree(...)` fallback when needed.
3. Compute effective dependency file set:
   - all repo files for whole-repo dependency
   - selected explicit repo paths for selected-file dependency
   - required files plus enabled optional files for optional-selection dependency
4. Download missing files from that effective set into raw snapshot cache.
5. Write one repo-level metadata file describing cached files now present for that snapshot.

This keeps cache repo-scoped while still supporting exact-file dependency selection.

Optional-file selection rules:

- package authors declare available optional files in manifest
- project stores enabled optional file paths per owner dependency
- download only respects effective selection after project overrides are applied
- if no optional ONNX file is enabled and no required ONNX exists, HF Hub should not download whole repo by default

Selection rule:

- if one owner depends on specific repo files, HF Hub ensures only those files are needed for that request
- cache still remains repo-snapshot-scoped, not owner-selection-scoped
- raw cache metadata answers "what files are cached for this repo commit"
- owner metadata answers "what files this dependency selected"

Pre-download selection UX should use remote repo inventory metadata rather than waiting for raw cache to exist. Selection belongs before download.

## Reliability

Download path remains first-class infrastructure:

- background execution through Unity editor task/progress APIs
- resumable file transfers
- persisted sync queue state across domain reload
- file locking around snapshot writes

Each file download can resume independently. Snapshot lock protects repo metadata write and duplicate snapshot work.

## Auto-Heal for Cache Corruption

Some cache states indicate genuine on-disk corruption rather than a missing download:

| Code | Meaning |
|---|---|
| `RawCacheMetadataInvalid` | Snapshot metadata JSON is absent or unparseable |
| `RawCacheMetadataMismatch` | Metadata repository or revision fields do not match declared dependency |
| `RawCacheRevisionRefUnreadable` | Revision ref file exists but cannot be read |

These are treated as auto-healable because `GetOrDownloadAsync` already resolves the correct state from the HuggingFace API and writes clean metadata on every download. No manual intervention by the user is needed.

Two automatic heal triggers fire using `DownloadOnly` mode so no re-import runs unless necessary:

1. **At editor startup** - `BackgroundSyncManager.ResumeIfNeeded` runs `HealCorruptionAtStartup` when no persisted sync queue exists. If any dependency has a corruption code, a scoped download-only sync starts immediately.
2. **On Model Hub window refresh** - `ModelHubWindow.Refresh` calls `StartHealCorruptionIfNeeded` after rebuilding the report. Only corruption-code dependencies are included.

`SyncStateChanged` events (fired on sync completion or failure) call `RefreshReport` instead of `Refresh` to avoid an infinite heal loop when corruption persists across retries.

`RawCacheFilesMissing` is intentionally excluded from auto-heal because it is a legitimate user-driven state (new file selection), not corruption. It surfaces the **Update + Import** button instead.

Likewise, optional-file `Selection Required` state is not corruption and must never auto-heal into a whole-repo download. It requires explicit project choice.
