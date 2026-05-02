using System;

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// Describes one canonical HuggingFace model id that a Unity package requires from the Hub.
    /// </summary>
    [Serializable]
    public sealed class ModelDependencyDefinition
    {
        /// <summary>
        /// Creates an immutable model dependency definition from one manifest entry.
        /// </summary>
        /// <param name="id">Canonical HuggingFace model identifier, matching Hub `from_pretrained(...)` style source naming.</param>
        /// <param name="revision">Branch, tag, or commit to resolve from the Hub.</param>
        public ModelDependencyDefinition(
            string id,
            string revision)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Revision = revision ?? throw new ArgumentNullException(nameof(revision));
        }

        /// <summary>
        /// Canonical HuggingFace model identifier used for generated paths, cache snapshots, and runtime lookup.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Branch, tag, or commit to resolve before downloading.
        /// </summary>
        public string Revision { get; }

        /// <summary>
        /// Effective branch, tag, or commit after applying manifest defaulting rules for omitted revisions.
        /// </summary>
        public string EffectiveRevision => ManifestRevision.NormalizeForResolution(Revision);
    }
}
