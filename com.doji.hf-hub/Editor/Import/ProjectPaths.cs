using System;
using System.IO;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Centralizes runtime payload, shared import, and owner-binding paths used by HF Hub.
    /// </summary>
    internal static class ProjectPaths
    {
        private const string SharedImportMetadataFileName = "hf-hub-shared-import.json";
        private const string BindingMetadataFileName = "hf-hub-binding.json";
        private const string ImportsRoot = "Library/Doji/HfHub/imports";
        private const string SharedImportsRoot = ImportsRoot + "/shared";
        private const string BindingImportsRoot = ImportsRoot + "/bindings";
        private const string LegacyImportMetadataFileName = "hf-hub-import.json";
        private const string LegacyImportStateRoot = "Library/Doji/HfHub/import-state";
        private const string CacheEstimateFileName = "hf-hub-cache-size.json";
        private const string CacheEstimatesRoot = "Library/Doji/HfHub/cache-size-estimates";
        private const string RuntimeRegistryFileName = "hf-hub-runtime.json";
        private const string RuntimeRegistriesRoot = ModelHub.RuntimeStreamingAssetsRoot + "/registries";

        /// <summary>
        /// Returns editor-only binding folder under `Library` for one owner dependency.
        /// </summary>
        public static string GetImportStateFolder(string packageId, string modelId)
        {
            return GetImportBindingFolder(packageId, modelId);
        }

        /// <summary>
        /// Returns shared import folder for one generated runtime artifact identity.
        /// </summary>
        public static string GetSharedImportFolder(string repository, string resolvedCommitHash, string sentisVersion, string recipeSetHash)
        {
            return $"{SharedImportsRoot}/{ModelHub.SanitizePathSegment(repository)}/{ModelHub.SanitizePathSegment(resolvedCommitHash)}/{ModelHub.SanitizePathSegment(sentisVersion)}/{ModelHub.SanitizePathSegment(recipeSetHash)}";
        }

        /// <summary>
        /// Returns shared import metadata path under `Library`.
        /// </summary>
        public static string GetSharedImportMetadataFilePath(string repository, string resolvedCommitHash, string sentisVersion, string recipeSetHash)
        {
            return $"{GetSharedImportFolder(repository, resolvedCommitHash, sentisVersion, recipeSetHash)}/{SharedImportMetadataFileName}";
        }

        /// <summary>
        /// Returns editor-only binding folder under `Library` for one owner dependency.
        /// </summary>
        public static string GetImportBindingFolder(string packageId, string modelId)
        {
            return $"{BindingImportsRoot}/{ModelHub.SanitizePathSegment(packageId)}/{ModelHub.SanitizePathSegment(modelId)}";
        }

        /// <summary>
        /// Returns editor-only binding metadata path under `Library`.
        /// </summary>
        public static string GetImportBindingMetadataFilePath(string packageId, string modelId)
        {
            return $"{GetImportBindingFolder(packageId, modelId)}/{BindingMetadataFileName}";
        }

        /// <summary>
        /// Returns legacy monolithic import folder used before shared import split.
        /// </summary>
        public static string GetLegacyImportStateFolder(string packageId, string modelId)
        {
            return $"{LegacyImportStateRoot}/{ModelHub.SanitizePathSegment(packageId)}/{ModelHub.SanitizePathSegment(modelId)}";
        }

        /// <summary>
        /// Returns legacy monolithic import metadata path used before shared import split.
        /// </summary>
        public static string GetLegacyImportMetadataFilePath(string packageId, string modelId)
        {
            return $"{GetLegacyImportStateFolder(packageId, modelId)}/{LegacyImportMetadataFileName}";
        }

        /// <summary>
        /// Returns project-local sidecar metadata path for one requested repository revision cache-size lookup.
        /// </summary>
        public static string GetCacheSizeEstimateMetadataPath(string repository, string requestedRevision)
        {
            return $"{CacheEstimatesRoot}/{ModelHub.SanitizePathSegment(repository)}/{ModelHub.SanitizePathSegment(requestedRevision)}/{CacheEstimateFileName}";
        }

        /// <summary>
        /// Returns runtime registry JSON path for one package under shared StreamingAssets root.
        /// </summary>
        public static string GetRuntimeRegistryAssetPath(string packageId)
        {
            return $"{RuntimeRegistriesRoot}/{ModelHub.SanitizePathSegment(packageId)}/{RuntimeRegistryFileName}";
        }

        /// <summary>
        /// Returns human-readable runtime folder under shared StreamingAssets root using repository owner and model path.
        /// </summary>
        public static string GetRuntimeSnapshotFolder(string repository, string resolvedCommitHash)
        {
            return $"{ModelHub.RuntimeStreamingAssetsRoot}/{ModelHub.SanitizeRelativePath(repository)}";
        }

        /// <summary>
        /// Returns runtime `.sentis` file path derived from one repository-relative ONNX file.
        /// </summary>
        public static string GetRuntimeSentisAssetPath(string repository, string resolvedCommitHash, string repoPath)
        {
            var sanitizedRelativeRepoPath = ModelHub.SanitizeRelativePath(repoPath);
            var sentisRelativePath = Path.ChangeExtension(sanitizedRelativeRepoPath, ".sentis")?.Replace('\\', '/')
                ?? throw new InvalidOperationException($"Could not derive .sentis path from '{repoPath}'.");
            return $"{GetRuntimeSnapshotFolder(repository, resolvedCommitHash)}/{sentisRelativePath}";
        }

        /// <summary>
        /// Converts project asset path under `Assets/StreamingAssets/` into runtime-relative path under `Application.streamingAssetsPath`.
        /// </summary>
        public static string ToStreamingAssetsRelativePath(string assetPath)
        {
            const string streamingAssetsPrefix = "Assets/StreamingAssets/";
            if (!assetPath.StartsWith(streamingAssetsPrefix, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Expected asset path under Assets/StreamingAssets, got '{assetPath}'.");
            }

            return assetPath.Substring(streamingAssetsPrefix.Length);
        }

        /// <summary>
        /// Converts Unity asset path to absolute project file path.
        /// </summary>
        public static string ToAbsolutePath(string assetPath)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }

        /// <summary>
        /// Converts project-relative path such as `Library/...` into absolute project file path.
        /// </summary>
        public static string ToProjectAbsolutePath(string projectRelativePath)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, projectRelativePath));
        }

        /// <summary>
        /// Returns true when path segment is safe for generated asset paths.
        /// </summary>
        public static string SanitizePathSegment(string value)
        {
            return ModelHub.SanitizePathSegment(value);
        }
    }
}
