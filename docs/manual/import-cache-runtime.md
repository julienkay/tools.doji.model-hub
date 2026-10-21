# Import, Cache, and Runtime Output

HF Hub resolves manifest dependencies in the Editor, downloads source files into a shared raw cache, and generates project-owned runtime files for Unity builds.

## Import All

Use `Tools/Doji/HF Hub/Import All` to resolve discovered repository dependencies, populate shared raw cache, and generate project-owned runtime files.

Current sync flow:

1. Scan installed packages for `hf-hub-manifest.json`, `ProjectSettings/Packages/tools.doji.hf-hub/hf-hub-manifest.json`, and ScriptableObject project assets for `ProjectManifestAsset`.
2. Validate manifest contents.
3. Resolve each dependency to one concrete HuggingFace repository commit.
4. Read the repository file inventory for that commit.
5. Download only the files required by current manifest owners into shared raw cache.
6. Write snapshot metadata and revision ref files.
7. Materialize only the selected `.onnx` files into project-owned `.sentis` output through Unity Inference editor conversion code.
8. Serialize resulting `.sentis` files into generated runtime snapshot.
9. Write editor-only import metadata under `Library/Doji/HfHub/imports/`.

Sync runs as an editor background task instead of blocking the editor while downloads are in progress.

Whole-repository dependencies still follow the same ONNX rule. HF Hub reads full repository inventory, caches all repo files for that revision, then imports only repo paths whose extension is `.onnx`.

If a project declares the same repository at multiple requested revisions, HF Hub marks each conflicting row as error and does not write cache or import changes for that repository. `Import All` can still sync other repositories in the project, but conflicting rows fail until manifests agree on one revision.

If a repo contains multiple ONNX files and Unity Inference cannot convert one variant, HF Hub keeps the repo sync and any successful `.sentis` outputs for the other ONNX files. HF Hub records that skipped file as import warning in Model Hub diagnostics so later refreshes still show what was only partially converted. Sync only fails when a repo contains ONNX files and none of them convert successfully.

## Runtime Use

HF Hub is designed around offline runtime use.

Default expectation:

- package authors or project users sync repositories in the Editor first
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

## Shared Download Cache

Downloaded files are stored in the Hugging Face cache, outside the Unity project. It is the same cache the `huggingface_hub` Python library uses, so a model downloaded by Python tools is not downloaded again in Unity, and the other way round.

Cache folder, in this order:

1. a custom folder set in `Project Settings > Doji > AI > AI Model Hub`
2. the `HF_HUB_CACHE` environment variable
3. the `hub` folder inside the `HF_HOME` environment variable
4. `~/.cache/huggingface/hub` (on Windows `%USERPROFILE%\.cache\huggingface\hub`)

Layout:

```text
models--<owner>--<name>/refs/<revision>
models--<owner>--<name>/blobs/<etag>
models--<owner>--<name>/snapshots/<commit>/<repo-file>
```

File content is stored once in `blobs/`. Each snapshot file is a link to its blob, so revisions that share a file store it once. Where the file system does not allow symbolic links (Windows without Developer Mode), snapshot files are plain copies instead, as in the Python library.

HF Hub keeps its own index of cached files outside this cache, in `%LocalAppData%/Doji/HfHub/index` on Windows, `~/Library/Application Support/Doji/HfHub/index` on macOS, and `~/.cache/doji/hf-hub/index` on Linux. Manifest selection stays owner-scoped and is not inferred from cache contents.

If downloaded files are removed but generated project assets still exist, the model stays usable in that project. HF Hub keeps it in imported state and downloads files again only when a later update or import needs them.

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
- model id, revision, resolved commit hash, and dependency selection fingerprint
- raw snapshot root and raw metadata path
- repository file inventory for the resolved commit
- Sentis version
- Unity version for diagnostics
- persisted import warnings such as partial ONNX conversion outcomes
- generated runtime registry path
- generated runtime file list
- import timestamp
