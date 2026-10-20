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

Optional package-authored file toggles do not participate in public runtime lookup.

Reason:

- runtime code already has stable lookup shape: `repoId + repoPath`
- optional file selection is package-management/editor state
- once project enables and imports a file, runtime should load it by original repo-relative path

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

Repo-scoped runtime path is intentional. HF Hub keeps generated runtime folders human-readable and does not encode commit or revision in `StreamingAssets`.

That simplicity depends on one project-level rule:

- one Unity project may publish only one requested revision for one canonical `repo_id`
- if multiple owners request same `repo_id` at different revisions, HF Hub blocks editor sync for that repo until project resolves conflict to one revision

Additional owner rule:

- one owner manifest may publish only one dependency entry for one canonical `repo_id`
- optional files inside one repo must stay inside that single owner entry, not as duplicate manifest rows

## Current Implementation Limits

- runtime locator currently reads registry JSON and generated payloads through direct filesystem APIs under `Application.streamingAssetsPath`
- treat current implementation as desktop/editor-first until HF Hub has a platform-safe runtime loader for cases where `StreamingAssets` is packed or virtualized
- owner-specific runtime lookup assumes each owner publishes its own registry; package manifests and ScriptableObject project manifest assets now publish distinct owner identities, while the Project Settings JSON manifest remains one shared project-level owner
- owner registry entry should contain only files in that owner's effective selection, even when shared repo runtime payload contains larger union for other owners

## Ambiguity Contract

Single-string lookup by `repo_id` is allowed only when exactly one owner publishes that repo.

If multiple owners declare same `repo_id`:

- `ModelHub.TryResolveRuntimeModel(repoId, out repo)` throws ambiguity error
- owner-specific overload is required

That keeps default API simple while preserving explicit escape hatch for rare multi-owner collisions.

## Owner Versus Repo Union

Optional files create two legitimate file sets:

- repo materialization union: all generated runtime files needed by any owner of one repo
- owner effective selection: files one specific owner should see in its runtime registry

Runtime locator must publish owner effective selection, not repo union.

Why:

- package runtime code should not accidentally discover variants its package did not select
- owner registry is package contract surface
- repo-union output is implementation detail needed for shared import reuse

This means:

- shared `StreamingAssets` folder may contain more `.sentis` files than one owner registry exposes
- `TryResolveRuntimeModel(packageId, repoId, out model)` returns only that owner's selected files
- global repo lookup remains union across owners only in ambiguity sense, not in per-owner file exposure
