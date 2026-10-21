# Roadmap

Features the package does not have yet, and open design questions. Remove an
entry when it ships or is rejected.

## Features

- **Runtime ONNX loading.** Add `tools.doji.onnx-model-loader` as a
  dependency behind `IOnnxModelConverter`. It was left out of 1.0.0 because
  editor-only signature reading needs only `tools.doji.formats.onnx`. Before
  adding it, remove the loader's own copies of `Onnx.cs`, `ProtoReader.cs`,
  and `OnnxModelSignatureReader` so it depends on `tools.doji.formats.onnx`.
- **Runtime downloads.** Reuse the `Runtime` Hub logic and
  `tools.doji.downloads` to fetch models in players.
- **Authentication and gated models.** Optional HuggingFace token support.
- **Project revision override.** An opt-in, clearly visible override when
  packages request conflicting revisions. Never pick a winner silently.
- **Offline and portable cache.** Reuse and move the shared cache without
  network access.
- **Shared `huggingface_hub` cache.** Use the Python library's cache location
  (`HF_HUB_CACHE`) and layout (`models--owner--name`, `blobs/`, `refs/`,
  `snapshots/`, `.locks/`), so Unity and Python tools reuse each other's
  downloads. Move HF Hub's bookkeeping out of snapshot folders into a
  rebuildable index under the Doji folder, and migrate the existing `raw/`
  cache by moving files, not downloading them again.
- **Cache overview.** A treemap of the whole shared cache in Model Hub, per
  repository or per file, marking models this project does not use and
  partial downloads, with Remove from Cache. Build it on the shared cache.
- **Remote browsing.** Browse HuggingFace repositories in Model Hub.
- **License display.** Show model license information on first import.
- **`from_pretrained` helpers.** Extend the wrapper only when a concrete
  tokenizer, config, or runtime need appears.

## Open questions

- Which cleanup or eviction policy should the shared cache use?
- Is the project-open plus `projectChanged` prompt hook enough for package
  imports and updates, or does HF Hub need a Package Manager event?
- Should `tools.doji.downloads` own the Editor `Progress` adapter, or should
  HF Hub keep that orchestration?
