# Backlog And Open Questions

## Important Near-Term Gap

- Current manifest and sync flow support only one `sourceFile` per model entry.
- Important future support: package dependencies that need multiple files from one HuggingFace repo, including ONNX plus config/tokenizer/preprocess companion files.
- Reference direction from `huggingface_hub`: use snapshot-style repo download with optional filtering (`allow_patterns` / `ignore_patterns`) rather than only single-file download.

## V1 Scope Exclusions

- HuggingFace authentication is out of scope for v1.
- Gated model support is out of scope for v1.
- Offline-first reuse beyond the local raw model cache is out of scope for v1.

## Backlog

- Add optional HuggingFace authentication support.
- Add gated model workflows.
- Define a stronger offline story for disconnected reuse and cache portability.
- Future UI direction: browse HuggingFace models from the Project Settings surface.
- Soften first-time model dependency notification shown after package import.
- Replace scary or technical info dialog copy with calmer user-facing language and simple acknowledge flow when package brings required models.
- Low-priority future UX: when first importing a model, surface license information when available so users can see the model's license before using it.

## Open Questions

- Where should the shared cache live on each platform, and how should cleanup or eviction work?
- What is the best package-manifest discovery hook in the Editor for prompting after package import or update?
- What exact trigger and copy should import-time model notification use so it informs users without sounding alarming or too technical?
- What is the best runtime-facing contract for packages to locate their generated `.sentis` assets without hard-coded project paths?
- Which editor UX is primary beyond the initial prompt: project settings, window, inspector workflow, or a mix?
- Should downloader package include its own editor adapter for `Progress`, or should `com.doji.hf-hub` keep all editor UI orchestration and use downloader package as pure core/runtime library?
