using System;
using UnityEngine;

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// Stores project-owned optional file selections keyed by manifest owner and model id.
    /// </summary>
    [CreateAssetMenu(
        fileName = "AI Model Hub Optional File Selections",
        menuName = "Doji/AI/AI Model Hub Optional File Selections",
        order = 1001)]
    public sealed class ProjectOptionalFileSelectionsAsset : ScriptableObject
    {
        [field: SerializeField]
        public int Version { get; private set; } = 1;

        [field: SerializeField]
        public ProjectOptionalFileSelectionEntry[] Selections { get; set; } = Array.Empty<ProjectOptionalFileSelectionEntry>();
    }

    /// <summary>
    /// Stores one owner-scoped enabled optional-file set in Unity-serializable form.
    /// </summary>
    [Serializable]
    public sealed class ProjectOptionalFileSelectionEntry
    {
        [field: SerializeField]
        public string OwnerId { get; set; }

        [field: SerializeField]
        public string ModelId { get; set; }

        [field: SerializeField]
        public string[] EnabledOptionalFiles { get; set; }
    }
}
