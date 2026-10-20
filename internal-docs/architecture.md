# Architecture

## Assemblies

| Assembly | Namespace | Contents |
|---|---|---|
| `Doji.ModelHub` | `Doji.AI.ModelHub` | `Runtime/Manifest`, `Runtime/Core`, `Runtime/Locator`, `Runtime/Conversion` |
| `Doji.ModelHub.Editor` | `Doji.AI.ModelHub.Editor` | `Editor/Discovery`, `Cache`, `Sync`, `Import`, `Status`, `InferenceBridge`, `UI` |
| `Doji.ModelHub.Editor.Tests` | `Doji.AI.ModelHub.Editor.Tests` | EditMode tests in `Tests/Editor` |

The companion project references the package and its dependencies with
`file:` and lists them in `testables`.

## Dependencies

| Package | Use |
|---|---|
| `tools.doji.downloads` | Resumable transfer, persisted queue state, file locks, Editor `Progress` operations |
| `tools.doji.formats.onnx` | `OnnxModelSignatureReader`: ONNX input shapes for the import UI, without loading weights |
| `com.unity.ai.inference` | ONNX to `.sentis` conversion and runtime `Model` loading |
| `com.unity.nuget.newtonsoft-json` | Manifest, metadata and registry JSON |

Generic download behavior belongs in `tools.doji.downloads`, not here.
HF Hub owns HuggingFace resolution, cache policy, prompts, status, and
import orchestration. Robust downloads are still core to HF Hub, so treat
regressions there as HF Hub bugs.

## Flow

1. **Discover.** Packages declare dependencies in `hf-hub-manifest.json` at
   the package root. Projects declare them in
   `ProjectSettings/Packages/tools.doji.hf-hub/hf-hub-manifest.json` or in
   `ProjectManifestAsset` assets. See [manifest-schema.md](manifest-schema.md).
2. **Resolve and download.** The Hub API resolves the revision to a commit.
   Only the selected files are downloaded, into a shared per-user cache with
   HuggingFace-style `refs/` and `snapshots/` folders. Sync runs in the
   background and resumes after a domain reload. See
   [download-and-cache.md](download-and-cache.md).
3. **Import.** Selected ONNX files are converted to `.sentis` under
   `Assets/StreamingAssets/hf-hub/<owner>/<model>/`, with import metadata
   for stale detection. See
   [import-and-generated-assets.md](import-and-generated-assets.md) and
   [per-file-conversion-recipes.md](per-file-conversion-recipes.md).
4. **Load.** Sync writes per-owner registries under
   `Assets/StreamingAssets/hf-hub/registries/`. Runtime code resolves models
   by `repo_id` through `Hub`. See [runtime-locator.md](runtime-locator.md).

Status (`Ready`, `Missing`, `Stale`, `Failed`, conflict states) is computed
locally, without network access. One project must request one revision per
repository; conflicting revisions block import and removal for that
repository. See [editor-model-hub-ui.md](editor-model-hub-ui.md).

## Editor surfaces

- `Window > AI Model Hub`
- `Project Settings > Doji > AI > AI Model Hub`
- `Tools > Doji > HF Hub`: `Import All`, `Show Status`, `Open Project Settings`
- custom Inspector for writable `hf-hub-manifest.json` files

## Conventions

- `.sentis` output depends on the installed Sentis version. Import metadata
  records it; a version change marks the import stale.
- Never load full ONNX models to show files, recipes, or import settings.
  Read signatures through `tools.doji.formats.onnx`.
- Avoid long-lived references to ONNX `ModelAsset` objects; they force
  import work on domain reload.
- Use `huggingface_hub` (checked out at `references/huggingface_hub`) as the
  reference for Hub API, download, and cache behavior.

## Known limits

- Runtime lookup reads registries and models with `File` and `Directory`
  under `Application.streamingAssetsPath`
  ([RuntimeLocatorService.cs](../tools.doji.hf-hub/Runtime/Locator/RuntimeLocatorService.cs)).
  This works in the Editor and on desktop, not on Android or Web, where
  StreamingAssets needs `UnityWebRequest`.
- Repository ids map to cache folders by replacing `/` with `--`
  ([CachePaths.cs](../tools.doji.hf-hub/Editor/Cache/CachePaths.cs)). The
  mapping is not reversible, so two distinct ids can collide.
- Runtime ONNX conversion is not supported. `RuntimeOnnxModelConverter` is a
  placeholder; see [roadmap.md](roadmap.md).
