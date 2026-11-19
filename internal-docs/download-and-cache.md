# Download And Cache

## Cache Location

AI Model Hub uses the Hugging Face cache of the `huggingface_hub` Python library, so Unity projects and Python tools reuse each other's downloads (see `VISION.md`).

Cache root, resolved like `huggingface_hub` (`CachePaths.GetDefaultCacheRoot`):

1. `HF_HUB_CACHE`
2. `HUGGINGFACE_HUB_CACHE` (legacy name)
3. `HF_HOME/hub`
4. `$XDG_CACHE_HOME/huggingface/hub`
5. `~/.cache/huggingface/hub` (also on Windows: `%USERPROFILE%\.cache\huggingface\hub`)

In the Editor, a custom folder in `Preferences > AI Model Hub` replaces the default (`CacheFolderSettings`, stored per user in `EditorPrefs`, hooked in through `CachePaths.CacheRootOverride`). It applies at once and to every project of the user. Players never read it.

`CachePaths.GetCacheRootSource` and `GetDataRootSource` say what decided each folder: `editor_setting`, the environment variable, or `default`. `CacheFolders.Describe` adds the free space of each drive (`IDiskSpace`) and the stale variables.

Stale variables (Windows): Unity Hub passes the environment it started with to every Editor it launches, so a variable changed after Unity Hub started never reaches the Editor. `CacheFolders.FindStaleVariables` compares each folder variable in the process with the value new programs get (user value, else machine value; `IEnvironmentVariables`, faked in tests). The window, Preferences, Project Settings, and `model_hub_cache_info` show the difference. macOS and Linux cannot read that value, so the check reports nothing there.

Changing a folder (`CacheFolderChange.Apply`) offers to move the existing content (`CacheFolderMover`): on the same drive it renames, across drives it checks the free space, copies, checks every file's size, and deletes the source only after the user confirms. A copy recreates each snapshot symlink as a relative link to the copied blob, or copies the content where the new drive allows no symlinks. `.locks` is not copied. A file the destination already has is kept. Syncs do not start during a move. The setting can also set `HF_HOME` (the parent of a folder named `hub`) or else `HF_HUB_CACHE` for the user, after asking, so Python follows.

Before downloading, `RawCacheService` checks that the missing files fit on the cache drive and, for a full sync, that the converted models fit on the data drive (about the ONNX size with external weights). `ProjectImportService.ImportAsync` checks the pending conversions again. Requirements on one drive add up; a sync that does not fit fails with `InsufficientDiskSpaceException`, which names the needed and free size per drive.

Layout, as in `huggingface_hub`:

```text
<cache-root>/models--<owner>--<name>/refs/<revision>           (nested for revisions with "/", e.g. refs/pr/1)
<cache-root>/models--<owner>--<name>/snapshots/<commit>/<repo-file>   (relative symlink to ../../blobs/<etag>, or a plain file)
<cache-root>/models--<owner>--<name>/blobs/<etag>
<cache-root>/.locks/models--<owner>--<name>/<etag>.lock
```

Repository ids are not case-sensitive on the Hub. A download takes the Hub's spelling of the id from the revision API (`id`), as `huggingface_hub` does, so `julienkay/sentis-midas` is stored as `models--julienkay--sentis-MiDaS`. `CachePaths.ResolveRepositoryFolderName` reuses an existing repository folder whose name differs only in case, for the repository root, blob locks, and the index, so manifests with any spelling find the same folder on case-sensitive file systems.

Files are stored like `_hf_hub_download_to_cache_dir` and `_create_symlink` in `huggingface_hub` (`CacheBlobs.StoreAsync`, shared with players, and `CacheFiles`):

1. The etag names the blob: the sha256 for LFS files, the git sha1 for small files. `huggingface_hub` reads it from `X-Linked-Etag` on a HEAD request per file. AI Model Hub reads the same etag and size from the tree listing of the resolved commit (`lfs.oid`, else `oid`), so it sends no HEAD request. HEAD is only a fallback for entries without them; `UnityWebRequest` follows its redirect to the CDN, whose `ETag` is a xet hash, so only `X-Linked-*` headers are trusted from it.
   Files download from `/resolve/<commit>/<path>`. The downloader follows the redirect to a signed CDN URL on each attempt, so its resume state keeps a URL that never expires.
