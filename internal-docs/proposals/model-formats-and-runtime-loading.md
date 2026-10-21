# Proposal: Model Formats and Runtime Loading

Status: draft for discussion. Nothing here is implemented yet.

## Problem

HF Hub assumes every model is ONNX: it converts `.onnx` files to `.sentis` and writes one copy per project into `Assets/StreamingAssets/hf-hub`. Three needs break that assumption:

1. **Repositories that ship `.sentis` files.** A package author converts once and publishes `.sentis` files; consumers should load them as downloaded. Today a dependency that selects only `.sentis` files fails with "does not select any ONNX file".
2. **No project copies.** Converted files should live in one shared folder per machine, and builds should take them from there.
3. **Runtime loading on every platform.** The current API calls `ModelLoader.Load(path)`, which uses `File.OpenRead`. That fails on Android, where StreamingAssets is inside the APK, and on the Web, where StreamingAssets is only reachable over HTTP. Later, players should also download models at runtime and convert ONNX on the device.

## Terms

| Term | Meaning |
|---|---|
| Source file | A file of a Hugging Face repository, stored once as a blob in the shared cache. |
| Model file | A source file HF Hub can turn into an Inference Engine `Model`: `.onnx` (needs conversion) or `.sentis` (ready to load). |
| Companion file | A file a model file cannot work without, such as ONNX external weights (`model.onnx_data`). |
| Runtime model | The `.sentis` file that `ModelLoader` reads: a downloaded `.sentis` blob, or the converted output of an `.onnx` file. |
| Conversion settings | Quantization and fixed input dimensions. They apply to ONNX conversion only. |

## 1. Format is decided per file, not per repository

The extension decides a file's format. No repository-level mode exists.

