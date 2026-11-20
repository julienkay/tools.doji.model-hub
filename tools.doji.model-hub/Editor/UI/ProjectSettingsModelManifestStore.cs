using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Owns the project-scoped JSON model manifest stored under ProjectSettings.
    /// </summary>
    internal static class ProjectSettingsModelManifestStore
    {
        private const string DefaultRevision = "main";
        private const string ProjectSettingsPackagesFolderName = "Packages";
        private const string PackageId = "tools.doji.model-hub";


        public static string GetManifestPath(string projectRoot = null)
        {
            projectRoot ??= Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(
                projectRoot,
                "ProjectSettings",
                ProjectSettingsPackagesFolderName,
                PackageId,
                Hub.ManifestFileName);
        }

        public static bool TryAddModel(string owner, string modelName, string[] files, out string errorMessage)
        {
            errorMessage = null;
            var ownerTrimmed = owner?.Trim();
            var modelNameTrimmed = modelName?.Trim();
            if (string.IsNullOrWhiteSpace(ownerTrimmed) || string.IsNullOrWhiteSpace(modelNameTrimmed))
            {
                errorMessage = "Owner and model are required.";
                return false;
            }

            return TryAddModelId($"{ownerTrimmed}/{modelNameTrimmed}", DefaultRevision, files, out errorMessage);
        }

        internal static bool TryAddModelId(string modelId, string revision, string[] files, out string errorMessage, string projectRoot = null)
        {
            errorMessage = null;
            if (!TryLoadManifestDrafts(projectRoot, out var version, out var drafts, out errorMessage))
            {
                return false;
            }

            var normalizedModelId = modelId?.Trim();
            if (drafts.Any(draft => string.Equals(draft.Id, normalizedModelId, StringComparison.Ordinal)))
            {
                errorMessage = $"Project manifest already declares `{normalizedModelId}`.";
                return false;
            }

            drafts.Add(new ManifestModelDraft(
                normalizedModelId,
                string.IsNullOrWhiteSpace(revision) ? DefaultRevision : revision.Trim(),
                ToRequiredFileEntries(files)));
            return TryWriteManifest(version, drafts, out errorMessage, projectRoot);
        }

        internal static bool TryReadManifestContents(string projectRoot, out string manifestPath, out string contents, out string errorMessage)
        {
            manifestPath = GetManifestPath(projectRoot);
            contents = null;
            errorMessage = null;
            if (!File.Exists(manifestPath))
            {
                return false;
            }

            try
            {
                contents = File.ReadAllText(manifestPath);
                return true;
            }
            catch (Exception exception)
            {
                errorMessage = $"Failed to read project manifest: {exception.Message}";
                return true;
            }
        }

        internal static bool TryLoadManifestDrafts(string projectRoot, out int version, out List<ManifestModelDraft> drafts, out string errorMessage)
        {
            version = 1;
            drafts = new List<ManifestModelDraft>();
            errorMessage = null;

            if (!TryReadManifestContents(projectRoot, out _, out var contents, out errorMessage))
            {
                return true;
            }

            if (errorMessage != null)
            {
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

        internal static bool TryLoadManifestEntries(
            string projectRoot,
            out int version,
            out ManifestModelEntry[] models,
            out string errorMessage)
        {
            version = 1;
            models = Array.Empty<ManifestModelEntry>();
            errorMessage = null;

            if (!TryReadManifestContents(projectRoot, out _, out var contents, out errorMessage))
            {
                return true;
            }

            if (errorMessage != null || !ManifestParser.TryParse(contents, out var manifest, out errorMessage))
            {
                return false;
            }

            version = manifest.Version;
            models = ModelManifestInspectorUtility.ToEntries(manifest);
            return true;
        }

        internal static bool TryWriteManifest(int version, IReadOnlyList<ManifestModelDraft> drafts, out string errorMessage, string projectRoot = null)
        {
            errorMessage = null;
            if (!ManifestFactory.TryCreateManifest(version, drafts ?? Array.Empty<ManifestModelDraft>(), out var manifest, out errorMessage))
            {
                return false;
            }

            var manifestPath = GetManifestPath(projectRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath) ?? string.Empty);
            File.WriteAllText(manifestPath, ManifestWriter.ToJson(manifest.Version, drafts));
            DiscoveredDependencyCache.Invalidate();
            AssetDatabase.Refresh();
            ProjectSettingsProvider.Refresh();
            return true;
        }

        private static ModelDependencyFileDefinition[] ToRequiredFileEntries(IReadOnlyList<string> files)
        {
            return ModelDependencyDefinition.NormalizeSelectedFiles(files)
                .Select(path => new ModelDependencyFileDefinition(path))
                .ToArray();
        }
    }
}
