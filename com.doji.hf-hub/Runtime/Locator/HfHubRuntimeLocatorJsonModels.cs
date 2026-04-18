using System;
using System.Linq;
using UnityEngine;

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// Stores JSON-serializable runtime locator manifest data written into `StreamingAssets`.
    /// </summary>
    [Serializable]
    internal sealed class HfHubRuntimeLocatorManifestDocument
    {
        [SerializeField]
        private HfHubRuntimeLocatorEntryDocument[] _models = Array.Empty<HfHubRuntimeLocatorEntryDocument>();

        [SerializeField]
        private string _packageId;

        [SerializeField]
        private string _packageVersion;

        [SerializeField]
        private int _version;

        public HfHubRuntimeLocatorManifestDocument()
        {
        }

        public HfHubRuntimeLocatorManifestDocument(HfHubRuntimeLocatorManifest manifest)
        {
            _version = manifest.Version;
            _packageId = manifest.PackageId;
            _packageVersion = manifest.PackageVersion;
            _models = manifest.Models.Select(model => new HfHubRuntimeLocatorEntryDocument(model)).ToArray();
        }

        public HfHubRuntimeLocatorManifest ToManifest()
        {
            return new HfHubRuntimeLocatorManifest(
                _version,
                _packageId,
                _packageVersion,
                (_models ?? Array.Empty<HfHubRuntimeLocatorEntryDocument>())
                .Select(model => model.ToEntry())
                .ToArray());
        }
    }

    /// <summary>
    /// Stores JSON-serializable runtime locator entry data for one model id.
    /// </summary>
    [Serializable]
    internal sealed class HfHubRuntimeLocatorEntryDocument
    {
        [SerializeField]
        private string _displayName;

        [SerializeField]
        private HfHubRuntimeLocatorFileDocument[] _files = Array.Empty<HfHubRuntimeLocatorFileDocument>();

        [SerializeField]
        private string _modelId;

        [SerializeField]
        private string _primarySentisRelativePath;

        public HfHubRuntimeLocatorEntryDocument()
        {
        }

        public HfHubRuntimeLocatorEntryDocument(HfHubRuntimeLocatorEntry entry)
        {
            _modelId = entry.ModelId;
            _displayName = entry.DisplayName;
            _primarySentisRelativePath = entry.PrimarySentisRelativePath;
            _files = entry.Files.Select(file => new HfHubRuntimeLocatorFileDocument(file)).ToArray();
        }

        public HfHubRuntimeLocatorEntry ToEntry()
        {
            return new HfHubRuntimeLocatorEntry(
                _modelId,
                _displayName,
                _primarySentisRelativePath,
                (_files ?? Array.Empty<HfHubRuntimeLocatorFileDocument>())
                .Select(file => file.ToFile())
                .ToArray());
        }
    }

    /// <summary>
    /// Stores JSON-serializable runtime locator file data.
    /// </summary>
    [Serializable]
    internal sealed class HfHubRuntimeLocatorFileDocument
    {
        [SerializeField]
        private string _kind;

        [SerializeField]
        private string _relativePath;

        [SerializeField]
        private string _sourceFile;

        public HfHubRuntimeLocatorFileDocument()
        {
        }

        public HfHubRuntimeLocatorFileDocument(HfHubRuntimeLocatorFile file)
        {
            _relativePath = file.RelativePath;
            _sourceFile = file.SourceFile;
            _kind = file.Kind;
        }

        public HfHubRuntimeLocatorFile ToFile()
        {
            return new HfHubRuntimeLocatorFile(_relativePath, _sourceFile, _kind);
        }
    }
}
