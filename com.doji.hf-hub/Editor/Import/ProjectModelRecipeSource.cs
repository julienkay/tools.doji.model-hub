using System;
using System.IO;
using System.Linq;
using UnityEditor;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Resolves effective file-level conversion recipes from project-owned override asset plus built-in defaults.
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
    /// Loads project-owned conversion overrides from one versioned asset under `Assets/`.
    /// </summary>
    internal sealed class ProjectModelRecipeSource : IProjectModelRecipeSource
    {
        public const string DefaultAssetPath = "Assets/Doji/HfHub/HF Hub Conversion Overrides.asset";

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

            return new EffectiveOnnxConversionRecipe(
                OnnxImportMetadataUtility.CreateDefaultRecipe(),
                "Built-in Default",
                null,
                false);
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
                    Recipe = CloneRecipe(OnnxImportMetadataUtility.CreateDefaultRecipe())
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
            if (IsDefaultRecipe(normalizedRecipe))
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

        private static bool IsDefaultRecipe(OnnxConversionRecipe recipe)
        {
            return string.Equals(
                OnnxImportMetadataUtility.ComputeRecipeHash(recipe),
                OnnxImportMetadataUtility.ComputeRecipeHash(OnnxImportMetadataUtility.CreateDefaultRecipe()),
                StringComparison.Ordinal);
        }
    }
}
