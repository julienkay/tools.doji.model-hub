# HF Hub

## Purpose

HF Hub is package management for AI models in Unity.

An AI-powered Unity package needs model files that are too large to ship with
it. Git LFS, OpenUPM, the Asset Store, and GitHub all make large binaries hard
to distribute. HF Hub removes that problem: the package references its models
on HuggingFace, and HF Hub gets them into the user's project.

## Users

- **Package developers** build AI-powered Unity packages. They declare the
  models their package needs in a manifest. They do not host, ship, or
  version the model files themselves.
- **Package consumers** install those packages as usual. HF Hub detects the
  required models and gets them into the project with one click.

## Principles

- **Reference, do not ship.** Models stay on HuggingFace. Packages and
  projects contain a manifest, not the model files. Do not commit downloaded
  models, cache contents, or generated `.sentis` files.
- **The package owns the manifest.** A package declares its own model
  dependencies. A consumer project does not need to know which models its
  packages use.
- **One click for consumers.** Detect missing models, ask the user once, then
  download and import them. Do not make the user find files, pick revisions,
  or convert models by hand.
- **Download once, reuse everywhere.** Keep raw downloads in a shared local
  cache, so many projects, and other tools that use Hugging Face models, use
  the same files.
- **Share the Hugging Face cache.** Use the same cache location and layout as
  the official `huggingface_hub` Python library. A model downloaded by Python
  tools is reused in Unity, and the other way round. Keep HF Hub's own
  bookkeeping outside that cache, and treat it as a rebuildable index: the
  files in the cache are the truth.
- **Built for Sentis.** Load `.sentis` files as published, and convert ONNX
  files to `.sentis` once per machine, shared by every project. Treat
  converted output as specific to the converter and Sentis version, and
  convert again when either changes. Builds ship `.sentis` files, never ONNX.
- **The project decides delivery.** A package writes its loading code once.
  The project chooses whether builds bundle each model or download it at
  runtime.
- **Keep the Editor fast.** Do not hold long-lived references to large ONNX
  assets. Do not load full models only to show their files or settings.
- **Runtime-safe core.** Keep Hub logic out of `Editor` when it does not need
  `UnityEditor`, so players download and cache with the same code. Only
  editor UX, import, the build step, and Package Manager integration belong
  in `Editor`.
- **Follow `huggingface_hub`.** Use the official Python library as the
  reference for Hub API, download, cache, and manifest behavior.

## Out of scope

- Hosting or mirroring models outside HuggingFace.
- Training, fine-tuning, or editing models.
- Inference engines other than Sentis.

## Decision test

Does the change make it easier for a package developer to depend on a model,
or for a consumer to get that model into a project? If not, do not make the
change.
