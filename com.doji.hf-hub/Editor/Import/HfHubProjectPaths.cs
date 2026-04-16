using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Centralizes project-local generated artifact and temporary import paths.
    /// </summary>
    internal static class HfHubProjectPaths
    {
        private const string GeneratedRoot = "Assets/Doji/HfHub/Generated";
        private const string TemporaryImportRoot = "Assets/Doji/HfHub/TempImport";
        private const string ImportMetadataFileName = "hf-hub-import.json";
        private const string RuntimeRegistryFileName = "hf-hub-runtime.json";
        private const string RuntimeRegistriesRoot = ModelHub.RuntimeStreamingAssetsRoot + "/Registries";
        private const string RuntimeModelsRoot = ModelHub.RuntimeStreamingAssetsRoot + "/Models";

        /// <summary>
        /// Returns project-owned output folder for one package model dependency.
        /// </summary>
        public static string GetGeneratedModelFolder(string packageId, string modelId)
        {
            return $"{GeneratedRoot}/{SanitizePathSegment(packageId)}/{SanitizePathSegment(modelId)}";
        }

        /// <summary>
        /// Returns generated `.sentis` asset path for one dependency.
        /// </summary>
        public static string GetGeneratedSentisAssetPath(string packageId, string modelId, string sourceFile)
        {
            var fileName = $"{Path.GetFileNameWithoutExtension(sourceFile)}.sentis";
            return $"{GetGeneratedModelFolder(packageId, modelId)}/{fileName}";
        }

        /// <summary>
        /// Returns import metadata sidecar path for one dependency.
        /// </summary>
        public static string GetGeneratedMetadataAssetPath(string packageId, string modelId)
        {
            return $"{GetGeneratedModelFolder(packageId, modelId)}/{ImportMetadataFileName}";
        }

        /// <summary>
        /// Returns runtime registry JSON path for one package under shared StreamingAssets root.
        /// </summary>
        public static string GetRuntimeRegistryAssetPath(string packageId)
        {
            return $"{RuntimeRegistriesRoot}/{SanitizePathSegment(packageId)}/{RuntimeRegistryFileName}";
        }

        /// <summary>
        /// Returns model folder under shared StreamingAssets root using package + model segments for global uniqueness.
        /// </summary>
        public static string GetRuntimeModelFolder(string packageId, string modelId)
        {
            return $"{RuntimeModelsRoot}/{SanitizePathSegment(packageId)}/{SanitizePathSegment(modelId)}";
        }

        /// <summary>
        /// Returns primary runtime `.sentis` file path under shared StreamingAssets root.
        /// </summary>
        public static string GetRuntimeSentisAssetPath(string packageId, string modelId, string sourceFile)
        {
            var fileName = $"{Path.GetFileNameWithoutExtension(sourceFile)}.sentis";
            return $"{GetRuntimeModelFolder(packageId, modelId)}/{fileName}";
        }

        /// <summary>
        /// Returns runtime companion/source file path under shared StreamingAssets root while preserving relative repo folders.
        /// </summary>
        public static string GetRuntimeSourceAssetPath(string packageId, string modelId, string sourceFile)
        {
            var sanitizedRelativeSourcePath = SanitizeRelativePath(sourceFile);
            return $"{GetRuntimeModelFolder(packageId, modelId)}/repo/{sanitizedRelativeSourcePath}";
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
        /// Returns temporary project asset path used to trigger Unity's ONNX importer.
        /// </summary>
        public static string GetTemporaryOnnxAssetPath(string packageId, string modelId, string sourceFile)
        {
            var fileName = Path.GetFileName(sourceFile);
            return $"{TemporaryImportRoot}/{SanitizePathSegment(packageId)}/{SanitizePathSegment(modelId)}/{fileName}";
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
        /// Returns true when path segment is safe for generated asset paths.
        /// </summary>
        public static string SanitizePathSegment(string value)
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

        private static string SanitizeRelativePath(string value)
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
    }
}
