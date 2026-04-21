using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Scans project assets for `ProjectManifestAsset` declarations and normalizes them into standard Hub discovery data.
    /// </summary>
    internal static class ProjectManifestDiscovery
    {
        internal const string ProjectManifestOwnerId = "project";
        private const string ProjectManifestOwnerDisplayName = "Project";
        private const string ProjectManifestOwnerVersion = "project";

        public static DiscoveryReport DiscoverProjectManifests()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var snapshots = AssetDatabase
                .FindAssets("t:ProjectManifestAsset")
                .Select(guid => AssetDatabase.GUIDToAssetPath(guid))
                .Where(assetPath => !string.IsNullOrWhiteSpace(assetPath))
                .Select(assetPath => new ProjectManifestAssetSnapshot(
                    assetPath,
                    Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar)),
                    projectRoot,
                    AssetDatabase.LoadAssetAtPath<ProjectManifestAsset>(assetPath)))
                .ToArray();

            return DiscoverProjectManifests(snapshots);
        }

        internal static DiscoveryReport DiscoverProjectManifests(IReadOnlyList<ProjectManifestAssetSnapshot> snapshots)
        {
            var dependencies = new List<DiscoveredModelDependency>();
            var issues = new List<ManifestDiscoveryIssue>();

            foreach (var snapshot in (snapshots ?? Array.Empty<ProjectManifestAssetSnapshot>())
                         .OrderBy(item => item.ManifestPath, StringComparer.Ordinal))
            {
                if (snapshot.Asset == null)
                {
                    issues.Add(new ManifestDiscoveryIssue(
                        ProjectManifestOwnerId,
                        snapshot.ManifestPath,
                        "Failed to load project manifest asset."));
                    continue;
                }

                if (!snapshot.Asset.TryCreateManifest(out var manifest, out var errorMessage))
                {
                    issues.Add(new ManifestDiscoveryIssue(ProjectManifestOwnerId, snapshot.ManifestPath, errorMessage));
                    continue;
                }

                var manifestHash = ComputeManifestHash(snapshot.Asset.ToStableJson());
                foreach (var model in manifest.Models)
                {
                    dependencies.Add(new DiscoveredModelDependency(
                        ProjectManifestOwnerId,
                        ProjectManifestOwnerDisplayName,
                        ProjectManifestOwnerVersion,
                        snapshot.ResolvedPath,
                        snapshot.ManifestPath,
                        manifestHash,
                        model));
                }
            }

            return new DiscoveryReport(dependencies, issues);
        }

        private static string ComputeManifestHash(string contents)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(contents ?? string.Empty);
            var hashBytes = sha256.ComputeHash(bytes);
            return string.Concat(hashBytes.Select(hashByte => hashByte.ToString("x2")));
        }
    }

    /// <summary>
    /// Carries the editor data needed to test project-manifest discovery without touching the AssetDatabase.
    /// </summary>
    internal sealed class ProjectManifestAssetSnapshot
    {
        public ProjectManifestAssetSnapshot(
            string assetPath,
            string manifestPath,
            string resolvedPath,
            ProjectManifestAsset asset)
        {
            AssetPath = assetPath;
            ManifestPath = manifestPath;
            ResolvedPath = resolvedPath;
            Asset = asset;
        }

        public string AssetPath { get; }
        public string ManifestPath { get; }
        public string ResolvedPath { get; }
        public ProjectManifestAsset Asset { get; }
    }
}
