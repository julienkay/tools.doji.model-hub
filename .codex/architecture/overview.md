# Architecture Overview

## Scope

This package targets Unity `6000.3.10f1` with Sentis `2.6.1`.

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

The next approved architecture slice is optional package-owned model selection:

- package authors can declare optional exact repo-relative files inside one repo dependency
- projects choose enabled optional files before download or import
- raw cache stays repo-scoped
- generated runtime artifacts stay repo-scoped
- owner bindings and runtime registries stay owner-scoped

## Package Boundary

One important boundary: generic resumable download infrastructure is not embedded inside `com.doji.hf-hub`.

Instead we depend on:

- a downloader package `com.doji.downloads`
- purpose-built (maintened by us), but generic with no HuggingFace or Sentis knowledge

Why:

- resumable file transfer, persisted jobs, and recovery are useful beyond AI model imports
- those concerns must eventually work at runtime too
- editor-only UX such as prompts and Project Settings should not define core download architecture
- HuggingFace resolution and `.sentis` import are separate concerns from URL-to-file transfer

Caveat:

- robust, reasumable downloads are still the bread-and-butter of a "Package Manager-like" tool like hf-hub, so even if downloads are delegated to a separate package we need to make sure it's still treated as a first class feature and works flawlessly

