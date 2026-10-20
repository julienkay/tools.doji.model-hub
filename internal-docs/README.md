# Design documents

These documents describe how the package works and why. They are not work
plans: a plan belongs in a pull request or an issue, not here. The one
exception is the roadmap, which lists features the package does not have yet.

| Document | Subject |
|---|---|
| [VISION.md](../VISION.md) | The package purpose, product direction, and decision rules |
| [architecture.md](architecture.md) | Assemblies, package boundaries, end-to-end flow, known limits |
| [manifest-schema.md](manifest-schema.md) | Manifest sources, schema, file selection, optional files, validation |
| [download-and-cache.md](download-and-cache.md) | HuggingFace resolution, shared raw cache layout, sync and resume |
| [import-and-generated-assets.md](import-and-generated-assets.md) | ONNX to `.sentis` conversion, generated paths, import metadata, stale detection |
| [per-file-conversion-recipes.md](per-file-conversion-recipes.md) | Per-file input shapes and conversion settings, project overrides |
| [runtime-locator.md](runtime-locator.md) | Runtime registries and lookup by `repo_id` |
| [editor-model-hub-ui.md](editor-model-hub-ui.md) | Model Hub window, Project Settings, Inspectors, states and actions |
| [visual-scenario-sandbox.md](visual-scenario-sandbox.md) | Scenario fixtures for checking Model Hub UI states |
| [testing.md](testing.md) | Map from behavior contracts to test fixtures |
| [roadmap.md](roadmap.md) | Missing features and open questions |
| [references/](../references) | Local `huggingface_hub` reference checkout (gitignored) |
