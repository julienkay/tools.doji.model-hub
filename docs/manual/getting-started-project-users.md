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

Optional files that are off do not count for status, download, import, builds, or startup prompts. Optional files that are on count like any other selected file.

## Download And Import

Use the setup prompt, the action button of a model in Model Hub, **Import All** in the Model Hub toolbar, or `Tools/Doji/HF Hub/Import All`.

- `Download & Import`: download the selected files into the shared cache, then convert the ONNX files.
- `Import`: the files are already downloaded; convert the ONNX files.
- `Update`: download newly selected files and import again.
- `Choose Files`: turn on at least one optional file first.

The `⋮` menu of a model can pull its Sentis files into the project, remove the model from the project, or remove the downloaded files from the cache. See [Editor Surfaces](editor-surfaces.md#status-and-actions).

Downloaded files and converted models live in shared folders outside the project. Nothing is written into `Assets`.

## Choose How Builds Deliver Models

Builds add every imported model in one of two ways:

- **Bundle in the build** (default): the build contains the Sentis model.
- **Download at runtime**: the player downloads the model from Hugging Face the first time it loads, at the commit tested in this project.

Set the default in `Project Settings > Doji > AI > AI Model Hub > In builds`, and override it per model in Model Hub. A build fails with a list when a model is not imported. See [Builds](import-cache-runtime.md#builds).

## Load Models

Runtime code loads a model by repository id and file path. The same call works in Play mode and in players, for bundled and downloaded models:

```csharp
Model model = await Hub.LoadModelAsync("onnx-community/tiny-clip-test-ONNX", "onnx/model.onnx");
```

Use [Editor Surfaces](editor-surfaces.md) for Model Hub details and [Import, Cache, and Runtime Loading](import-cache-runtime.md) for paths, builds, and the runtime API.
