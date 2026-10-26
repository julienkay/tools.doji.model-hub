# Getting Started

AI Model Hub separates model declaration from model materialization.

Package maintainers describe which Hugging Face models and files a package needs. Unity project users decide which project-scoped optional files to include, then import the selected sources into generated runtime assets.

## Installation

AI Model Hub and its dependencies `tools.doji.downloads` and `tools.doji.formats.onnx` are published on OpenUPM. Add a scoped registry for `tools.doji` to `Packages/manifest.json`, then add the package:

```json
{
  "scopedRegistries": [
    {
      "name": "OpenUPM",
      "url": "https://package.openupm.com",
      "scopes": [ "tools.doji" ]
    }
  ],
  "dependencies": {
    "tools.doji.model-hub": "1.0.0"
  }
}
```

AI Model Hub requires Unity 6000.3 LTS or later and Unity Inference Engine (Sentis) `2.6.1`, which Package Manager installs automatically.

Package maintainers add `tools.doji.model-hub` to the `dependencies` of their own `package.json` instead, so their users get AI Model Hub with the package.

## For Package Maintainers

Use this path when you publish or maintain a Unity package that needs AI model files.

Package maintainers create `model-hub-manifest.json` at package root. The manifest names Hugging Face models, requested revisions, and optional file scope. The package can stay small because source ONNX files and generated `.sentis` files are not shipped inside the package.

Typical package maintainer tasks:

- declare one or more model dependencies by HuggingFace `repo_id`
- use every file of a model, or exact selected files
- mark selected files as optional when the project should choose variants before import
- keep the package manifest local and writable while authoring

Continue with [For Package Maintainers](getting-started-package-maintainers.md).

## For Unity Project Users

Use this path when a Unity project consumes a package with model dependencies or declares project-owned model dependencies.

Unity project users work in Project Settings and Model Hub. AI Model Hub discovers package and project manifests, shows model status, downloads files into the shared Hugging Face cache, and converts ONNX files into a shared folder. Builds bundle the models or download them at runtime.

This workflow is project-scoped. Optional-file selections, conversion settings, delivery modes, and setup records belong to the current project, even when the model dependency came from an installed package.

Typical Unity project user tasks:

- inspect discovered package and project manifests
- add project-owned model dependencies
- enable optional model files for this project
- run `Download`, `Convert`, or `Download All`
- choose whether builds bundle each model or download it at runtime

Continue with [For Unity Project Users](getting-started-project-users.md).

## Where Details Live

- [Manifest Schema](manifest-reference.md) documents manifest types, fields, validation, whole-repo scope, selected files, and per-file `optional` flags.
- [Editor Surfaces](editor-surfaces.md) documents Model Hub, the setup prompt, Project Settings, and manifest Inspectors.
- [Setup, Cache, and Runtime Loading](import-cache-runtime.md) documents the sync flow, the cache and converted-model folders, builds, players, and the runtime API.
