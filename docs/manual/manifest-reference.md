# Manifest Schema

HF Hub manifest entries describe HuggingFace repositories and the file scope to import from each resolved repository snapshot.

## Manifest Types

HF Hub discovers three manifest types in one Unity project.

## Project Settings Manifest

Use the Project Settings manifest when model dependencies belong to the current Unity project itself.

- location: `ProjectSettings/Packages/tools.doji.hf-hub/hf-hub-manifest.json`
- edit from: `Project Settings > Doji > AI > AI Model Hub`
- add from: Model Hub `+ Add Model`

This is the main project-scoped manifest.

## Package Manifest

Use a package manifest when model dependencies belong to a Unity package.

- filename: `hf-hub-manifest.json`
- location: package root
- discovered from: installed packages in the project
- edit from: custom Inspector when selecting that JSON manifest

HF Hub only lets you author package manifests directly when the package source is local and writable from the current project. Package cache entries stay read-only until you embed or otherwise localize that package source.

## ScriptableObject Manifest

Use `ProjectManifestAsset` when model dependencies need to ship through non-package project content, for example older Unity asset or `.unitypackage` style distribution.

- create from: `Assets > Create > Doji > AI > HF Hub Project Manifest`
- lives under: normal Unity assets, typically somewhere in `Assets/`
- discovered in addition to the Project Settings JSON manifest and package manifests
- edit from: same focused manifest Inspector used by package JSON manifests

## Fields

- `version`: manifest schema version. Current value is `1`.
- `id`: canonical HuggingFace model identifier. In Hub API terms this is the `repo_id` string such as `owner/name`.
- `revision`: optional branch, tag, or commit to resolve. Defaults to `main`.
- `files`: optional exact repo-relative file objects. Omit this field to require the whole repository. If present, each entry has `path` and optional `optional`.
- `files[].path`: exact repo-relative file path.
- `files[].optional`: `true` when the project owner may enable that file before download or import. Omitted or `false` means the file is always required once the manifest entry is present.

Model ids must be unique inside one manifest. File paths must be unique inside one model entry after trimming whitespace and converting `\` to `/`.

HF Hub rejects a manifest that contains any other property, or an `optional` value that is not `true` or `false`. A misspelled key would otherwise be ignored, and a model entry that lost its `files` would download the whole repository. The Model Hub window shows the error for the package that owns the manifest.

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

Whole-repository dependencies cache every repository file for that revision. HF Hub imports only repo paths whose extension is `.onnx`.

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

HF Hub normalizes selected paths before storing them:

- trims whitespace
- converts `\` to `/`
- removes duplicates
- sorts paths with ordinal comparison

Selected non-ONNX files still matter. HF Hub downloads and tracks them in raw cache when they are part of dependency scope, but it does not expose import settings for them and does not generate runtime `.sentis` output from them.

If dependency scope contains no `.onnx` file at all, HF Hub treats that dependency as not importable and surfaces a `This dependency does not currently select any ONNX file.` diagnostic in Model Hub.

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

Until the project enables an optional file, HF Hub ignores that file for status, download, import, generated runtime registries, and startup prompts. After the project enables it, the file behaves like any other selected dependency file and missing cache or generated output requires action.

If a dependency declares only optional ONNX files and none are enabled yet, HF Hub shows `Choose files` in Model Hub instead of downloading the whole repository. That state does not open the startup auto prompt by itself.
