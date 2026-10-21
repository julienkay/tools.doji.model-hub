# For Package Maintainers

Package maintainers declare model dependencies without shipping large model files in the Unity package.

Create `hf-hub-manifest.json` at the package root. HF Hub discovers that file when the package is installed in a Unity project.

```text
Packages/com.example.my-package/hf-hub-manifest.json
```

## Minimal Manifest

This manifest requires the full repository inventory for `hf-internal-testing/tiny-random-bert` at `main`.

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

Omitting `files` means whole-repository scope. HF Hub caches every repo file for the resolved revision, then imports only `.onnx` files into `.sentis` runtime output.

## Selected Files

Use `files` when the package needs only a known subset of repository files.

```json
{
  "version": 1,
  "models": [
    {
      "id": "onnx-community/tiny-clip-test-ONNX",
      "revision": "main",
      "files": [
        {
          "path": "onnx/model.onnx"
        },
        {
          "path": "preprocessor_config.json"
        }
      ]
    }
  ]
}
```

Selected non-ONNX files are downloaded and tracked in raw cache when they are part of dependency scope. They are not converted to `.sentis`.

## Optional Variants

Mark a selected file as optional when the package can work with more than one model variant and the Unity project should choose which variant to include.

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

The `optional` flag does not mean "download if present." It means "show this selected repo file as project-selectable." Until the project enables an optional file, HF Hub ignores that file for status, download, import, runtime registry output, and startup prompts.

If every ONNX file in a dependency is optional, Model Hub shows `Choose files` until the project turns on at least one optional ONNX file.

## Authoring In Unity

Select a writable `hf-hub-manifest.json` package asset in Unity to use the focused manifest Inspector. Installed package cache entries are read-only until the package source is embedded or otherwise made local and writable.

The file picker uses one repository file list:

- checked rows are selected by the manifest
- unchecked rows stay visible but greyed out
- `Optional` is available only on selected rows
- leaving every row unchecked keeps whole-repository scope

Use [Manifest Schema](manifest-reference.md) for field-level rules and validation details.
