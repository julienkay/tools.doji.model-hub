# Runtime Loading

Package code loads a model by `repoId` and the repository path its manifest lists, whatever the format and however the model reaches the device:

```csharp
var model = await Hub.LoadModelAsync("julienkay/sentis-MiDaS", "sentis/midas_v21_small_256.sentis"); // used as downloaded
var model = await Hub.LoadModelAsync("julienkay/sentis-MiDaS", "onnx/midas_v21_small_256.onnx");     // its converted model
```

The package writes the loading code once. The project decides delivery (bundle or download) in Project Settings and per model. The registry carries that decision to the player.

## Registry

[RuntimeRegistry.cs](../tools.doji.hf-hub/Runtime/Locator/RuntimeRegistry.cs), JSON through `JsonUtility`, `version` 2. `FromJson` refuses newer versions.

```text
RuntimeRegistry   version, onnxConverterId, packages[]
RuntimePackageEntry   packageId, packageVersion, models[]
RuntimeModelEntry   repoId, cacheRepoId, revision, commit, delivery ("bundle" | "download"), files[]
RuntimeModelFile   repoPath, format ("onnx" | "sentis"), relativePath,
                   source {repoPath, etag, size}, companions[], recipeHash, quantization, inputShapes[]
```

- `relativePath` is the bundled path under StreamingAssets: `hf-hub/models/<owner>/<name>/<repoPath with .sentis>` (`Hub.GetRuntimeModelRelativePath`).
- `cacheRepoId` is the repository id as the Editor's cache folder spells it, so a player downloads into the same folder name as the Hub and Python use. Lookup uses `repoId`, case-insensitively.
- `commit` is the commit the Editor resolved. Players download from `/resolve/<commit>/` and never call the revision API.
- `source` and `companions` hold etags and sizes, so players name blobs like the Editor does.
- `quantization`, `inputShapes`, and `recipeHash` let a player convert an ONNX file with the settings tested in the Editor.
- `localPath` is `[NonSerialized]`: the Editor catalog sets it, builds never write it.

A package entry lists only its own owner's selection. The shared import may hold more files for other owners.

## Catalog sources

`RuntimeModels.GetCatalog(Async)` takes the first that exists:

1. `RegistryOverride` (tests)
2. In the Editor, `EditorCatalog`: [EditorRuntimeCatalog.cs](../tools.doji.hf-hub/Editor/Build/EditorRuntimeCatalog.cs) builds the registry from the project's import records with `forEditor: true`, so files point at the converted store, the cache, or pulled copies. Play mode needs no StreamingAssets files, in either delivery mode. It is rebuilt after a sync or a project change.
3. In players, `StreamingAssets/hf-hub/hf-hub-runtime.json`, read with `File` where StreamingAssets is a folder and with `UnityWebRequest` on Android and the Web.

## Lookup

`RuntimeRegistry.TryFind(packageId, repoId, repoPath)`:

- With a package id, only that package's entry counts.
- Without one, the first package (ordered by id) whose entry has the repository and the file wins. Two packages that select different files of one repository both work without a package id. Pass one only when the same path must resolve to a specific owner.
- A miss throws `FileNotFoundException`. With no entry: "has no model". With an entry: "has no file", listing the known files.

## Build

[RuntimeRegistryBuilder.cs](../tools.doji.hf-hub/Editor/Build/RuntimeRegistryBuilder.cs) turns a `StatusReport` into a `Plan`: registry, bundled files, and problems. [HfHubBuildProcessor.cs](../tools.doji.hf-hub/Editor/Build/HfHubBuildProcessor.cs) runs it in `BuildPlayerProcessor.PrepareForBuild`:

- Problems fail the build with `BuildFailedException` and a list: a model that is not imported, a missing runtime model file, a download-mode file without an etag, or a download-mode ONNX file when `tools.doji.onnx-model-loader` is not installed.
- Each bundled runtime model goes in with `BuildPlayerContext.AddAdditionalPathToStreamingAssets`, straight from the converted store or the cache. Nothing is copied into `Assets`.
- A runtime model shared by two packages is bundled once.
- A pulled copy under `Assets/StreamingAssets/hf-hub/models/` is already in the build, so it is not added again.
- The registry is written to `Library/Doji/HfHub/build/hf-hub-runtime.json` and added as `hf-hub/hf-hub-runtime.json`.
- `onnxConverterId` is `doji-onnx-model-loader@<version>+inference@<version>` when the loader package is installed, else null.

