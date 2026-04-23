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
        internal const string LegacyProjectManifestOwnerId = "project";
        private const string ProjectManifestOwnerIdPrefix = "project-manifest-";
        private const string ProjectManifestOwnerDisplayName = "Project";
        private const string ProjectManifestOwnerVersion = "project";

        public static DiscoveryReport DiscoverProjectManifests()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var snapshots = AssetDatabase
                .FindAssets("t:ProjectManifestAsset")
                .Select(guid => new
                {
                    Guid = guid,
                    AssetPath = AssetDatabase.GUIDToAssetPath(guid)
                })
                .Where(snapshot => !string.IsNullOrWhiteSpace(snapshot.AssetPath))
                .Select(snapshot => new ProjectManifestAssetSnapshot(
                    snapshot.AssetPath,
                    Path.Combine(projectRoot, snapshot.AssetPath.Replace('/', Path.DirectorySeparatorChar)),
                    projectRoot,
                    AssetDatabase.LoadAssetAtPath<ProjectManifestAsset>(snapshot.AssetPath),
                    snapshot.Guid))
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
                var ownerId = GetOwnerId(snapshot.AssetGuid, snapshot.AssetPath, snapshot.ManifestPath);
                if (snapshot.Asset == null)
                {
                    issues.Add(new ManifestDiscoveryIssue(
                        ownerId,
                        snapshot.ManifestPath,
                        "Failed to load project manifest asset."));
                    continue;
                }

                if (!snapshot.Asset.TryCreateManifest(out var manifest, out var errorMessage))
                {
                    issues.Add(new ManifestDiscoveryIssue(ownerId, snapshot.ManifestPath, errorMessage));
                    continue;
                }

                var manifestHash = ComputeManifestHash(snapshot.Asset.ToStableJson());
                foreach (var model in manifest.Models)
                {
                    dependencies.Add(new DiscoveredModelDependency(
                        ownerId,
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

        internal static bool IsProjectManifestOwnerId(string ownerId)
        {
            return string.Equals(ownerId, LegacyProjectManifestOwnerId, StringComparison.Ordinal) ||
                (!string.IsNullOrWhiteSpace(ownerId) &&
                 ownerId.StartsWith(ProjectManifestOwnerIdPrefix, StringComparison.Ordinal));
        }

        internal static string GetOwnerDisplayName(string ownerId, string fallbackDisplayName = null)
        {
            return IsProjectManifestOwnerId(ownerId)
                ? ProjectManifestOwnerDisplayName
                : fallbackDisplayName ?? ownerId ?? string.Empty;
        }

        internal static string GetOwnerId(string assetGuid, string assetPath, string manifestPath)
        {
            if (!string.IsNullOrWhiteSpace(assetGuid))
            {
                return $"{ProjectManifestOwnerIdPrefix}{assetGuid.Trim().ToLowerInvariant()}";
            }

            var pathKey = NormalizePathForOwnerId(string.IsNullOrWhiteSpace(assetPath) ? manifestPath : assetPath);
            return $"{ProjectManifestOwnerIdPrefix}{ComputeManifestHash(pathKey)}";
        }

        private static string ComputeManifestHash(string contents)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(contents ?? string.Empty);
            var hashBytes = sha256.ComputeHash(bytes);
            return string.Concat(hashBytes.Select(hashByte => hashByte.ToString("x2")));
        }

        private static string NormalizePathForOwnerId(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/').Trim();
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
            ProjectManifestAsset asset,
            string assetGuid = null)
        {
            AssetPath = assetPath;
            ManifestPath = manifestPath;
            ResolvedPath = resolvedPath;
            Asset = asset;
            AssetGuid = assetGuid;
        }

        public string AssetPath { get; }
        public string ManifestPath { get; }
        public string ResolvedPath { get; }
        public ProjectManifestAsset Asset { get; }
        public string AssetGuid { get; }
    }
}
