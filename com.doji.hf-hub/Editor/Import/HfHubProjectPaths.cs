using System;
using System.IO;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Centralizes runtime payload and editor-only import-state paths used by HF Hub.
    /// </summary>
    internal static class HfHubProjectPaths
    {
        private const string ImportMetadataFileName = "hf-hub-import.json";
        private const string ImportStateRoot = "Library/Doji/HfHub/import-state";
        private const string RuntimeRegistryFileName = "hf-hub-runtime.json";
        private const string RuntimeRegistriesRoot = ModelHub.RuntimeStreamingAssetsRoot + "/registries";

        /// <summary>
        /// Returns opaque editor-only import-state folder under `Library` for one package model dependency.
        /// </summary>
        public static string GetImportStateFolder(string packageId, string modelId)
        {
            return $"{ImportStateRoot}/{ModelHub.SanitizePathSegment(packageId)}/{ModelHub.SanitizePathSegment(modelId)}";
        }

        /// <summary>
        /// Returns editor-only import metadata path under `Library`.
        /// </summary>
        public static string GetImportMetadataFilePath(string packageId, string modelId)
        {
            return $"{GetImportStateFolder(packageId, modelId)}/{ImportMetadataFileName}";
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
