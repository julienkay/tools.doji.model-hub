using System;
using UnityEngine;

namespace Doji.AI.ModelHub.Sandbox
{
    /// <summary>
    /// High-level fake local state that visual sandbox writes for one declared AI Model Hub dependency.
    /// </summary>
    public enum VisualScenarioState
    {
        DownloadNeeded,
        ConversionNeeded,
        Imported,
        ImportedCacheRemoved,
        UpdateNeeded,
        RuntimeFileMissing,
        InvalidImportMetadata,
        InvalidRawMetadata,
        PartialConversionWarning
    }

    /// <summary>
    /// Describes one fake AI Model Hub dependency entry that sandbox materializes into local project/cache files.
    /// </summary>
    [Serializable]
    public sealed class VisualScenarioEntry
    {
        [field: SerializeField]
        public string Repository { get; set; } = "sandbox/example-model";

        [field: SerializeField]
        public string Revision { get; set; } = "main";

        [field: SerializeField]
        public string ResolvedCommitHash { get; set; } = "sandbox-commit-001";

        [field: SerializeField]
        public string RepoPath { get; set; } = "models/model.onnx";

        [field: SerializeField]
        public string[] SelectedFiles { get; set; }

        [field: SerializeField]
        public string[] RepositoryFiles { get; set; }

        [field: SerializeField]
        public VisualScenarioState State { get; set; } = VisualScenarioState.ConversionNeeded;

        [field: SerializeField]
        public string PartialWarningRepoPath { get; set; } = "models/model_bnb4.onnx";

        [field: SerializeField]
        public string PartialWarningDetail { get; set; } = "unsupported operators or data types remain.";
    }

    /// <summary>
    /// Stores maintainer-authored fake AI Model Hub states for visual verification inside companion Unity project.
    /// </summary>
    [CreateAssetMenu(
        fileName = "AI Model Hub Visual Scenario",
        menuName = "Doji/AI/AI Model Hub Visual Scenario",
        order = 1001)]
    public sealed class VisualScenarioAsset : ScriptableObject
    {
        [field: SerializeField]
        [TextArea]
        public string Notes { get; set; } =
            "Use unique sandbox repo ids so fake artifacts do not collide with real generated AI Model Hub content.";

        [field: SerializeField]
        public VisualScenarioEntry[] Entries { get; set; } = Array.Empty<VisualScenarioEntry>();
    }
}
