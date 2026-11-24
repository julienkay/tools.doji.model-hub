# Architecture

## Assemblies

| Assembly | Namespace | Contents |
|---|---|---|
| `Doji.ModelHub` | `Doji.AI.ModelHub` | `Runtime/Manifest`, `Core`, `Cache`, `Locator`, `Conversion` |
| `Doji.ModelHub.Editor` | `Doji.AI.ModelHub.Editor` | `Editor/Discovery`, `Cache`, `Sync`, `Import`, `Build`, `Status`, `InferenceBridge`, `UI`, `Automation` |
| `Doji.ModelHub.Editor.Cli` | `Doji.AI.ModelHub.Editor.Cli` | `Editor/Cli`: Unity CLI commands. Compiles only with `com.unity.pipeline` (`MODEL_HUB_PIPELINE`). |
| `Doji.ModelHub.Editor.Tests` | `Doji.AI.ModelHub.Editor.Tests` | EditMode tests in `Tests/Editor` |

The companion project references the package and its dependencies with
`file:` and lists them in `testables`.

## Dependencies

| Package | Use |
|---|---|
| `tools.doji.downloads` | Resumable transfer, persisted queue state, file locks, Editor `Progress` operations |
| `tools.doji.formats.onnx` | `OnnxModelSignatureReader`: ONNX input shapes for the import UI, without loading weights |
| `com.unity.ai.inference` | ONNX to `.sentis` conversion in the Editor and runtime `Model` loading |
| `tools.doji.onnx-model-loader` | ONNX conversion, in the Editor and in players |
| `com.unity.nuget.newtonsoft-json` | Manifest, metadata and registry JSON |
| `com.unity.pipeline` (optional) | Unity CLI commands, behind the `MODEL_HUB_PIPELINE` version define. See [command-line-automation.md](command-line-automation.md). |

Generic download behavior belongs in `tools.doji.downloads`, not here.
AI Model Hub owns HuggingFace resolution, cache policy, prompts, status, and
import orchestration. Robust downloads are still core to AI Model Hub, so treat
regressions there as AI Model Hub bugs.

## Flow

1. **Discover.** Packages declare dependencies in `model-hub-manifest.json` at
   the package root. Projects declare them in
   `ProjectSettings/Packages/tools.doji.model-hub/model-hub-manifest.json` or in
   `ProjectManifestAsset` assets. See [manifest-schema.md](manifest-schema.md).
2. **Resolve and download.** The Hub API resolves the revision to a commit.
   Only the selected files are downloaded, into a shared per-user cache with
   HuggingFace-style `refs/` and `snapshots/` folders. Sync runs in the
   background and resumes after a domain reload. See
   [download-and-cache.md](download-and-cache.md).
3. **Import.** The extension decides each file's format. Selected `.onnx`
   files are converted into the shared converted store in the data root,
   keyed by source etag, converter, and recipe. `.sentis` files are used as
   downloaded. Import records go to `Library/Doji/ModelHub/imports/`; nothing
   goes into `Assets`. See
   [import-and-generated-assets.md](import-and-generated-assets.md) and
   [per-file-conversion-recipes.md](per-file-conversion-recipes.md).
4. **Build.** A `BuildPlayerProcessor` writes the runtime registry and adds
   bundled runtime models to the build's StreamingAssets from the shared
   folders. Each model is bundled or downloaded at runtime, as Project
   Settings and per-model overrides say.
5. **Load.** `Hub.LoadModelAsync(repoId, repoPath)` finds the file in the
   registry (in the Editor, one built from import records), then loads it
   from StreamingAssets or downloads, converts, and caches it on the
   device. See [runtime-loading.md](runtime-loading.md).

Status (`Ready`, `Missing`, `Stale`, `Failed`, conflict states) is computed
locally, without network access. One project must request one revision per
repository; conflicting revisions block import and removal for that
repository. See [editor-model-hub-ui.md](editor-model-hub-ui.md).

## Editor surfaces

- `Window > AI Model Hub`
- `Project Settings > Doji > AI > AI Model Hub`
- `Tools > Doji > AI Model Hub`: `Set Up All Models`, `Show Status`, `Open Project Settings`
- custom Inspector for writable `model-hub-manifest.json` files
- Unity CLI commands `model_hub_*` when `com.unity.pipeline` is installed

## Conventions

- Converted `.sentis` output is keyed by Sentis **format generation**, not
  by exact version (`SentisCompatibility`). Updates within a generation
  reuse conversions; AI Model Hub never deletes old ones on its own (`HubStorage`).
- Never load full ONNX models to show files, recipes, or import settings.
  Read signatures through `tools.doji.formats.onnx`.
- Avoid long-lived references to ONNX `ModelAsset` objects; they force
  import work on domain reload.
- Use `huggingface_hub` (checked out at `references/huggingface_hub`) as the
  reference for Hub API, download, and cache behavior.
- In user-facing text (UI labels, tooltips, dialogs, status and error
  messages, user docs), a Hugging Face model repository is a **model**, not a
  repository. Hugging Face asks authors to put each model variant in its own
  repository with its own `hf.co/org/model-name` URL
  ([Model release checklist](https://github.com/huggingface/hub-docs/blob/main/docs/hub/model-release-checklist.md)),
  so one repository is one model in the normal case. A repository that holds
  several model files, for example ONNX and Sentis variants, is still one
  model with several files. Say "repository" only where the Hub concept
  matters: `repo_id`, datasets and spaces in the cache, and code. Code keeps
  `Repository` names, because they match the Hub API.

## Known limits

- Only Windows players are tested, by the companion project's build smoke
  test. Android, iOS, Web, macOS, and Linux players are untested on
  devices. See [runtime-loading.md](runtime-loading.md#known-limits).
- Repository ids map to cache folders by replacing `/` with `--`
  ([CachePaths.cs](../tools.doji.model-hub/Runtime/Cache/CachePaths.cs)). The
  mapping is not reversible, so two distinct ids can collide.
