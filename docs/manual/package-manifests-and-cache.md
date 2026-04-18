# Package Manifests, Cache, and Generated Models

Packages that depend on remote HuggingFace model files declare them in `hf-hub-manifest.json` at package root.

Projects can also declare dependencies with a `HfHubProjectManifestAsset` created from `Assets > Create > Doji > AI > HF Hub Project Manifest`.

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
- `id`: package-local lookup alias used by HF Hub inside one Unity package.
- `displayName`: user-facing model name.
- `repository`: HuggingFace model id or repository id, same source concept used by `from_pretrained(...)`.
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

## Project Manifest Assets

Project manifest assets use the same schema fields as package manifests, but are edited through the Inspector instead of raw JSON.

Use them when:

- the dependency belongs to one Unity project, not a reusable UPM package
- you want a context-menu-created asset that artists or designers can inspect in Unity
- you still want HF Hub to participate in the same shared cache, sync, and runtime locator flow

Package manifests remain plain JSON because they travel better inside source-controlled UPM packages.

## Discovery

When Unity project contains installed packages with `hf-hub-manifest.json` or project manifest assets, HF Hub scans those manifests and builds one dependency list for the project.

Current development build includes:

- manifest parsing and validation
- installed-package discovery
- project-manifest asset discovery
- local dependency status classification as `Ready`, `Missing`, `Stale`, or `Failed`
- editor status command at `Tools/Doji/HF Hub/Show Status`
- editor sync command at `Tools/Doji/HF Hub/Sync Models`
- Project Settings page at `Project Settings > Doji > AI > AI Model Hub`

## Sync Models

Use `Tools/Doji/HF Hub/Sync Models` to resolve discovered package dependencies, populate shared raw cache, and generate project-owned `.sentis` assets.

Current sync flow:

1. Scan installed packages for `hf-hub-manifest.json` and project assets for `HfHubProjectManifestAsset`.
2. Validate manifest contents.
3. Resolve each dependency to remote HuggingFace file version.
4. Download missing raw files into shared cache.
5. Write cache metadata and revision ref files.
6. Convert cached ONNX directly through Unity Inference editor conversion code.
7. Serialize resulting model to a generated `.sentis` file under shared `StreamingAssets`.
8. Write editor-only import metadata under `Library/Doji/HfHub/import-state/`.

Current sync flow still follows the manifest limitation above, so it only syncs one declared `sourceFile` per model entry.

Sync now runs as an editor background task instead of blocking the editor while downloads are in progress.

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

Starting sync from either the menu, Project Settings, or the prompt now uses Unity's background Progress UI.

The Project Settings page is the main user-facing status surface for this package at the moment.

Each dependency row also exposes clickable links for:

- `Source`: opens declared HuggingFace repository file in browser
- `Raw Cache`: reveals downloaded source file in local file browser
- `Runtime .sentis`: selects and pings generated runtime asset in Unity Project view

## Runtime Use Assumption

HF Hub is designed around stronger offline runtime use than the upstream Python Hub libraries.

Default expectation:

- package authors sync models in the Editor first
- builds include or otherwise already have the generated runtime `.sentis` payloads
- runtime code resolves local payloads by model id through HF Hub locator APIs

Runtime download is still planned, but that is additive. Default workflow assumes the file was downloaded and converted in the Editor before runtime load happens.

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

Generated runtime payloads live under:

```text
Assets/StreamingAssets/hf-hub/<serialized-repo>/snapshots/<resolved-commit>/
```

Current generated runtime files:

```text
<source-file-name>.sentis
```

Example:

```text
Assets/StreamingAssets/hf-hub/models--hf-internal-testing--tiny-random-bert/snapshots/<commit>/model.sentis
```

The `.sentis` file is runtime-facing payload intended for editor and runtime load paths.

HF Hub also writes locator metadata under shared `StreamingAssets` root:

```text
Assets/StreamingAssets/hf-hub/registries/<owner-id>/hf-hub-runtime.json
```

Runtime code should resolve generated payloads by canonical HuggingFace `repo_id` through:

```csharp
if (ModelHub.TryResolveRuntimeModel("hf-internal-testing/tiny-random-bert", out var model))
{
    var sentisPath = model.PrimarySentisPath;
}
```

Or load directly, upstream-style:

```csharp
var model = ModelHub.LoadRuntimeModel("hf-internal-testing/tiny-random-bert");
```

This avoids hard-coding `Assets/...` or `StreamingAssets/...` path conventions in package or project runtime code.

Owner-specific overloads still exist for rare ambiguous cases where same repository id appears in more than one manifest owner.

Companion project includes sample MonoBehaviour at `projects/HF Hub/Assets/HF Hub/HfHubRuntimeLocatorSample.cs` that demonstrates this exact lookup and load path.

In other words, runtime `load(<modelId>)` should usually mean "resolve local generated payload and load it", not "go to network first".

Editor-only import provenance currently lives under:

```text
Library/Doji/HfHub/import-state/<owner-id>/<model-id>/hf-hub-import.json
```

That file is maintainer/debug state, not user-facing project content.

Stored fields include:

- package id and package version
- model id and display name
- manifest hash
- repository, revision, resolved commit hash, and source file
- raw cache file path and metadata path
- Sentis version
- Unity version for diagnostics
- generated `.sentis` asset path
- generated runtime registry path
- generated runtime `.sentis` path
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

While a download is still in progress, HF Hub also keeps a partial payload file next to the final target:

```text
model.onnx.downloading
```

That partial file is used to resume interrupted downloads instead of restarting from zero.

## File Locking

HF Hub uses file-level lock next to cached raw file during download and metadata write:

```text
model.onnx.lock
```

This prevents duplicate writes when multiple editor operations try to cache same model file.

## Temporary Import Assets

Current implementation does not create temporary ONNX Unity assets during conversion.

## Background Sync and Domain Reloads

HF Hub currently persists active sync work to:

```text
Library/Doji/HfHub/sync-state.json
```

That state file allows the package to resume pending sync work after a domain reload or editor script reload.

Current behavior:

- downloads run under Unity's Progress API in the background
- partial `.downloading` files are resumed with HTTP `Range` requests when possible
- pending dependency queue is restored after domain reload from `Library/Doji/HfHub/sync-state.json`

This is the current foundation for making model downloads the reliable core workflow of the package.
