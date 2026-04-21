# Runtime Locator

## Public Contract

Runtime code should resolve one HuggingFace repository snapshot by canonical `repo_id`, then address individual files inside that snapshot by repository-relative path.

Canonical model id here means exact HuggingFace `repo_id` string such as `owner/name`. HF Hub treats that string as source identity and only sanitizes it when mapping to local filesystem paths.

Desired usage:

- repo-level resolution by stable `repo_id`
- model loading by repo-relative ONNX path
- thin higher-level wrappers can map `from_pretrained(...)` style behavior onto repo snapshot plus file selection

Current API direction:

- `ModelHub.TryResolveRuntimeModel(repoId, out repo)`
- `repo.TryGetSentisPath("onnx/model.onnx", out path)`
- `var pretrained = ModelHub.FromPretrained(repoId)`
- `pretrained.LoadModel("onnx/model.onnx")`
- `ModelHub.LoadRuntimeModel(repoId, "onnx/model.onnx")`

## Why Repo-Level Dependencies

Repo-level manifest entries simplify almost every layer:

- manifest schema stays small
- downloader no longer needs per-file dependency semantics
- cache naturally follows upstream snapshot shape
- generated runtime layout mirrors repo snapshot contents
- ambiguity is only about `repo_id`, not `repo_id + file` ownership
- runtime wrappers can choose files the same way upstream libraries do

This fits real HuggingFace usage better because model repos usually need multiple files together: ONNX, config, tokenizer, vocab, preprocess metadata, and quantized variants.

## Locator Role

Runtime locator still matters even with repo-level dependencies:

- Unity runtime has one shared `StreamingAssets` root
- file layout must be globally collision-safe across owners and repos
- package code should not hard-code generated `StreamingAssets` paths
- runtime wrappers need one stable repo lookup that returns whole generated file set

## Current Runtime Layout

Generated runtime files live under:

```text
Assets/StreamingAssets/hf-hub/<repo-owner>/<repo-name>/
```

Current generated contents:

- `<onnx-file>.sentis` for every converted ONNX file
- `registries/<owner-id>/hf-hub-runtime.json` for owner-scoped locator data

## Ambiguity Contract

Single-string lookup by `repo_id` is allowed only when exactly one owner publishes that repo.

If multiple owners declare same `repo_id`:

- `ModelHub.TryResolveRuntimeModel(repoId, out repo)` throws ambiguity error
- owner-specific overload is required

That keeps default API simple while preserving explicit escape hatch for rare multi-owner collisions.
