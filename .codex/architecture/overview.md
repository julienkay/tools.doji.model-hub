# Architecture Overview

## Scope

This first slice targets Unity `6000.3.10f1` with Sentis `2.6.1`.

The goal is a narrow end-to-end path:

1. Let a UPM package declare one or more HuggingFace ONNX model dependencies in a package-owned manifest.
2. Detect those manifests when packages are present in a Unity project.
3. Prompt the user in the Unity Editor to download and import the required model files.
4. Download the raw ONNX files into a shared machine-local cache.
5. Convert them into project-owned `.sentis` assets for use by the package inside the current project.

Out of scope for v1:

- HuggingFace authentication
- gated models
- offline workflows beyond reusing already downloaded raw cache data

## Current Implementation Map

The current package already covers the main editor-side vertical slice:

- package manifest discovery and normalized dependency modeling in `com.doji.hf-hub/Runtime`
- editor package enumeration plus status surfaces for discovery results
- shared raw-cache infrastructure and HuggingFace-specific metadata handling
- background sync orchestration through Unity `Progress` with persisted resume state
- project-owned `.sentis` generation plus generated-asset metadata for stale detection
- local status classification as `Ready`, `Missing`, `Stale`, or `Failed`

The remaining active architecture slice is the runtime locator contract that removes hard-coded project paths from package runtime usage and supports future multi-file model pipelines.

## Package Boundary

Current prototype proved one important boundary: generic resumable download infrastructure should not stay embedded inside `com.doji.hf-hub`.

Target shape:

- one generic downloader package with no HuggingFace or Sentis knowledge
- one HuggingFace Hub package that consumes that downloader package

Why:

- resumable file transfer, persisted jobs, and recovery are useful beyond AI model imports
- those concerns must eventually work at runtime too
- editor-only UX such as prompts and Project Settings should not define core download architecture
- HuggingFace resolution and `.sentis` import are separate concerns from URL-to-file transfer

Recommended first extraction package:

- `com.doji.downloads`

Keep it in the same repository first. Separate repository can wait until API shape proves stable.
