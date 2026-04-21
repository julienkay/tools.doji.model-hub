# Package Manifests, Cache, and Generated Runtime Files

Packages declare HuggingFace dependencies by canonical model id in `hf-hub-manifest.json` at package root.

Projects can declare the same dependency shape with `HfHubProjectManifestAsset` from `Assets > Create > Doji > AI > HF Hub Project Manifest`.

Example:

```json
{
  "version": 1,
  "models": [
    {
      "id": "hf-internal-testing/tiny-random-bert"
    }
  ]
}
```

## Manifest Fields

- `version`: manifest schema version. Current value is `1`.
- `id`: canonical HuggingFace model identifier. In Hub API terms this is the `repo_id` string such as `owner/name`.
- `revision`: optional branch, tag, or commit to resolve. Defaults to `main`.

Model ids must be unique inside one manifest.

## Dependency Scope

One manifest entry means one HuggingFace model id and one resolved repository snapshot.

During sync HF Hub:

1. resolves the declared revision to one concrete commit
2. downloads all files listed in that repository snapshot
3. keeps that raw repository snapshot in shared cache as source of truth
4. converts every `.onnx` file in that snapshot to `.sentis`

This keeps package and project manifests simple. No separate local alias or `repository` field exists in v1 schema. Runtime code chooses individual files from resolved snapshot the same way upstream workflows choose files inside one `repo_id`.

## Discovery

When a Unity project contains installed packages with `hf-hub-manifest.json` or project manifest assets, HF Hub scans those manifests and builds one dependency list for the project.

Current development build includes:

- manifest parsing and validation
- installed-package discovery
- project-manifest asset discovery
- local dependency status classification as `Ready`, `Missing`, `Stale`, or `Failed`
- first-run auto prompt window when discovered dependencies need attention
- Model Hub window at `Window > AI Model Hub`
- editor import command at `Tools/Doji/HF Hub/Import All`
- Project Settings page at `Project Settings > Doji > AI > AI Model Hub`

## Editor Surfaces

HF Hub now splits configuration and inventory into separate editor surfaces.

### Project Settings

Use `Project Settings > Doji > AI > AI Model Hub` for configuration only.

Current setting:

- shared cache location override for this project

The page also shows:

- platform default cache root
- effective cache root after overrides
- current Sentis version

### Model Hub Window

Use `Window > AI Model Hub` to inspect declared and imported repositories.

The Model Hub window groups entries by repository and revision so one HuggingFace repo appears once even when multiple package or project manifests depend on it.

For each repository, the window shows:

- local state
- which package or project manifests depend on it
- raw cache location
- cache size on disk and estimated cache download size when metadata is known
- generated runtime `.sentis` files
- generated runtime asset size on disk and estimated runtime size from import metadata when available
- manifest and import diagnostics

The diagnostics tab can also show warnings for cases that are still usable but need maintainer attention:

- revision ambiguity when same repository is declared at multiple requested revisions in one project
- partial ONNX conversion when at least one ONNX file converted successfully but another variant was skipped

Revision ambiguity matters because generated runtime paths are repository-scoped. If multiple revisions of same repo are declared, HF Hub warns so you can decide which revision should own generated artifacts in that project.

Header actions include:

- clickable repository link to open resolved repository page on HuggingFace
- `Import` to sync only the selected repository entry when it still needs download or regeneration
- `Manage` for ready entries, with:
  - `Remove From Project` to delete generated runtime assets and import metadata from this project
  - `Remove From Cache` to delete this machine's raw cache entry while keeping generated project assets
- `Locate` to ping one dependent manifest in Unity; when more than one manifest declares same repository, HF Hub shows a chooser menu first

If raw cache is removed but generated project assets still exist, the repository stays usable in that project. HF Hub keeps it in imported state and downloads raw files again only when a later update or re-import needs cache content.

## Import All

Use `Tools/Doji/HF Hub/Import All` to resolve discovered repository dependencies, populate shared raw cache, and generate project-owned runtime files.

Current sync flow:

