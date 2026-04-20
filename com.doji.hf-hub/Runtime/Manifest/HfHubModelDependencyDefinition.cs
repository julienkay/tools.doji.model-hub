using System;

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// Describes one remote repository snapshot that a Unity package requires from the HuggingFace Hub.
    /// </summary>
    [Serializable]
    public sealed class HfHubModelDependencyDefinition
    {
        /// <summary>
        /// Creates an immutable model dependency definition from one manifest entry.
        /// </summary>
        /// <param name="id">Canonical HuggingFace model identifier, matching Hub `from_pretrained(...)` style source naming.</param>
        /// <param name="revision">Branch, tag, or commit to resolve from the Hub.</param>
        public HfHubModelDependencyDefinition(
            string id,
            string revision)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Revision = revision ?? throw new ArgumentNullException(nameof(revision));
        }

        /// <summary>
        /// Canonical HuggingFace model identifier used for generated paths and runtime lookup.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Branch, tag, or commit to resolve before downloading.
        /// </summary>
        public string Revision { get; }
    }
}
