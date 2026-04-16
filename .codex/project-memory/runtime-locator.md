# Runtime Locator

## Working Assumptions

- Runtime Sentis loading should use `StreamingAssets`.
- Generated model payloads and related repo files may need to be copied under one shared project `StreamingAssets` root.
- There can be only one `StreamingAssets` root in project, so generated paths must be globally collision-safe across packages and models.
- Package-facing runtime usage should feel like string-based pretrained model creation/loading, similar in spirit to `create_model("id")` or `from_pretrained("repo-or-id")`, not like manual path construction.
- Package authors should be able to build higher-level runtime wrappers and pipelines that load several generated models and companion config/tokenizer/preprocess files dynamically by string id.

## Recommended Direction

1. Generate package-owned runtime locator metadata alongside imported artifacts.
   Prefer one generated registry per package that maps package-local model ids to unique `StreamingAssets` relative paths and metadata.
2. Put imported runtime payloads under one HF Hub-owned `StreamingAssets` subtree.
   Keep path unique by including sanitized package id + model id and, if needed, source-relative file names.
3. Treat locator metadata as runtime source of truth behind a runtime API.
   Runtime package code should ask locator API for model entry by string id instead of composing `Assets/...` strings or parsing registry files manually.
4. Keep editor import metadata and runtime locator metadata separate if their concerns differ.
   Editor metadata tracks stale detection/import provenance; runtime locator tracks loadable runtime paths.
5. Preserve room for multi-file model repos.
   Locator should not assume only one `.sentis` file forever; it should be able to map a model id to multiple streamed files.
6. Keep registry implementation detail private to HF Hub.
   Package authors should consume high-level lookup/load methods; JSON registry exists only so runtime can resolve ids to streamed file locations without hard-coded paths.
7. Use registry to enable dynamic package-defined loading patterns.
   This is the mechanism that should allow Unity package authors to build Transformers/Diffusers-style wrappers and pipelines that discover all files for a model id at runtime without hard-coded file lists in every package.

## Desired Runtime Usage Pattern

- package code should be able to load by stable string id such as package-local `modelId`
- package wrapper APIs may mirror `create_model("id")` / `from_pretrained("id")`
- optional safety should allow pinning or validating expected resolved revision / commit when loading generated models
- package pipelines should be able to resolve a model entry into primary `.sentis` plus companion files such as tokenizer/config/preprocess files
- registry-backed lookup should support package-defined runtime composition of several model entries into larger inference pipelines
- direct file-path composition in package runtime code is not acceptable target design

## Implementation Bias

- Start with generated registry asset/API and unique `StreamingAssets` path conventions.
- Update import pipeline to emit runtime locator data together with generated files.
- Then update runtime API surface to resolve models through locator instead of hard-coded generated asset root constants.
- Expose string-based lookup/load entry points; keep registry JSON as internal storage detail.

## Runtime Contract Target

- public contract should look like string-based lookup/load APIs
- internal runtime locator should use generated registry because multi-file support and pipeline composition matter
- deterministic path conventions still matter for stability, but packages should not rely on them directly
