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

        [Serializable]
        private sealed class ManifestDocument
        {
            public int version = 1;
            public ManifestModelDocument[] models = Array.Empty<ManifestModelDocument>();
        }

        [Serializable]
        private sealed class ManifestModelDocument
        {
            public string id;
            public string revision;
        }

        public static string GetManifestPath(string projectRoot = null)
        {
            projectRoot ??= Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(projectRoot, "ProjectSettings", Hub.ManifestFileName);
        }

        public static bool TryAddModel(string owner, string modelName, out string errorMessage)
        {
            errorMessage = null;
            var ownerTrimmed = owner?.Trim();
            var modelNameTrimmed = modelName?.Trim();
            if (string.IsNullOrWhiteSpace(ownerTrimmed) || string.IsNullOrWhiteSpace(modelNameTrimmed))
            {
                errorMessage = "Owner and model are required.";
                return false;
            }

            return TryAddModelId($"{ownerTrimmed}/{modelNameTrimmed}", DefaultRevision, out errorMessage);
        }

        internal static bool TryAddModelId(string modelId, string revision, out string errorMessage, string projectRoot = null)
        {
            errorMessage = null;
            if (!TryLoadDrafts(projectRoot, out var drafts, out errorMessage))
            {
                return false;
            }

            var normalizedModelId = modelId?.Trim();
            if (drafts.Any(draft => string.Equals(draft.Id, normalizedModelId, StringComparison.Ordinal)))
            {
                errorMessage = $"Project manifest already declares `{normalizedModelId}`.";
                return false;
            }

            drafts.Add(new ManifestModelDraft(normalizedModelId, string.IsNullOrWhiteSpace(revision) ? DefaultRevision : revision.Trim()));
            if (!ManifestFactory.TryCreateManifest(1, drafts, out var manifest, out errorMessage))
            {
                return false;
            }

            var manifestPath = GetManifestPath(projectRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath) ?? string.Empty);
            File.WriteAllText(manifestPath, BuildManifestJson(manifest));
            AssetDatabase.Refresh();
            return true;
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

        private static bool TryLoadDrafts(string projectRoot, out List<ManifestModelDraft> drafts, out string errorMessage)
        {
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

            drafts.AddRange(manifest.Models.Select(model => new ManifestModelDraft(model.Id, model.Revision)));
            return true;
        }

        private static string BuildManifestJson(PackageManifest manifest)
        {
            var document = new ManifestDocument
            {
                version = manifest?.Version ?? 1,
                models = (manifest?.Models ?? Array.Empty<ModelDependencyDefinition>())
                    .Select(model => new ManifestModelDocument
                    {
                        id = model.Id,
                        revision = string.Equals(model.Revision, DefaultRevision, StringComparison.Ordinal)
                            ? null
                            : model.Revision
                    })
                    .ToArray()
            };
            return JsonUtility.ToJson(document, true);
        }
    }
}