- `.onnx`: converted. Conversion settings apply.
- `.sentis`: loaded as downloaded. Conversion settings do not apply.
- `.gguf` and others: not model files for HF Hub (see [GGUF](#7-gguf)).

**Mixed repositories already exist, and one of them is ours.** `julienkay/sentis-MiDaS` has 12 `.onnx` files under `onnx/` and 11 `.sentis` files under `sentis/`. Declaring mixed repositories out of scope would exclude the main example. With per-file formats they need no special case. The manifest's `files` list already says which files a package uses, and each selected file loads by its own format. A package that lists `sentis/midas_v21_small_256.sentis` gets the prebuilt file. One that lists `onnx/midas_v21_small_256.onnx` gets a conversion.

Rules:

- A **whole-repository** dependency on a repository that has both formats is invalid. Validation says "This repository has both ONNX and Sentis files. List the files you use." HF Hub never picks one format silently. The existing "no ONNX file" check becomes "no model file".
- A whole-repository dependency on a single-format repository works as today.
- The manifest schema does not change.

The format of a whole repository is still useful as a **label**, derived from the tree listing before any download: `ONNX`, `Sentis`, `ONNX + Sentis`, or `No model files`. The Add Model popup and Model Hub show it.

### Companion files

ONNX models over 2 GB keep weights in external data files. HF Hub does not handle them today: the `.onnx` file downloads alone and conversion fails. Proposal:

- Selecting `x.onnx` also selects siblings named `x.onnx_data`, `x.onnx.data`, and `x.onnx_data_*`, the names exporters and `transformers.js` use.
- After download, `tools.doji.formats.onnx` reads the external data locations from the initializers, without loading weights. A missing location fails with the file name.

## 2. Runtime model identity

A runtime model is found by what produced it, never by project or path.

| Source | Key | Where |
|---|---|---|
| `.sentis` | the blob etag | the blob in the shared Hugging Face cache, unchanged |
| `.onnx` | hash of blob etag, converter id and version, conversion settings | `<data-root>/converted/<models--owner--name>/<key>.sentis`, plus a small JSON sidecar naming the source path, etag, converter, and settings |

- The converter id is part of the key, for example `unity-editor-importer@2.6.1` or `onnx-model-loader@1.0.0`. Converters may differ even when they aim to match, and an Inference Engine upgrade must never reuse an older conversion.
- `.sentis` files carry a format version (`ModelWriter.version`, 8 in Inference Engine 2.6.1). Older files are upgraded on load, and newer files load with a warning. Model Hub can read the version from the file header without loading weights. It shows "Built for a newer Inference Engine" instead of failing later.
- Converted files never go into the Hugging Face cache, so Python tools never see them.
- Concurrent conversions of the same key take a lock and write through a temporary name, like blob downloads.

## 3. Where files live

| Context | Downloaded files | Runtime models | How the runtime finds them |
|---|---|---|---|
| Editor | shared Hugging Face cache | `.sentis` blobs in the cache; conversions in the data root | absolute paths, resolved by the editor |
| Player, bundled (default) | not shipped | added to the build's StreamingAssets by the build step | registry in StreamingAssets with relative paths |
| Player, runtime download (later) | `persistentDataPath/huggingface/hub`, same layout | `.sentis` blobs, or conversions in `persistentDataPath/hf-hub/converted` | runtime registry built from the package manifests |

**Build step.** `BuildPlayerProcessor.PrepareForBuild` calls `BuildPlayerContext.AddAdditionalPathToStreamingAssets` for every runtime model the project uses. Nothing is copied into `Assets`, and nothing is left behind when a build fails. Check this API on 6000.3. A runtime model that is missing fails the build with a list of models, or converts first when the source is in the cache. A build never ships without it silently. ONNX source files are never shipped.

**Pull into project.** A context-menu action copies one runtime model into `Assets/StreamingAssets/hf-hub`. The build step skips models that are already there.

**Status.** "Imported" means "the runtime model exists for this source, converter, and settings". A dependency on `.sentis` files is Ready as soon as it is downloaded: it has no import step.

## 4. Runtime loading API and platforms

Callers use the repository path they declared, whatever the format:

```csharp
var model = await Hub.LoadModelAsync("julienkay/sentis-MiDaS", "sentis/midas_v21_small_256.sentis");
var model = await Hub.LoadModelAsync("julienkay/sentis-MiDaS", "onnx/midas_v21_small_256.onnx"); // converted file
```

`ModelLoader.Load(Stream)` exists, so the loader chooses how to read per platform:

| Platform | StreamingAssets access | Plan |
|---|---|---|
| Windows, macOS, Linux | file | load from the file |
| iOS | file | load from the file |
| Android | inside the APK, `jar:` URL | copy once to `persistentDataPath` with `UnityWebRequest` and a file download handler, then load from the file. Loading from memory would hold the model twice at peak. |
| Web | HTTP | `UnityWebRequest` to memory, then `Load(Stream)`. No file system to copy to. |

- `LoadModelAsync` returns `Awaitable<Model>` and works everywhere.
- The synchronous `LoadModel` stays for file platforms. On Android and the Web it throws an error that points to `LoadModelAsync`.

Bundling has limits on mobile and the Web: Google Play caps the base APK at 150 MB, and the Web downloads everything up front. Large models need the runtime download mode.

## 5. Runtime downloads and runtime ONNX (later)

**Runtime downloads.** The players download into `persistentDataPath/huggingface/hub` with the same layout, locks, and etags. The runtime registry is built from the package manifests shipped with the build, not from a scan. The download and cache code that does not need `UnityEditor` moves to `Runtime`, as the vision already asks.

**Runtime ONNX.** `tools.doji.onnx-model-loader` converts ONNX at runtime and aims to match the Editor importer. The flow is: download `.onnx` and its companions, convert once with `OnnxModelLoader`, apply quantization with `ModelQuantizer.QuantizeWeights` (a runtime API), and save with `ModelWriter.Save` to the device's converted folder. Later loads read the `.sentis` file.

- Fixed input dimensions need a public option on the loader. `DynamicDimConfigs` is internal today.
- HF Hub should reference the loader from an optional assembly, so that bundled-only projects do not compile it.
- The Editor could use the same converter later and drop the bridge to Unity's internal Editor converter. That waits until tests show both produce the same model.

## 5a. Delivery mode: chosen by the project, not the package

The package developer writes the loading code once (`Hub.LoadModelAsync`, `Hub.FromPretrained`). The project owner decides how each model reaches the device. The same code works in both modes, because the registry the build writes tells the runtime where to look.

| Mode | What the build does | What the player does |
|---|---|---|
| Copy to StreamingAssets (default) | adds the runtime model: the `.sentis` blob, or the converted `.sentis` for an ONNX source | loads it from StreamingAssets |
| Download at runtime | ships only the registry entry | downloads into the device cache on first use, then loads from there |

- A build never ships ONNX files. An ONNX source is always bundled as its converted `.sentis`, made with the conversion settings tested in the Editor.
- "Download at runtime" for an ONNX source needs runtime ONNX conversion (phase 5). With a `.sentis` source it needs only runtime downloads (phase 4). Until phase 5, the setting can only be chosen for models with `.sentis` sources, and the UI says why.
- **Project default and per-model override.** Project Settings has the default mode. Model Hub has an override per model, for "bundle the small ones, download the large one".
- **The build pins the commit.** The registry stores the commit resolved in the Editor, so players download exactly the tested files and skip the revision API.
- **Async with progress and cancellation.** In download mode, the first load can take minutes. `LoadModelAsync(repoId, repoPath, progress, cancellationToken)` works in both modes, and `Hub.EnsureAvailableAsync(repoId)` lets a loading screen fetch models up front.
- **Device cache.** On mobile and the Web the cache is per app, under `persistentDataPath`. Desktop players could share `~/.cache/huggingface/hub` with Python, as an opt-in, because an app that writes to the user's home folder can surprise people.
- **Only public repositories download at runtime.** A token cannot ship in a build. Gated or private repositories stay bundled, unless the application supplies a token at runtime.
- **Editor Play mode** loads from the local shared cache in both modes. A toggle can test download mode in Play mode against a separate folder.
- The mode reaches the player through the registry, which the build step writes from Project Settings and the overrides.

## 6. Editor UX

- The Add Model popup and the Model Hub header show the repository format label.
- The file picker filters by "Model files" (`.onnx` and `.sentis`), and each row shows its format.
- Conversion settings show only for `.onnx` files (already true).
- The cache overview gets a "Converted" section for the data root's converted folder, with Remove.

## 7. GGUF

`.gguf` needs a different runtime, such as llama.cpp. The vision names Sentis as the only inference engine. Supporting GGUF is a vision change first. The per-file format above keeps that door open: a new format brings its own loader and key, and the manifest stays the same.

## Phases

1. **Formats per file.** File formats, `.sentis` dependencies without an import step, mixed-repository validation, companion files, format labels in the UI.
2. **No project copies.** Converted folder in the data root, editor-side resolution, the build step, pull into project.
3. **Platform-safe loading.** `LoadModelAsync`, the Android copy, the Web request, relative-path registries.
4. **Runtime downloads.** Download and cache code moves to `Runtime`; device cache; runtime registry.
5. **Runtime ONNX.** `onnx-model-loader` integration, converted folder on the device.

## Open decisions

Agreed:

- Formats per file. A whole-repository dependency on a repository with both formats is invalid.
- Android copies a runtime model once to `persistentDataPath`.
- The converter id is part of the conversion key.
- Builds bundle converted `.sentis` files and never ONNX files.
- The project chooses the delivery mode, with a default in Project Settings and overrides per model.
- GGUF stays out until the vision changes.

Open:

1. When the runtime download mode is needed. It decides how early phase 4 matters.
2. Whether desktop players may share the user's `~/.cache/huggingface/hub`.
