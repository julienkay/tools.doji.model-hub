# Runtime Loading

Package code loads a model by `repoId` and the repository path its manifest lists, whatever the format and however the model reaches the device:

```csharp
var model = await Hub.LoadModelAsync("julienkay/sentis-MiDaS", "sentis/midas_v21_small_256.sentis"); // used as downloaded
var model = await Hub.LoadModelAsync("julienkay/sentis-MiDaS", "onnx/midas_v21_small_256.onnx");     // its converted model
```

The package writes the loading code once. The project decides delivery (bundle or download) in Project Settings and per model. The registry carries that decision to the player.

## Registry

[RuntimeRegistry.cs](../tools.doji.model-hub/Runtime/Locator/RuntimeRegistry.cs), JSON through `JsonUtility`, `version` 2. `FromJson` refuses newer versions.

```text
RuntimeRegistry   version, onnxConverterId, packages[]
RuntimePackageEntry   packageId, packageVersion, models[]
RuntimeModelEntry   repoId, cacheRepoId, revision, commit, delivery ("bundle" | "download"), files[], dataFiles[]
RuntimeModelFile   repoPath, label, format ("onnx" | "sentis"), relativePath,
                   source {repoPath, etag, size}, companions[], recipeHash, quantization, inputShapes[]
RuntimeDataFile    repoPath, relativePath, source {repoPath, etag, size}
```

- `dataFiles` are the selected files that are neither model files nor ONNX companions (configs, tokenizers). `RuntimeRegistryBuilder.CreateDataFiles` takes them from `DiscoveredModelDependency.GetSelectedRepoPaths` and the import record's `RepositoryFiles`; whole-repository dependencies skip dotfiles. Their `relativePath` is `model-hub/files/<owner>/<name>/<repoPath>` (`Hub.GetRuntimeDataRelativePath`). Bundled builds add them from `RawSnapshotRootPath`; the Editor catalog sets `localPath` to the cache file. `Hub.ReadFileAsync`/`ReadTextAsync` find them with `RuntimeRegistry.TryFindDataFile`, which follows the same package rules as `TryFind`.

- `relativePath` is the bundled path under StreamingAssets: `model-hub/models/<owner>/<name>/<repoPath with .sentis>` (`Hub.GetRuntimeModelRelativePath`).
- `cacheRepoId` is the repository id as the Editor's cache folder spells it, so a player downloads into the same folder name as the Hub and Python use. Lookup uses `repoId`, case-insensitively.
- `commit` is the commit the Editor resolved. Players download from `/resolve/<commit>/` and never call the revision API.
- `source` and `companions` hold etags and sizes, so players name blobs like the Editor does.
- `quantization`, `inputShapes`, and `recipeHash` let a player convert an ONNX file with the settings tested in the Editor.
- `localPath` is `[NonSerialized]`: the Editor catalog sets it, builds never write it.
- `label` is the manifest's `files[].label`, for `Hub.GetModelFiles` and pickers in players.

A package entry lists only its own owner's selection. The shared import may hold more files for other owners.

## Catalog sources

`RuntimeModels.GetCatalog(Async)` takes the first that exists:

1. `RegistryOverride` (tests)
2. In the Editor, `EditorCatalog`: [EditorRuntimeCatalog.cs](../tools.doji.model-hub/Editor/Build/EditorRuntimeCatalog.cs) builds the registry from the project's import records with `forEditor: true`, so files point at the converted store, the cache, or pulled copies. Play mode needs no StreamingAssets files, in either delivery mode. It is rebuilt after a sync or a project change.
3. In players, `StreamingAssets/model-hub/model-hub-runtime.json`, read with `File` where StreamingAssets is a folder and with `UnityWebRequest` on Android and the Web.

## Lookup

`RuntimeRegistry.TryFind(packageId, repoId, repoPath)`:

- With a package id, only that package's entry counts.
- Without one, the first package (ordered by id) whose entry has the repository and the file wins. Two packages that select different files of one repository both work without a package id. Pass one only when the same path must resolve to a specific owner.
- A miss throws `FileNotFoundException`. With no entry: "has no model". With an entry: "has no file", listing the known files.

## Pickers and files of your own

