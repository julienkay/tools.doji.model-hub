# Command Line and CI

AI Model Hub adds commands to the [Unity CLI](https://docs.unity3d.com/Packages/com.unity.pipeline@0.8/manual/index.html) when the project has the `com.unity.pipeline` package. Scripts, CI jobs, and coding agents use them to see which models a project needs, to download and set them up, and to turn optional files on, without the Model Hub window.

The commands do what the Model Hub buttons do. They never open a dialog: each command answers in under a second, a download runs in the background, and a step that needs a person's decision is refused with a message that names the argument to pass. Without `com.unity.pipeline`, the commands are not compiled and the rest of AI Model Hub works as before.

## Commands

| Command | Does |
|---|---|
| `model_hub_status [--repo <id>]` | Lists every model the project's manifests declare: the owner package, the model id, the requested revision, the commit it resolved to, the state and its reason, and each file with whether it is optional, turned on, downloaded, and set up for this project. Reads local state only. |
| `model_hub_sync [--repo <id>] [--mode full\|download] [--wait true]` | Starts a background download and setup of the models that need it, and returns at once. `full` (the default) downloads and converts; `download` only downloads. |
| `model_hub_sync_status` | Says whether a sync runs, shows its progress (the model, the current file, bytes downloaded), and the result of the last sync for each model. |
| `model_hub_set_file --repo <id> --path <path> [--enabled true\|false] [--quantization none\|float16\|uint8]` | Turns an optional file on or off, or sets the quantization of an ONNX file, like the **Files** tab. It does not start a download: run `model_hub_sync` afterwards. |
| `model_hub_cancel` | Cancels the running sync. Files that finished downloading stay in the cache. |
| `model_hub_remove_cache --repo <id> --confirm true` | Deletes one model's downloaded files from the cache on this machine, like **Remove from Cache**. |

`--repo` takes the Hugging Face model id exactly as the manifest writes it, for example `owner/model`. Ids are case-sensitive here; a wrong case gets a hint with the declared id.

## Download a project's models

With the project open in the Editor:

```bash
unity command model_hub_status
unity command model_hub_sync
unity command model_hub_sync_status
```

Run `model_hub_sync_status` again until `finished` is `true`. `run.state` is then `succeeded`, `failed`, or `canceled`, and `run.results` has one entry per model with `ok` and, for a failure, the error message.

When `model_hub_sync` starts nothing, `started` is `false` and each model in `dependencies` says why:

| `reason` | Meaning | Fix |
|---|---|---|
| `ready` | Every selected file is downloaded and set up. | None. |
| `invalid_repository` | The model was not found on Hugging Face. | Correct the model id in the manifest. |
| `selection_required` | All model files of the dependency are optional, and none is turned on. | Turn one on with `model_hub_set_file --enabled true`. |
| `revision_conflict` | Manifests request different revisions of the same model. | Change the manifests so they use one revision. |
| `already_running` | Another sync runs. Only one sync runs at a time. | Wait for it, or run `model_hub_cancel`. |

**Set Up All Models** in `Tools > Doji > AI Model Hub` writes the same explanation to the Console when it has nothing to do.

## Turn on an optional file

```bash
unity command model_hub_set_file --repo owner/model --path onnx/model_large.onnx --enabled true
unity command model_hub_sync --repo owner/model
```

The selection is saved in the project, in the same asset the Model Hub window uses. Quantization applies to ONNX files that the project uses; set it after the file list is known, for example after a `--mode download` sync.

## One-shot runs in CI

`unity run` starts the Editor in batch mode, runs one command, and exits. In batch mode `model_hub_sync` waits until the sync ends, so the job ends with the models ready:

```bash
unity run path/to/Project --command model_hub_sync -- --mode full
```

The command fails when an argument is invalid, for example an unknown model id. Read `run.state` and `run.results` in the output to see whether every model succeeded. In an open Editor, `--wait true` makes `model_hub_sync` wait the same way; the default is to return at once and let you poll.

## Requests that need a person

Some steps should not happen without a person's decision. The commands refuse them and say how to proceed:

- `model_hub_remove_cache` deletes files that other projects on this machine may use. Without `--confirm true` it changes nothing.
- Optional files are often large variants. Turn them on only when the project needs them.

Model licenses are shown, never accepted: `model_hub_status` lists the license each model card declares, when the cache has the model card. AI Model Hub downloads public models only and sends no Hugging Face token, so there is no license prompt to answer.
