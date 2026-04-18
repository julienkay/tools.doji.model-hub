# HuggingFace Hub Reference Concepts

Maintainer notes distilled from local `references/huggingface_hub` checkout.

Purpose:

- keep repeat upstream findings in one tracked place
- align HF Hub package concepts with official `huggingface_hub`
- reduce need to re-read large upstream files for same architectural questions

## Core Identity Model

Primary upstream identity concepts:

- `repo_id`: canonical HuggingFace source id, usually `owner/model-name`
- `repo_type`: `"model"` by default, or `"dataset"`, `"space"`, `"kernel"`
- `revision`: branch, tag, or commit requested by caller
- `commit_hash`: resolved immutable revision target
- `filename` / repo-relative path: concrete file identity inside repo snapshot

Upstream does not introduce extra local alias ids for cache identity.

Closest matching HF Hub package rule:

- use HuggingFace `repository` as canonical upstream source identity
- use repo-relative file path to distinguish concrete payloads inside one repo
- treat `revision` as resolution input, not primary model identity

Key upstream references:

- `repo_folder_name(...)` in `src/huggingface_hub/file_download.py`
- `snapshot_download(...)` in `src/huggingface_hub/_snapshot_download.py`
- `REPO_ID_SEPARATOR` in `src/huggingface_hub/constants.py`

## Cache Layout

`hf_hub_download(...)` cache layout is repo-scoped:

```text
<cache>/<serialized-repo>/
  refs/<revision>
  blobs/<etag-or-sha>
  snapshots/<commit>/<relative-file-path>
```

Meaning:

- repo folder groups everything by upstream repo identity
- `refs/<revision>` stores mutable revision -> commit mapping
- `blobs/` stores deduplicated file content
- `snapshots/<commit>/...` preserves repo-relative file paths within resolved commit

Important implication for HF Hub:

- uniqueness is `repo_id + commit + relative file path`
- not `repo_id + local alias`

## Repo Folder Serialization

Upstream serializes repo id into one flat folder name:

```text
models--owner--model-name
datasets--owner--dataset-name
```

Rule source:

- `repo_folder_name(repo_id, repo_type)`
- `REPO_ID_SEPARATOR = "--"`

Separator safe because upstream disallows `--` in repo ids for this serialization use.

Useful HF Hub takeaway:

- if Unity paths need flat serialization for repo ids, prefer same `--` separator rule
- if nested folders are acceptable, raw `owner/model-name` shape is closer to upstream mental model

## Revision Handling

Upstream treats revision in two stages:

1. caller asks for `revision` like `main`
2. hub resolves to `commit_hash`
3. cache stores `refs/main -> <commit>`
4. files materialize under `snapshots/<commit>/...`

Useful HF Hub takeaway:

- user-facing config can accept `revision`
- generated/import metadata should store resolved commit hash too
- stale detection should compare against resolved commit, not only requested revision

## File Identity Inside Repo

Upstream file download API is:

```python
hf_hub_download(repo_id=..., filename=..., revision=...)
```

Concrete file uniqueness comes from:

- `repo_id`
- `revision` resolved to commit
- `filename` as repo-relative path

Pointer path logic:

```text
snapshots/<commit>/<relative-file-path>
```

Useful HF Hub takeaway:

- one HuggingFace repo can legitimately contain multiple relevant files
- if HF Hub manifest ever supports multi-file entries, key files by repo-relative path within one repo entry
- avoid inventing opaque ids when `repository + sourceFile` already expresses upstream identity clearly

## Snapshot Download Semantics

`snapshot_download(...)` identity still starts from `repo_id`.

Filtering happens with:

- `allow_patterns`
- `ignore_patterns`

It returns local snapshot folder for one resolved commit.

Useful HF Hub takeaway:

- future multi-file manifest shape should probably describe filtered file selection from one repo
- this aligns more with `snapshot_download(...)` than repeated single-file-only entries

## `from_pretrained(...)` Mental Model

In Transformers-style APIs, user usually passes:

- `pretrained_model_name_or_path`

Hosted Hub case usually means:

- HuggingFace repo id like `owner/model-name`

Useful HF Hub takeaway:

- package user should think in upstream repo ids first
- extra package-local alias ids should be avoided unless one repo must expose multiple logical models with same repo source

## Current HF Hub Package Guidance

When choosing ids and paths:

- prefer `repository` as canonical upstream id
- use `sourceFile` when one repo needs file-level uniqueness
- keep runtime/load APIs as close as practical to official Hub source concepts
- only add extra local entry ids if multi-model-per-repo use case proves they are necessary
