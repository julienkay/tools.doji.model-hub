using System;
using System.Linq;
using UnityEngine;

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// Stores project-owned HF Hub dependencies as a Unity asset so non-package projects can declare models in-editor.
    /// </summary>
    [CreateAssetMenu(
        fileName = "HF Hub Project Manifest",
        menuName = "Doji/AI/HF Hub Project Manifest",
        order = 1000)]
    public sealed class HfHubProjectManifestAsset : ScriptableObject
    {
        [field: SerializeField]
        public int Version { get; private set; } = 1;

        [field: SerializeField]
        public HfHubProjectManifestModelAssetEntry[] Models { get; private set; } = Array.Empty<HfHubProjectManifestModelAssetEntry>();

        internal bool TryCreateManifest(out HfHubPackageManifest manifest, out string errorMessage)
        {
            var drafts = (Models ?? Array.Empty<HfHubProjectManifestModelAssetEntry>())
                .Select(model => new HfHubManifestModelDraft(
                    model?.Id,
                    model?.DisplayName,
                    model?.Repository,
                    model?.Revision,
                    model?.SourceFile))
                .ToArray();
            return HfHubManifestFactory.TryCreateManifest(Version, drafts, out manifest, out errorMessage);
        }

        internal string ToStableJson()
        {
            return JsonUtility.ToJson(new HfHubProjectManifestAssetDocument(this));
        }
    }

    /// <summary>
    /// Stores one project-manifest model entry in Unity-serializable form.
    /// </summary>
    [Serializable]
    public sealed class HfHubProjectManifestModelAssetEntry
    {
        [field: SerializeField]
        public string Id { get; set; }

        [field: SerializeField]
        public string DisplayName { get; set; }

        [field: SerializeField]
        public string Repository { get; set; } = string.Empty;

        [field: SerializeField]
        public string Revision { get; set; } = "main";

        [field: SerializeField]
        public string SourceFile { get; set; } = string.Empty;
    }

    [Serializable]
    internal sealed class HfHubProjectManifestAssetDocument
    {
        [SerializeField]
        private int _version;

        [SerializeField]
        private HfHubProjectManifestAssetEntryDocument[] _models = Array.Empty<HfHubProjectManifestAssetEntryDocument>();

        public HfHubProjectManifestAssetDocument(HfHubProjectManifestAsset asset)
        {
            _version = asset.Version;
            _models = (asset.Models ?? Array.Empty<HfHubProjectManifestModelAssetEntry>())
                .Select(model => new HfHubProjectManifestAssetEntryDocument(model))
                .ToArray();
        }
    }

    [Serializable]
    internal sealed class HfHubProjectManifestAssetEntryDocument
    {
        [SerializeField]
        private string _id;

        [SerializeField]
        private string _displayName;

        [SerializeField]
        private string _repository;

        [SerializeField]
        private string _revision;

        [SerializeField]
        private string _sourceFile;

        public HfHubProjectManifestAssetEntryDocument(HfHubProjectManifestModelAssetEntry model)
        {
            _id = model?.Id;
            _displayName = model?.DisplayName;
            _repository = model?.Repository;
            _revision = model?.Revision;
            _sourceFile = model?.SourceFile;
        }
    }
}