- `Hub.GetModelFiles(Async)(packageId)` maps the catalog's files to `HubModelFile` (`HubModelFile.FromRegistry`). It lists only what the catalog has: included and set-up files.
- `HubModelReference` serializes `_packageId`, `_repoId`, `_repoPath`, and `_modelAsset`. A model asset wins over the file fields.
- The Inspector drawer ([HubModelReferenceDrawer.cs](../tools.doji.model-hub/Editor/UI/HubModelReferenceDrawer.cs)) lists more than the catalog: `HubModelPicker.Build` takes every declared model file from the status report and marks it `Included` (in the catalog), `NotTurnedOn` (optional, not enabled), `NeedsDownload`, `NeedsConversion` (the dependency's `PendingConversions` lists the file; the entry keeps `ConversionCause`), or `NeedsSetup` (import pending without a conversion). Whole-repository dependencies list their imported runtime files. `HubModelReferenceDrawer.GetStatus` maps the state to the status line's text and button. `NotTurnedOn` and `NeedsDownload` look the same to users ("Not downloaded yet", **Download**); for `NotTurnedOn`, **Download** first writes the optional-file selection. **Download**, **Convert**, and **Set up** start `BackgroundSyncManager.StartForRepository`. While a sync runs, the drawer polls its progress every 250 ms and rebuilds when it ends.
- `HubModelLoader` (one per package) maps strings to a model: empty for the default, `IsFilePath` (exists or rooted) for `Hub.LoadModelFromFile`, `owner/name:path` for another repository, else a path in the default repository. It wraps failures of package files in an `InvalidOperationException` that names the package and `Window > AI Model Hub`; cancellation and failures of the user's own files and assets pass through.
- `PretrainedModel<T>` reads `[PretrainedModel]` from `T` once into a static `HubModelLoader`, creates instances through the private parameterless constructor (`Activator.CreateInstance(nonPublic: true)`), and calls `Initialize`. The `Backend` setter calls `Release` then `Initialize`. `[HubModel(typeof(T))]` reads the package id from the same attribute.
- `Hub.LoadModelFromFile` bypasses the registry. ONNX files load with `OnnxModelLoader.Load` and are not cached.

## Build

[RuntimeRegistryBuilder.cs](../tools.doji.model-hub/Editor/Build/RuntimeRegistryBuilder.cs) turns a `StatusReport` into a `Plan`: registry, bundled files, and problems. [ModelHubBuildProcessor.cs](../tools.doji.model-hub/Editor/Build/ModelHubBuildProcessor.cs) runs it in `BuildPlayerProcessor.PrepareForBuild`:

- Problems fail the build with `BuildFailedException` and a list: a model that is not imported, a missing runtime model file, or a download-mode file without an etag.
- Each bundled runtime model goes in with `BuildPlayerContext.AddAdditionalPathToStreamingAssets`, straight from the converted store or the cache. Nothing is copied into `Assets`.
- A runtime model shared by two packages is bundled once.
- A pulled copy under `Assets/StreamingAssets/model-hub/models/` is already in the build, so it is not added again.
- The registry is written to `Library/Doji/ModelHub/build/model-hub-runtime.json` and added as `model-hub/model-hub-runtime.json`.
- `onnxConverterId` is `doji-onnx-model-loader-v3@<version>+<format generation>`. `formatGeneration` records the Editor's generation; `HubStorage.FormatGeneration` reads it in players.

Builds never ship `.onnx` files.

## Players

[RuntimeModels.cs](../tools.doji.model-hub/Runtime/Locator/RuntimeModels.cs) decides per file:

| Delivery | Format | Player |
|---|---|---|
| bundle | any | Load `relativePath` from StreamingAssets. |
| download | `.sentis` | `RuntimeDownloader.EnsureFileAsync`, then load the snapshot file. |
| download | `.onnx` | Download source and companions, look up the converted store by key, else convert with `OnnxConverter`, apply the recipe, save, then load. |
| bundle | data file | Read from StreamingAssets: `File` where it is a folder, `UnityWebRequest` into memory on Android and the Web (no copy). |
| download | data file | `RuntimeDownloader.EnsureFileAsync`, then read the snapshot file. `EnsureAvailableAsync` fetches them too. |

Platform access to bundled files:

| Platform | How |
|---|---|
| Windows, macOS, Linux, iOS | `ModelLoader.Load(path)` from StreamingAssets |
| Android | Copy once with `UnityWebRequest` and `DownloadHandlerFile` to `<data-root>/bundled/<Application.version>/<relativePath>`, then load the copy. Loading from memory would hold the model twice at peak. Old versions' copies stay until the app calls `HubStorage.DeleteBundledCopiesOfOtherVersions()`. |
| Web | `UnityWebRequest` into memory, `ModelLoader.Load(Stream)` |

The synchronous `Hub.LoadModel` and `Hub.FromPretrained` work only when a local file exists without waiting (`TryGetLocalPathWithoutWaiting`). Otherwise they throw and point to the async method.

### Device folders

[CachePaths.cs](../tools.doji.model-hub/Runtime/Cache/CachePaths.cs), shared with the Editor:

| Platform | Cache root | Data root |
|---|---|---|
| Editor | per-user custom folder (`CacheRootOverride`), else Hugging Face default | per-user custom folder (`DataRootOverride`), else `DOJI_MODEL_HUB_DATA_ROOT`, else per-user Doji folder |
| Windows, macOS, Linux players | Hugging Face default: `HF_HUB_CACHE`, `HF_HOME/hub`, `~/.cache/huggingface/hub` | per-user Doji folder, same as the Editor |
| Other players | `persistentDataPath/huggingface/hub` | `persistentDataPath/model-hub` |

Desktop players share the user's Hugging Face cache by default, so a model the Editor or Python downloaded is reused. A project that must not write to the user's home folder bundles its models. A `persistentDataPath` option for desktop players is on the [roadmap](roadmap.md).

### Downloads

[RuntimeDownloader.cs](../tools.doji.model-hub/Runtime/Locator/RuntimeDownloader.cs):

- URL: `https://huggingface.co/<cacheRepoId>/resolve/<commit>/<escaped path>`.
- `CacheBlobs.StoreAsync` ([CacheBlobs.cs](../tools.doji.model-hub/Runtime/Cache/CacheBlobs.cs)), shared with Editor sync: with an etag, take the blob lock, download into `blobs/<etag>` when missing or the wrong size, and link the snapshot file. Without an etag, download into the snapshot path.
- The player then writes `refs/<revision>` only when it is missing. Editor sync always writes it, because it just resolved the revision.
- Desktop and mobile use `ResumableDownloader` from `tools.doji.downloads`. The Web has no streaming handler: the file arrives in memory and is written once.
- `BlobLock` uses `flock` and symbolic links only under `UNITY_EDITOR || UNITY_STANDALONE`. Other players use an in-process lock and copies.
- The downloader does not await with `ConfigureAwait(false)`: `ModelLoader` and `UnityWebRequest` need the main thread.
- On the Web, `persistentDataPath` is in memory until the player copies it to IndexedDB. `PersistentStorage.Flush` ([PersistentStorage.cs](../tools.doji.model-hub/Runtime/Cache/PersistentStorage.cs)) runs `FS.syncfs` from [PersistentStorage.jslib](../tools.doji.model-hub/Runtime/Cache/PersistentStorage.jslib) after each download, conversion, and `HubStorage` delete, one copy at a time. It does nothing when the template sets `autoSyncPersistentDataPath`, because Unity then saves each file when it closes. Without it, every page load downloads and converts again. It does not call Unity's `JS_FileSystem_Sync`, which logs a deprecation warning.

### ONNX on the device

[OnnxModelLoaderConverter.cs](../tools.doji.model-hub/Runtime/Conversion/OnnxModelLoaderConverter.cs) is the default `RuntimeModels.OnnxConverter`. `Hub.RegisterOnnxConverter(converter, converterId)` replaces it, and null restores it.

`RuntimeModels.CurrentConverterId` is the registered converter's id, else the registry's `onnxConverterId`. It is part of the conversion key, so a loader upgrade converts again.

## Known limits

- The build smoke test (`projects/AI Model Hub/Assets/AI Model Hub/Build Smoke Test/`) has passed on Windows, Android (IL2CPP, ARM64), and the Web. iOS, macOS, and Linux players are not verified on devices.
- Like `huggingface_hub`, downloads are checked by size only, not by content hash.
- The Web download writes the whole file from memory, then loads it again.
- Only public repositories download at runtime. A token cannot ship in a build.
