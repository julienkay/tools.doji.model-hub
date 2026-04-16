# AGENTS.md

## Project Intent

This repository contains the Unity package `com.doji.hf-hub` and its companion project `HF Hub`.

Reference and Download AI models from HuggingFace

It is built with Unity 6000.3 LTS. There is no compile step, CLI build command, or test runner to invoke. Do not attempt to build, compile, or run the project. 

## Package Direction

- `com.doji.hf-hub` is a Unity-package-friendly reference and import layer for HuggingFace models. It solves the problem of creating packages or projects that use large AI models without shipping them inside the UPM package or Unity projects or even including them in the repo / git LFS explicitly. Essentially package management for AI models specifically for Unity (Sentis).
- Prefer a package-owned manifest workflow: UPM packages declare remote model dependencies, the Hub detects those manifests when packages are present in a project, then downloads source ONNX files into a shared local cache and converts them into project-owned `.sentis` assets after user confirmation.
- Prefer runtime-safe implementations for Hub logic whenever possible so future runtime downloads can reuse same code. Keep only UnityEditor integration points, editor UX, and Package Manager adapters in `Editor`.
- Optimize for Sentis editor and runtime behavior. Avoid long-lived references to large ONNX `ModelAsset` files when they would force expensive import work during domain reloads.
- Prefer `.sentis` artifacts for project/runtime consumption because they can be loaded from flatbuffer data without repeated ONNX import cost.
- Assume `.sentis` output may depend on the installed Sentis version. Favor regeneration or version-keyed caches over treating generated `.sentis` files as universally stable.
- Reuse raw downloads across projects whenever possible. If conversion caching is added, key it by model identity and Sentis version.
- Use `huggingface_hub` as the primary reference implementation for Hub interactions and manifest behavior. Keep the local reference checkout in the top-level ignored `references/` folder and do not vendor it into this package.
- The official `huggingface_hub` reference checkout should live at `references/huggingface_hub`. Reuse that local clone in future sessions when present, and only refresh it when a task depends on newer upstream behavior.
- Unless the user explicitly says otherwise, assume the package is still in active development. Prefer the current architecture and schema over maintaining backwards compatibility with earlier ideas, drafts, or implementations.
- Do not preserve, describe, or optimize for legacy architecture variants unless the user explicitly asks for historical comparison or migration work.

## Source Of Truth

Prefer these locations:

- repository root: orient from the repository root for searches, docs, and cross-package work; from this companion Unity project working directory, the repository root is `../..`
- `com.doji.hf-hub/`: local package under development
- `docs/`: user-facing package documentation only; keep maintainer notes and internal architecture docs out of DocFX content
- `.codex/skills/`: project-specific skills; when a task matches one, read and follow the relevant `SKILL.md`
- `references/`: maintainer-only local reference checkouts; reuse existing clones before downloading again
- `.codex/project-memory.md`: durable project memory and agent-oriented project context
- `projects/HF Hub/Assets`: companion-project assets and test scenes

Prefer working in the package folder when functionality is package-owned.

## Project Memory

Keep durable project memory in `.codex/project-memory.md`.
Keep internal architecture notes, planning notes, and maintainer-only decisions under `.codex/`.

## Conventions

When adding code:

- keep editor-only code in `Editor` folders or editor-only assemblies
- use asmdefs deliberately and keep assembly references explicit
- keep namespaces, folders, and asmdef boundaries coherent for Unity and Rider
- preserve `.meta` files when moving Unity assets or scripts

## C# Style (Unity)

- No raw public fields.
- Prefer properties over public fields.
- Use `[field: SerializeField] public TYPE Name { get; private set; }` when inspector/serializer input should be externally read-only.
- Use `[field: SerializeField] public TYPE Name { get; set; }` for serialized data objects/DTOs that are intentionally edited through tooling code.
- Use `[SerializeField] private TYPE name;` for private Inspector fields.
- Private instance fields use `_camelCase`.
- Private computed properties may use `PascalCase` when they read like derived values rather than stored state.
- Private static readonly shared collections/values may use `PascalCase` when treated as internal constants/shared state.
- Place multiple attributes on separate lines.
- Prefer property-based public APIs; keep backing fields private or compiler-generated.
- Follow Unity conventions for `MonoBehaviour` and `EditorWindow` methods; do not add unnecessary `Update()` or `FixedUpdate()`.

## Documentation Style

- Document every public type and any non-obvious internal type with an XML summary.
- Start summaries high-level: explain role and intent before mechanics.
- Do not narrate obvious code. Avoid filler like `Gets or sets`, `Helper for`, or comments that restate the method name.
- Add detail only where behavior is surprising, constrained by Unity/serialization, or shaped by a workaround.
- Prefer short summaries plus targeted `<param>` notes over long blocks.
- Document why a piece exists or what contract it preserves when that is not obvious from the implementation.

## Avoid

- Do not modify `Library/`, `Temp/`, `Obj/`, `Logs/`, `.idea` or `UserSettings` unless the task is explicitly about them.
- Do not commit downloaded model files, shared cache contents, generated `.sentis` artifacts, or temporary reference checkouts unless the task explicitly calls for versioning them.

## Validation

- Unity-specific moves must preserve matching `.meta` files
- each completed implementation phase must add or update tests that cover phase output before treating phase as done
- when a change affects package users or package-author workflow, add or update user-facing docs under `docs/` in same phase
- after larger refactors, verify the current file contents before describing the final state; do not rely on intent, partial diffs, or memory
- when multiple files should have changed, confirm each affected file explicitly
