using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Resolves and opens Project Settings links for source, raw cache, and generated runtime payloads.
    /// </summary>
    internal static class HfHubProjectSettingsLinks
    {
        /// <summary>
        /// Returns HuggingFace repository URL for one declared source file.
        /// </summary>
        public static string GetSourceRepositoryUrl(HfHubDiscoveredModelDependency dependency)
        {
            if (dependency == null)
            {
                throw new ArgumentNullException(nameof(dependency));
            }

            var escapedRevision = Uri.EscapeDataString(dependency.Revision ?? string.Empty);
            var escapedSourceSegments = (dependency.SourceFile ?? string.Empty)
                .Replace('\\', '/')
                .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            var escapedSourcePath = string.Join("/", Array.ConvertAll(escapedSourceSegments, Uri.EscapeDataString));
            return $"https://huggingface.co/{dependency.Repository}/blob/{escapedRevision}/{escapedSourcePath}";
        }

        /// <summary>
        /// Opens one local file or folder in Finder/Explorer.
        /// </summary>
        public static void RevealPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var normalizedPath = path.Replace('\\', '/');
            var absolutePath = Path.IsPathRooted(normalizedPath)
                ? normalizedPath
                : HfHubProjectPaths.ToProjectAbsolutePath(normalizedPath);

            if (File.Exists(absolutePath) || Directory.Exists(absolutePath))
            {
                EditorUtility.RevealInFinder(absolutePath);
            }
        }

        /// <summary>
        /// Selects one Unity asset and pings it in Project view.
        /// </summary>
        public static void PingAsset(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return;
            }

            var asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (asset == null)
            {
                return;
            }

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        /// <summary>
        /// Opens declared remote source file in default browser.
        /// </summary>
        public static void OpenSourceRepositoryFile(HfHubDiscoveredModelDependency dependency)
        {
            Application.OpenURL(GetSourceRepositoryUrl(dependency));
        }
    }
}
