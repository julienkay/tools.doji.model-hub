# For Package Maintainers

Package maintainers declare model dependencies without shipping large model files in the Unity package. Package code then loads the models by repository id and file path; the project that uses the package decides whether builds bundle them or download them at runtime.

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

Omitting `files` means whole-repository scope. HF Hub downloads every repository file for the resolved revision. `.onnx` files are converted to Sentis models, and `.sentis` files are used as downloaded. A repository with both formats needs a `files` list.

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

Selected files that are not models, such as `preprocessor_config.json`, are downloaded with the model.

If you convert a model yourself and publish the `.sentis` file on Hugging Face, list that file. Projects then use it as downloaded, without converting it.

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

If every model file in a dependency is optional, Model Hub shows `Choose files` until the project turns on at least one.

## Authoring In Unity

Select a writable `hf-hub-manifest.json` package asset in Unity to use the focused manifest Inspector. Installed package cache entries are read-only until the package source is embedded or otherwise made local and writable.

The file picker uses one repository file list:

- checked rows are selected by the manifest
- unchecked rows stay visible but greyed out
- `Optional` is available only on selected rows
- leaving every row unchecked keeps whole-repository scope

## Load Models In Package Code

Load a model by the repository id and the path the manifest lists:

```csharp
using Doji.AI.ModelHub;
using Unity.InferenceEngine;

Model model = await Hub.LoadModelAsync("onnx-community/tiny-clip-test-ONNX", "onnx/model.onnx");
```

Use `LoadModelAsync`: it works on every platform and for both delivery modes. If another package uses the same repository, pass your package id as the first argument. See [Load models at runtime](import-cache-runtime.md#load-models-at-runtime).

Use [Manifest Schema](manifest-reference.md) for field-level rules and validation details.
