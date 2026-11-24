# Import and Converted Models

Import turns the selected model files of one dependency into **runtime models**: the `.sentis` files that `ModelLoader` reads. It writes nothing into `Assets`.

| Term | Meaning |
|---|---|
| Model file | A source file AI Model Hub can load: `.onnx` (converted) or `.sentis` (used as downloaded). `ModelFiles.GetFormat` decides by extension. |
| Companion file | A file a model file needs, such as ONNX external weights. `ModelFiles.GetCompanionFiles` finds `<x>.onnx_data`, `<x>.onnx.data`, and `<x>.onnx_data_<n>` next to `<x>.onnx`. `GetSelectedRepoPaths` adds them to the selection. |
| Runtime model | A downloaded `.sentis` blob, or the converted output of an `.onnx` file. |
| Converted store | `<data-root>/converted/`, shared by every project on the machine. |

## Flow

[ProjectImportService.cs](../tools.doji.model-hub/Editor/Import/ProjectImportService.cs):

1. Resolve the selection (`GetSelectedModelRepoPaths`). `GetSelectionProblem` rejects a whole-repository dependency on a mixed repository and a selection without model files.
2. For each `.sentis` file: record the cached snapshot path as the runtime model. No conversion.
3. For each `.onnx` file: compute the conversion key, reuse `ConvertedModelStore.TryGet` when it hits, otherwise convert with the Editor importer, apply the recipe with `OnnxConversionSettings.Apply`, and `ConvertedModelStore.Save`.
4. Write the shared import record and one binding per owner.

A conversion failure skips that file when another ONNX file converts. The model stays **Ready** with a warning, and `SkippedRepoPaths` keeps status from retrying it every time. Import fails only when no ONNX file converts.

`ImportAsync(..., allowConversion: false)` throws `ConversionRequiredException` (with the ONNX repo paths) before it writes anything when a selected ONNX file has no store hit. The two early returns that reuse current records run first, so a current import never throws.

## Consent boundary

Import is not a user-facing word. To the user a model is **Ready**, needs a download, or needs a conversion. The boundary is cost:

| Work | Cost | Runs |
|---|---|---|
| Records under `Library/Doji/ModelHub/imports`, `.sentis` files, ONNX files with a store hit | file writes | without a prompt, by `AutoImport` |
| Downloads | network, disk | after consent: setup prompt, Model Hub button, toolbar, menu |
| New conversions (first conversion, changed recipe, new format generation) | CPU, RAM, time | after consent; user actions count: **Convert**, **Apply & Convert**, the toolbar, a build |

Status computes the split up front. `StatusService` wraps every import-pending status (`DependencyStatus.ImportPendingCodes`: missing, invalid, or stale bindings and shared records, missing runtime files) with `ProjectImportService.GetPendingConversions`: the selected ONNX files without a store hit. That gives:

- `IsImportPending`: every selected file is cached; only this project's import is missing or out of date
- `CanImportAutomatically`: import pending and no pending conversions
- `NeedsConversion`: import pending with pending conversions; `ConversionCause` says why: `FormatChanged` when the recorded Sentis version is in another format generation, `SettingsChanged` when a pending file's recorded `RecipeHash` differs from the current recipe, else `FirstConversion`
- `NeedsStartupPrompt` excludes `CanImportAutomatically`, so a cheap import never prompts

Invalid binding and shared records are `Missing`, not `Failed`: they are rewritten from the cache like missing ones.

`CachedModelImporter.Import(report, allowConversion, ...)` runs the imports synchronously from the cache and never downloads. Without conversions it takes `CanImportAutomatically` statuses and reports a `ConversionRequiredException` as `NeedConversion` (the store entry vanished between status and import); with conversions it takes every `IsImportPending` status. Callers:

