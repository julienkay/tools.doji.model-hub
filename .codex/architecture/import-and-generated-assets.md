# Import And Generated Assets

## Conversion Trigger

V1 conversion is editor-driven and package-discovery-based.

Primary trigger:

- package manifests are discovered when Editor detects package changes or opens a project

Behavior:

1. Scan installed packages for `hf-hub-manifest.json`.
2. Collect unresolved or stale repository dependencies.
3. Show user prompt listing repository dependencies that need attention.
4. If user approves, resolve each dependency to one concrete HuggingFace repository snapshot.
5. Download all snapshot files into shared raw cache.
6. Convert every cached `.onnx` file in that snapshot.
7. Write generated `.sentis` runtime files into shared `StreamingAssets` root.
8. Write editor-only provenance metadata under `Library/Doji/HfHub/import-state/...`.

## Generated Layout

Generated runtime payloads live under:

```text
Assets/StreamingAssets/hf-hub/<repo-owner>/<repo-name>/
```

Current generated output includes:

- `<onnx-file>.sentis`
- `Assets/StreamingAssets/hf-hub/registries/<package-id>/hf-hub-runtime.json`
- `Library/Doji/HfHub/import-state/<package-id>/<model-id>/hf-hub-import.json`

Raw repository files stay in shared cache. Runtime output preserves upstream relative file layout, replacing ONNX extensions with `.sentis`.

Runtime import paths are intentionally human-readable and do not encode resolved commit or revision. Any future ambiguity or multi-revision conflict handling belongs in HF Hub UI/workflow policy, not in the generated runtime path layout.

## Import Metadata

Import metadata is repo-level, not file-level.

It records:

- manifest owner id and version
- dependency id and display name
- manifest hash
- repository, requested revision, resolved commit
- raw snapshot root and raw metadata path
- persisted import warnings when conversion is partial but still usable
- generated runtime registry path
- generated runtime file list
- Sentis version
- Unity version

## Stale Detection

Repo import is stale when any of these change:

- manifest content hash
- resolved repo commit for requested revision
- raw snapshot metadata path or root
- generated runtime files missing
- installed Sentis version

Because import metadata stores whole runtime file list, validation is repo-wide instead of one-file-at-a-time.
