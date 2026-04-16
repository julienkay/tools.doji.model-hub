using System;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Sidecar metadata written next to generated project-owned `.sentis` artifact.
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
        public string ImportedAtUtc { get; set; }
    }

    /// <summary>
    /// Represents one generated project-owned `.sentis` artifact and its metadata.
    /// </summary>
    internal sealed class HfHubImportedModelArtifact
    {
        /// <summary>
        /// Stores generated asset paths and import metadata for one dependency.
        /// </summary>
        public HfHubImportedModelArtifact(
            string sentisAssetPath,
            string metadataAssetPath,
            HfHubImportedModelMetadata metadata)
        {
            SentisAssetPath = sentisAssetPath;
            MetadataAssetPath = metadataAssetPath;
            Metadata = metadata;
        }

        public string SentisAssetPath { get; }
        public string MetadataAssetPath { get; }
        public HfHubImportedModelMetadata Metadata { get; }
    }
}
