# Package Manifests and Shared Cache

Packages that depend on remote HuggingFace model files declare them in `hf-hub-manifest.json` at package root.

Example:

```json
{
  "version": 1,
  "models": [
    {
      "id": "tiny-random-bert",
      "displayName": "Tiny Random BERT",
      "repository": "hf-internal-testing/tiny-random-bert",
      "revision": "main",
      "sourceFile": "model.onnx"
    }
  ]
}
```

## Manifest Fields

- `version`: manifest schema version. Current value is `1`.
- `id`: package-local model identifier.
- `displayName`: user-facing model name.
- `repository`: HuggingFace repository id.
- `revision`: branch, tag, or commit to resolve.
- `sourceFile`: file path inside remote repository.

Model ids must be unique inside one manifest.

## Discovery

When Unity project contains installed packages with `hf-hub-manifest.json`, HF Hub scans those manifests and builds dependency list for project.

Current development build includes:

- manifest parsing and validation
- installed-package discovery
- editor status command at `Tools/Doji/HF Hub/Show Status`
- editor sync command at `Tools/Doji/HF Hub/Sync Models`

## Sync Models

Use `Tools/Doji/HF Hub/Sync Models` to resolve discovered package dependencies and populate shared raw cache.

Current sync flow:

1. Scan installed packages for `hf-hub-manifest.json`.
2. Validate manifest contents.
3. Resolve each dependency to remote HuggingFace file version.
4. Download missing raw files into shared cache.
5. Write cache metadata and revision ref files.

Current sync scope stops at raw source caching. It does not yet import `.sentis` assets into Unity project.

## Shared Raw Cache

Downloaded source model files are stored in shared per-user cache outside Unity project.

Current cache roots:

- macOS: `~/Library/Application Support/Doji/HfHub`
- Windows: `%LocalAppData%/Doji/HfHub`
- Linux: `$XDG_CACHE_HOME/doji/hf-hub` or `~/.cache/doji/hf-hub`

Current raw cache layout:

```text
raw/<serialized-repo>/refs/<revision>
raw/<serialized-repo>/snapshots/<commit>/<sourceFile>
```

Example:

```text
raw/hf-internal-testing--tiny-random-bert/refs/main
raw/hf-internal-testing--tiny-random-bert/snapshots/<commit>/model.onnx
```

## Sidecar Metadata

Each cached raw file gets sidecar metadata file next to cached file:

```text
model.onnx.hfhub.json
```

Current sidecar records:

- repository
- requested revision
- resolved commit hash
- source file
- entity tag when available
- file size when available
- cached file path
- download timestamp

## File Locking

HF Hub uses file-level lock next to cached raw file during download and metadata write:

```text
model.onnx.lock
```

This prevents duplicate writes when multiple editor operations try to cache same model file.
