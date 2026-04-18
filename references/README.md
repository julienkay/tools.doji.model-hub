# Reference Repositories

This folder is for maintainer-only upstream notes plus local reference checkouts.

Tracked maintainer docs may live here.

Ignored checkout:

- `references/huggingface_hub`

## huggingface_hub

- Local path: `references/huggingface_hub`
- Remote: `https://github.com/huggingface/huggingface_hub`
- Current checkout intent: local reference for cache layout, download semantics, revision handling, and Hub API behavior
- Current cloned revision: `f6b3dbe966b906e10494d8224e5857dc07db4cd7`

Primary files to inspect first:

- `src/huggingface_hub/file_download.py`
- `src/huggingface_hub/_snapshot_download.py`
- `src/huggingface_hub/hf_api.py`
- `src/huggingface_hub/constants.py`

How future sessions should use it:

- Reuse this checkout if it already exists.
- Pull updates only when needed for a task that depends on newer Hub behavior.
- Treat it as reference code, not vendored package code.
- Prefer reading only the specific files relevant to the current decision.

Tracked maintainer notes in this folder:

- [`huggingface-hub-concepts.md`](./huggingface-hub-concepts.md)
