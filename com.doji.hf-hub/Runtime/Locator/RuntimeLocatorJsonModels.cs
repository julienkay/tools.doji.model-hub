using System;
using System.Linq;
using UnityEngine;

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// Stores JSON-serializable runtime locator manifest data written into `StreamingAssets`.
    /// </summary>
    [Serializable]
    internal sealed class RuntimeLocatorManifestDocument
    {
        [SerializeField]
        private RuntimeLocatorEntryDocument[] _models = Array.Empty<RuntimeLocatorEntryDocument>();

        [SerializeField]
        private string _packageId;

        [SerializeField]
        private string _packageVersion;

        [SerializeField]
        private int _version;

        public RuntimeLocatorManifestDocument()
        {
        }

        public RuntimeLocatorManifestDocument(RuntimeLocatorManifest manifest)
        {
            _version = manifest.Version;
            _packageId = manifest.PackageId;
            _packageVersion = manifest.PackageVersion;
            _models = manifest.Models.Select(model => new RuntimeLocatorEntryDocument(model)).ToArray();
        }

        public RuntimeLocatorManifest ToManifest()
        {
            return new RuntimeLocatorManifest(
                _version,
                _packageId,
                _packageVersion,
                (_models ?? Array.Empty<RuntimeLocatorEntryDocument>())
                .Select(model => model.ToEntry())
                .ToArray());
        }
    }

    /// <summary>
    /// Stores JSON-serializable runtime locator entry data for one canonical `repo_id`.
    /// </summary>
    [Serializable]
    internal sealed class RuntimeLocatorEntryDocument
    {
        [SerializeField]
        private RuntimeLocatorFileDocument[] _files = Array.Empty<RuntimeLocatorFileDocument>();

        [SerializeField]
        private string _repoId;

        public RuntimeLocatorEntryDocument()
        {
        }

        public RuntimeLocatorEntryDocument(RuntimeLocatorEntry entry)
        {
            _repoId = entry.RepoId;
            _files = entry.Files.Select(file => new RuntimeLocatorFileDocument(file)).ToArray();
        }

        public RuntimeLocatorEntry ToEntry()
        {
            return new RuntimeLocatorEntry(
                _repoId,
                (_files ?? Array.Empty<RuntimeLocatorFileDocument>())
                .Select(file => file.ToFile())
                .ToArray());
        }
    }

    /// <summary>
    /// Stores JSON-serializable runtime locator file data.
    /// </summary>
    [Serializable]
    internal sealed class RuntimeLocatorFileDocument
    {
        [SerializeField]
        private string _kind;

        [SerializeField]
        private string _relativePath;

        [SerializeField]
        private string _repoPath;

        public RuntimeLocatorFileDocument()
        {
        }

        public RuntimeLocatorFileDocument(RuntimeLocatorFile file)
        {
            _relativePath = file.RelativePath;
            _repoPath = file.RepoPath;
            _kind = file.Kind;
        }

        public RuntimeLocatorFile ToFile()
        {
            return new RuntimeLocatorFile(_relativePath, _repoPath, _kind);
        }
    }
}
