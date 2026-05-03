# Current Work

## Focus
Selected-file manifest UX is working again; next work is broader follow-up around remaining manifest/discovery validation.

## Next Tasks
- [ ] Verify in Unity that existing ScriptableObject manifest assets (e.g. `Assets/HF Hub/Custom Dependencies.asset`) are still discovered and repos appear in Model Hub.
- [ ] Review whether add-model popups should become revision-aware too, or stay repo-id-only until they expose revision input.
- [ ] Decide whether invalid revision state should surface a stronger inline message beyond icon tooltip and disabled file-selection button.

## Blockers And Decisions Needed
- First slice remains exact explicit repo paths only. No glob support yet.
- Dependencies with no selected `.onnx` file currently stay invalid for import and surface diagnostics instead of generating runtime output.
- No CLI Unity test runner here. Further editor validation still needed for remaining manifest/discovery checks.

## Read Before Editing
- [com.doji.hf-hub/Editor/Cache/RepositoryValidationCacheService.cs](../com.doji.hf-hub/Editor/Cache/RepositoryValidationCacheService.cs)
- [com.doji.hf-hub/Editor/UI/ManifestModelListEditor.cs](../com.doji.hf-hub/Editor/UI/ManifestModelListEditor.cs)
- [com.doji.hf-hub/Editor/UI/ManifestDocumentEditor.cs](../com.doji.hf-hub/Editor/UI/ManifestDocumentEditor.cs)
- [com.doji.hf-hub/Editor/UI/ModelIdPopupValidationController.cs](../com.doji.hf-hub/Editor/UI/ModelIdPopupValidationController.cs)
- [com.doji.hf-hub/Tests/Editor/Cache/RepositoryValidationCacheServiceTests.cs](../com.doji.hf-hub/Tests/Editor/Cache/RepositoryValidationCacheServiceTests.cs)
- [com.doji.hf-hub/Tests/Editor/UI/ManifestModelListEditorTests.cs](../com.doji.hf-hub/Tests/Editor/UI/ManifestModelListEditorTests.cs)
