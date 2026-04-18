using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// Defines the package-level file and path conventions used by the HuggingFace Hub integration.
    /// </summary>
    public static class ModelHub
    {
        /// <summary>
        /// Package manifest filename that declares remote model dependencies for a Unity package.
        /// </summary>
        public const string ManifestFileName = "hf-hub-manifest.json";

        /// <summary>
        /// Copyable example manifest filename that package authors can use as a starting point.
        /// </summary>
        public const string ManifestTemplateFileName = "hf-hub-manifest.template.json";

        /// <summary>
        /// Root folder inside the Unity project where runtime-loadable generated files are written for builds.
        /// </summary>
        public const string RuntimeStreamingAssetsRoot = "Assets/StreamingAssets/hf-hub";

        /// <summary>
        /// Relative root inside `Application.streamingAssetsPath` used by generated runtime locator registries and files.
        /// </summary>
        public const string RuntimeStreamingRelativeRoot = "hf-hub";

        /// <summary>
        /// Resolves one synchronized model by stable package-local string id.
        /// </summary>
        public static bool TryResolveRuntimeModel(
            string packageId,
            string modelId,
            out HfHubResolvedRuntimeModel model)
        {
            return HfHubRuntimeLocatorService.TryResolveModel(packageId, modelId, out model);
        }

        /// <summary>
        /// Resolves one synchronized model by stable package-local string id or throws when registry data is missing.
        /// </summary>
        public static HfHubResolvedRuntimeModel ResolveRuntimeModel(string packageId, string modelId)
        {
            if (TryResolveRuntimeModel(packageId, modelId, out var model))
            {
                return model;
            }

            throw new FileNotFoundException(
                $"HF Hub runtime model '{packageId}/{modelId}' was not found under '{Application.streamingAssetsPath}'.");
        }

        internal static string GetRuntimeRegistryRelativePath(string packageId)
        {
            return $"{RuntimeStreamingRelativeRoot}/registries/{SanitizePathSegment(packageId)}/hf-hub-runtime.json";
        }

        internal static string GetRuntimeRegistryAbsolutePath(string packageId)
        {
            return Path.Combine(
                Application.streamingAssetsPath,
                GetRuntimeRegistryRelativePath(packageId).Replace('/', Path.DirectorySeparatorChar));
        }

        internal static string GetRuntimeAbsolutePath(string relativePath)
        {
            return Path.Combine(
                Application.streamingAssetsPath,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        internal static string SanitizePathSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "unnamed";
            }

            var invalidCharacters = Path.GetInvalidFileNameChars();
            var sanitized = new string(value
                .Select(character => invalidCharacters.Contains(character) || character == '/' || character == '\\'
                    ? '-'
                    : character)
                .ToArray());

            return sanitized.Replace(" ", "-");
        }

        internal static string SanitizeRelativePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "unnamed";
            }

            return string.Join(
                "/",
                value.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(SanitizePathSegment));
        }

        internal static string SerializeRepositoryId(string repository)
        {
            if (string.IsNullOrWhiteSpace(repository))
            {
                return "models--unnamed";
            }

            return $"models--{string.Join("--", repository.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries).Select(SanitizePathSegment))}";
        }
    }
}
