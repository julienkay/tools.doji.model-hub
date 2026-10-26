using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// One model file that a package declares and the project includes, for example a variant in a model picker.
    /// </summary>
    public sealed class HubModelFile
    {
        internal HubModelFile(string packageId, string repoId, string repoPath, string label, ModelDelivery delivery)
        {
            PackageId = packageId;
            RepoId = repoId;
            RepoPath = repoPath;
            Label = ModelDependencyFileDefinition.NormalizeLabel(label);
            Delivery = delivery;
        }

        /// <summary>
        /// Package whose manifest declares the file.
        /// </summary>
        public string PackageId { get; }

        /// <summary>
        /// Hugging Face repository id, for example <c>julienkay/sentis-MiDaS</c>.
        /// </summary>
        public string RepoId { get; }

        /// <summary>
        /// Path of the file in the repository, for example <c>onnx/midas_v21_small_256.onnx</c>.
        /// </summary>
        public string RepoPath { get; }

        /// <summary>
        /// The <c>label</c> the manifest sets for the file, or null.
        /// </summary>
        public string Label { get; }

        /// <summary>
        /// Name to show in a picker: the label, else the repository path without its extension and without a leading
        /// <c>onnx/</c> or <c>sentis/</c> folder, for example <c>unet/model</c>.
        /// </summary>
        public string DisplayName => GetDisplayName(Label, RepoPath);

        public ModelDelivery Delivery { get; }

        /// <summary>
        /// A reference that loads this file.
        /// </summary>
        public HubModelReference ToReference()
        {
            return new HubModelReference(PackageId, RepoId, RepoPath);
        }

        public override string ToString()
        {
            return $"{RepoId}:{RepoPath}";
        }

        internal static string GetDisplayName(string label, string repoPath)
        {
            var name = ModelDependencyFileDefinition.NormalizeLabel(label);
            if (name != null)
            {
                return name;
            }

            var path = repoPath ?? string.Empty;
            var extension = Path.GetExtension(path);
            path = path.Substring(0, path.Length - extension.Length);
            foreach (var folder in new[] { "onnx/", "sentis/" })
            {
                if (path.StartsWith(folder, StringComparison.OrdinalIgnoreCase) && path.Length > folder.Length)
                {
                    return path.Substring(folder.Length);
                }
            }

            return path;
        }

        internal static IReadOnlyList<HubModelFile> FromRegistry(RuntimeRegistry registry, string packageId)
        {
            return (registry?.packages ?? Array.Empty<RuntimePackageEntry>())
                .Where(package => package != null && (packageId == null || string.Equals(package.packageId, packageId, StringComparison.Ordinal)))
                .SelectMany(package => (package.models ?? Array.Empty<RuntimeModelEntry>())
                    .Where(model => model != null)
                    .SelectMany(model => (model.files ?? Array.Empty<RuntimeModelFile>())
                        .Where(file => file != null)
                        .Select(file => new HubModelFile(package.packageId, model.repoId, file.repoPath, file.label, model.Delivery))))
                .ToArray();
        }
    }
}
