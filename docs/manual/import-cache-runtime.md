# Setup, Cache, and Runtime Loading

AI Model Hub downloads model files into the shared Hugging Face cache, turns them into Sentis models in a shared folder outside the project, and adds them to builds. Nothing is written into `Assets` unless you pull a model into the project.

| What | Where | Shared by |
|---|---|---|
| Downloaded files | Hugging Face cache, for example `~/.cache/huggingface/hub` | All Unity projects and Python tools on this machine |
| Converted Sentis models | AI Model Hub data folder, `converted/` | All Unity projects on this machine |
| Setup records | `Library/Doji/ModelHub/imports/` | This project only |
| Models in a build | The build's StreamingAssets, added at build time | That build only |

## Model files

The file extension decides how AI Model Hub uses a file:

| Extension | What AI Model Hub does | Conversion settings |
|---|---|---|
| `.onnx` | Converts it to a Sentis model. | Quantization and input shapes apply. |
| `.sentis` | Uses the downloaded file as it is. There is no conversion. | Do not apply. |
| anything else | Downloads it when selected, for example a config or tokenizer file. It is not a model. | — |

Selecting an `.onnx` file also selects its external weight files, such as `model.onnx_data` or `model.onnx.data`, when the repository has them.

A whole-repository dependency works when the repository has only one model format. A repository with both `.onnx` and `.sentis` files needs a file list, so AI Model Hub never picks a format for you. See [Manifest Schema](manifest-reference.md#model-files).

## Setting up models

A model is ready when its selected files are downloaded, its ONNX files are converted, and this project has a record of both. AI Model Hub splits that work by cost:

| Work | When it runs |
|---|---|
| Write this project's records, and reuse converted models that exist | By itself: when the Editor starts, after project changes, after a download, and before Play mode. |
| Download files | After you confirm: the setup prompt, a Model Hub button, or the menu. |
| Convert ONNX files | After you confirm, because large models take CPU, memory, and time. A build or **Apply & Convert** counts as confirmation. |

So a model that another project on this machine already downloaded and converted with the same settings is ready without a prompt. A model needs a new conversion the first time, after its conversion settings change, and after a Sentis update that starts a new format generation. Model Hub then shows **Needs conversion** with the reason.

Use the setup prompt, the action button of a model in Model Hub, the toolbar button (**Download All**, **Convert All**, or **Set Up All**), or `Tools > Doji > AI Model Hub > Set Up All Models`. The work runs as a background task with progress and **Cancel**, and the Editor stays usable:

1. Find every manifest: installed packages, `ProjectSettings/Packages/tools.doji.model-hub/model-hub-manifest.json`, and project manifest assets.
2. Validate them, and skip models that are ready.
3. For models with missing files: resolve the dependency to one commit, read the repository's file list at that commit, and download the selected files that are not in the cache yet. Models whose files are all cached skip this step and need no network.
4. Convert each selected `.onnx` file with the Unity Inference Engine importer, unless a conversion with the same source file, format generation, and conversion settings already exists.
5. Write the records to `Library/Doji/ModelHub/imports/`.

A dependency that selects only `.sentis` files is **Ready** as soon as its files are downloaded.

If a project declares the same repository at different revisions, AI Model Hub marks each of those rows as **Conflict** and does not download or convert that repository. Other repositories still sync.

If one ONNX file of a repository cannot be converted, AI Model Hub keeps the other converted files. The model is **Ready**, with a warning that lists the skipped files. AI Model Hub does not try again until the model, its file selection, its conversion settings, or the Inference Engine version changes. The setup fails only when no ONNX file converts.

## Download cache

Downloaded files are stored in the Hugging Face cache, the same cache the `huggingface_hub` Python library uses. A model that Python tools downloaded is not downloaded again in Unity, and the other way round.

Cache folder, in this order:

1. a custom folder set in `Project Settings > Doji > AI > AI Model Hub` (Editor only)
2. the `HF_HUB_CACHE` environment variable
3. the `hub` folder inside the `HF_HOME` environment variable
4. `~/.cache/huggingface/hub` (on Windows `%USERPROFILE%\.cache\huggingface\hub`)

Layout:

```text
models--<owner>--<name>/refs/<revision>
models--<owner>--<name>/blobs/<etag>
models--<owner>--<name>/snapshots/<commit>/<repo-file>
```

File content is stored once in `blobs/`. Each snapshot file is a link to its blob. Where the file system does not allow symbolic links (Windows without Developer Mode), snapshot files are copies, as in the Python library.

## AI Model Hub data folder

AI Model Hub keeps its own files outside the Hugging Face cache, so Python tools never see them:

| Platform | Folder |
|---|---|
| Windows | `%LocalAppData%\Doji\ModelHub` |
| macOS | `~/Library/Application Support/Doji/ModelHub` |
| Linux | `~/.cache/doji/model-hub` |

It holds:

- `index/`: each cached repository's file list per commit
- `converted/models--<owner>--<name>/<key>.sentis`: converted Sentis models, each with a `.json` file that names the source file, its etag, the converter, and the conversion settings

A converted model is found by what produced it: the source file's etag, the converter, the Inference Engine **format generation**, and the conversion settings. Projects with the same model and settings share one conversion.

Most Inference Engine updates keep the `.sentis` format: older files are upgraded when they load. So an update reuses existing conversions. AI Model Hub converts again only when the update starts a new format generation, which AI Model Hub's own compatibility table decides. Project Settings shows the installed version and its generation.

**Remove from Project** deletes this project's records. Downloaded files and converted models stay, so the model is ready again without a conversion.

### Clean up converted models

AI Model Hub never deletes converted models on its own, because other projects on the machine may use them. Delete them from your own code with `HubStorage`, for example after moving every project to a new format generation:

```csharp
using Doji.AI.ModelHub;

string current = HubStorage.FormatGeneration;
int deleted = HubStorage.DeleteConvertedModels(model => model.FormatGeneration != current);
```

`HubStorage.GetConvertedModels()` lists each converted model with its repository, source path, converter, format generation, size, and date. A deleted model is converted again the next time it is needed. Models that are being written are skipped. The same methods work in players, on the device's converted folder.

Earlier versions of AI Model Hub kept downloads in a private `raw` folder in the data folder. On the first Editor start after an update, AI Model Hub moves those files into the Hugging Face cache without downloading them again, then deletes `raw`.

## Builds

When you build a player, AI Model Hub adds each model to the build in one of two ways. The project decides, not the package.

| In builds | What the build contains | What the player does |
|---|---|---|
| **Bundle in the build** (default) | The Sentis model: the downloaded `.sentis` file, or the converted model of an `.onnx` file. | Loads it from StreamingAssets. |
| **Download at runtime** | Only the model's entry in AI Model Hub's registry. | Downloads the files from Hugging Face the first time the model loads, then loads them from the device. |

Set the default in `Project Settings > Doji > AI > AI Model Hub > In builds`. Override it per model in Model Hub with the **In builds** field.

The build adds files to StreamingAssets directly from the shared folders. It does not copy them into `Assets`, so a failed build leaves nothing behind:

```text
StreamingAssets/model-hub/model-hub-runtime.json
StreamingAssets/model-hub/models/<owner>/<name>/<repo-path>.sentis
```

For example, `onnx/model.onnx` of `onnx-community/tiny-clip-test-ONNX` is bundled as `model-hub/models/onnx-community/tiny-clip-test-ONNX/onnx/model.sentis`.

Rules:

- A build never contains `.onnx` files. An ONNX model is always bundled as its converted Sentis model, made with the conversion settings tested in the Editor.
- Starting a build counts as confirmation: the build converts every downloaded model that needs it, with a progress bar, before it adds the models.
- A build never downloads. It fails, with a list, when a model is not downloaded. Download it in Model Hub first. A build never ships without a model silently.
- The registry stores the commit that the Editor resolved. Players download exactly that commit and do not ask Hugging Face for the latest revision.
- **Download at runtime** for an `.onnx` file needs the `tools.doji.onnx-model-loader` package, which converts ONNX models on the device. Without it, the build fails for that model.
- Only public repositories can download at runtime. A build cannot contain an access token. A gated or private file fails with an `IOException` that names the repository and file.

### Pull into project

**Pull into Project** in the Model Hub `⋮` menu copies a model's Sentis files into:

```text
Assets/StreamingAssets/model-hub/models/<owner>/<name>/
```

Builds then use these copies instead of adding the model, and Play mode loads them. Use it to keep a model's exact file under version control. **Remove Pulled Copies** deletes them again. **Remove from Project** also deletes them, unless another manifest still uses the repository.

## Where players store models

| Player | Downloaded files | Converted ONNX models |
|---|---|---|
| Windows, macOS, Linux | The user's Hugging Face cache, shared with Python tools and the Editor | The AI Model Hub data folder of that user |
| Android, iOS, Web | `persistentDataPath/huggingface/hub` | `persistentDataPath/model-hub/converted` |

On the Web, `persistentDataPath` lives in the browser's IndexedDB. AI Model Hub saves it after each download and conversion, so a model downloads once per browser.

Desktop players use the same environment variables as the Editor (`HF_HUB_CACHE`, `HF_HOME`). The Project Settings custom folder applies to the Editor only. If your application must not write to the user's Hugging Face cache, bundle its models.

How players read bundled models:

| Platform | How |
|---|---|
| Windows, macOS, Linux, iOS | Load from the file in StreamingAssets. |
| Android | StreamingAssets is inside the APK. The first load copies the model once to `persistentDataPath/model-hub/bundled/<app-version>/`, then loads the copy. Copies of earlier app versions stay until you call `HubStorage.DeleteBundledCopiesOfOtherVersions()`, for example once after an app update. |
| Web | Download the model from StreamingAssets into memory, then load it. |

Bundled models make the app larger. Google Play limits the base APK to 150 MB, and Web players download everything before they start. Set large models to **Download at runtime**.

## Load models at runtime

Load a model by its repository id and the repository path the manifest declares. The same code works for bundled and downloaded models, in Play mode and in players:

```csharp
using Doji.AI.ModelHub;
using Unity.InferenceEngine;

Model model = await Hub.LoadModelAsync(
    "onnx-community/tiny-clip-test-ONNX",
    "onnx/model.onnx");
```

For an `.onnx` path, AI Model Hub loads its converted Sentis model. A `.sentis` path loads the downloaded file.

`LoadModelAsync` works on every platform. In download mode, the first call downloads the files and, for ONNX files, converts them. Pass a progress handler and a cancellation token to show progress:

```csharp
// DownloadProgress is in the Doji.Downloads namespace. TotalBytes is null when the size is unknown.
var progress = new Progress<DownloadProgress>(p => Debug.Log($"{p.DownloadedBytes} / {p.TotalBytes}"));
Model model = await Hub.LoadModelAsync(repoId, repoPath, progress, cancellationToken);
```

Fetch every file of a model up front, for example on a loading screen:

```csharp
await Hub.EnsureAvailableAsync("onnx-community/tiny-clip-test-ONNX", progress, cancellationToken);
```

`from_pretrained(...)` style:

```csharp
PretrainedRepository repo = await Hub.FromPretrainedAsync("onnx-community/tiny-clip-test-ONNX");
Model model = await repo.LoadModelAsync("onnx/model.onnx");
```

`repo.Files` lists the model files the project uses, and `repo.Commit` is the commit the build uses.

### Synchronous loading

`Hub.LoadModel(repoId, repoPath)` and `Hub.FromPretrained(repoId)` do not wait. They work in the Editor, for bundled models on Windows, macOS, Linux, and iOS, and for downloaded models that are already on the device. On Android and the Web, and for models that are not downloaded yet, they throw an error that points to the async method.

### Several packages, one repository

When two packages use the same repository, each sees only the files its own manifest selects. Pass the package id to load the files of one package:

```csharp
Model model = await Hub.LoadModelAsync("com.example.depth", "julienkay/sentis-MiDaS", "sentis/midas_v21_small_256.sentis");
```

### Errors

| Message starts with | Cause |
|---|---|
| `AI Model Hub has no model '<repo>'` | No manifest declares the model, or it is not ready in this project. In a player, the build did not include it. |
| `AI Model Hub model '<repo>' has no file '<path>'` | The manifest does not select that file. The message lists the files it does select. |
| `... is downloaded on first use. Use Hub.LoadModelAsync` | A synchronous call for a model that is not on the device yet. |
| `... needs an ONNX converter` | A player downloads an `.onnx` file but `tools.doji.onnx-model-loader` is not installed. |
