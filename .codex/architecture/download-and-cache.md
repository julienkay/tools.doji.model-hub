# Download And Cache

## Cache Location

V1 uses shared per-user cache outside Unity project so raw HuggingFace repository snapshots can be reused across projects.

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

Revision ref maps requested revision to resolved commit. Snapshot metadata records whole repo file list for that commit.

Raw cache is not the same lifecycle as imported project assets. If a user removes cache from Model Hub, HF Hub may delete the selected revision ref and the snapshot only when no other revision ref still points at that resolved commit. Generated project assets can remain valid after cache removal and should not be treated as automatically missing.

## Download Model

Dependency declaration is repo-level.

Download flow:

1. Resolve `repository + revision` through HuggingFace model revision API.
2. Read resolved commit hash and `siblings` file list.
3. Download every sibling file into raw snapshot cache.
4. Write one repo-level metadata file once snapshot is complete.

This matches upstream snapshot semantics better than file-by-file dependency declarations.

## Reliability

Download path remains first-class infrastructure:

- background execution through Unity editor task/progress APIs
- resumable file transfers
- persisted sync queue state across domain reload
- file locking around snapshot writes

Each file download can resume independently. Snapshot lock protects repo metadata write and duplicate snapshot work.
