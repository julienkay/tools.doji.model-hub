# Package Manifests, Cache, and Generated Models

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

## Current Limitation

Current manifest format supports one `sourceFile` per model entry.

Not supported yet:

- declaring that whole repo snapshot should be downloaded
- declaring multiple required files for one model entry
- declaring filtered companion files such as `config.json`, tokenizer files, vocab files, merges files, or preprocess metadata next to ONNX payload

This matters for real model packages because many ONNX repos need more than one file to be usable.

## Discovery

When Unity project contains installed packages with `hf-hub-manifest.json`, HF Hub scans those manifests and builds dependency list for project.

Current development build includes:

- manifest parsing and validation
- installed-package discovery
- local dependency status classification as `Ready`, `Missing`, `Stale`, or `Failed`
- editor status command at `Tools/Doji/HF Hub/Show Status`
- editor sync command at `Tools/Doji/HF Hub/Sync Models`
- Project Settings page at `Project Settings > Doji > AI > AI Model Hub`

## Sync Models

Use `Tools/Doji/HF Hub/Sync Models` to resolve discovered package dependencies, populate shared raw cache, and generate project-owned `.sentis` assets.

Current sync flow:

1. Scan installed packages for `hf-hub-manifest.json`.
2. Validate manifest contents.
3. Resolve each dependency to remote HuggingFace file version.
4. Download missing raw files into shared cache.
5. Write cache metadata and revision ref files.
6. Copy cached ONNX into a temporary Unity import location.
7. Let Unity Inference import that ONNX file.
8. Serialize resulting model to a generated `.sentis` file owned by the current project.
9. Write generated-artifact metadata next to that `.sentis` file.
10. Remove the temporary ONNX import asset again.

Current sync flow still follows the manifest limitation above, so it only syncs one declared `sourceFile` per model entry.

## Status and Prompting

HF Hub currently evaluates each discovered dependency into one of these local states:

- `Ready`: raw cache and generated `.sentis` artifact both match current manifest and Sentis version
- `Missing`: required raw cache or generated project artifact does not exist yet
- `Stale`: generated metadata no longer matches current manifest, raw cache commit, or Sentis version
- `Failed`: a local metadata file or cache ref exists but is unreadable or invalid

When dependencies are `Missing`, `Stale`, or `Failed`, HF Hub currently:

- shows that state in `Tools/Doji/HF Hub/Show Status`
- shows the same state in `Project Settings > Doji > AI > AI Model Hub`
- prompts once per editor session to sync now, postpone, or open Project Settings

The Project Settings page is the main user-facing status surface for this package at the moment.

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

## Generated Project Artifacts

Generated project-owned assets live under:

```text
Assets/Doji/HfHub/Generated/<package-id>/<model-id>/
```

Current generated files:

```text
<source-file-name>.sentis
hf-hub-import.json
```

Example:

```text
Assets/Doji/HfHub/Generated/com.doji.example/tiny-random-bert/model.sentis
Assets/Doji/HfHub/Generated/com.doji.example/tiny-random-bert/hf-hub-import.json
```

The `.sentis` file is the project-owned artifact intended for loading in editor and runtime.

The `hf-hub-import.json` sidecar currently records:

- package id and package version
- model id and display name
- manifest hash
- repository, revision, resolved commit hash, and source file
- raw cache file path and metadata path
- Sentis version
- Unity version for diagnostics
- generated `.sentis` asset path
- import timestamp

HF Hub currently regenerates the `.sentis` artifact when the generated file is missing or when recorded metadata no longer matches the package manifest, raw commit, or installed Sentis version.

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

## Temporary Import Assets

During conversion, HF Hub creates a temporary ONNX asset under:

```text
Assets/Doji/HfHub/TempImport/
```

That temporary asset exists only long enough for Unity Inference to import the ONNX file and is then removed again.
