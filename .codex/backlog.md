# Backlog

Use this file for work that should stay discoverable across sessions but is not part of the active slice in `.codex/current-work.md`.

This is the home for:

- longer-scoped epics
- lower-priority work
- parked ideas that should not disappear
- open questions that matter beyond the current task

## Open Questions

- Where should the shared cache live on each platform, and how should cleanup or eviction work?
- What is the best package-manifest discovery hook in the Editor for prompting after package import or update?
- What exact trigger and copy should import-time model notification use so it informs users without sounding alarming or too technical?
- Which editor UX is primary beyond the initial prompt: Project Settings, window, inspector workflow, or a mix?
- Should `com.doji.downloads` eventually include its own editor adapter for `Progress`, or should `com.doji.hf-hub` keep editor UI orchestration and use downloader as pure core/runtime infrastructure?

## Near-Term Backlog

- Add support for package dependencies that need multiple files from one HuggingFace repo, including ONNX plus config, tokenizer, or preprocess companion files.
- Follow the `huggingface_hub` `snapshot_download(...)` direction for filtered multi-file snapshot downloads instead of a single-file-only model entry shape.

## Future Scope

- Add runtime-capable ONNX converter package behind `IHfHubOnnxModelConverter` when runtime conversion work starts.
- Extend `from_pretrained(...)` wrapper API only when concrete tokenizer/config/runtime helper needs appear.
- Add optional HuggingFace authentication support.
- Add gated model workflows.
- Define a stronger offline story for disconnected reuse and cache portability.
- Explore browsing HuggingFace models from the Project Settings surface.
- Soften first-time model dependency notification and replace overly technical dialog copy.
- Surface license information during first import when model metadata makes it available.
