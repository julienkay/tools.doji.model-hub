# Manifest Schema

AI Model Hub manifest entries describe Hugging Face repositories and the files a package or project uses from each resolved repository snapshot.

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
- `id`: canonical HuggingFace model identifier. In Hub API terms this is the `repo_id` string such as `owner/name`. The repository must be public: AI Model Hub sends no access token, so gated and private models fail to download with a message that says so.
- `revision`: optional branch, tag, or commit to resolve. Defaults to `main`.
- `files`: optional exact repo-relative file objects. Omit this field to require the whole repository. If present, each entry has `path` and optional `optional`.
- `files[].path`: exact repo-relative file path.
- `files[].optional`: `true` when the project owner may enable that file before download or import. Omitted or `false` means the file is always required once the manifest entry is present.

Model ids must be unique inside one manifest. File paths must be unique inside one model entry after trimming whitespace and converting `\` to `/`.

AI Model Hub rejects a manifest that contains any other property, or an `optional` value that is not `true` or `false`. A misspelled key would otherwise be ignored, and a model entry that lost its `files` would download the whole repository. The Model Hub window shows the error for the package that owns the manifest.

## Whole-Repository Scope

Omit `files` to require the whole repository file inventory for the resolved revision.

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

Whole-repository dependencies download every repository file for that revision. The repository's model files must all have one format; see [Model files](#model-files).

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

Selecting an `.onnx` file also selects its external weight files when the repository has them: `<file>.onnx_data`, `<file>.onnx.data`, and numbered parts such as `<file>.onnx_data_1`. Do not list them yourself.

A repository can contain both formats. `julienkay/sentis-MiDaS`, for example, has ONNX files under `onnx/` and Sentis files under `sentis/`. List the files you use, and each one loads by its own format:

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
| A whole-repository dependency on a repository with both `.onnx` and `.sentis` files | ``Repository `<id>` has both ONNX and Sentis files. List the files you use in the manifest.`` |
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

If a dependency declares only optional model files and none are enabled yet, AI Model Hub shows `Choose files` in Model Hub instead of downloading the whole repository. That state does not open the startup auto prompt by itself.
