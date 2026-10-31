# Manifest Schema

AI Model Hub manifest entries describe Hugging Face models and the files a package or project uses from each model at a resolved commit.

## Manifest Types

AI Model Hub discovers three manifest types in one Unity project.

## Project Settings Manifest

Use the Project Settings manifest when model dependencies belong to the current Unity project itself.

- location: `ProjectSettings/Packages/tools.doji.model-hub/model-hub-manifest.json`
- edit from: `Project Settings > Doji > AI > AI Model Hub`
- add from: Model Hub `+ Add Model`

This is the main project-scoped manifest.

## Package Manifest

Use a package manifest when model dependencies belong to a Unity package.

- filename: `model-hub-manifest.json`
- location: package root
- discovered from: installed packages in the project
- edit from: custom Inspector when selecting that JSON manifest

AI Model Hub only lets you author package manifests directly when the package source is local and writable from the current project. Package cache entries stay read-only until you embed or otherwise localize that package source.

## ScriptableObject Manifest

Use `ProjectManifestAsset` when model dependencies need to ship through non-package project content, for example older Unity asset or `.unitypackage` style distribution.

- create from: `Assets > Create > Doji > AI > AI Model Hub Project Manifest`
- lives under: normal Unity assets, typically somewhere in `Assets/`
- discovered in addition to the Project Settings JSON manifest and package manifests
- edit from: same focused manifest Inspector used by package JSON manifests

## Fields

