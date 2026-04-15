# My Project

Reference and download AI models from HuggingFace for Unity Sentis packages.

Current architecture direction:

- shared Hub logic belongs in `com.doji.hf-hub/Runtime`
- `Editor` should contain only UnityEditor adapters and editor UX
- this split is intentional so package can later download HuggingFace files at runtime too

## Installation

Add the package via UPM using:

`com.doji.hf-hub`

## Documentation

Document the public workflow, setup steps, and examples here.
