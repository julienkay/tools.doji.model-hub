# Model Manifests, Cache, and Generated Runtime Files

Packages declare HuggingFace dependencies by canonical model id in a model manifest named `hf-hub-manifest.json` at package root.

Projects can declare the same dependency shape with `ProjectManifestAsset` from `Assets > Create > Doji > AI > HF Hub Project Manifest`.

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

When a Unity project contains installed packages with `hf-hub-manifest.json` model manifests or project manifest assets, HF Hub scans those manifests and builds one dependency list for the project.

Current development build includes:

- manifest parsing and validation
- installed-package discovery
- project-manifest asset discovery
- local dependency status classification as `Ready`, `Missing`, `Stale`, or `Failed`
- first-run auto prompt window when discovered dependencies need attention
- editor Console info reminder after domain reload while dependencies still need attention
- custom Inspector for selected model manifest assets in packages
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

Selecting a model manifest asset in a package shows a focused Inspector summary for the manifest version and declared models, with shortcuts into Model Hub and the source file location. Writable package sources can add or remove model entries and apply those changes directly from the Inspector; installed package cache entries stay read-only.

### Model Hub Window

Use `Window > AI Model Hub` to inspect declared and imported repositories.

The Model Hub window groups entries by repository and revision so one HuggingFace repo appears once even when multiple package or project manifests depend on it.

Project manifest assets still show as `Project` in editor UI, but HF Hub now tracks each manifest asset as its own owner for bindings, registries, and stale/removal checks.

For each repository, the window shows:

- local state
- which package or project manifests depend on it
- raw cache location
- cache size on disk and estimated cache download size when metadata is known
- raw ONNX file rows in the `Files` tab when cache metadata is available
- generated runtime `.sentis` files
- generated runtime asset size on disk and estimated runtime size from import metadata when available
- manifest and import diagnostics

The `Files` tab is now source-aware. For each ONNX file it can show:

- repository-relative ONNX path
- raw cached file location and size when source files are present locally
- effective conversion recipe source such as built-in default or project override
- editable quantization and static-dims import settings for that file
- generated `.sentis` output path when the file has been materialized into the project

The `Files` tab now behaves more like a traditional importer. Each repository keeps one edit buffer with repo-wide `Revert` and `Apply` actions. `Apply` saves project-owned overrides and immediately reimports that repository. If raw cache is missing, HF Hub downloads source files first and then imports with the applied settings.

Project-owned file overrides live in `ProjectModelConversionOverridesAsset`. HF Hub creates or updates that asset automatically when you apply file-level conversion changes. You can also create it manually from `Assets > Create > Doji > AI > HF Hub Conversion Overrides`.

The diagnostics tab can also show repository diagnostics for cases that need maintainer attention:

- revision conflict when same repository is declared at multiple requested revisions in one project
- partial ONNX conversion when at least one ONNX file converted successfully but another variant was skipped

Revision conflict matters because generated runtime paths are repository-scoped. HF Hub does not choose a winner. Conflicting rows show `Conflict`, keep `Locate` available, and block `Import`, `Remove From Project`, and `Remove From Cache` until all manifests agree on one requested revision for that repository.

When a conflict owner comes from an installed package under Unity Package Manager, that manifest may not be directly editable from the current project. Model Hub shows owner source and resolution guidance for each dependent, can copy a conflict summary to clipboard, and opens Package Manager so you can change package versions, embed/localize package sources, or remove one dependency.

Header actions include:

- clickable repository link to open resolved repository page on HuggingFace
- `Download` when the repository is not cached on this machine yet; current behavior downloads and imports in one action
- `Import` when raw cache already exists and HF Hub needs to materialize `.sentis` output into the project
- `Manage` for ready entries, with:
  - `Remove From Project` to delete generated runtime assets and import metadata from this project
  - `Remove From Cache` to delete this machine's raw cache entry while keeping generated project assets
- `Locate` to ping one dependent manifest in Unity; when more than one manifest declares same repository, HF Hub shows a chooser menu first

If raw cache is removed but generated project assets still exist, the repository stays usable in that project. HF Hub keeps it in imported state and downloads raw files again only when a later update or re-import needs cache content.

## Import All

Use `Tools/Doji/HF Hub/Import All` to resolve discovered repository dependencies, populate shared raw cache, and generate project-owned runtime files.

Current sync flow:

1. Scan installed packages for `hf-hub-manifest.json` and project assets for `ProjectManifestAsset`.
2. Validate manifest contents.
3. Resolve each dependency to one concrete HuggingFace repository commit.
4. Download all repository files in that snapshot into shared raw cache.
5. Write snapshot metadata and revision ref files.
6. Materialize cached `.onnx` files into project-owned `.sentis` output through Unity Inference editor conversion code.
7. Serialize resulting `.sentis` files into generated runtime snapshot.
8. Write editor-only import metadata under `Library/Doji/HfHub/imports/`.

Sync runs as an editor background task instead of blocking the editor while downloads are in progress.

If a project declares the same repository at multiple requested revisions, HF Hub marks each conflicting row as error and does not write cache or import changes for that repository. `Import All` can still sync other repositories in the project, but conflicting rows fail until manifests agree on one revision.

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
Library/Doji/HfHub/imports/bindings/<owner-id>/<model-id>/hf-hub-binding.json
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
