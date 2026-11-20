using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Owns local-package manifest authoring for package roots that live inside current Unity project.
    /// </summary>
    internal static class LocalPackageManifestStore
    {
        private const string DefaultRevision = "main";

        internal readonly struct LocalPackageOption
        {
            public LocalPackageOption(string packageId, string displayName, string resolvedPath)
            {
                PackageId = packageId;
                DisplayName = displayName;
                ResolvedPath = resolvedPath;
            }

            public string PackageId { get; }
            public string DisplayName { get; }
            public string ResolvedPath { get; }
            public string Label => string.IsNullOrWhiteSpace(DisplayName) || string.Equals(DisplayName, PackageId, StringComparison.Ordinal)
                ? PackageId
                : $"{DisplayName} ({PackageId})";
        }

        public static IReadOnlyList<LocalPackageOption> GetProjectLocalPackages()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var packages = UnityEditor.PackageManager.PackageInfo
                .GetAllRegisteredPackages()
                .Select(package => new InstalledPackageSnapshot(
                    package.name,
                    package.displayName,
                    package.version,
                    package.resolvedPath))
                .ToArray();
            return GetProjectLocalPackages(packages, projectRoot);
        }

        internal static IReadOnlyList<LocalPackageOption> GetProjectLocalPackages(
            IReadOnlyList<InstalledPackageSnapshot> packages,
            string projectRoot)
        {
            var normalizedProjectRoot = NormalizePath(projectRoot).TrimEnd('/');
            return (packages ?? Array.Empty<InstalledPackageSnapshot>())
                .Where(package => !string.IsNullOrWhiteSpace(package?.Name) && !string.IsNullOrWhiteSpace(package.ResolvedPath))
                .Where(package =>
                {
                    var normalizedResolvedPath = NormalizePath(package.ResolvedPath).TrimEnd('/');
                    return normalizedResolvedPath.StartsWith($"{normalizedProjectRoot}/", StringComparison.OrdinalIgnoreCase) &&
                        !normalizedResolvedPath.StartsWith($"{normalizedProjectRoot}/Library/PackageCache/", StringComparison.OrdinalIgnoreCase);
                })
                .OrderBy(package => package.DisplayName ?? package.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(package => package.Name, StringComparer.Ordinal)
                .Select(package => new LocalPackageOption(
                    package.Name,
                    string.IsNullOrWhiteSpace(package.DisplayName) ? package.Name : package.DisplayName,
                    package.ResolvedPath))
                .ToArray();
        }

        public static bool TryAddModel(LocalPackageOption package, string owner, string modelName, string[] files, out string errorMessage)
        {
            errorMessage = null;
            if (string.IsNullOrWhiteSpace(package.PackageId) || string.IsNullOrWhiteSpace(package.ResolvedPath))
            {
                errorMessage = "Select a local package.";
                return false;
            }

            var ownerTrimmed = owner?.Trim();
            var modelNameTrimmed = modelName?.Trim();
            if (string.IsNullOrWhiteSpace(ownerTrimmed) || string.IsNullOrWhiteSpace(modelNameTrimmed))
            {
                errorMessage = "Owner and model are required.";
                return false;
            }

            return TryAddModelId(package, $"{ownerTrimmed}/{modelNameTrimmed}", DefaultRevision, files, out errorMessage);
        }

        internal static bool TryAddModelId(LocalPackageOption package, string modelId, string revision, string[] files, out string errorMessage)
        {
            errorMessage = null;
            if (!TryLoadManifestDrafts(package, out var version, out var drafts, out errorMessage))
            {
                return false;
            }

            var normalizedModelId = modelId?.Trim();
            if (drafts.Any(draft => string.Equals(draft.Id, normalizedModelId, StringComparison.Ordinal)))
            {
                errorMessage = $"Package manifest already declares `{normalizedModelId}`.";
                return false;
            }

            drafts.Add(new ManifestModelDraft(
                normalizedModelId,
                string.IsNullOrWhiteSpace(revision) ? DefaultRevision : revision.Trim(),
                ToRequiredFileEntries(files)));
            return TryWriteManifest(package, version, drafts, out errorMessage);
        }

        internal static string GetManifestPath(LocalPackageOption package)
        {
            return Path.Combine(package.ResolvedPath, Hub.ManifestFileName);
        }

        private static bool TryLoadManifestDrafts(LocalPackageOption package, out int version, out List<ManifestModelDraft> drafts, out string errorMessage)
        {
            version = 1;
            drafts = new List<ManifestModelDraft>();
            errorMessage = null;
            var manifestPath = GetManifestPath(package);
            if (!File.Exists(manifestPath))
            {
                return true;
            }

            string contents;
            try
            {
                contents = File.ReadAllText(manifestPath);
            }
            catch (Exception exception)
            {
                errorMessage = $"Failed to read package manifest: {exception.Message}";
                return false;
            }

            if (!ManifestParser.TryParse(contents, out var manifest, out errorMessage))
            {
                return false;
            }

            version = manifest.Version;
            drafts.AddRange(manifest.Models.Select(model => new ManifestModelDraft(model.Id, model.Revision, model.FileEntries, model.Optional)));
            return true;
        }

        private static bool TryWriteManifest(LocalPackageOption package, int version, IReadOnlyList<ManifestModelDraft> drafts, out string errorMessage)
        {
            errorMessage = null;
            if (!ManifestFactory.TryCreateManifest(version, drafts ?? Array.Empty<ManifestModelDraft>(), out _, out errorMessage))
            {
                return false;
            }

            var manifestPath = GetManifestPath(package);
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath) ?? string.Empty);
            File.WriteAllText(manifestPath, ManifestWriter.ToJson(version, drafts));
            DiscoveredDependencyCache.Invalidate();
            AssetDatabase.Refresh();
            return true;
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Replace('\\', '/');
        }

        private static ModelDependencyFileDefinition[] ToRequiredFileEntries(IReadOnlyList<string> files)
        {
            return ModelDependencyDefinition.NormalizeSelectedFiles(files)
                .Select(path => new ModelDependencyFileDefinition(path))
                .ToArray();
        }
    }
}
