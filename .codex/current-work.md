# Current Work

## Focus
Optional file selection is implemented. Next session should focus on verification follow-up, any user-reported fixes, and broader polish around the new owner-scoped `optionalFiles` workflow.

## Next Tasks
- [ ] Run the new editor tests inside Unity and fix any failures.
- [ ] Verify the pre-download optional-file UX in Model Hub against real manifests, especially multi-owner same-repo cases.
- [ ] Decide whether project-owned optional selections should stay as a user-creatable asset menu item or become HF Hub-internal only.

## Blockers And Decisions Needed
- No architectural blockers remain for the file-path-first `optionalFiles` slice.
- Validation is still pending in-editor because this repo does not have a CLI Unity test/build path.

## Read Before Editing
- [.codex/architecture/manifest-schema.md](./architecture/manifest-schema.md)
- [com.doji.hf-hub/Editor/Discovery/EditorManifestDiscovery.cs](../com.doji.hf-hub/Editor/Discovery/EditorManifestDiscovery.cs)
- [com.doji.hf-hub/Editor/Import/ProjectImportService.cs](../com.doji.hf-hub/Editor/Import/ProjectImportService.cs)
- [com.doji.hf-hub/Editor/Status/StatusService.cs](../com.doji.hf-hub/Editor/Status/StatusService.cs)
- [com.doji.hf-hub/Editor/UI/ModelHubWindow.DetailsPane.cs](../com.doji.hf-hub/Editor/UI/ModelHubWindow.DetailsPane.cs)
