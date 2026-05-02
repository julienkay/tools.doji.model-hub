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
    public sealed class ProjectManifestAsset : ScriptableObject
    {
        [field: SerializeField]
        public int Version { get; private set; } = 1;

        [field: SerializeField]
        public ProjectManifestModelAssetEntry[] Models { get; private set; } = Array.Empty<ProjectManifestModelAssetEntry>();

        internal bool TryCreateManifest(out PackageManifest manifest, out string errorMessage)
        {
            var drafts = (Models ?? Array.Empty<ProjectManifestModelAssetEntry>())
                .Select(model => new ManifestModelDraft(
                    model?.Id,
                    model?.Revision,
                    model?.Files))
                .ToArray();
            return ManifestFactory.TryCreateManifest(Version, drafts, out manifest, out errorMessage);
        }

        internal string ToStableJson()
        {
            return JsonUtility.ToJson(new ProjectManifestAssetDocument(this));
        }
    }

    /// <summary>
    /// Stores one project-manifest model entry in Unity-serializable form.
    /// </summary>
    [Serializable]
    public sealed class ProjectManifestModelAssetEntry
    {
        [field: SerializeField]
        public string Id { get; set; }

        [field: SerializeField]
        public string Revision { get; set; } = "main";

        [field: SerializeField]
        public string[] Files { get; set; }
    }

    [Serializable]
    internal sealed class ProjectManifestAssetDocument
    {
        [SerializeField]
        private int _version;

        [SerializeField]
        private ProjectManifestAssetEntryDocument[] _models = Array.Empty<ProjectManifestAssetEntryDocument>();

        public ProjectManifestAssetDocument(ProjectManifestAsset asset)
        {
            _version = asset.Version;
            _models = (asset.Models ?? Array.Empty<ProjectManifestModelAssetEntry>())
                .Select(model => new ProjectManifestAssetEntryDocument(model))
                .ToArray();
        }
    }

    [Serializable]
    internal sealed class ProjectManifestAssetEntryDocument
    {
        [SerializeField]
        private string _id;

        [SerializeField]
        private string _revision;

        [SerializeField]
        private string[] _files;

        public ProjectManifestAssetEntryDocument(ProjectManifestModelAssetEntry model)
        {
            _id = model?.Id;
            _revision = model?.Revision;
            _files = model?.Files;
        }
    }
}
