---
name: model-hub-cli
description: Download, set up, and inspect the AI Model Hub (`tools.doji.model-hub`) models of a Unity project from the terminal with the Unity CLI (`unity command model_hub_*`, `unity run --command model_hub_sync`). Use when a project's models are missing or not ready, when a scene or test fails because a Hub model is not downloaded, when turning on an optional model file, or when setting up models in CI. Needs `com.unity.pipeline` in the project.
---

# Set up AI Model Hub models from the command line

The commands exist when the project has `com.unity.pipeline`. Check with `unity list | grep model_hub`. Each
command answers in under a second and never opens a dialog. Never use `eval` with
`EditorApplication.ExecuteMenuItem` or reflection on Hub types for this: the commands report what happened, the
menu item does not.

## Workflow

Follow this order. Do not skip step 1.

1. **Status.** `unity command model_hub_status` (or `--repo owner/model`). Read each dependency's `state`,
   `reasonCode`, `message`, and `files` (`optional`, `enabled`, `cached`, `imported`, `sizeBytes`).
2. **Sync.** `unity command model_hub_sync` (or `--repo owner/model`; `--mode download` only downloads). It
   returns at once. If `started` is `false`, read each `dependencies[].reason` and stop or fix:
   - `ready`: nothing to do.
   - `selection_required`: every model file is optional and none is on. Ask the user which file to turn on.
   - `invalid_repository`: the manifest's model id is wrong. Tell the user.
   - `revision_conflict`: manifests request two revisions of one model. Tell the user; do not edit package manifests.
   - `already_running`: go to step 3.
3. **Poll.** `unity command model_hub_sync_status` every 5 to 10 seconds until `finished` is `true`. While it runs,
   `progress` has `phase`, `currentFile`, `downloadedBytes`, and `totalBytes`. When finished, `run.state` is
   `succeeded`, `failed`, `canceled`, or `interrupted`, and `run.results[]` has `ok` and `message` per model.
4. **Confirm.** Run `model_hub_status` again. Every needed dependency should be `Ready`.

In batch mode, one call does steps 2 and 3:

```bash
unity run <project> --command model_hub_sync -- --mode full
```

It waits until the sync ends and prints the run record.

## Consent

Ask the user in chat, and wait for a clear yes, before you:

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
| `model_hub_set_file --repo <id> --path <p> [--enabled true\|false] [--quantization none\|float16\|uint8]` | Change one file like the Files tab. Does not sync: run `model_hub_sync --repo <id>` afterwards. |
| `model_hub_cancel` | Stop the running sync. Finished files stay. |
| `model_hub_remove_cache --repo <id> --confirm true` | Delete one model from the shared cache. |

`--repo` is case-sensitive and must match the manifest. An unknown id fails with the list of declared models.
A failed command returns `success: false` with the reason in the error message; fix the argument, do not retry
blindly. Downloads resume after a domain reload; `model_hub_sync_status` then reports `resumable` or `running`.
