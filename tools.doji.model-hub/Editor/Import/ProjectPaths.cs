using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Centralizes runtime payload, shared import, and owner-binding paths used by AI Model Hub.
    /// </summary>
    internal static class ProjectPaths
    {
        private const string SharedImportMetadataFileName = "model-hub-shared-import.json";
        private const string BindingMetadataFileName = "model-hub-binding.json";
        private const string ImportsRoot = "Library/Doji/ModelHub/imports";
        private const string SharedImportsRoot = ImportsRoot + "/shared";
        private const string BindingImportsRoot = ImportsRoot + "/bindings";
        private const string CacheEstimateFileName = "model-hub-cache-size.json";
        private const string CacheEstimatesRoot = "Library/Doji/ModelHub/cache-size-estimates";

        /// <summary>
        /// Returns editor-only binding folder under `Library` for one owner dependency.
        /// </summary>
        public static string GetImportStateFolder(string packageId, string modelId)
        {
            return GetImportBindingFolder(packageId, modelId);
        }

        /// <summary>
        /// Returns the shared import folder of one repository commit, Inference Engine format generation, and settings
        /// set. The last folder name is a short hash of all three, so paths stay under the Windows 260-character limit
        /// for long repository ids. An Inference Engine update within one generation keeps the folder.
        /// </summary>
        public static string GetSharedImportFolder(string repository, string resolvedCommitHash, string sentisVersion, string recipeSetHash)
        {
            var key = TextHash.Sha256($"{resolvedCommitHash}|{SentisCompatibility.GetGeneration(sentisVersion)}|{recipeSetHash}").Substring(0, 16);
            return $"{SharedImportsRoot}/{Hub.SanitizePathSegment(repository)}/{key}";
        }

        /// <summary>
        /// Returns the folder of every shared import of one repository.
        /// </summary>
        public static string GetSharedImportRepositoryFolder(string repository)
        {
            return $"{SharedImportsRoot}/{Hub.SanitizePathSegment(repository)}";
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
            return $"{BindingImportsRoot}/{Hub.SanitizePathSegment(packageId)}/{Hub.SanitizePathSegment(modelId)}";
        }

        /// <summary>
        /// Returns editor-only binding metadata path under `Library`.
        /// </summary>
        public static string GetImportBindingMetadataFilePath(string packageId, string modelId)
        {
            return $"{GetImportBindingFolder(packageId, modelId)}/{BindingMetadataFileName}";
        }



        /// <summary>
        /// Returns project-local sidecar metadata path for one requested repository revision cache-size lookup.
        /// </summary>
        public static string GetCacheSizeEstimateMetadataPath(string repository, string requestedRevision)
        {
            return $"{CacheEstimatesRoot}/{Hub.SanitizePathSegment(repository)}/{Hub.SanitizePathSegment(requestedRevision)}/{CacheEstimateFileName}";
        }

        /// <summary>
        /// Path of one runtime model inside StreamingAssets, the same in builds and for models pulled into the
        /// project: <c>model-hub/models/owner/name/onnx/model.sentis</c>.
        /// </summary>
        public static string GetRuntimeModelRelativePath(string repository, string repoPath)
        {
            return Hub.GetRuntimeModelRelativePath(repository, repoPath);
        }

        /// <summary>
        /// Project asset path of a runtime model pulled into <c>Assets/StreamingAssets</c>.
        /// </summary>
        public static string GetPulledModelAssetPath(string repository, string repoPath)
        {
            return "Assets/StreamingAssets/" + GetRuntimeModelRelativePath(repository, repoPath);
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
            return Hub.SanitizePathSegment(value);
        }
    }
}
