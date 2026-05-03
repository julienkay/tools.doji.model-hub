# Current Work

## Focus
Selected-file vs whole-repo dependency support now has broader regression coverage and a maintainer test map; next work is Unity-side validation of remaining manifest discovery/UI behavior.

## Next Tasks
- [ ] Verify in Unity that existing ScriptableObject manifest assets (e.g. `Assets/HF Hub/Custom Dependencies.asset`) are still discovered and repos appear in Model Hub.
- [ ] Decide whether invalid revision state should surface a stronger inline message beyond icon tooltip and disabled file-selection button.

## Blockers And Decisions Needed
- First slice remains exact explicit repo paths only. No glob support yet.
- Whole-repo import/status depend on populated raw `RepositoryFiles` inventory; keep downloader metadata aligned with that contract.

## Read Before Editing
- [.codex/architecture/testing.md](./architecture/testing.md)
- [com.doji.hf-hub/Editor/UI/ManifestModelListEditor.cs](../com.doji.hf-hub/Editor/UI/ManifestModelListEditor.cs)
- [com.doji.hf-hub/Editor/UI/ManifestDocumentEditor.cs](../com.doji.hf-hub/Editor/UI/ManifestDocumentEditor.cs)
- [com.doji.hf-hub/Editor/UI/ModelIdPopupValidationController.cs](../com.doji.hf-hub/Editor/UI/ModelIdPopupValidationController.cs)
- [com.doji.hf-hub/Tests/Editor/UI/ManifestModelListEditorTests.cs](../com.doji.hf-hub/Tests/Editor/UI/ManifestModelListEditorTests.cs)
