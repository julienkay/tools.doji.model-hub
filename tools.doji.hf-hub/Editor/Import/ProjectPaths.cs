using System;
using System.IO;
using System.Linq;
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

        /// <summary>
        /// Returns editor-only binding folder under `Library` for one owner dependency.
        /// </summary>
        public static string GetImportStateFolder(string packageId, string modelId)
        {
            return GetImportBindingFolder(packageId, modelId);
        }

        /// <summary>
        /// Returns the shared import folder of one repository commit, Inference Engine version, and settings set. The
        /// last folder name is a short hash of all three, so paths stay under the Windows 260-character limit for long
        /// repository ids.
        /// </summary>
        public static string GetSharedImportFolder(string repository, string resolvedCommitHash, string sentisVersion, string recipeSetHash)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var identity = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes($"{resolvedCommitHash}|{sentisVersion}|{recipeSetHash}"));
            var key = string.Concat(System.Linq.Enumerable.Take(identity, 8).Select(value => value.ToString("x2")));
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
        /// Returns legacy monolithic import folder used before shared import split.
        /// </summary>
        public static string GetLegacyImportStateFolder(string packageId, string modelId)
        {
            return $"{LegacyImportStateRoot}/{Hub.SanitizePathSegment(packageId)}/{Hub.SanitizePathSegment(modelId)}";
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
            return $"{CacheEstimatesRoot}/{Hub.SanitizePathSegment(repository)}/{Hub.SanitizePathSegment(requestedRevision)}/{CacheEstimateFileName}";
        }

        /// <summary>
        /// Path of one runtime model inside StreamingAssets, the same in builds and for models pulled into the
        /// project: <c>hf-hub/models/owner/name/onnx/model.sentis</c>.
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
