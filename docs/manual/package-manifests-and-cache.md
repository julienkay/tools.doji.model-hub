# Model Manifests and Cache

AI Model Hub has two main workflows.

- Package maintainers declare HuggingFace model dependencies in package manifests without committing model files into the package.
- Unity project users resolve those declarations in the Editor, download the files into the shared Hugging Face cache, and choose whether builds bundle each model or download it at runtime.

Start with the workflow that matches your role:

- [Getting Started](getting-started.md)
- [For Package Maintainers](getting-started-package-maintainers.md)
- [For Unity Project Users](getting-started-project-users.md)

Reference pages:

- [Manifest Schema](manifest-reference.md)
- [Editor Surfaces](editor-surfaces.md)
- [Import, Cache, and Runtime Loading](import-cache-runtime.md)
