using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// Resolves generated runtime model data from package-owned HF Hub locator registries.
    /// </summary>
    internal static class HfHubRuntimeLocatorService
    {
        /// <summary>
        /// Resolves one synchronized model by package id and model id.
        /// </summary>
        public static bool TryResolveModel(
            string packageId,
            string modelId,
            out HfHubResolvedRuntimeModel model)
        {
            model = null;
            if (string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(modelId))
            {
                return false;
            }

            if (!TryLoadManifest(packageId, out var manifest))
            {
                return false;
            }

            var entry = manifest.Models.FirstOrDefault(candidate =>
                string.Equals(candidate.ModelId, modelId, StringComparison.Ordinal));
            if (entry == null)
            {
                return false;
            }

            var files = entry.Files
                .Select(file => new HfHubResolvedRuntimeFile(
                    ModelHub.GetRuntimeAbsolutePath(file.RelativePath),
                    file.SourceFile,
                    file.Kind))
                .ToArray();

            model = new HfHubResolvedRuntimeModel(
                manifest.PackageId,
                manifest.PackageVersion,
                entry.ModelId,
                entry.DisplayName,
                ModelHub.GetRuntimeAbsolutePath(entry.PrimarySentisRelativePath),
                files);
            return true;
        }

        internal static bool TryLoadManifest(string packageId, out HfHubRuntimeLocatorManifest manifest)
        {
            manifest = null;

            var absolutePath = ModelHub.GetRuntimeRegistryAbsolutePath(packageId);
            if (!File.Exists(absolutePath))
            {
                return false;
            }

            try
            {
                var json = File.ReadAllText(absolutePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return false;
                }

                var document = JsonUtility.FromJson<HfHubRuntimeLocatorManifestDocument>(json);
                if (document == null)
                {
                    return false;
                }

                manifest = document.ToManifest();
                return manifest != null;
            }
            catch
            {
                return false;
            }
        }
    }
}
