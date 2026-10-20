using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Resolves project-owned optional file selections for one manifest owner and model id.
    /// </summary>
    internal interface IProjectOptionalFileSelectionSource
    {
        string[] GetEnabledOptionalFiles(string ownerId, string modelId, IReadOnlyList<string> declaredOptionalFiles);
        void SetEnabledOptionalFiles(string ownerId, string modelId, IReadOnlyList<string> enabledOptionalFiles);
    }

    /// <summary>
    /// Loads and saves owner-scoped optional file selections from one project-owned asset under `Assets/`.
    /// </summary>
    internal sealed class ProjectOptionalFileSelectionSource : IProjectOptionalFileSelectionSource
    {
        public const string DefaultAssetPath = "Assets/Doji/HfHub/HF Hub Optional File Selections.asset";

        public string[] GetEnabledOptionalFiles(string ownerId, string modelId, IReadOnlyList<string> declaredOptionalFiles)
        {
            if (!TryFindSelection(ownerId, modelId, out var entry))
            {
                return Array.Empty<string>();
            }

            var declaredSet = new HashSet<string>(
                ModelDependencyDefinition.NormalizeSelectedFiles(declaredOptionalFiles),
                StringComparer.Ordinal);
            return (entry.EnabledOptionalFiles ?? Array.Empty<string>())
                .Where(path => declaredSet.Contains(path))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }

        public void SetEnabledOptionalFiles(string ownerId, string modelId, IReadOnlyList<string> enabledOptionalFiles)
        {
            if (string.IsNullOrWhiteSpace(ownerId))
            {
                throw new ArgumentException("Owner id is required.", nameof(ownerId));
            }

            if (string.IsNullOrWhiteSpace(modelId))
            {
                throw new ArgumentException("Model id is required.", nameof(modelId));
            }

            var normalizedFiles = ModelDependencyDefinition.NormalizeSelectedFiles(enabledOptionalFiles);
            var asset = GetOrCreateDefaultAsset();
            var entries = (asset.Selections ?? Array.Empty<ProjectOptionalFileSelectionEntry>()).ToList();
            var existingEntry = entries.FirstOrDefault(item =>
                string.Equals(item?.OwnerId, ownerId, StringComparison.Ordinal) &&
                string.Equals(item?.ModelId, modelId, StringComparison.Ordinal));

            if (normalizedFiles.Length == 0)
            {
                if (existingEntry != null)
                {
                    entries.Remove(existingEntry);
                    asset.Selections = SortEntries(entries);
                    EditorUtility.SetDirty(asset);
                    AssetDatabase.SaveAssets();
                }

                return;
            }

            if (existingEntry == null)
            {
                entries.Add(new ProjectOptionalFileSelectionEntry
                {
                    OwnerId = ownerId,
                    ModelId = modelId,
                    EnabledOptionalFiles = normalizedFiles
                });
            }
            else
            {
                existingEntry.EnabledOptionalFiles = normalizedFiles;
            }

            asset.Selections = SortEntries(entries);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
        }

        private bool TryFindSelection(string ownerId, string modelId, out ProjectOptionalFileSelectionEntry entry)
        {
            entry = null;
            var assetPath = GetExistingAssetPath();
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return false;
            }

            var asset = AssetDatabase.LoadAssetAtPath<ProjectOptionalFileSelectionsAsset>(assetPath);
            entry = (asset?.Selections ?? Array.Empty<ProjectOptionalFileSelectionEntry>())
                .FirstOrDefault(item =>
                    string.Equals(item?.OwnerId, ownerId, StringComparison.Ordinal) &&
                    string.Equals(item?.ModelId, modelId, StringComparison.Ordinal));
            return entry != null;
        }

        private static string GetExistingAssetPath()
        {
            if (AssetDatabase.LoadAssetAtPath<ProjectOptionalFileSelectionsAsset>(DefaultAssetPath) != null)
            {
                return DefaultAssetPath;
            }

            var assetGuid = AssetDatabase.FindAssets("t:ProjectOptionalFileSelectionsAsset")
                .OrderBy(guid => guid, StringComparer.Ordinal)
                .FirstOrDefault();
            return string.IsNullOrWhiteSpace(assetGuid)
                ? null
                : AssetDatabase.GUIDToAssetPath(assetGuid);
        }

        private static ProjectOptionalFileSelectionsAsset GetOrCreateDefaultAsset()
        {
            var existing = AssetDatabase.LoadAssetAtPath<ProjectOptionalFileSelectionsAsset>(DefaultAssetPath);
            if (existing != null)
            {
                return existing;
            }

            EnsureFolderExists(Path.GetDirectoryName(DefaultAssetPath)?.Replace('\\', '/'));
            var asset = UnityEngine.ScriptableObject.CreateInstance<ProjectOptionalFileSelectionsAsset>();
            AssetDatabase.CreateAsset(asset, DefaultAssetPath);
            AssetDatabase.SaveAssets();
            return asset;
        }

        private static ProjectOptionalFileSelectionEntry[] SortEntries(IEnumerable<ProjectOptionalFileSelectionEntry> entries)
        {
            return (entries ?? Array.Empty<ProjectOptionalFileSelectionEntry>())
                .OrderBy(item => item.OwnerId, StringComparer.Ordinal)
                .ThenBy(item => item.ModelId, StringComparer.Ordinal)
                .ToArray();
        }

        private static void EnsureFolderExists(string assetFolderPath)
        {
            if (string.IsNullOrWhiteSpace(assetFolderPath) || AssetDatabase.IsValidFolder(assetFolderPath))
            {
                return;
            }

            var parts = assetFolderPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            var currentFolder = parts[0];
            for (var index = 1; index < parts.Length; index++)
            {
                var nextFolder = $"{currentFolder}/{parts[index]}";
                if (!AssetDatabase.IsValidFolder(nextFolder))
                {
                    AssetDatabase.CreateFolder(currentFolder, parts[index]);
                }

                currentFolder = nextFolder;
            }
        }
    }

    /// <summary>
    /// Combines package and project manifest discovery into the editor-facing dependency scan.
    /// </summary>
    internal static class EditorManifestDiscovery
    {
        public static DiscoveryReport DiscoverInstalledManifests()
        {
            var packageReport = PackageDiscovery.DiscoverInstalledPackageManifests();
            var projectReport = ProjectManifestDiscovery.DiscoverProjectManifests();

            return ApplyProjectOptionalSelections(
                new DiscoveryReport(
                    packageReport.Dependencies
                        .Concat(projectReport.Dependencies)
                        .OrderBy(dependency => dependency.PackageId, StringComparer.Ordinal)
                        .ThenBy(dependency => dependency.ModelId, StringComparer.Ordinal)
                        .ToArray(),
                    packageReport.Issues
                        .Concat(projectReport.Issues)
                        .OrderBy(issue => issue.PackageId, StringComparer.Ordinal)
                        .ThenBy(issue => issue.ManifestPath, StringComparer.Ordinal)
                        .ToArray()),
                new ProjectOptionalFileSelectionSource());
        }

        internal static DiscoveryReport ApplyProjectOptionalSelections(
            DiscoveryReport report,
            IProjectOptionalFileSelectionSource selectionSource)
        {
            if (report == null)
            {
                return new DiscoveryReport(Array.Empty<DiscoveredModelDependency>(), Array.Empty<ManifestDiscoveryIssue>());
            }

            if (selectionSource == null)
            {
                return report;
            }

            var dependencies = report.Dependencies
                .Select(dependency =>
                {
                    if (dependency == null || !dependency.HasOptionalFiles)
                    {
                        return dependency;
                    }

                    var model = new ModelDependencyDefinition(
                        dependency.ModelId,
                        dependency.Revision,
                        dependency.FileEntries.ToArray());
                    return new DiscoveredModelDependency(
                        dependency.PackageId,
                        dependency.PackageDisplayName,
                        dependency.PackageVersion,
                        dependency.PackageResolvedPath,
                        dependency.ManifestPath,
                        dependency.ManifestHash,
                        model,
                        selectionSource.GetEnabledOptionalFiles(
                            dependency.PackageId,
                            dependency.ModelId,
                            dependency.GetOptionalRepoPaths()));
                })
                .OrderBy(dependency => dependency.PackageId, StringComparer.Ordinal)
                .ThenBy(dependency => dependency.ModelId, StringComparer.Ordinal)
                .ToArray();

            return new DiscoveryReport(dependencies, report.Issues.ToArray());
        }
    }
}
