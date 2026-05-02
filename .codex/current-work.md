# Current Work

## Focus
Plan and next implementation slice for whole-repo vs selected-file HuggingFace dependencies, including matching Model Hub UX.

## Next Tasks
- [ ] Define additive manifest extension on current unreleased schema for one repo dependency with optional explicit repo file selection and whole-repo fallback.
- [ ] Thread file selection through discovery, cache, import, and status without changing raw cache identity away from repo + resolved commit.
- [ ] Design shared repo/file picker UX for Project Settings, manifest inspector, and Model Hub add flows.
- [ ] Extend Model Hub details plan so Files/Storage/Summary communicate selected vs unselected repo files clearly.

## Blockers And Decisions Needed
- Keep manifest `version: 1` for first file-selection slice because package is still unreleased.
- Decide whether first slice supports explicit repo paths only or also glob/pattern authoring. Prefer explicit repo paths first.
- Decide whether Model Hub Files tab should show full remote repo inventory or only selected/downloaded files. Prefer full inventory with unselected rows dimmed.

## Read Before Editing
- [references/huggingface-hub-concepts.md](../references/huggingface-hub-concepts.md)
- [com.doji.hf-hub/Runtime/Manifest/ManifestDiscovery.cs](../com.doji.hf-hub/Runtime/Manifest/ManifestDiscovery.cs)
- [com.doji.hf-hub/Editor/Cache/HttpClient.cs](../com.doji.hf-hub/Editor/Cache/HttpClient.cs)
- [com.doji.hf-hub/Editor/Cache/RawCacheService.cs](../com.doji.hf-hub/Editor/Cache/RawCacheService.cs)
- [com.doji.hf-hub/Editor/Status/ModelHubService.cs](../com.doji.hf-hub/Editor/Status/ModelHubService.cs)
