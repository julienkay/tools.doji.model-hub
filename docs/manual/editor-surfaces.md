# Editor Surfaces

HF Hub splits configuration, manifest authoring, repository inventory, and import actions across Project Settings, manifest Inspectors, and Model Hub.

## Project Settings

Use `Project Settings > Doji > AI > AI Model Hub` for project configuration.

Current settings:

- shared cache location override for this project
- editable project model manifest stored under `ProjectSettings/Packages/com.doji.hf-hub/hf-hub-manifest.json`
- same repository-exists validation icons used by the package-manifest Inspector

Manifest validation icons validate both repository id and effective revision. A green check means HF Hub could resolve that exact `id` plus revision combination on Hugging Face. An invalid branch, tag, or commit shows as invalid even when the repository itself exists.

The page also shows:

- platform default cache root
- effective cache root after overrides
- current Sentis version

## Manifest Inspectors

Selecting a model manifest asset in a package shows a focused Inspector for declared models, with shortcuts into Model Hub and the source file location.

Writable package sources can add or remove model entries, choose whole-repo scope or explicit file scope, and apply those changes directly from the Inspector. Installed package cache entries stay read-only.

The file-scope picker uses one repository file list:

- checked files are selected by the manifest
- unchecked files stay visible but greyed out
- `Optional` is available only on selected rows
- selected rows with `Optional` off are always required
- selected rows with `Optional` on are stored in the manifest but must be enabled per project owner in Model Hub before HF Hub downloads or imports them
- leaving every file unchecked keeps whole-repository scope

If the Inspector already knows a declared repository does not exist on HuggingFace, HF Hub keeps that invalid dependency out of Model Hub and skips it during sync until the manifest entry is fixed.

## Model Hub Window

Use `Window > AI Model Hub` to inspect declared and imported repositories.

The Model Hub window groups entries by repository and revision so one HuggingFace repo appears once even when multiple package or project manifests depend on it.

The `ProjectSettings/Packages/com.doji.hf-hub/hf-hub-manifest.json` source shows as `Project Settings` in editor UI. ScriptableObject project manifest assets show their asset name, for example `Custom Dependencies` or `HF Hub Project Manifest`, and HF Hub tracks each asset as its own owner for bindings, registries, and stale/removal checks.

For each repository, the window shows:

- local state
- which package or project manifests depend on it
- raw cache location
- selected-source size and current local cache size when metadata is known
- full repository file inventory in the `Files` tab when inventory is known
- generated runtime `.sentis` files
- generated runtime asset size on disk and estimated runtime size from import metadata when available
- manifest and import diagnostics

## Files Tab

The `Files` tab can show:

- every known repo file path for the resolved revision
- whether that file is currently selected by any manifest owner
- how many current manifest owners select that file
- raw cached file location and size when that source file exists locally
- generated `.sentis` output path when that ONNX file has been materialized into the project
- effective conversion recipe source and editable import settings only for selected ONNX files

Selected ONNX rows behave like a traditional importer. Each repository keeps one edit buffer with repo-wide `Revert` and `Apply` actions. `Apply` saves project-owned overrides and immediately reimports that repository. If raw cache is missing, HF Hub downloads the selected source files first and then imports with the applied settings.

Unselected rows remain visible so you can inspect full repository inventory even when the current project depends on only one subset of files.

## Optional File Choices

Optional file choice happens before raw cache exists. When a manifest owner declares optional file rows, the Summary tab shows owner-scoped checkboxes so you can enable ONNX variants before download.

HF Hub stores enabled optional-file choices in:

```text
Assets/Doji/HfHub/HF Hub Optional File Selections.asset
```

The asset is project-owned and keyed by manifest owner plus model id, so different package or project manifests can enable different optional files from the same repository.

Unenabled optional files do not count as selected files in Model Hub status, `Files` tab owner counts, runtime registry output, or startup prompt logic. Enabled optional files do count, so missing source cache or missing generated runtime output is surfaced like any other selected file.

## Actions And Diagnostics

Header actions include:

- `+ > Add model to project...` to append one entry to `ProjectSettings/Packages/com.doji.hf-hub/hf-hub-manifest.json`
- `+ > Add model to package...` to pick a local package inside the project and create or update its `hf-hub-manifest.json`
- `Configure` when a manifest owner must choose optional ONNX files first
- `Download + Import` when the repository is not cached on this machine yet
- `Import` when raw cache already exists and HF Hub needs to materialize `.sentis` output into the project
- `Manage > Remove From Project` to delete generated runtime assets and import metadata from this project
- `Manage > Remove From Cache` to delete this machine's raw cache entry while keeping generated project assets
- `Locate` to ping one dependent manifest in Unity; when more than one manifest declares the same repository, HF Hub shows a chooser menu first

The diagnostics tab can show:

- revision conflict when same repository is declared at multiple requested revisions in one project
- partial ONNX conversion when at least one ONNX file converted successfully but another variant was skipped

Revision conflict matters because generated runtime paths are repository-scoped. HF Hub does not choose a winner. Conflicting rows show `Conflict`, keep `Locate` available, and block `Import`, `Remove From Project`, and `Remove From Cache` until all manifests agree on one requested revision for that repository.

When a conflict owner comes from an installed package under Unity Package Manager, that manifest may not be directly editable from the current project. Model Hub shows owner source and resolution guidance for each dependent, can copy a conflict summary to clipboard, and opens Package Manager so you can change package versions, embed or localize package sources, or remove one dependency.
