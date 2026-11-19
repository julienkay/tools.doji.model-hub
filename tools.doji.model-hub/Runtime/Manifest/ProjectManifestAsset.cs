using System;
using System.Linq;
using UnityEngine;

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// Stores project-owned AI Model Hub dependencies as a Unity asset so non-package projects can declare models in-editor.
    /// </summary>
    [CreateAssetMenu(
        fileName = "AI Model Hub Project Manifest",
        menuName = "Doji/AI/AI Model Hub Project Manifest",
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
                    model?.Files?.Length > 0
                        ? model.Files.Select(file => file?.ToDefinition()).ToArray()
                        : null))
                .ToArray();
            return ManifestFactory.TryCreateManifest(Version, drafts, out manifest, out errorMessage);
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
        public ProjectManifestFileAssetEntry[] Files { get; set; }
    }

    /// <summary>
    /// Stores one selected repository file and whether project owners may opt into it.
    /// </summary>
    [Serializable]
    public sealed class ProjectManifestFileAssetEntry
    {
        [field: SerializeField]
        public string Path { get; set; }

        [field: SerializeField]
        public bool Optional { get; set; }

        [field: SerializeField]
        public string Label { get; set; }

        /// <summary>
        /// Recommended quantization for an ONNX file: <c>none</c>, <c>float16</c>, or <c>uint8</c>. Empty, with no
        /// <see cref="InputShapes"/>, means the manifest recommends no conversion settings.
        /// </summary>
        [field: SerializeField]
        public string Quantization { get; set; }

        /// <summary>
        /// Recommended fixed input dimensions for an ONNX file.
        /// </summary>
        [field: SerializeField]
        public ProjectManifestInputShapeAssetEntry[] InputShapes { get; set; }

        internal ModelDependencyFileDefinition ToDefinition()
        {
            return new ModelDependencyFileDefinition(Path, Optional, Label, ToRecipe(Quantization, InputShapes));
        }

        internal static OnnxConversionRecipe ToRecipe(string quantization, ProjectManifestInputShapeAssetEntry[] inputShapes)
        {
            var shapes = (inputShapes ?? Array.Empty<ProjectManifestInputShapeAssetEntry>())
                .Where(shape => !string.IsNullOrWhiteSpace(shape?.Name))
                .Select(shape => new OnnxStaticInputShapeOverride
                {
                    InputName = shape.Name.Trim(),
                    Dimensions = (shape.Dimensions ?? Array.Empty<int>()).ToArray()
                })
                .ToArray();
            if (string.IsNullOrWhiteSpace(quantization) && shapes.Length == 0)
            {
                return null;
            }

            return new OnnxConversionRecipe
            {
                QuantizationMode = OnnxConversionSettings.NormalizeQuantizationMode(quantization),
                StaticInputShapeOverrides = shapes
            };
        }
    }

    /// <summary>
    /// Stores one recommended fixed input shape for an ONNX file.
    /// </summary>
    [Serializable]
    public sealed class ProjectManifestInputShapeAssetEntry
    {
        [field: SerializeField]
        public string Name { get; set; }

        [field: SerializeField]
        public int[] Dimensions { get; set; }
    }
}
