# Current Work

## Focus
Model Hub header add-menu now drives project manifest authoring. Active slice is project-scoped `ProjectSettings/hf-hub-manifest.json` discovery plus add-model UX.

## Next Tasks
- [ ] Verify Unity compiles the new `AddProjectModelPopup` and `ProjectSettingsModelManifestStore` flow cleanly.
- [ ] Decide whether `Add model to package...` should stay clipboard help or open a real package-manifest authoring flow.
- [ ] Review any remaining user-facing text that still implies project manifests only live as ScriptableObject assets.

## Blockers And Decisions Needed
- Unity compile/status not verified in-session.

## Read Before Editing
- [com.doji.hf-hub/Editor/UI/ModelHubWindow.Actions.cs](../com.doji.hf-hub/Editor/UI/ModelHubWindow.Actions.cs)
- [com.doji.hf-hub/Editor/Discovery/ProjectManifestDiscovery.cs](../com.doji.hf-hub/Editor/Discovery/ProjectManifestDiscovery.cs)
- [com.doji.hf-hub/Editor/UI/ProjectSettingsModelManifestStore.cs](../com.doji.hf-hub/Editor/UI/ProjectSettingsModelManifestStore.cs)
