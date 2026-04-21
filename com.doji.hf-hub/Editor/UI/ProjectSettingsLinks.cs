using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Resolves and opens Project Settings links for source, raw cache, and generated runtime payloads.
    /// </summary>
    internal static class ProjectSettingsLinks
    {
        /// <summary>
        /// Carries one manifest locate target shown in editor menus.
        /// </summary>
        internal readonly struct ManifestLocateTarget
        {
            /// <summary>
            /// Stores menu label and source path for one manifest locate action.
            /// </summary>
            public ManifestLocateTarget(string label, string manifestPath)
            {
                Label = label;
                ManifestPath = manifestPath;
            }

            public string Label { get; }

            public string ManifestPath { get; }
        }

        /// <summary>
        /// Returns HuggingFace repository URL for one declared dependency.
        /// </summary>
        public static string GetSourceRepositoryUrl(DiscoveredModelDependency dependency)
        {
            if (dependency == null)
            {
                throw new ArgumentNullException(nameof(dependency));
            }

            var escapedRevision = Uri.EscapeDataString(dependency.Revision ?? string.Empty);
            return $"https://huggingface.co/{dependency.Repository}/tree/{escapedRevision}";
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
                : ProjectPaths.ToProjectAbsolutePath(normalizedPath);

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
        /// Pings one manifest asset when Unity can resolve it, otherwise reveals backing file.
        /// </summary>
        public static void LocateManifest(string manifestPath)
        {
            if (string.IsNullOrWhiteSpace(manifestPath))
            {
                return;
            }

            var assetPath = TryGetProjectAssetPath(manifestPath);
            if (!string.IsNullOrWhiteSpace(assetPath))
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(assetPath);
                if (asset != null)
                {
                    Selection.activeObject = asset;
                    EditorGUIUtility.PingObject(asset);
                    return;
                }
            }

            RevealPath(manifestPath);
        }

        /// <summary>
        /// Builds stable locate targets for one repository item's dependent manifests.
        /// </summary>
        public static IReadOnlyList<ManifestLocateTarget> BuildManifestLocateTargets(ModelHubItem item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            return item.Dependents
                .Where(dependent => !string.IsNullOrWhiteSpace(dependent.Dependency.ManifestPath))
                .Select(dependent => new ManifestLocateTarget(
                    $"{dependent.Dependency.PackageDisplayName} / {dependent.Dependency.ModelId}",
                    dependent.Dependency.ManifestPath))
                .GroupBy(target => target.ManifestPath, StringComparer.Ordinal)
                // Shared manifest paths can be contributed by multiple dependents; pick one stable label.
                .Select(group => group
                    .OrderBy(target => target.Label, StringComparer.Ordinal)
                    .First())
                .OrderBy(target => target.Label, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Converts absolute manifest paths under project root into Unity asset paths when possible.
        /// </summary>
        internal static string TryGetProjectAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var normalizedPath = path.Replace('\\', '/');
            if (!Path.IsPathRooted(normalizedPath))
            {
                return normalizedPath;
            }

            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."))
                .Replace('\\', '/')
                .TrimEnd('/');
            if (!normalizedPath.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return normalizedPath.Substring(projectRoot.Length + 1);
        }

        /// <summary>
        /// Opens declared remote repository snapshot in default browser.
        /// </summary>
        public static void OpenSourceRepository(DiscoveredModelDependency dependency)
        {
            Application.OpenURL(GetSourceRepositoryUrl(dependency));
        }
    }
}