1. Scan installed packages for `hf-hub-manifest.json` and project assets for `HfHubProjectManifestAsset`.
2. Validate manifest contents.
3. Resolve each dependency to one concrete HuggingFace repository commit.
4. Download all repository files in that snapshot into shared raw cache.
5. Write snapshot metadata and revision ref files.
6. Convert every cached `.onnx` file in that snapshot through Unity Inference editor conversion code.
7. Serialize resulting `.sentis` files into generated runtime snapshot.
8. Write editor-only import metadata under `Library/Doji/HfHub/import-state/`.

Sync runs as an editor background task instead of blocking the editor while downloads are in progress.

If a repo contains multiple ONNX files and Unity Inference cannot convert one variant, HF Hub keeps the repo sync and any successful `.sentis` outputs for the other ONNX files. HF Hub records that skipped file as import warning in Model Hub diagnostics so later refreshes still show what was only partially converted. Sync only fails when a repo contains ONNX files and none of them convert successfully.

## Runtime Use

HF Hub is designed around stronger offline runtime use than the upstream Python Hub libraries.

Default expectation:

- package authors sync repositories in the Editor first
- builds include or otherwise already have generated runtime snapshot files
- runtime code resolves local repo snapshots by `repo_id`
- runtime code loads one generated model by repo-relative file path

Runtime lookup:

```csharp
if (ModelHub.TryResolveRuntimeModel("onnx-community/tiny-clip-test-ONNX", out var repo))
{
    var hasModel = repo.TryGetSentisPath("onnx/model.onnx", out var sentisPath);
}
```

Runtime load:

```csharp
var model = ModelHub.LoadRuntimeModel(
    "onnx-community/tiny-clip-test-ONNX",
    "onnx/model.onnx");
```

`from_pretrained(...)` style wrapper:

```csharp
var repo = ModelHub.FromPretrained("onnx-community/tiny-clip-test-ONNX");
var model = repo.LoadModel("onnx/model.onnx");
```

If same `repo_id` appears in more than one manifest owner, single-string lookup is ambiguous and throws. Use owner-specific overloads in that case:

```csharp
var repo = ModelHub.FromPretrained(
    "com.example.owner",
    "onnx-community/tiny-clip-test-ONNX");
var model = repo.LoadModel("onnx/model.onnx");
```

## Shared Raw Cache

Downloaded repository snapshots are stored in shared per-user cache outside the Unity project.

Current cache roots:

- macOS: `~/Library/Application Support/Doji/HfHub`
- Windows: `%LocalAppData%/Doji/HfHub`
- Linux: `$XDG_CACHE_HOME/doji/hf-hub` or `~/.cache/doji/hf-hub`

Current raw cache layout:

```text
raw/<serialized-repo>/refs/<revision>
raw/<serialized-repo>/snapshots/<commit>/<repo-file>
raw/<serialized-repo>/snapshots/<commit>/snapshot.hfhub.json
```

## Generated Project Artifacts

Generated runtime snapshot files live under:

```text
Assets/StreamingAssets/hf-hub/<repo-owner>/<repo-name>/
```

Current generated runtime output includes:

```text
<onnx-file>.sentis
```

Example:

```text
Assets/StreamingAssets/hf-hub/onnx-community/tiny-clip-test-ONNX/onnx/model.sentis
```

HF Hub also writes runtime locator metadata under:

```text
Assets/StreamingAssets/hf-hub/registries/<owner-id>/hf-hub-runtime.json
```

Editor-only import provenance lives under:

```text
Library/Doji/HfHub/import-state/<owner-id>/<model-id>/hf-hub-import.json
```

Here `<model-id>` is sanitized canonical HuggingFace `id` value.

Stored import fields include:

- package id and package version
- model id as declared canonical HuggingFace `id`
- manifest hash
- model id, revision, and resolved commit hash
- raw snapshot root and raw metadata path
- Sentis version
- Unity version for diagnostics
- persisted import warnings such as partial ONNX conversion outcomes
- generated runtime registry path
- generated runtime file list
- import timestamp
