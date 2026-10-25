# Roadmap

Features the package does not have yet, and open design questions. Remove an
entry when it ships or is rejected.

## Features

- **Device testing.** Run the build smoke test on iOS, macOS, and Linux
  players.
- **`persistentDataPath` cache for desktop players.** An opt-in for
  applications that must not write to the user's Hugging Face cache.
- **Large-model stress tests.** Downloads, conversion, and builds with models
  over 2 GB and external weights.
- **Download verification in players.** Check the content hash against the
  etag, not only the size.
- **`.sentis` format version.** Read `ModelWriter.version` from the file
  header and warn when a downloaded `.sentis` file targets a newer Inference
  Engine.
- **Shared signature reader.** Remove the onnx-model-loader's own copies of
  `Onnx.cs`, `ProtoReader.cs`, and `OnnxModelSignatureReader` so it depends
  on `tools.doji.formats.onnx`.
- **Editor converter.** Convert in the Editor with
  `tools.doji.onnx-model-loader` too, once tests show it matches the Unity
  importer.
- **Authentication and gated models.** Optional HuggingFace token support.
- **Project revision override.** An opt-in, clearly visible override when
  packages request conflicting revisions. Never pick a winner silently.
- **Offline and portable cache.** Reuse and move the shared cache without
  network access.
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
