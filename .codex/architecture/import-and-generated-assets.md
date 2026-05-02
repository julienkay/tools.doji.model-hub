# Import And Generated Assets

## Conversion Trigger

Conversion is editor-driven and package-discovery-based.

Primary trigger:

- package manifests are discovered when Editor detects package changes or opens a project

Behavior:

1. Scan installed packages for `hf-hub-manifest.json`.
2. Collect unresolved or stale repository dependencies.
3. Show user prompt listing repository dependencies that need attention.
4. If user approves, resolve each dependency to one concrete HuggingFace repository snapshot.
5. Download dependency-required snapshot files into shared raw cache.
6. Convert selected cached `.onnx` files in that dependency scope.
7. Write generated `.sentis` runtime files into shared `StreamingAssets` root.
8. Write shared import metadata under `Library/Doji/HfHub/imports/shared/...`.
9. Write owner binding metadata under `Library/Doji/HfHub/imports/bindings/...`.

## Generated Layout

Generated runtime payloads live under:

```text
Assets/StreamingAssets/hf-hub/<repo-owner>/<repo-name>/
```

Current generated output includes:

- `<onnx-file>.sentis`
- `Assets/StreamingAssets/hf-hub/registries/<package-id>/hf-hub-runtime.json`
- `Library/Doji/HfHub/imports/shared/<repo>/<commit>/<sentis-version>/hf-hub-shared-import.json`
- `Library/Doji/HfHub/imports/bindings/<package-id>/<model-id>/hf-hub-binding.json`

Raw repository files stay in shared cache. Runtime output preserves upstream relative file layout for selected ONNX inputs, replacing ONNX extensions with `.sentis`.

Runtime import paths are intentionally human-readable and do not encode resolved commit or revision.

Current policy:

- repo-scoped runtime layout stays stable and human-readable
- HF Hub does not support multiple requested revisions of same repository in one Unity project
- if discovery finds same repository at different requested revisions, project must resolve that conflict before HF Hub writes or rewrites generated runtime payload for that repo

Conflict behavior:

- conflicting repo must not report `Ready`
- conflicting repo must not import, re-import, or remove shared runtime payload through normal Model Hub actions until conflict is resolved
- existing generated runtime files may remain on disk for inspection, but they are not treated as authoritative while conflict exists

## Import Metadata

Import storage is split in two:

- shared import metadata is repo-snapshot-level
- owner binding metadata is owner-dependency-level

Shared import metadata records:

- repository and resolved commit
- raw snapshot root and raw metadata path
- persisted import warnings when conversion is partial but still usable
- generated runtime snapshot path
- generated runtime file list
- Sentis version
- Unity version

Owner binding metadata records:

- manifest owner id and version
- dependency id
- manifest hash
- repository and requested revision
- resolved commit
- generated runtime registry path
- pointer to shared import metadata
- selected dependency file set or equivalent selection fingerprint

Owner-scoped file selection belongs with owner binding state and stale checks, not with raw cache metadata. Raw cache describes available source files for one repo snapshot; owner binding describes what this owner expects to use from that snapshot.

## Stale Detection

Owner binding is stale when any of these change:

- manifest content hash
- requested revision
- selected dependency file set
- resolved repo commit for requested revision
- installed Sentis version
- shared import metadata pointer

Shared import is stale when any of these change:

- resolved repo commit for requested revision
- raw snapshot metadata path or root
- generated runtime files missing
- installed Sentis version

Because shared import metadata stores generated runtime file list for one repo snapshot materialization, validation stays snapshot-aware instead of one-file-at-a-time while owner binding still preserves dependency scope.

## Shared Import Split

Why split:

- generated `.sentis` artifacts are repo-scoped and should convert once per repo snapshot
- stale checks and runtime registry ownership are package-scoped and must remain owner-specific

Current rule:

- shared import key = `repository + resolvedCommitHash + SentisVersion`
- owner binding key = `packageId + modelId`

That lets multiple owners reuse one generated repo artifact while keeping per-owner manifest-hash validation and per-owner runtime locator registries.
- project manifest assets use stable per-asset owner ids, so repeated project manifests can share repo artifacts without collapsing binding identity
