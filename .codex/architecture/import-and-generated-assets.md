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

Generated runtime payloads currently live under:

```text
Assets/StreamingAssets/hf-hub/<serialized-repo>/snapshots/<resolved-commit>/
```

Current conversion flow:

1. Resolve the required ONNX file from the shared raw cache.
2. Convert cached ONNX file directly through Unity Inference editor conversion code without importing that ONNX file as a Unity asset.
3. Serialize the converted model as `.sentis` directly into `StreamingAssets` with `ModelWriter.Save(...)`.
4. Write editor-only provenance metadata under `Library/Doji/HfHub/import-state/...`.

Current generated output now includes:

- `Assets/StreamingAssets/hf-hub/<serialized-repo>/snapshots/<resolved-commit>/<source-file-name>.sentis`
- `Assets/StreamingAssets/hf-hub/registries/<package-id>/hf-hub-runtime.json`
- `Library/Doji/HfHub/import-state/<package-id>/<model-id>/hf-hub-import.json`

`StreamingAssets` `.sentis` is build-facing payload. Registry JSON is string-id lookup index used by runtime locator APIs. `Library` metadata is editor-side provenance for stale detection and re-import decisions.

Conversion abstraction now hangs off `IHfHubOnnxModelConverter`:

- editor uses Unity Inference internal ONNX conversion through a small bridge assembly
- runtime placeholder currently throws `NotImplementedException`
- future custom ONNX package should implement the same contract for runtime-safe conversion

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