2. A complete blob already in the cache, from another revision or from Python, is reused without a download.
3. Otherwise the file downloads to `blobs/<etag>`, with partial data in `<etag>.downloading` so an interrupted download resumes. Upstream downloads to a process-unique `<etag>.<id>.incomplete` and never resumes, so the two never share partial files.
4. The snapshot path becomes a relative symlink to the blob. `CacheFiles.AreSymlinksSupported` probes each cache folder once, like `are_symlinks_supported`.
5. Without symlinks (Windows without Developer Mode), a new blob moves into the snapshot and an existing blob is copied, exactly like upstream. AI Model Hub logs this once.
6. Files without an etag download straight into the snapshot.

On Windows a symlink reports 0 bytes, so sizes go through `CacheFiles.GetLength`, which opens the file and follows the link.

Each blob download and link runs under `BlobLock` on `.locks/models--<owner>--<name>/<etag>.lock`, the lock file `huggingface_hub` uses, so Unity and Python never write one blob at the same time. `BlobLock` matches the `filelock` package:

- Windows: open with read, write, and delete sharing, then lock byte 0, length 1 (`FileStream.Lock`, the same region as `msvcrt.locking(fd, LK_NBLCK, 1)`).
- macOS and Linux: `flock(LOCK_EX | LOCK_NB)` through P/Invoke. .NET's `FileStream.Lock` uses `fcntl` record locks there, which do not exclude `flock`.

A waiting download polls every 100 ms, logs once that it waits for another program, and stops when the sync is cancelled. The lock file is not deleted after release; `filelock` handles existing lock files.

The Windows path was checked against a Python process that locks like `filelock` (`os.open` plus `msvcrt.locking`), in both directions. The `flock` path is not yet checked on macOS or Linux.

The per-snapshot index lock (`<data-root>/index/.../<commit>.json.lock`) is AI Model Hub's own and only guards the index entry.

Remove from Cache works like `delete_revisions` in `huggingface_hub` (`ProjectRemovalService.RemoveRawCache`):

1. Delete the requested revision ref. Stop here when another ref still points at the same commit.
2. Read which blobs the snapshot's links point to (`CacheFiles.TryGetLinkTarget`: `GetFinalPathNameByHandleW` on Windows, `readlink` elsewhere). Plain-file snapshots point to none.
3. Delete the snapshot folder and its index entry.
4. Delete those blobs that no remaining snapshot links to, including snapshots that Python tools created.
5. Delete empty `refs`, `snapshots`, `blobs`, and repository folders.

Other files in `blobs/`, such as another program's partial `.incomplete` download, are never deleted.

AI Model Hub's own bookkeeping never goes into the cache. `huggingface_hub` reports every unknown folder in the cache root as a corrupted entry. The index lives in AI Model Hub's data folder (`CachePaths.GetDataRoot`):

- Windows: `%LocalAppData%/Doji/ModelHub`
- macOS: `~/Library/Application Support/Doji/ModelHub`
- Linux: `$XDG_CACHE_HOME/doji/model-hub` or `~/.cache/doji/model-hub`

```text
<data-root>/index/models--<owner>--<name>/<commit>.json
<data-root>/index/models--<owner>--<name>/<commit>.json.lock
```

Data root: the Editor setting, then `DOJI_MODEL_HUB_DATA_ROOT`, then the per-user default. Tests use `TestCacheScope`, which points `HF_HUB_CACHE` and `DOJI_MODEL_HUB_DATA_ROOT` at temporary folders.

The index entry records the repository file list, file etags, and the model card's license fields for one commit. It is a hint, never the truth:

- `RawCacheService.ReadSnapshot` builds every snapshot view from disk: the files in `snapshots/<commit>` count as cached, whoever wrote them (AI Model Hub or Python), with sizes read through links. Status, the sync, import, and Model Hub all use this view.
- The index only adds the repository file list and etags, and only when it describes the same repository (ignoring letter case) and commit. A missing, unreadable, or foreign index is ignored.
- A file-selective dependency needs no index: its selected files are either on disk or not.
- A whole-repository dependency needs the file list to know what "every file" is. Without it, status reports `RawCacheFilesMissing` ("Update needed"). The update resolves the file list, downloads nothing that is already on disk with the right size, and writes a new index entry.

