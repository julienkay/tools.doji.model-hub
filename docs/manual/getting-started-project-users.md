# For Unity Project Users

Unity project users resolve model dependencies for the current Unity project.

HF Hub discovers model manifests from installed packages, project settings, and project manifest assets. Model Hub then shows which repositories need download, import, optional-file configuration, repair, or revision conflict resolution.

## Add Or Discover Dependencies

Package dependencies appear when an installed package contains `hf-hub-manifest.json`.

Project-owned dependencies can be added from:

- `Project Settings > Doji > AI > AI Model Hub`
- Model Hub `+ Add Model`
- `Assets > Create > Doji > AI > HF Hub Project Manifest`

Project Settings writes to:

```text
ProjectSettings/Packages/tools.doji.hf-hub/hf-hub-manifest.json
```

ScriptableObject project manifests are normal Unity assets and are discovered in addition to the Project Settings manifest.

## Configure Optional Files

Open `Window > AI Model Hub`.

Select the model. When its manifest marks files as optional, the **Optional files** section of the **Overview** tab lists them with their sizes. Turning on a file includes it in this project only.

HF Hub stores project-owned optional-file choices in:

```text
Assets/Doji/HfHub/HF Hub Optional File Selections.asset
```

Unenabled optional files do not count as selected files for status, download, import, runtime registry output, or startup prompts. Enabled optional files do count. If an enabled optional ONNX file is missing from cache or generated runtime output, HF Hub surfaces it like other unresolved import work.

## Download And Import

Use the setup prompt, the action button of a model in Model Hub, **Import All** in the Model Hub toolbar, or `Tools/Doji/HF Hub/Import All`.

- `Download & Import`: download the selected files into the shared cache, then generate runtime files.
- `Import`: the files are already downloaded; generate the project files.
- `Update`: download newly selected files and import again.
- `Choose Files`: turn on at least one optional file first.

The `⋮` menu of a model can remove the imported files from the project or the downloaded files from the cache. See [Editor Surfaces](editor-surfaces.md#status-and-actions).

Generated `.sentis` files are project-owned and live under:

```text
Assets/StreamingAssets/hf-hub/<repo-owner>/<repo-name>/
```

Runtime registry files live under:

```text
Assets/StreamingAssets/hf-hub/registries/<owner-id>/hf-hub-runtime.json
```

## Runtime Lookup

After import, runtime code resolves generated project assets by model id and repo-relative file path.

```csharp
if (ModelHub.TryResolveRuntimeModel("onnx-community/tiny-clip-test-ONNX", out var repo))
{
    var hasModel = repo.TryGetSentisPath("onnx/model.onnx", out var sentisPath);
}
```

If the same `repo_id` appears in more than one manifest owner, single-string lookup is ambiguous. Use owner-specific overloads.

```csharp
var repo = ModelHub.FromPretrained(
    "com.example.owner",
    "onnx-community/tiny-clip-test-ONNX");
var model = repo.LoadModel("onnx/model.onnx");
```

Use [Editor Surfaces](editor-surfaces.md) for Model Hub details and [Import, Cache, and Runtime Output](import-cache-runtime.md) for generated paths and cache behavior.
