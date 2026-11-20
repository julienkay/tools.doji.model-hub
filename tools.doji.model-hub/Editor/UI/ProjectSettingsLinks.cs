using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
        /// Describes how directly one manifest owner can be edited from current Unity project.
        /// </summary>
        internal enum ManifestOwnerResolutionKind
        {
            ProjectManifest,
            EmbeddedPackage,
            LocalPackage,
            InstalledPackage
        }

        /// <summary>
        /// Carries owner editability and resolution guidance for one conflicting manifest owner.
        /// </summary>
        internal readonly struct ManifestOwnerResolutionInfo
        {
            /// <summary>
            /// Stores owner source kind and resolution guidance used by conflict UX.
            /// </summary>
            public ManifestOwnerResolutionInfo(
                ManifestOwnerResolutionKind kind,
                string ownerTypeLabel,
                bool isDirectlyEditable,
                bool suggestsPackageManager,
                string resolutionHint)
            {
                Kind = kind;
                OwnerTypeLabel = ownerTypeLabel;
                IsDirectlyEditable = isDirectlyEditable;
                SuggestsPackageManager = suggestsPackageManager;
                ResolutionHint = resolutionHint;
            }

            public ManifestOwnerResolutionKind Kind { get; }
            public string OwnerTypeLabel { get; }
            public bool IsDirectlyEditable { get; }
            public bool SuggestsPackageManager { get; }
            public string ResolutionHint { get; }
        }

        /// <summary>
        /// Carries one manifest locate target shown in editor menus.
        /// </summary>
        internal readonly struct ManifestLocateTarget
        {
            /// <summary>
            /// Stores menu label and source path for one manifest locate action.
            /// </summary>
            public ManifestLocateTarget(string label, string manifestPath, string ownerId)
            {
                Label = label;
                ManifestPath = manifestPath;
                OwnerId = ownerId;
            }

            public string Label { get; }

            public string ManifestPath { get; }

            public string OwnerId { get; }
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
        /// Pings one manifest asset when Unity can resolve it, otherwise reveals backing file.
        /// </summary>
        public static void LocateManifest(string manifestPath)
        {
            if (string.IsNullOrWhiteSpace(manifestPath))
            {
                return;
            }

            var asset = LoadManifestAsset(manifestPath);
            if (asset != null)
            {
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
                return;
            }

            RevealPath(manifestPath);
        }

        /// <summary>
        /// Opens one manifest owner target, including Project Settings when the owner lives there.
        /// </summary>
        internal static void LocateManifest(ManifestLocateTarget target)
        {
            if (IsProjectSettingsManifestOwnerId(target.OwnerId))
            {
                SettingsService.OpenProjectSettings(EditorOperations.ProjectSettingsPath);
                return;
            }

            LocateManifest(target.ManifestPath);
        }

        /// <summary>
        /// Resolves one Unity object suitable for Console context when exactly one unresolved manifest owns a reminder.
        /// </summary>
        internal static UnityEngine.Object TryGetSingleManifestContextObject(IEnumerable<DependencyStatus> dependencyStatuses)
        {
            var unresolvedDependencies = (dependencyStatuses ?? Array.Empty<DependencyStatus>())
                .Where(status => status?.NeedsAttention == true)
                .Select(status => status.Dependency)
                .Where(dependency => dependency != null && !string.IsNullOrWhiteSpace(dependency.ManifestPath))
                .GroupBy(dependency => dependency.ManifestPath, StringComparer.Ordinal)
                .Select(group => group.First())
                .Take(2)
                .ToArray();

            return unresolvedDependencies.Length != 1 ? null : LoadManifestAsset(unresolvedDependencies[0]);
        }

        /// <summary>
        /// Opens Unity Package Manager so package-owned conflict resolution options are nearby.
        /// </summary>
        public static void OpenPackageManager()
        {
            if (!EditorApplication.ExecuteMenuItem("Window/Package Manager"))
            {
                UnityEngine.Debug.LogWarning("AI Model Hub could not open Unity Package Manager from menu.");
            }
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
                    BuildManifestLocateTargetLabel(dependent.Dependency),
                    dependent.Dependency.ManifestPath,
                    dependent.Dependency.PackageId))
                .GroupBy(
                    target => BuildManifestLocateTargetKey(target),
                    StringComparer.Ordinal)
                // Shared manifest paths can be contributed by multiple dependents; pick one stable label.
                .Select(group => group
                    .OrderBy(target => target.Label, StringComparer.Ordinal)
                    .First())
                .OrderBy(target => target.Label, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Classifies one manifest owner by how directly it can be edited from current project.
        /// </summary>
        internal static ManifestOwnerResolutionInfo GetManifestOwnerResolutionInfo(DiscoveredModelDependency dependency)
        {
            if (dependency == null)
            {
                throw new ArgumentNullException(nameof(dependency));
            }

            var normalizedManifestPath = NormalizePath(dependency.ManifestPath);
            var assetPath = TryGetProjectAssetPath(normalizedManifestPath);
            if (ProjectManifestDiscovery.IsProjectSettingsManifestOwnerId(dependency.PackageId))
            {
                return new ManifestOwnerResolutionInfo(
                    ManifestOwnerResolutionKind.ProjectManifest,
                    "Project Settings manifest",
                    true,
                    false,
                    "Change the revision in Project Settings > AI Model Hub.");
            }

            if (ProjectManifestDiscovery.IsProjectManifestAssetOwnerId(dependency.PackageId) ||
                IsProjectAssetPath(assetPath))
            {
                return new ManifestOwnerResolutionInfo(
                    ManifestOwnerResolutionKind.ProjectManifest,
                    "Project manifest asset",
                    true,
                    false,
                    "Change the revision in this manifest asset.");
            }

            if (IsEmbeddedPackageAssetPath(assetPath))
            {
                return new ManifestOwnerResolutionInfo(
                    ManifestOwnerResolutionKind.EmbeddedPackage,
                    "Embedded package",
                    true,
                    false,
                    "Change the revision in this embedded package's manifest.");
            }

            if (IsInstalledPackageCachePath(normalizedManifestPath) ||
                IsInstalledPackageCachePath(NormalizePath(dependency.PackageResolvedPath)))
            {
                return new ManifestOwnerResolutionInfo(
                    ManifestOwnerResolutionKind.InstalledPackage,
                    "Installed package",
                    false,
                    true,
                    "Update the package in Package Manager, or embed it to edit its manifest.");
            }

            return new ManifestOwnerResolutionInfo(
                ManifestOwnerResolutionKind.LocalPackage,
                "Local package",
                true,
                false,
                "Change the revision in this local package's manifest.");
        }

        /// <summary>
        /// Returns true when conflict owners include installed packages that are not directly editable in project.
        /// </summary>
        internal static bool HasPackageManagerOwnedConflict(ModelHubItem item)
        {
            return (item?.Dependents ?? Array.Empty<DependencyStatus>())
                .Any(dependent => GetManifestOwnerResolutionInfo(dependent.Dependency).SuggestsPackageManager);
        }

        /// <summary>
        /// Builds short conflict guidance for Model Hub summary view.
        /// </summary>
        internal static string BuildRevisionConflictHelpMessage(ModelHubItem item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            var resolutionInfos = item.Dependents
                .Select(dependent => GetManifestOwnerResolutionInfo(dependent.Dependency))
                .ToArray();
            var hasPackageManagerOwners = resolutionInfos.Any(info => info.SuggestsPackageManager);
            var hasEditableOwners = resolutionInfos.Any(info => info.IsDirectlyEditable);

            if (hasPackageManagerOwners && hasEditableOwners)
            {
                return "Change the revision in the manifests you can edit so they match the installed packages, or update those packages in Package Manager.";
            }

            if (hasPackageManagerOwners)
            {
                return "The conflicting manifests belong to installed packages. Update one of the packages in Package Manager, or embed it to edit its manifest.";
            }

            return "Open each manifest below and set the same revision in all of them.";
        }

        /// <summary>
        /// Builds clipboard-ready conflict summary with owners, revisions, and suggested resolution path.
        /// </summary>
        internal static string BuildRevisionConflictSummary(ModelHubItem item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            var revisions = item.Dependents
                .Select(dependent => dependent.Dependency.Revision)
                .Where(revision => !string.IsNullOrWhiteSpace(revision))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(revision => revision, StringComparer.Ordinal)
                .ToArray();
            var builder = new StringBuilder();
            builder.AppendLine("AI Model Hub revision conflict");
            builder.AppendLine($"Model: {item.Repository}");
            builder.AppendLine($"Requested revisions: {string.Join(", ", revisions)}");
            builder.AppendLine("Owners:");

            foreach (var dependent in item.Dependents
                         .OrderBy(entry => entry.Dependency.PackageDisplayName, StringComparer.Ordinal)
                         .ThenBy(entry => entry.Dependency.ModelId, StringComparer.Ordinal))
            {
                var dependency = dependent.Dependency;
                var resolutionInfo = GetManifestOwnerResolutionInfo(dependency);
                var ownerLabel = ProjectManifestDiscovery.IsProjectManifestOwnerId(dependency.PackageId)
                    ? ProjectManifestDiscovery.GetOwnerDisplayName(dependency.PackageId, dependency.PackageDisplayName)
                    : $"{dependency.PackageId} {dependency.PackageVersion}";
                builder.AppendLine(
                    $"- {resolutionInfo.OwnerTypeLabel}: {dependency.PackageDisplayName} ({ownerLabel})");
                builder.AppendLine($"  model: {dependency.ModelId}");
                builder.AppendLine($"  requested revision: {dependency.Revision}");
                builder.AppendLine($"  manifest: {GetDisplayPath(dependency.ManifestPath)}");
                builder.AppendLine($"  resolve: {resolutionInfo.ResolutionHint}");
            }

            builder.Append("AI Model Hub does not set up or remove this model until every manifest requests the same revision.");
            return builder.ToString();
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

        private static bool IsProjectAssetPath(string assetPath)
        {
            return !string.IsNullOrWhiteSpace(assetPath) &&
                assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase);
        }

        private static UnityEngine.Object LoadManifestAsset(string manifestPath)
        {
            var assetPath = TryGetProjectAssetPath(manifestPath);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return null;
            }

            return AssetDatabase.LoadMainAssetAtPath(assetPath);
        }

        private static UnityEngine.Object LoadManifestAsset(DiscoveredModelDependency dependency)
        {
            if (dependency == null)
            {
                return null;
            }

            var asset = LoadManifestAsset(dependency.ManifestPath);
            if (asset != null)
            {
                return asset;
            }

            if (string.IsNullOrWhiteSpace(dependency.PackageId) ||
                ProjectManifestDiscovery.IsProjectManifestOwnerId(dependency.PackageId))
            {
                return null;
            }

            // Local file: packages can resolve outside project root, but Unity still often exposes them
            // through virtual Packages/<package-id>/... asset paths that AssetDatabase can ping.
            return AssetDatabase.LoadMainAssetAtPath($"Packages/{dependency.PackageId}/{Hub.ManifestFileName}");
        }

        private static bool IsEmbeddedPackageAssetPath(string assetPath)
        {
            return !string.IsNullOrWhiteSpace(assetPath) &&
                assetPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsInstalledPackageCachePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var projectRoot = NormalizePath(Path.GetFullPath(Path.Combine(Application.dataPath, ".."))).TrimEnd('/');
            return path.StartsWith($"{projectRoot}/Library/PackageCache/", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetDisplayPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var normalizedPath = NormalizePath(path);
            var assetPath = TryGetProjectAssetPath(normalizedPath);
            return string.IsNullOrWhiteSpace(assetPath)
                ? normalizedPath
                : assetPath;
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? path
                : path.Replace('\\', '/');
        }

        private static bool IsProjectSettingsManifestOwnerId(string ownerId)
        {
            return ProjectManifestDiscovery.IsProjectSettingsManifestOwnerId(ownerId);
        }

        private static string BuildManifestLocateTargetKey(ManifestLocateTarget target)
        {
            if (IsProjectSettingsManifestOwnerId(target.OwnerId))
            {
                return target.OwnerId ?? string.Empty;
            }

            return NormalizePath(target.ManifestPath) ?? target.OwnerId ?? string.Empty;
        }

        private static string BuildManifestLocateTargetLabel(DiscoveredModelDependency dependency)
        {
            if (dependency == null)
            {
                return string.Empty;
            }

            return IsProjectSettingsManifestOwnerId(dependency.PackageId)
                ? ProjectManifestDiscovery.GetOwnerDisplayName(dependency.PackageId, dependency.PackageDisplayName)
                : $"{dependency.PackageDisplayName} / {dependency.ModelId}";
        }
    }
}
