# Current Work

## Focus
Manifest inspector UX now shared across package JSON, Project Settings JSON, and `ProjectManifestAsset` ScriptableObject manifests; next work is Unity-side validation of inspector behavior and any remaining inline validation gaps.

## Next Tasks
- [ ] Verify in Unity that existing `ProjectManifestAsset` assets show shared inspector, preserve Apply/Revert flow, and still appear in Model Hub discovery.
- [ ] Decide whether invalid revision state needs stronger inline row messaging beyond tooltip + disabled file-selection button across all manifest surfaces.

## Blockers And Decisions Needed
- First slice remains exact explicit repo paths only. No glob support yet.
- Whole-repo import/status depend on populated raw `RepositoryFiles` inventory; keep downloader metadata aligned with that contract.

## Read Before Editing
- [com.doji.hf-hub/Editor/UI/ManifestDocumentEditor.cs](../com.doji.hf-hub/Editor/UI/ManifestDocumentEditor.cs)
- [com.doji.hf-hub/Editor/UI/ProjectManifestAssetInspector.cs](../com.doji.hf-hub/Editor/UI/ProjectManifestAssetInspector.cs)
- [com.doji.hf-hub/Editor/UI/ManifestModelListEditor.cs](../com.doji.hf-hub/Editor/UI/ManifestModelListEditor.cs)
- [com.doji.hf-hub/Editor/UI/ModelIdPopupValidationController.cs](../com.doji.hf-hub/Editor/UI/ModelIdPopupValidationController.cs)
- [com.doji.hf-hub/Tests/Editor/UI/ManifestModelListEditorTests.cs](../com.doji.hf-hub/Tests/Editor/UI/ManifestModelListEditorTests.cs)