Project-local remote inventory metadata may also exist before any download. That pre-download inventory supports optional file selection and size estimates; it is not cache identity.

Current implementation notes:

- the index entry still stores the requested revision; ref files are authoritative for revision-to-commit mapping, and `ReadSnapshot` ignores the stored revision
- index entries describe one `repository + resolved commit`, not owner dependency scope

The download cache and the converted store have separate lifecycles. If a user removes cache from Model Hub, AI Model Hub deletes the revision ref, and deletes the snapshot and its index entry only when no other ref still points at that commit. Converted models stay valid after cache removal. A `.sentis` dependency uses the cached file itself, so removing the cache makes it need a download again.

Players download with the same layout, blob names, and locks; see [runtime-loading.md](runtime-loading.md#downloads).

## Selection Boundary

AI Model Hub keeps two different truths separate:

- the cache index answers: which repo files are currently cached on this machine for this resolved commit
- dependency selection answers: which repo files one manifest owner requires

Those are not interchangeable.

Why:

- many owners can depend on same repo and same resolved commit while selecting different subsets
- the download cache may accumulate union of files requested across owners, reimports, or earlier sessions
- cached extra files do not mean current owner selected them
- uncached files do not mean current owner would never select them later

Design rule:

- cache identity stays `repository + resolved commit`
- the cache index records cached file inventory for that snapshot
- owner selection must live in manifest/discovery and later owner-binding or import metadata, not in the cache index
- stale detection and UI must never infer owner dependency scope by reading only snapshot cache metadata

## Download Model

Dependency declaration is repo-scoped, with optional file-level narrowing inside repo.

## Simple Rule

In simple terms:

- upstream `huggingface_hub` can download only one file, only selected files, or whole repo
- AI Model Hub ensures only dependency-required files are downloaded into cache for that request

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
- if no optional model file is enabled and no required model file exists, AI Model Hub does not download the whole repository

Selection rule:

- if one owner depends on specific repo files, AI Model Hub ensures only those files are needed for that request
- cache still remains repo-snapshot-scoped, not owner-selection-scoped
- the cache index answers "what files are cached for this repo commit"
- owner metadata answers "what files this dependency selected"

Pre-download selection UX should use remote repo inventory metadata rather than waiting for the download cache to exist. Selection belongs before download.

## Reliability

Download path remains first-class infrastructure:

- background execution through Unity editor task/progress APIs
- resumable file transfers
- persisted sync queue state across domain reload
- file locking around snapshot writes

Each file download can resume independently. Snapshot lock protects repo metadata write and duplicate snapshot work.

## Damaged Cache

Only damage on disk counts:

| Code | Meaning |
|---|---|
| `RawCacheRevisionRefUnreadable` | Revision ref file exists but cannot be read |

`DependencyStatus.DamagedCacheCodes` names this set. Model Hub shows these models as `Needs repair` with a **Repair** button and the download size; the setup prompt counts them as downloads. The fix is a normal sync.

AI Model Hub's index is never damage, because it is a hint (see Cache Location). The former `RawCacheMetadataInvalid`, `RawCacheMetadataMismatch`, and `RawCacheMetadataMissingAfterImport` codes described index problems and were removed when status started reading the cache from disk.

AI Model Hub never repairs on its own. An earlier auto-heal started download-only syncs at editor startup and on every Model Hub refresh, and with damaged bookkeeping it downloaded every selected file again, for whole-repository dependencies the whole repository, without asking. That broke the product rule to ask the user once before downloading.

Rules:

- turn 401 and 403 into `HubAccess` messages: a denied file download means the model is gated or private; a denied or missing revision lookup means no public repository has that id and revision (the Hub answers 401 for missing repositories too). AI Model Hub sends no token
- start downloads only from an explicit user action: the setup prompt, a Model Hub button, the toolbar, the menu, or a `model_hub_sync` command
- `BackgroundSyncManager.ResumeIfNeeded` only resumes a persisted queue the user already started

The index repository check ignores letter case. Hugging Face repository ids are case-insensitive, and the cache folder is case-insensitive on Windows and macOS, so `owner/Model` and `owner/model` share one snapshot.

`RawCacheFilesMissing` is not damage: the selection grew after the first download, or the file list of a whole-repository dependency is unknown. It shows as `Update needed`.