- `version`: manifest schema version. Current value is `1`.
- `id`: canonical HuggingFace model identifier. In Hub API terms this is the `repo_id` string such as `owner/name`. The model must be public: AI Model Hub sends no access token, so gated and private models fail to download with a message that says so.
- `revision`: optional branch, tag, or commit to resolve. Defaults to `main`.
- `files`: optional exact repo-relative file objects. Omit this field to require every file of the model. If present, each entry has `path`, and optionally `optional`, `label`, and `conversion`.
- `files[].path`: exact repo-relative file path.
- `files[].optional`: `true` when the project owner may enable that file before download or import. Omitted or `false` means the file is always required once the manifest entry is present.
- `files[].label`: optional name for a model file in model pickers, such as the `[HubModel]` Inspector dropdown and `HubModelFile.DisplayName`. For example `"Small (63 MB)"`. Leading and trailing spaces are removed, and an empty label counts as none.
- `files[].conversion`: optional conversion settings for an `.onnx` file. See [Conversion Settings](#conversion-settings).

Model ids must be unique inside one manifest. File paths must be unique inside one model entry after trimming whitespace and converting `\` to `/`.

AI Model Hub rejects a manifest that contains any other property, an `optional` value that is not `true` or `false`, a `label` that is not a string, or an invalid `conversion`. A misspelled key would otherwise be ignored, and a model entry that lost its `files` would download the whole repository. The Model Hub window shows the error for the package that owns the manifest.

## All Files

Omit `files` to require every file of the model at the resolved revision.

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

Dependencies without `files` download every file of the model at that revision. The model's `.onnx` and `.sentis` files must all have one format; see [Model files](#model-files).

## Explicit File Scope

Include `files` to require only exact repo-relative files.

```json
{
  "version": 1,
  "models": [
    {
      "id": "onnx-community/tiny-clip-test-ONNX",
      "files": [
        {
          "path": "onnx/model.onnx"
        }
      ]
    }
  ]
}
```

AI Model Hub normalizes selected paths before storing them:

- trims whitespace
- converts `\` to `/`
- removes duplicates
- sorts paths with ordinal comparison

Selected files that are not model files, such as `config.json` or tokenizer files, are downloaded into the cache with the model files.

## Model files

The extension of a selected file decides how AI Model Hub uses it:

- `.onnx`: converted to a Sentis model on import. Conversion settings apply.
- `.sentis`: loaded as downloaded. There is no import step and no conversion setting.
- any other extension: downloaded, not loaded as a model.

Selecting an `.onnx` file also selects its external weight files when the model has them: `<file>.onnx_data`, `<file>.onnx.data`, and numbered parts such as `<file>.onnx_data_1`. Do not list them yourself.

A model can contain both formats. `julienkay/sentis-MiDaS`, for example, has ONNX files under `onnx/` and Sentis files under `sentis/`. List the files you use, and each one loads by its own format:

```json
{
  "version": 1,
  "models": [
    {
      "id": "julienkay/sentis-MiDaS",
      "files": [
        { "path": "sentis/midas_v21_small_256.sentis" }
      ]
    }
  ]
}
```

Validation fails in two cases. Model Hub shows the dependency as **Error** with the message:

| Case | Message |
|---|---|
| A dependency without `files` on a model with both `.onnx` and `.sentis` files | ``Model `<id>` has both ONNX and Sentis files. List the files you use in the manifest.`` |
| The selection has no `.onnx` or `.sentis` file | ``Dependency `<id>` does not select any model file. Select at least one `.onnx` or `.sentis` file.`` |

AI Model Hub never picks a format for you. Package code loads a file by the path the manifest lists, for example `Hub.LoadModelAsync("julienkay/sentis-MiDaS", "sentis/midas_v21_small_256.sentis")`.

## Optional File Scope

Set `files[].optional` when package authors want project-owned pre-download choice without splitting one repo into duplicate manifest rows.

```json
{
  "version": 1,
  "models": [
    {
      "id": "julienkay/sentis-MiDaS",
      "files": [
        {
          "path": "config.json"
        },
        {
          "path": "onnx/midas_v21_small_256.onnx",
          "optional": true
        },
        {
          "path": "onnx/dpt_swin2_tiny_256.onnx",
          "optional": true
        }
      ]
    }
  ]
}
```

Required and optional are both part of the manifest's explicit file scope. The `optional` flag means "show this selected repo file as project-selectable."

Until the project enables an optional file, AI Model Hub ignores that file for status, download, import, builds, and startup prompts. After the project enables it, the file behaves like any other selected file, and a missing download or conversion requires action.

If a dependency declares only optional model files and none are enabled yet, AI Model Hub shows `Choose files` in Model Hub instead of downloading every file of the model. That state does not open the startup auto prompt by itself.

## Conversion Settings

Set `files[].conversion` on an `.onnx` file to choose how AI Model Hub converts it to Sentis. Use it when the model is known to work well quantized, or needs fixed input dimensions.

```json
{
  "version": 1,
  "models": [
    {
      "id": "julienkay/sentis-MiDaS",
      "files": [
        {
          "path": "onnx/midas_v21_small_256.onnx",
          "conversion": {
            "quantization": "float16",
            "inputShapes": { "input": [1, 3, 256, 256] }
          }
        }
      ]
    }
  ]
}
```

- `quantization`: `none`, `float16`, or `uint8`. Omitted means `none`. Lower precision makes the converted model smaller and can reduce accuracy.
- `inputShapes`: maps a model input name to its full dimension list. Each dimension is a size, or `-1` to keep it dynamic. AI Model Hub skips a shape whose input name or rank does not match the model, and reports a warning.

`conversion` must set at least one of the two keys. Other keys, other quantization names, and `conversion` on a file that is not `.onnx` fail validation.

The manifest settings are defaults. A project can override them per file in the Model Hub **Files** tab. AI Model Hub uses, in order:

1. the project override
2. the manifest's `conversion`
3. no quantization and the input shapes of the ONNX file

If two manifests set different `conversion` for the same file of the same model, neither wins: AI Model Hub converts with the built-in settings, and the **Files** tab shows `Built-in Default (manifests disagree: ...)` with the manifest owners. A project override settles it. A manifest without `conversion` for that file does not count.

Changing `conversion` converts the file again. Builds and players that download the model use the same settings.
