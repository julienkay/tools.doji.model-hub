---
name: model-hub-cli
description: Download, set up, and inspect the AI Model Hub (`tools.doji.model-hub`) models of a Unity project from the terminal with the Unity CLI (`unity command model_hub_*`, `unity run --command model_hub_sync`). Use when a project's models are missing or not ready, when a scene or test fails because a Hub model is not downloaded or not enabled, when enabling an optional model or turning on an optional model file, or when setting up models in CI. Needs `com.unity.pipeline` in the project.
---

# Set up AI Model Hub models from the command line

The commands exist when the project has `com.unity.pipeline`. Check with `unity list | grep model_hub`. Each
command answers in under a second and never opens a dialog. Never use `eval` with
`EditorApplication.ExecuteMenuItem` or reflection on Hub types for this: the commands report what happened, the
menu item does not.

## Workflow

Follow this order. Do not skip step 1.

1. **Status.** `unity command model_hub_status` (or `--repo owner/model`). Read each dependency's `state`,
   `reasonCode`, `message`, `optional`, `enabled`, `downloadSizeBytes`, and `files` (`optional`, `enabled`,
   `cached`, `imported`, `sizeBytes`). `NotEnabled` is an optional model the project did not enable: it needs no
   action unless the user wants it.
2. **Sync.** `unity command model_hub_sync` (or `--repo owner/model`; `--mode download` only downloads). It
   returns at once. If `started` is `false`, read each `dependencies[].reason` and stop or fix:
   - `ready`: nothing to do.
   - `selection_required`: every model file is optional and none is on. Ask the user which file to turn on.
   - `not_enabled`: an optional model the project did not enable. Not an error. Enable it only if the user wants it.
   - `invalid_repository`: the manifest's model id is wrong. Tell the user.
   - `revision_conflict`: manifests request two revisions of one model. Tell the user; do not edit package manifests.
   - `already_running`: go to step 3.
3. **Poll.** `unity command model_hub_sync_status` every 5 to 10 seconds until `finished` is `true`. While it runs,
   `progress` has `phase`, `currentFile`, `downloadedBytes`, and `totalBytes`. When finished, `run.state` is
   `succeeded`, `failed`, `canceled`, or `interrupted`, and `run.results[]` has `ok` and `message` per model.
4. **Confirm.** Run `model_hub_status` again. Every needed dependency should be `Ready` (or `NotEnabled` for
   optional models nobody asked for).

In batch mode, one call does steps 2 and 3:

```bash
unity run <project> --command model_hub_sync -- --mode full
```

It waits until the sync ends and prints the run record.

## Consent

Ask the user in chat, and wait for a clear yes, before you:

- **enable an optional model** (`model_hub_set_model --enabled true`). Optional models are often large. Say the
  model and its `downloadSizeBytes` when known.
- **turn on an optional file** (`model_hub_set_file --enabled true`). Optional files are often large variants.
  Say the file and its `sizeBytes` when known.
- **start a sync that downloads a lot.** Say the model and the size first when it is more than a few hundred MB
  and the user did not already ask for the download.
- **delete from the cache** (`model_hub_remove_cache --confirm true`). Other projects on the machine lose the files.
- **change quantization** (`model_hub_set_file --quantization ...`). It changes the project's import settings.

Never accept a license or terms for the user. `model_hub_status` lists `license` per model; show it to the user
when they decide. If a command refuses and names a flag such as `--confirm true`, ask the user before you pass it.

## Other commands

| Command | Use |
|---|---|
| `model_hub_set_model --repo <id> --enabled true\|false [--owner <package-id>]` | Enable or disable an optional model (`"optional": true` in the manifest) for the project, like the Enable and Disable buttons. Saved in ProjectSettings. Does not sync: run `model_hub_sync --repo <id>` afterwards. |
| `model_hub_set_file --repo <id> --path <p> [--enabled true\|false] [--quantization none\|float16\|uint8]` | Change one file like the Files tab. Does not sync: run `model_hub_sync --repo <id>` afterwards. A file of a model that is not enabled needs `model_hub_set_model` first. |
| `model_hub_cache_info` | The download cache (`hubCache`) and the converted-models folder (`dataRoot`): `path`, `source` (`editor_setting`, `HF_HUB_CACHE`, `HF_HOME`, `DOJI_MODEL_HUB_DATA_ROOT`, or `default`), `drive`, `freeBytes`. `staleVariables` and `warning` list variables that changed after Unity started. |
| `model_hub_cancel` | Stop the running sync. Finished files stay. |
| `model_hub_remove_cache --repo <id> --confirm true` | Delete one model from the shared cache. |

Without `com.unity.pipeline`, run `Unity -batchmode -projectPath <p> -executeMethod
Doji.AI.ModelHub.Editor.ModelHubBatch.Sync` (or `.Status`) without `-quit`
(`Sync` exits with 2 when `-quit` is given). Optional `-modelHubRepo <id>`,
`-modelHubMode full|download`, `-modelHubOutput <file>` (JSON). Exit code 0 ready, 1 failed or not ready, 2 bad
argument. Do not reflect on internal Model Hub types.

A sync that fails with "Not enough disk space" names the space needed and free per drive. Run
`model_hub_cache_info` and tell the user; do not delete cache content to make room. If `staleVariables` is not
empty, the Editor still uses an old `HF_HOME` or similar value: tell the user to quit Unity Hub (also from the
tray) and start it and the Editor again, or to choose the folder in `Preferences > Doji > AI > AI Model Hub`.
Changing folders and setting environment variables is the user's decision; there is no command for it.

`--repo` is case-sensitive and must match the manifest. An unknown id fails with the list of declared models.
A failed command returns `success: false` with the reason in the error message; fix the argument, do not retry
blindly. Downloads resume after a domain reload; `model_hub_sync_status` then reports `resumable` or `running`.
