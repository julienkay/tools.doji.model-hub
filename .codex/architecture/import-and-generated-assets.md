# Import And Generated Assets

## Conversion Trigger

V1 conversion is editor-driven and package-discovery-based.

Primary trigger:

- package manifests are discovered when the Editor detects package changes or opens a project

Behavior:

1. Scan installed packages for `hf-hub-manifest.json`.
2. Collect unresolved or stale model dependencies declared by those package manifests.
3. Show the user a prompt listing the package and model dependencies that need attention.
4. If the user approves, resolve each model entry to a cached raw ONNX file, downloading it if missing.
5. Convert missing or stale models using the currently installed Sentis version.
6. Write a small metadata file next to each generated asset so later scans can detect staleness cheaply.

The key behavior is automatic prompting, not silent automatic import. The Hub should behave more like Unity Package Manager: package dependencies are discovered automatically, but the expensive content step is made explicit to the user.

The expensive content step must still be non-blocking:

- approval is explicit
- execution is backgrounded
- progress is visible
- interruption should be recoverable

Fallback trigger:

- `Tools/Doji/HF Hub/Sync Models`

The menu command remains available to rescan package manifests and retry downloads or conversion after a user initially postpones the prompt.

## Current Generated Layout

Generated project-owned artifacts currently live under:

```text
Assets/Doji/HfHub/Generated/<package-id>/<model-id>/
```

Current conversion flow:

1. Resolve the required ONNX file from the shared raw cache.
2. Copy it into a temporary project import path under `Assets/Doji/HfHub/TempImport/...`.
3. Let Unity and Sentis import that temporary ONNX asset.
4. Serialize the imported model as `.sentis` with `ModelWriter.Save(...)`.
5. Delete the temporary ONNX import asset after conversion completes.

Each generated output folder currently includes:

- `<source-file-name>.sentis`
- `hf-hub-import.json`

That metadata file is the editor-side provenance record used for stale detection and re-import decisions.

## Sentis Version Invalidation

Each generated model gets a sidecar metadata file in its derived output folder.

Minimum metadata:

```json
{
  "manifestVersion": 1,
  "packageId": "com.example.ai-feature",
  "modelId": "distilbert-base-uncased",
  "repository": "hf-internal-testing/tiny-random-bert",
  "revision": "main",
  "sourceFile": "model.onnx",
  "manifestHash": "<computed-hash>",
  "sentisVersion": "2.6.1",
  "unityVersion": "6000.3.10f1"
}
```

During `Sync Models`, a generated `.sentis` asset is stale and must be rebuilt if any of these change:

- package manifest content hash
- installed Sentis version
- generated asset or metadata file missing

The manifest hash covers dependency identity changes such as `repository`, `revision`, or `sourceFile` without maintaining a larger invalidation matrix.

Unity version is recorded for diagnostics, but Sentis version is the primary rebuild key beyond manifest changes.
