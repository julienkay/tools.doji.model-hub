# Downloader Package

## Package Boundary

- New package is independent from `Doji.AI.ModelHub` and HuggingFace concerns.
- Working package name: `com.doji.downloads`.
- Keep it in the same repository for now; separate repo can wait until the API shape proves stable.

## Downloader Package Owns

- persisted download-job state
- resumable URL-to-file transfer
- partial file conventions such as `.downloading` or `.part`
- file locks and duplicate-work prevention
- transport abstraction
- retry, cancel, and progress contracts
- runtime-safe core API
- optional Unity editor adapter for `Progress` integration

## HF Hub Still Owns

- package `hf-hub-manifest.json` discovery
- HuggingFace repo/revision/file resolution
- HuggingFace-specific cache policy and metadata
- user-facing model dependency UX
- raw-model-to-`.sentis` import pipeline
- generated asset metadata and stale detection
- package/runtime locator contract for generated models

## Current Repository Status

- `com.doji.downloads/` now contains runtime/editor implementation for shared progress, file locking, resumable `UnityWebRequest` downloads, persisted queue-state storage, generic editor recovery/progress runner primitives, and generic sequential item-queue execution.
- `projects/HF Hub/Packages/manifest.json` and `Packages/packages-lock.json` reference `com.doji.downloads`.
- Downloader-owned tests now live under `com.doji.downloads/Tests/Editor/`.

## Remaining Downloader Guidance

- Keep HF Hub as queue owner over downloader primitives.
- Reuse downloader package for mechanics, not HuggingFace-specific queue meaning.
- Avoid further generalization until another real consumer needs it.
