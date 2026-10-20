# Visual Scenario Sandbox

## Intent

The visual scenario sandbox is a maintainer-only companion-project tool for generating fake HF Hub local state so Model Hub UI and diagnostics can be reviewed without downloading real models.

Primary location:

- `projects/HF Hub/Assets/HF Hub/Scenario Sandbox/`

Use it to materialize fake project manifests, raw cache artifacts, import metadata, and runtime files for visual review.

## Scope

The sandbox exists for UI and status inspection, not for end-user workflows and not for package runtime behavior.

Rules:

- keep sandbox code in companion project only, not package assemblies
- prefer unique `sandbox/...` repo ids so fake artifacts do not collide with real generated content
- use sandbox for visual verification of obscure Model Hub states such as `import needed`, `update needed`, `partial conversion`, invalid metadata, and revision conflicts

## Generated Manifests

Scenario apply writes one generated project manifest asset per scenario entry under:

- `Assets/HF Hub/Scenario Sandbox/Generated/`

Those generated assets are still normal discovered project manifests.

Rules:

- generated sandbox manifest assets are treated as maintainer-only test inputs by status code and bypass `InvalidRepository` filtering even when repo ids do not exist remotely
- sandbox still seeds `RepositoryValidationCacheService` with `exists = true` during scenario apply so other editor surfaces read consistent validation state, but manifest-path bypass is authoritative for Model Hub visibility
- sandbox writes one generated project manifest asset per scenario entry so repeated repo ids simulate multiple manifests depending on the same repo
- repeated repo ids intentionally still share one project import/runtime slot because real HF Hub imports are repo-scoped by repository
- prefer explicit revision-conflict scenarios when visually testing repeated repo ids at different requested revisions

## Selected-File Scenarios

The sandbox can model both whole-repo and explicit selected-file dependencies.

Rules:

- for selected-file scenarios, fake manifest `files`, raw cache `Files`, and import metadata hashes must agree with the selected ONNX subset while raw/shared metadata still carries full repository inventory
- never serialize empty `files: []` for whole-repo cases; omit `files` entirely. Empty `files` is invalid by schema and produces `must select at least one file when 'files' is present`
- use repeated same-repo entries to test both multi-manifest shared dependencies and blocked revision-conflict errors before reaching for fake embedded packages

## Supported Workflow

The intended maintainer workflow is:

1. `Apply Scenario`
2. inspect Model Hub UI and diagnostics
3. `Cleanup Scenario`

Treat scenario states like `Import Needed`, `Update Needed`, `Partial Conversion`, and invalid metadata as pre-baked local status fixtures.

## Unsupported Workflow

Generated sandbox manifests are still eligible for normal Hub discovery, so if a maintainer presses `Download`, `Import`, or `Update + Import`, `BackgroundSyncManager` will route them into the real Hugging Face sync path instead of a sandbox-only stub.

Because the sandbox repo ids are intentionally fake, live sync/import against `sandbox/...` entries is not a supported workflow. Expect remote request failures such as `401 Unauthorized` or other upstream lookup errors rather than a successful import.
