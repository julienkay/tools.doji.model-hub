# Runtime Locator

## Public Contract

Runtime consumption should not require package code to compose generated asset paths manually.

Desired usage style is similar in spirit to Python ecosystem pretrained loading APIs:

- string-based model creation and lookup by stable id
- package-facing wrapper APIs such as `CreateModel("tiny-clip")` or runtime helpers analogous to `from_pretrained(...)`
- optional safety checks that can pin or validate expected resolved revision or commit
- higher-level package-defined pipelines that can dynamically resolve multiple model entries and companion files from stable string ids

Default runtime expectation is stronger offline-use than upstream Python libraries:

- runtime `load(<modelId>)` should usually resolve already-generated local payloads
- builds should normally ship or otherwise already contain required runtime payloads
- runtime download still matters as future capability, but should extend this contract rather than replace it

This means:

- generated runtime locator registry is implementation detail, not the primary package-facing API
- runtime code should call locator or load API with string id
- registry exists to map those ids to unique files under shared `StreamingAssets` root
- package code should not hard-code `Assets/...` or `StreamingAssets/...` conventions directly
- package code should be able to treat runtime load as local resolution first and network fallback later

## Working Assumptions

- Runtime Sentis loading should use `StreamingAssets`.
- Generated model payloads and related repository files may need to be copied under one shared project `StreamingAssets` root.
- There is only one `StreamingAssets` root in a Unity project, so generated runtime paths must be globally collision-safe across packages and models.
- Package authors should be able to build higher-level wrappers and pipelines that load several generated models and companion config, tokenizer, or preprocess files dynamically by string id.

## Why Registry Still Helps

- Unity runtime has one shared `StreamingAssets` root, so path uniqueness must be managed centrally
- multi-file model support needs more than `one id -> one sentis path`
- package authors should be able to build Transformers or Diffusers style wrappers and pipelines that ask for one model id and receive primary model plus companion files
- registry lets runtime evolve file layout later without breaking package-facing load APIs

Because Unity runtime loading will rely on `StreamingAssets`, generated runtime files should live under one shared root such as:

```text
Assets/StreamingAssets/hf-hub/
```

Path uniqueness must hold across all packages and models, so runtime file layout should follow upstream-style repo serialization plus resolved commit and preserve room for multiple companion files per model.

## Recommended Layering

1. Public runtime API:
   string-based lookup and load methods used by package code
2. Internal locator:
   generated per-package registry mapping ids to runtime file sets
3. Runtime file storage:
   deterministic shared `StreamingAssets` subtree with globally unique package and model paths

This is closest in spirit to the HuggingFace Python stack:

- `huggingface_hub` handles file resolution, download, and cache
- higher-level libraries expose `from_pretrained(...)` style model loading
- local file layout and metadata stay implementation detail below that public loading API

Key difference from upstream default assumptions:

- HF Hub for Unity is more offline-first at runtime
- editor sync is default preparation step
- locator/load APIs should make already-downloaded runtime payloads feel like normal local assets

## Recommended Direction

Current implementation:

1. Import writes generated runtime `.sentis` payloads under `Assets/StreamingAssets/hf-hub/<serialized-repo>/snapshots/<resolved-commit>/`.
2. Sync writes per-package locator registries under `Assets/StreamingAssets/hf-hub/registries/<package-id>/hf-hub-runtime.json`.
3. Import metadata and runtime locator metadata stay separate because they serve different contracts.
4. Runtime code resolves models by stable package id plus model id through `ModelHub.TryResolveRuntimeModel(...)`.
5. Runtime payload layout now leans closer to official Hub cache concepts by using serialized repository ids and resolved commits instead of package-owned path segments.
6. Runtime locator contract should continue to support future `load(<modelId>)` style APIs where local resolution is default and runtime download is optional fallback.
