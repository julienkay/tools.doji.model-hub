using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Resolves effective file-level conversion recipes: a project override, else the manifest default, else the
    /// built-in default.
    /// </summary>
    internal interface IProjectModelRecipeSource
    {
        EffectiveOnnxConversionRecipe GetEffectiveRecipe(string repository, string repoPath);
        string ComputeRecipeSetHash(string repository, System.Collections.Generic.IEnumerable<string> repoPaths);
        void SetProjectOverride(string repository, string repoPath, OnnxConversionRecipe recipe);
        string EnsureProjectOverride(string repository, string repoPath);
        bool RemoveProjectOverride(string repository, string repoPath);
    }

    /// <summary>
    /// Carries effective recipe together with its source and optional project asset path.
    /// </summary>
    internal sealed class EffectiveOnnxConversionRecipe
    {
        public EffectiveOnnxConversionRecipe(
            OnnxConversionRecipe recipe,
            string sourceLabel,
            string overrideAssetPath,
            bool hasProjectOverride)
        {
            Recipe = recipe ?? new OnnxConversionRecipe();
            SourceLabel = string.IsNullOrWhiteSpace(sourceLabel) ? "Built-in Default" : sourceLabel;
            OverrideAssetPath = overrideAssetPath;
            HasProjectOverride = hasProjectOverride;
        }

        public OnnxConversionRecipe Recipe { get; }
        public string SourceLabel { get; }
        public string OverrideAssetPath { get; }
        public bool HasProjectOverride { get; }
    }

    /// <summary>
    /// Loads project-owned conversion overrides from one versioned asset under `Assets/`, and manifest defaults from
    /// the <c>conversion</c> settings of discovered manifests.
    /// </summary>
    internal sealed class ProjectModelRecipeSource : IProjectModelRecipeSource
    {
        public const string DefaultAssetPath = "Assets/Doji/ModelHub/AI Model Hub Conversion Overrides.asset";

        private readonly IReadOnlyList<DiscoveredModelDependency> _dependencies;

        /// <param name="dependencies">Manifest dependencies to read defaults from. Null reads the installed manifests.</param>
        public ProjectModelRecipeSource(IReadOnlyList<DiscoveredModelDependency> dependencies = null)
        {
            _dependencies = dependencies;
        }

        public EffectiveOnnxConversionRecipe GetEffectiveRecipe(string repository, string repoPath)
        {
            if (TryFindOverride(repository, repoPath, out var entry, out var assetPath))
            {
                return new EffectiveOnnxConversionRecipe(
                    entry.Recipe ?? new OnnxConversionRecipe(),
                    "Project Override",
                    assetPath,
                    true);
            }

            return GetManifestDefault(repository, repoPath);
        }

        /// <summary>
        /// Returns the recipe that applies without a project override. Manifests that set no <c>conversion</c> for the
        /// file have no say. When manifests set different settings, none of them wins and the built-in default applies,
        /// until a project override settles it.
        /// </summary>
        internal EffectiveOnnxConversionRecipe GetManifestDefault(string repository, string repoPath)
        {
            var normalizedPath = ModelDependencyDefinition.NormalizeRepoPath(repoPath);
            var defaults = (_dependencies ?? DiscoveredDependencyCache.Dependencies)
                .Where(dependency => string.Equals(dependency?.Repository, repository, StringComparison.Ordinal))
                .SelectMany(dependency => dependency.FileEntries
                    .Where(file => file.Conversion != null && string.Equals(file.Path, normalizedPath, StringComparison.Ordinal))
                    .Select(file => (
                        Owner: ProjectManifestDiscovery.GetOwnerDisplayName(dependency.PackageId, dependency.PackageDisplayName),
                        Recipe: file.Conversion)))
                .GroupBy(item => OnnxImportMetadataUtility.ComputeRecipeHash(item.Recipe), StringComparer.Ordinal)
                .ToArray();
            if (defaults.Length == 1)
            {
                return new EffectiveOnnxConversionRecipe(
                    defaults[0].First().Recipe.Clone(),
                    $"Manifest: {JoinOwners(defaults[0].Select(item => item.Owner))}",
                    null,
                    false);
            }

            return new EffectiveOnnxConversionRecipe(
                OnnxImportMetadataUtility.CreateDefaultRecipe(),
                defaults.Length == 0
                    ? "Built-in Default"
                    : $"Built-in Default (manifests disagree: {JoinOwners(defaults.SelectMany(group => group.Select(item => item.Owner)))})",
                null,
                false);
        }

        private static string JoinOwners(IEnumerable<string> owners)
        {
            return string.Join(", ", owners.Distinct(StringComparer.Ordinal).OrderBy(owner => owner, StringComparer.Ordinal));
        }

        public string ComputeRecipeSetHash(string repository, System.Collections.Generic.IEnumerable<string> repoPaths)
        {
            var builder = new System.Text.StringBuilder();
            foreach (var repoPath in (repoPaths ?? Array.Empty<string>())
                         .Where(path => !string.IsNullOrWhiteSpace(path))
                         .Distinct(StringComparer.Ordinal)
                         .OrderBy(path => path, StringComparer.Ordinal))
            {
                var effectiveRecipe = GetEffectiveRecipe(repository, repoPath);
                builder.Append(repoPath);
                builder.Append(':');
                builder.Append(OnnxImportMetadataUtility.ComputeRecipeHash(effectiveRecipe.Recipe));
                builder.Append('\n');
            }

            return OnnxImportMetadataUtility.ComputeTextHash(builder.ToString());
        }

        public string EnsureProjectOverride(string repository, string repoPath)
        {
            if (string.IsNullOrWhiteSpace(repository))
            {
                throw new ArgumentException("Repository is required.", nameof(repository));
            }

            if (string.IsNullOrWhiteSpace(repoPath))
            {
                throw new ArgumentException("Repository path is required.", nameof(repoPath));
            }

            var asset = GetOrCreateDefaultAsset();
            var entries = (asset.Overrides ?? Array.Empty<ProjectModelConversionOverrideEntry>()).ToList();
            var existingEntry = entries.FirstOrDefault(item =>
                string.Equals(item?.Repository, repository, StringComparison.Ordinal) &&
                string.Equals(item?.RepoPath, repoPath, StringComparison.Ordinal));
            if (existingEntry == null)
            {
                entries.Add(new ProjectModelConversionOverrideEntry
                {
                    Repository = repository,
                    RepoPath = repoPath,
                    Recipe = CloneRecipe(GetManifestDefault(repository, repoPath).Recipe)
                });
                asset.Overrides = entries
                    .OrderBy(item => item.Repository, StringComparer.Ordinal)
                    .ThenBy(item => item.RepoPath, StringComparer.Ordinal)
                    .ToArray();
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();
            }

            return AssetDatabase.GetAssetPath(asset);
        }

        public void SetProjectOverride(string repository, string repoPath, OnnxConversionRecipe recipe)
        {
            if (string.IsNullOrWhiteSpace(repository))
            {
                throw new ArgumentException("Repository is required.", nameof(repository));
            }

            if (string.IsNullOrWhiteSpace(repoPath))
            {
                throw new ArgumentException("Repository path is required.", nameof(repoPath));
            }

            var normalizedRecipe = CloneRecipe(recipe);
            if (IsSameRecipe(normalizedRecipe, GetManifestDefault(repository, repoPath).Recipe))
            {
                RemoveProjectOverride(repository, repoPath);
                return;
            }

            var asset = GetOrCreateDefaultAsset();
            var entries = (asset.Overrides ?? Array.Empty<ProjectModelConversionOverrideEntry>()).ToList();
            var existingEntry = entries.FirstOrDefault(item =>
                string.Equals(item?.Repository, repository, StringComparison.Ordinal) &&
                string.Equals(item?.RepoPath, repoPath, StringComparison.Ordinal));
            if (existingEntry == null)
            {
                entries.Add(new ProjectModelConversionOverrideEntry
                {
                    Repository = repository,
                    RepoPath = repoPath,
                    Recipe = normalizedRecipe
                });
            }
            else
            {
                existingEntry.Recipe = normalizedRecipe;
            }

            asset.Overrides = entries
                .OrderBy(item => item.Repository, StringComparer.Ordinal)
                .ThenBy(item => item.RepoPath, StringComparer.Ordinal)
                .ToArray();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
        }

        public bool RemoveProjectOverride(string repository, string repoPath)
        {
            var assetPath = GetExistingAssetPath();
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return false;
            }

            var asset = AssetDatabase.LoadAssetAtPath<ProjectModelConversionOverridesAsset>(assetPath);
            if (asset == null)
            {
                return false;
            }

            var entries = (asset.Overrides ?? Array.Empty<ProjectModelConversionOverrideEntry>()).ToList();
            var removedCount = entries.RemoveAll(item =>
                string.Equals(item?.Repository, repository, StringComparison.Ordinal) &&
                string.Equals(item?.RepoPath, repoPath, StringComparison.Ordinal));
            if (removedCount <= 0)
            {
                return false;
            }

            asset.Overrides = entries.ToArray();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            return true;
        }

        internal bool TryFindOverride(
            string repository,
            string repoPath,
            out ProjectModelConversionOverrideEntry entry,
            out string assetPath)
        {
            entry = null;
            assetPath = GetExistingAssetPath();
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return false;
            }

            var asset = AssetDatabase.LoadAssetAtPath<ProjectModelConversionOverridesAsset>(assetPath);
            entry = (asset?.Overrides ?? Array.Empty<ProjectModelConversionOverrideEntry>())
                .FirstOrDefault(item =>
                    string.Equals(item?.Repository, repository, StringComparison.Ordinal) &&
                    string.Equals(item?.RepoPath, repoPath, StringComparison.Ordinal));
            return entry != null;
        }

        private static string GetExistingAssetPath()
        {
            if (AssetDatabase.LoadAssetAtPath<ProjectModelConversionOverridesAsset>(DefaultAssetPath) != null)
            {
                return DefaultAssetPath;
            }

            var assetGuid = AssetDatabase.FindAssets("t:ProjectModelConversionOverridesAsset")
                .OrderBy(guid => guid, StringComparer.Ordinal)
                .FirstOrDefault();
            return string.IsNullOrWhiteSpace(assetGuid)
                ? null
                : AssetDatabase.GUIDToAssetPath(assetGuid);
        }

        internal static ProjectModelConversionOverridesAsset GetOrCreateDefaultAsset()
        {
            var existing = AssetDatabase.LoadAssetAtPath<ProjectModelConversionOverridesAsset>(DefaultAssetPath);
            if (existing != null)
            {
                return existing;
            }

            EnsureFolderExists(Path.GetDirectoryName(DefaultAssetPath)?.Replace('\\', '/'));
            var asset = UnityEngine.ScriptableObject.CreateInstance<ProjectModelConversionOverridesAsset>();
            AssetDatabase.CreateAsset(asset, DefaultAssetPath);
            AssetDatabase.SaveAssets();
            return asset;
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

        private static OnnxConversionRecipe CloneRecipe(OnnxConversionRecipe recipe)
        {
            var source = recipe ?? new OnnxConversionRecipe();
            return new OnnxConversionRecipe
            {
                QuantizationMode = string.IsNullOrWhiteSpace(source.QuantizationMode)
                    ? "none"
                    : source.QuantizationMode.Trim(),
                StaticInputShapeOverrides = (source.StaticInputShapeOverrides ?? Array.Empty<OnnxStaticInputShapeOverride>())
                    .Where(item => item != null)
                    .Where(item => !string.IsNullOrWhiteSpace(item.InputName))
                    .Select(item => new OnnxStaticInputShapeOverride
                    {
                        InputName = item.InputName.Trim(),
                        Dimensions = (item.Dimensions ?? Array.Empty<int>()).ToArray()
                    })
                    .ToArray()
            };
        }

        private static bool IsSameRecipe(OnnxConversionRecipe recipe, OnnxConversionRecipe other)
        {
            return string.Equals(
                OnnxImportMetadataUtility.ComputeRecipeHash(recipe),
                OnnxImportMetadataUtility.ComputeRecipeHash(other),
                StringComparison.Ordinal);
        }
    }
}
