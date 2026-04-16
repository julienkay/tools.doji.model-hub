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
    }
}