- `AutoImport` (`[InitializeOnLoad]`): after Editor start, `projectChanged`, and `SyncStateChanged`, through `delayCall`, and directly on `ExitingEditMode`. It skips batch mode, Play mode, a running sync, and `AutoImport.Suspended` (set by the test assembly's `AutoImportSuspension` fixture). It logs each failure once per session.
- `ModelHubBuildProcessor.ImportCachedModels`: before `CreatePlan`, with conversions and a progress bar outside batch mode. A build never downloads; `RuntimeRegistryBuilder` reports models that are not downloaded.

Background sync downgrades `Full` to `MaterializeOnly` for import-pending dependencies (`BackgroundSyncManager.BuildPendingItems`), so **Convert** needs no network and shows "Converting", and it leaves `Ready` dependencies out.

## Conversion key

[ConvertedModelStore.cs](../tools.doji.model-hub/Runtime/Conversion/ConvertedModelStore.cs) is in `Runtime`, so players use the same store.

```text
key  = hex(sha256(etag | converterId | recipeHash)[0..16])
path = <data-root>/converted/<models--owner--name, lowercase>/<key>.sentis
       <data-root>/converted/<models--owner--name, lowercase>/<key>.json
```

- `etag` is the source blob name: sha256 for LFS files, git sha1 otherwise. `SourceFileIdentity` reads it from the index, else from the snapshot link target, else hashes the content.
- `converterId` names the converter and everything that changes its output. Editor: `unity-inference-editor-v3@<generation>`. Player: `doji-onnx-model-loader-v2@<loader version>+<generation>`. The `-v` suffixes change when `OnnxConversionSettings.Apply` changes its output.
- `<generation>` comes from [SentisCompatibility.cs](../tools.doji.model-hub/Runtime/Conversion/SentisCompatibility.cs): `sentis-f<ModelWriter.version>`, for example `sentis-f8`, with a revision suffix (`sentis-f8.1`) for importer changes that keep the format. The table is maintained by hand; AI Model Hub does not read the internal const at runtime. Inference Engine updates within one generation reuse conversions: `ModelLoader` upgrades older `.sentis` files on load and loads newer ones with a warning. `SentisCompatibilityTests.Generations_CoverTheInstalledModelWriterVersion` fails when the installed writer version is newer than the last entry's `checkedUpToWriterVersion`, so the decision is never skipped. Then either add a generation (older conversions must not be reused), add a revision (importer output changed, format did not), or raise `checkedUpToWriterVersion` (the new format still reuses old files; the id stays).
- `recipeHash` covers quantization (normalized: `int8` maps to `uint8`) and fixed input shapes.
- `Save` takes the blob lock, writes `<key>.sentis.incomplete`, and renames. The `.json` record names the source path, etag, converter, format generation, and recipe.
- Nothing deletes converted models automatically. `HubStorage.GetConvertedModels` and `DeleteConvertedModels` let projects and applications clean up when they decide to.
- Converted files never enter the Hugging Face cache, so Python tools never see them.

`OnnxConversionSettings.Apply` uses public APIs only. `ModelQuantizer.QuantizeWeights` replaces the model, so `Apply` returns the model to use.

## On each new Inference Engine version

`SentisCompatibilityTests.Generations_CoverTheInstalledModelWriterVersion` fails when the installed `ModelWriter.version` is newer than the last generation's `checkedUpToWriterVersion`. Before raising `checkedUpToWriterVersion`, check in the new package (`Library/PackageCache/com.unity.ai.inference@*/Runtime/Core/Serialization/`):

1. **Older files still load.** `ModelLoader` upgrades every writer version of the current generation to the new one (the `originalProgramVersion < ModelWriter.version` path), and the upgraded model runs the same as before. Load a converted model from the previous version and compare outputs on one input.
2. **Newer files still load in older versions.** `ModelLoader` of the previous version only warns on the newer version (`originalProgramVersion > ModelWriter.version`) and runs the file correctly. This matters because projects and desktop players on different versions share one converted store.

Both hold: raise `checkedUpToWriterVersion` of the last entry. The id stays, so existing conversions are reused.

Either fails: add `new Generation("<new version>", <new writer version>)`. The id becomes `sentis-f<N>`, and every ONNX file converts again.

## Import records

Import storage is split in two, under `Library/Doji/ModelHub/imports/`:

- `shared/<repo>/<hash>/model-hub-shared-import.json`: one per repository snapshot. `<hash>` is the first 8 bytes of `sha256(commit | formatGeneration | recipeSetHash)`, short enough for Windows `MAX_PATH`.
- `bindings/<owner-id>/<model-id>/model-hub-binding.json`: one per owner dependency.

Shared record (`SharedImportMetadata`): repository, resolved commit, snapshot root, repository file list, Sentis and Unity version, recipe set hash, diagnostics, `SkippedRepoPaths`, and `RuntimeFiles`.

Each `ImportedRuntimeFileMetadata` holds:

- `RepoPath` and `Format` (`onnx` or `sentis`)
- `FilePath`: absolute path of the runtime model (converted store or cache snapshot)
- `SourceEtag`, `SourceSizeInBytes`, `Companions` (repo path, etag, size): what a player needs to download the same files
- `ConversionKey` and `RecipeHash` for ONNX files

Binding record (`ImportBindingMetadata`): owner id and version, model id, requested revision, resolved commit, Sentis version, recipe set hash, selection hash, and the shared record path.

The shared record covers the union of every owner's selection for one repository and commit. A binding exposes only its own owner's selection. The runtime registry takes files per owner.

## Stale detection

Import and status use the same two checks, `ProjectImportService.DescribeStaleBinding` and `DescribeStaleShared`. Each returns null when the record is current, or the message that says why it is stale.

The shared record path holds the repository, the resolved commit, the Sentis format generation and the recipe set. So a binding is current when it points at the expected shared record and has the same requested revision and selection hash (selected and enabled optional files). The binding stores no manifest hash, so other manifest edits leave it current. The binding's recorded commit, Sentis version and recipe set only explain a path that differs. Records keep the exact Sentis version that imported them, for diagnostics.

A shared import is stale when the raw snapshot root, the repository inventory or the selected model file union changes, when a runtime file record is incomplete, or when a runtime model file is missing. Status checks `FilePath` on disk, so deleting a converted model makes the model need **Convert** again.

## Removal

[ProjectRemovalService.cs](../tools.doji.model-hub/Editor/Import/ProjectRemovalService.cs) deletes this project's bindings and shared records. Converted models stay in the store, because other projects may use them. Pulled copies (`PulledModels`) are deleted only when no binding for the repository remains.

## Pulled copies

[PulledModels.cs](../tools.doji.model-hub/Editor/Import/PulledModels.cs) copies runtime models to `Assets/StreamingAssets/model-hub/models/<owner>/<name>/<repoPath with .sentis>`, the same relative path a build uses. `RuntimeRegistryBuilder` checks that path: a pulled file is not bundled again, and the Editor catalog points at it.

## Revision conflicts

One project may request one revision per repository. With conflicting revisions, the repository is not imported or removed, and status shows **Conflict** for each row.

## Selection gating

A dependency whose model files are all optional and none enabled is **Choose files**, not an error. Sync skips it and the setup prompt ignores it.
