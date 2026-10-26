# For Package Maintainers

Package maintainers declare model dependencies without shipping large model files in the Unity package. Package code then loads the models by model id and file path; the project that uses the package decides whether builds bundle them or download them at runtime.

Create `model-hub-manifest.json` at the package root. AI Model Hub discovers that file when the package is installed in a Unity project.

```text
Packages/com.example.my-package/model-hub-manifest.json
```

## Minimal Manifest

This manifest requires every file of `hf-internal-testing/tiny-random-bert` at `main`.

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

Omitting `files` means the package uses every file of the model. AI Model Hub downloads every file for the resolved revision. `.onnx` files are converted to Sentis models, and `.sentis` files are used as downloaded. A repository with both formats needs a `files` list.

## Selected Files

Use `files` when the package needs only some files of the model.

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

The `optional` flag does not mean "download if present." It means "show this selected repo file as project-selectable." Until the project enables an optional file, AI Model Hub ignores that file for status, download, import, runtime registry output, and startup prompts.

If every model file in a dependency is optional, Model Hub shows `Choose files` until the project turns on at least one.

Give model files a `label` to name them in model pickers, for example `"label": "MiDaS v2.1 Small (63 MB)"`. See [Let users pick a model in the Inspector](package-model-api.md#let-users-pick-a-model-in-the-inspector).

## Authoring In Unity

Select a writable `model-hub-manifest.json` package asset in Unity to use the focused manifest Inspector. Installed package cache entries are read-only until the package source is embedded or otherwise made local and writable.

The file picker uses the model's file list:

- checked rows are selected by the manifest
- unchecked rows stay visible but greyed out
- `Optional` is available only on selected rows
- leaving every row unchecked uses every file of the model

## Load Models In Package Code

Load a model by the model id and the path the manifest lists:

```csharp
using Doji.AI.ModelHub;
using Unity.InferenceEngine;

Model model = await Hub.LoadModelAsync("onnx-community/tiny-clip-test-ONNX", "onnx/model.onnx");
```

Use `LoadModelAsync`: it works on every platform and for both delivery modes. If another package uses the same model, pass your package id as the first argument. See [Load models at runtime](import-cache-runtime.md#load-models-at-runtime).

[Design Your Package's Model API](package-model-api.md) shows which constructors and methods to offer, so users can load your package's models, pick a variant in the Inspector, or bring their own model.

Use [Manifest Schema](manifest-reference.md) for field-level rules and validation details.
