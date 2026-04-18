using System;

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// Describes one remote model file that a Unity package requires from the HuggingFace Hub.
    /// </summary>
    [Serializable]
    public sealed class HfHubModelDependencyDefinition
    {
        /// <summary>
        /// Creates an immutable model dependency definition from one manifest entry.
        /// </summary>
        /// <param name="id">Stable package-local alias used by HF Hub for lookup inside one Unity package.</param>
        /// <param name="displayName">User-facing model name shown in prompts and status output.</param>
        /// <param name="repository">HuggingFace model id or repository id, matching Hub `from_pretrained(...)` style source naming.</param>
        /// <param name="revision">Branch, tag, or commit to resolve from the Hub.</param>
        /// <param name="sourceFile">Source ONNX file path inside the remote repository.</param>
        public HfHubModelDependencyDefinition(
            string id,
            string displayName,
            string repository,
            string revision,
            string sourceFile)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
            Repository = repository ?? throw new ArgumentNullException(nameof(repository));
            Revision = revision ?? throw new ArgumentNullException(nameof(revision));
            SourceFile = sourceFile ?? throw new ArgumentNullException(nameof(sourceFile));
        }

        /// <summary>
        /// Stable package-local alias used for generated paths and package-facing runtime lookup.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// User-facing model name shown in editor prompts and status output.
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// HuggingFace model id or repository id that hosts the source model file.
        /// </summary>
        public string Repository { get; }

        /// <summary>
        /// Branch, tag, or commit to resolve before downloading.
        /// </summary>
        public string Revision { get; }

        /// <summary>
        /// Source ONNX file path inside the remote repository.
        /// </summary>
        public string SourceFile { get; }
    }
}
