using System;
using System.Linq;
using UnityEngine;

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// Stores project-owned file-level ONNX conversion overrides so generated Sentis output can differ per Unity project.
    /// </summary>
    [CreateAssetMenu(
        fileName = "AI Model Hub Conversion Overrides",
        menuName = "Doji/AI/AI Model Hub Conversion Overrides",
        order = 1001)]
    public sealed class ProjectModelConversionOverridesAsset : ScriptableObject
    {
        [field: SerializeField]
        public int Version { get; private set; } = 1;

        [field: SerializeField]
        public ProjectModelConversionOverrideEntry[] Overrides { get; set; } = Array.Empty<ProjectModelConversionOverrideEntry>();

        internal string ToStableJson()
        {
            return JsonUtility.ToJson(new ProjectModelConversionOverridesDocument(this));
        }
    }

    /// <summary>
    /// Stores one file-level conversion override for one repository-relative ONNX path.
    /// </summary>
    [Serializable]
    public sealed class ProjectModelConversionOverrideEntry
    {
        [field: SerializeField]
        public string Repository { get; set; }

        [field: SerializeField]
        public string RepoPath { get; set; }

        [field: SerializeField]
        public OnnxConversionRecipe Recipe { get; set; } = new();
    }

    [Serializable]
    internal sealed class ProjectModelConversionOverridesDocument
    {
        [SerializeField]
        private int _version;

        [SerializeField]
        private ProjectModelConversionOverrideEntryDocument[] _overrides = Array.Empty<ProjectModelConversionOverrideEntryDocument>();

        public ProjectModelConversionOverridesDocument(ProjectModelConversionOverridesAsset asset)
        {
            _version = asset.Version;
            _overrides = (asset.Overrides ?? Array.Empty<ProjectModelConversionOverrideEntry>())
                .Select(item => new ProjectModelConversionOverrideEntryDocument(item))
                .ToArray();
        }
    }

    [Serializable]
    internal sealed class ProjectModelConversionOverrideEntryDocument
    {
        [SerializeField]
        private string _repository;

        [SerializeField]
        private string _repoPath;

        [SerializeField]
        private OnnxConversionRecipe _recipe;

        public ProjectModelConversionOverrideEntryDocument(ProjectModelConversionOverrideEntry entry)
        {
            _repository = entry?.Repository;
            _repoPath = entry?.RepoPath;
            _recipe = entry?.Recipe ?? new OnnxConversionRecipe();
        }
    }
}
