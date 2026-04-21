using System;
using System.IO;
using System.Linq;
using Unity.InferenceEngine;
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
        /// Resolves one synchronized model by canonical HuggingFace model id.
        /// </summary>
        public static bool TryResolveRuntimeModel(string repoId, out ResolvedRuntimeModel model)
        {
            return RuntimeLocatorService.TryResolveModel(repoId, out model);
        }

        /// <summary>
        /// Resolves one synchronized model by manifest owner id plus canonical HuggingFace model id.
        /// </summary>
        public static bool TryResolveRuntimeModel(
            string packageId,
            string repoId,
            out ResolvedRuntimeModel model)
        {
            return RuntimeLocatorService.TryResolveModel(packageId, repoId, out model);
        }

        /// <summary>
        /// Resolves one synchronized model by canonical HuggingFace model id or throws when registry data is missing.
        /// </summary>
        public static ResolvedRuntimeModel ResolveRuntimeModel(string repoId)
        {
            if (TryResolveRuntimeModel(repoId, out var model))
            {
                return model;
            }

            throw new FileNotFoundException(
                $"HF Hub runtime model '{repoId}' was not found under '{Application.streamingAssetsPath}'.");
        }

        /// <summary>
        /// Resolves one synchronized model by manifest owner id plus canonical HuggingFace model id or throws when registry data is missing.
        /// </summary>
        public static ResolvedRuntimeModel ResolveRuntimeModel(string packageId, string repoId)
        {
            if (TryResolveRuntimeModel(packageId, repoId, out var model))
            {
                return model;
            }

            throw new FileNotFoundException(
                $"HF Hub runtime model '{packageId}/{repoId}' was not found under '{Application.streamingAssetsPath}'.");
        }

        /// <summary>
        /// Resolves one synchronized repository snapshot by canonical HuggingFace model id in `from_pretrained(...)` style.
        /// </summary>
        public static PretrainedRepository FromPretrained(string repoId)
        {
            return new PretrainedRepository(ResolveRuntimeModel(repoId));
        }

        /// <summary>
        /// Resolves one synchronized repository snapshot by manifest owner id plus canonical HuggingFace model id in `from_pretrained(...)` style.
        /// </summary>
        public static PretrainedRepository FromPretrained(string packageId, string repoId)
        {
            return new PretrainedRepository(ResolveRuntimeModel(packageId, repoId));
        }

        /// <summary>
        /// Resolves one synchronized repository snapshot by canonical HuggingFace model id and loads one serialized Sentis payload.
        /// </summary>
        public static Model LoadRuntimeModel(string repoId, string repoPath)
        {
            return FromPretrained(repoId).LoadModel(repoPath);
        }

        /// <summary>
        /// Resolves one synchronized repository snapshot and loads one serialized Sentis payload from generated runtime path.
        /// </summary>
        public static Model LoadRuntimeModel(string packageId, string repoId, string repoPath)
        {
            return FromPretrained(packageId, repoId).LoadModel(repoPath);
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
