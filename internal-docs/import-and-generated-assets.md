# Import and Converted Models

Import turns the selected model files of one dependency into **runtime models**: the `.sentis` files that `ModelLoader` reads. It writes nothing into `Assets`.

| Term | Meaning |
|---|---|
| Model file | A source file HF Hub can load: `.onnx` (converted) or `.sentis` (used as downloaded). `ModelFiles.GetFormat` decides by extension. |
| Companion file | A file a model file needs, such as ONNX external weights. `ModelFiles.GetCompanionFiles` finds `<x>.onnx_data`, `<x>.onnx.data`, and `<x>.onnx_data_<n>` next to `<x>.onnx`. `GetSelectedRepoPaths` adds them to the selection. |
| Runtime model | A downloaded `.sentis` blob, or the converted output of an `.onnx` file. |
| Converted store | `<data-root>/converted/`, shared by every project on the machine. |

## Flow

[ProjectImportService.cs](../tools.doji.hf-hub/Editor/Import/ProjectImportService.cs):

1. Resolve the selection (`GetSelectedModelRepoPaths`). `GetSelectionProblem` rejects a whole-repository dependency on a mixed repository and a selection without model files.
2. For each `.sentis` file: record the cached snapshot path as the runtime model. No conversion.
3. For each `.onnx` file: compute the conversion key, reuse `ConvertedModelStore.TryGet` when it hits, otherwise convert with the Editor importer, apply the recipe with `OnnxConversionSettings.Apply`, and `ConvertedModelStore.Save`.
4. Write the shared import record and one binding per owner.

A conversion failure skips that file when another ONNX file converts. The model stays **Ready** with a warning, and `SkippedRepoPaths` keeps status from retrying it every time. Import fails only when no ONNX file converts.

## Conversion key

[ConvertedModelStore.cs](../tools.doji.hf-hub/Runtime/Conversion/ConvertedModelStore.cs) is in `Runtime`, so players use the same store.

```text
key  = hex(sha256(etag | converterId | recipeHash)[0..16])
path = <data-root>/converted/<models--owner--name, lowercase>/<key>.sentis
       <data-root>/converted/<models--owner--name, lowercase>/<key>.json
```

- `etag` is the source blob name: sha256 for LFS files, git sha1 otherwise. `SourceFileIdentity` reads it from the index, else from the snapshot link target, else hashes the content.
- `converterId` names the converter and everything that changes its output. Editor: `unity-inference-editor-v2@<inference version>`. Player: `doji-onnx-model-loader@<loader version>+inference@<inference version>`. A new Inference Engine version never reuses an old conversion.
- `recipeHash` covers quantization (normalized: `int8` maps to `uint8`) and fixed input shapes.
- `Save` takes the blob lock, writes `<key>.sentis.incomplete`, and renames. The `.json` record names the source path, etag, converter, and recipe.
- Converted files never enter the Hugging Face cache, so Python tools never see them.

`OnnxConversionSettings.Apply` uses public APIs only. `ModelQuantizer.QuantizeWeights` replaces the model, so `Apply` returns the model to use.

## Import records

Import storage is split in two, under `Library/Doji/HfHub/imports/`:

- `shared/<repo>/<hash>/hf-hub-shared-import.json`: one per repository snapshot. `<hash>` is the first 8 bytes of `sha256(commit | sentisVersion | recipeSetHash)`, short enough for Windows `MAX_PATH`.
- `bindings/<owner-id>/<model-id>/hf-hub-binding.json`: one per owner dependency.

Shared record (`SharedImportMetadata`): repository, resolved commit, snapshot root, repository file list, Sentis and Unity version, recipe set hash, diagnostics, `SkippedRepoPaths`, and `RuntimeFiles`.

Each `ImportedRuntimeFileMetadata` holds:

- `RepoPath` and `Format` (`onnx` or `sentis`)
- `FilePath`: absolute path of the runtime model (converted store or cache snapshot)
- `SourceEtag`, `SourceSizeInBytes`, `Companions` (repo path, etag, size): what a player needs to download the same files
- `ConversionKey` and `RecipeHash` for ONNX files

Binding record (`ImportBindingMetadata`): owner id and version, model id, manifest hash, requested revision, resolved commit, Sentis version, recipe set hash, selection hash, and the shared record path.

The shared record covers the union of every owner's selection for one repository and commit. A binding exposes only its own owner's selection. The runtime registry takes files per owner.

## Stale detection

A binding is stale when the manifest hash, requested revision, selection, enabled optional files, resolved commit, Sentis version, or shared record path changes.

A shared import is stale when the resolved commit, the selected model file union, the recipe set, or the Sentis version changes, or when a runtime model file is missing. Status checks `FilePath` on disk, so deleting a converted model makes the model need **Import** again.

## Removal

[ProjectRemovalService.cs](../tools.doji.hf-hub/Editor/Import/ProjectRemovalService.cs) deletes this project's bindings and shared records. Converted models stay in the store, because other projects may use them. Pulled copies (`PulledModels`) are deleted only when no binding for the repository remains.

## Pulled copies

[PulledModels.cs](../tools.doji.hf-hub/Editor/Import/PulledModels.cs) copies runtime models to `Assets/StreamingAssets/hf-hub/models/<owner>/<name>/<repoPath with .sentis>`, the same relative path a build uses. `RuntimeRegistryBuilder` checks that path: a pulled file is not bundled again, and the Editor catalog points at it.

## Old project output

[LegacyProjectOutputCleanup.cs](../tools.doji.hf-hub/Editor/Import/LegacyProjectOutputCleanup.cs) runs once per Editor session (not in batch mode). It deletes `Assets/StreamingAssets/hf-hub/registries/` and old `Assets/StreamingAssets/hf-hub/<owner>/<name>/` folders that hold only `.sentis` files. It never touches `hf-hub/models/`.

## Revision conflicts

One project may request one revision per repository. With conflicting revisions, the repository is not imported, reimported, or removed, and status shows **Conflict** for each row.

## Selection gating

A dependency whose model files are all optional and none enabled is **Choose files**, not an error. `Import All` skips it and the startup prompt ignores it.