Builds never ship `.onnx` files.

## Players

[RuntimeModels.cs](../tools.doji.hf-hub/Runtime/Locator/RuntimeModels.cs) decides per file:

| Delivery | Format | Player |
|---|---|---|
| bundle | any | Load `relativePath` from StreamingAssets. |
| download | `.sentis` | `RuntimeDownloader.EnsureFileAsync`, then load the snapshot file. |
| download | `.onnx` | Download source and companions, look up the converted store by key, else convert with `OnnxConverter`, apply the recipe, save, then load. |

Platform access to bundled files:

| Platform | How |
|---|---|
| Windows, macOS, Linux, iOS | `ModelLoader.Load(path)` from StreamingAssets |
| Android | Copy once with `UnityWebRequest` and `DownloadHandlerFile` to `<data-root>/bundled/<Application.version>/<relativePath>`, then load the copy. Loading from memory would hold the model twice at peak. |
| Web | `UnityWebRequest` into memory, `ModelLoader.Load(Stream)` |

The synchronous `Hub.LoadModel` and `Hub.FromPretrained` work only when a local file exists without waiting (`TryGetLocalPathWithoutWaiting`). Otherwise they throw and point to the async method.

### Device folders

[CachePaths.cs](../tools.doji.hf-hub/Runtime/Cache/CachePaths.cs), shared with the Editor:

| Platform | Cache root | Data root |
|---|---|---|
| Editor | Project Settings custom folder (`CacheRootOverride`), else Hugging Face default | per-user Doji folder |
| Windows, macOS, Linux players | Hugging Face default: `HF_HUB_CACHE`, `HF_HOME/hub`, `~/.cache/huggingface/hub` | per-user Doji folder, same as the Editor |
| Other players | `persistentDataPath/huggingface/hub` | `persistentDataPath/hf-hub` |

Desktop players share the user's Hugging Face cache by default, so a model the Editor or Python downloaded is reused. A project that must not write to the user's home folder bundles its models. A `persistentDataPath` option for desktop players is on the [roadmap](roadmap.md).

### Downloads

[RuntimeDownloader.cs](../tools.doji.hf-hub/Runtime/Locator/RuntimeDownloader.cs):

- URL: `https://huggingface.co/<cacheRepoId>/resolve/<commit>/<escaped path>`.
- `CacheBlobs.StoreAsync` ([CacheBlobs.cs](../tools.doji.hf-hub/Runtime/Cache/CacheBlobs.cs)), shared with Editor sync: with an etag, take the blob lock, download into `blobs/<etag>` when missing or the wrong size, and link the snapshot file. Without an etag, download into the snapshot path.
- The player then writes `refs/<revision>` only when it is missing. Editor sync always writes it, because it just resolved the revision.
- Desktop and mobile use `ResumableDownloader` from `tools.doji.downloads`. The Web has no streaming handler: the file arrives in memory and is written once.
- `BlobLock` uses `flock` and symbolic links only under `UNITY_EDITOR || UNITY_STANDALONE`. Other players use an in-process lock and copies.
- The downloader does not await with `ConfigureAwait(false)`: `ModelLoader` and `UnityWebRequest` need the main thread.

### ONNX on the device

[OnnxModelLoaderConverter.cs](../tools.doji.hf-hub/Runtime/Conversion/OnnxModelLoaderConverter.cs) compiles under the `HF_HUB_ONNX_MODEL_LOADER` version define, set when `tools.doji.onnx-model-loader` is installed. It registers itself with `RuntimeInitializeOnLoadMethod`. `Hub.RegisterOnnxConverter(converter, converterId)` replaces it.

`RuntimeModels.CurrentConverterId` is the registered converter's id, else the registry's `onnxConverterId`. It is part of the conversion key, so a loader upgrade converts again.

## Known limits

- Android, iOS, Web, macOS, and Linux players are not verified on devices. Only a Windows player smoke test runs (`projects/HF Hub/Assets/HF Hub/Build Smoke Test/`).
- Players do not verify a downloaded file's hash against its etag; they check only the size.
- The Web download writes the whole file from memory, then loads it again.
- Only public repositories download at runtime. A token cannot ship in a build.
