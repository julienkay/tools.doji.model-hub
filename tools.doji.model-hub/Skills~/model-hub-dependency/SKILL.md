---
name: model-hub-dependency
description: Declare Hugging Face model dependencies for a Unity package or project with AI Model Hub (`tools.doji.model-hub`): write `model-hub-manifest.json` with model ids, revisions, file selection, optional variants, picker labels, and per-file conversion defaults (quantization, fixed input shapes), then load the models in code. Use when adding, changing, or reviewing a model dependency, or when a model should ship quantized.
---

# Declare an AI Model Hub dependency

Put `model-hub-manifest.json` at the package root (`Packages/<package-id>/`). For a project-owned dependency, use `ProjectSettings/Packages/tools.doji.model-hub/model-hub-manifest.json` instead. Never commit downloaded models or `.sentis` output.

```json
{
  "version": 1,
  "models": [
    {
      "id": "owner/name",
      "revision": "main",
      "files": [
        { "path": "config.json" },
        {
          "path": "onnx/model_small.onnx",
          "optional": true,
          "label": "Small (63 MB)",
          "conversion": { "quantization": "float16", "inputShapes": { "input": [1, 3, 256, 256] } }
        }
      ]
    }
  ]
}
```

| Key | Rule |
|---|---|
| `version` | Required, `1`. |
| `id` | Required. Public Hugging Face `repo_id`. Gated and private models fail. Unique per manifest. |
| `revision` | Branch, tag, or commit. Default `main`. Pin a commit for reproducible builds. |
| `files` | Omit to take every file of the repo. A repo with both `.onnx` and `.sentis` files then fails: list the files. |
| `files[].path` | Exact repo-relative path. `.onnx` is converted, `.sentis` is used as is, others are downloaded only. ONNX external weights (`.onnx_data`, `.onnx.data`) come along: do not list them. |
| `files[].optional` | `true` lets the project turn the file on. It is ignored until the project does. If all model files are optional, Model Hub shows `Choose files`. |
| `files[].label` | Name in model pickers (`[HubModel]` dropdown, `HubModelFile.DisplayName`). |
| `files[].conversion` | `.onnx` only. Default conversion for every project. |
| `conversion.quantization` | `none` (default), `float16`, or `uint8`. Exact lowercase. `int8` is invalid. |
| `conversion.inputShapes` | Input name to the full dimension list. `-1` keeps a dimension dynamic. |

Rules:

- Unknown keys fail validation, at every level. A typo never gets ignored silently.
- `conversion` needs at least one key. To quantize, set it in the manifest, not in a project override: a project override is per project and does not ship with the package.
- Order of effect: project override (Model Hub **Files** tab), then the manifest `conversion`, then the built-in default. Manifests that set different `conversion` for the same file fall back to the built-in default until the project overrides it.
- Use the selected `.sentis` file instead of `conversion` when the repo already ships a converted model.
- Match quantization to the source. Inference Engine expands fp16 ONNX weights to float32: for an fp16 file, set `float16` to keep its size. Quantized ONNX files (`MatMulNBits`, `QLinear*`, `DequantizeLinear`; often `_q4`, `_int8`, `_bnb4`) cannot be converted: select the float file and set `quantization`. Import warns when quantization changes no weight; the Files tab shows weight sizes before and after.

Load a model in package code by id and the exact listed path:

```csharp
Model model = await Hub.LoadModelAsync("owner/name", "onnx/model_small.onnx");
```

Pass your package id first when another package may use the same model. To check a manifest, open Model Hub (`Window > AI Model Hub`): validation errors appear on the owning package.

To download and set up the declared models from a terminal or CI, use the sibling skill `model-hub-cli` (`unity command model_hub_status`, `model_hub_sync`, `model_hub_sync_status`).

Full reference: `Documentation` link of the package, pages `manifest-reference` and `package-model-api`.
