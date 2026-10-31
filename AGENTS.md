# AGENTS.md

## Project Intent

This repository contains the Unity package `tools.doji.model-hub` and its companion project `AI Model Hub`.

Package management for AI models in Unity: packages reference HuggingFace models in a manifest; AI Model Hub downloads them and imports them as `.sentis` assets.

Unity 6000.6 for development; the package supports 6000.3 LTS and later.

## Package Direction

- Follow `VISION.md` for durable product direction, scope and the decision test.
- Packages own their model manifests. Do not commit downloaded models, cache contents, or generated `.sentis` files.
- Keep Hub logic in `Runtime` when it does not need `UnityEditor`; only Editor UX and Package Manager integration belong in `Editor`.
- Generic download behavior belongs in `tools.doji.downloads`; ONNX parsing belongs in `tools.doji.formats.onnx`.
- Use `huggingface_hub` as the reference for Hub API, download, and cache behavior. Reuse the gitignored checkout at `references/huggingface_hub`; refresh it only when a task needs newer upstream behavior.
- The package is in active development. Prefer the current design over backwards compatibility.
- Keep documentation about the current design and behavior. Update it with the code; remove temporary notes once the work they track is done.

## Source Of Truth

Prefer these locations:

- repository root: orient from the repository root for searches, docs, and cross-package work; from this companion Unity project working directory, the repository root is `../..`
- `tools.doji.model-hub/`: local package under development
  - `Runtime`: manifests, `Hub` lookup, runtime locator, conversion contracts
  - `Editor`: discovery, cache, sync, import, status, and UI
  - `Tests/Editor`: EditMode tests, grouped like the package folders
- `docs/`: user-facing package documentation only; keep maintainer notes and internal architecture docs out of DocFX content
- `internal-docs/`: design notes for maintainers; `internal-docs/README.md` indexes them. Read
  `internal-docs/architecture.md` first, then the topic doc for the area you change.
- `.claude/skills/`: project skills for tests (`unity-model-hub-test-creation`) and user docs (`user-facing-docfx-docs`)
- `tools.doji.model-hub/Skills~/`: skills that ship with the package for consumers (`model-hub-dependency`, `model-hub-cli`); update them with the manifest schema and the CLI commands
- `projects/AI Model Hub/Assets`: companion-project assets and test scenes

Prefer working in the package folder when functionality is package-owned.

## Conventions

When adding code:

- organize scripts by task area; prefer colocated or clearly scoped folders such as docs-, tooling-, or workflow-specific script locations over a shared miscellaneous scripts bucket
