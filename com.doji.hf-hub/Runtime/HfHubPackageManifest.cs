using System;
using System.Collections.Generic;
using System.Linq;

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// Declares the HuggingFace model dependencies required by one Unity package.
    /// </summary>
    [Serializable]
    public sealed class HfHubPackageManifest
    {
        private readonly HfHubModelDependencyDefinition[] _models;

        /// <summary>
        /// Creates an immutable package manifest from parsed manifest data.
        /// </summary>
        /// <param name="version">Manifest schema version.</param>
        /// <param name="models">Model dependencies required by the package.</param>
        public HfHubPackageManifest(int version, IReadOnlyList<HfHubModelDependencyDefinition> models)
        {
            Version = version;
            _models = models?.ToArray() ?? Array.Empty<HfHubModelDependencyDefinition>();
        }

        /// <summary>
        /// Manifest schema version used to validate parser and discovery behavior.
        /// </summary>
        public int Version { get; }

        /// <summary>
        /// Remote model files that this package requires the Hub to resolve and import.
        /// </summary>
        public IReadOnlyList<HfHubModelDependencyDefinition> Models => _models;
    }
}
