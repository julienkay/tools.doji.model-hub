using System;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Editor-only import metadata persisted under `Library` for one synchronized model payload.
    /// </summary>
    [Serializable]
    internal sealed class HfHubImportedModelMetadata
    {
        public string PackageId { get; set; }
        public string PackageVersion { get; set; }
        public string ModelId { get; set; }
        public string ModelDisplayName { get; set; }
        public string ManifestHash { get; set; }
        public string Repository { get; set; }
        public string RequestedRevision { get; set; }
        public string ResolvedCommitHash { get; set; }
        public string SourceFile { get; set; }
        public string RawFilePath { get; set; }
        public string RawMetadataPath { get; set; }
        public string SentisVersion { get; set; }
        public string UnityVersion { get; set; }
        public string SentisAssetPath { get; set; }
        public string RuntimeRegistryAssetPath { get; set; }
        public string RuntimeRegistryRelativePath { get; set; }
        public string RuntimeSentisAssetPath { get; set; }
        public string RuntimeSentisRelativePath { get; set; }
        public string ImportedAtUtc { get; set; }
    }

    /// <summary>
    /// Represents one generated runtime `.sentis` artifact and its editor-only import metadata.
    /// </summary>
    internal sealed class HfHubImportedModelArtifact
    {
        /// <summary>
        /// Stores generated asset paths and import metadata for one dependency.
        /// </summary>
        public HfHubImportedModelArtifact(
            string sentisAssetPath,
            string metadataFilePath,
            HfHubImportedModelMetadata metadata)
        {
            SentisAssetPath = sentisAssetPath;
            MetadataFilePath = metadataFilePath;
            Metadata = metadata;
        }

        public string SentisAssetPath { get; }
        public string MetadataFilePath { get; }
        public HfHubImportedModelMetadata Metadata { get; }
        public string RuntimeRegistryAssetPath => Metadata?.RuntimeRegistryAssetPath;
        public string RuntimeRegistryRelativePath => Metadata?.RuntimeRegistryRelativePath;
        public string RuntimeSentisAssetPath => Metadata?.RuntimeSentisAssetPath;
        public string RuntimeSentisRelativePath => Metadata?.RuntimeSentisRelativePath;
    }
}
