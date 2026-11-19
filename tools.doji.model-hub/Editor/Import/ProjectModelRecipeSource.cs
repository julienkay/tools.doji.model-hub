using System;
using System.Collections.Generic;
using System.Linq;

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
        bool RemoveProjectOverride(string repository, string repoPath);
    }

    /// <summary>
    /// Carries effective recipe together with its source.
    /// </summary>
    internal sealed class EffectiveOnnxConversionRecipe
    {
        public EffectiveOnnxConversionRecipe(
            OnnxConversionRecipe recipe,
            string sourceLabel,
            bool hasProjectOverride)
        {
            Recipe = recipe ?? new OnnxConversionRecipe();
            SourceLabel = string.IsNullOrWhiteSpace(sourceLabel) ? "Built-in Default" : sourceLabel;
            HasProjectOverride = hasProjectOverride;
        }

        public OnnxConversionRecipe Recipe { get; }
        public string SourceLabel { get; }
        public bool HasProjectOverride { get; }
    }

    /// <summary>
    /// Loads project-owned conversion overrides from <see cref="ProjectSettings"/>, and manifest defaults from the
    /// <c>conversion</c> settings of discovered manifests.
    /// </summary>
    internal sealed class ProjectModelRecipeSource : IProjectModelRecipeSource
    {
        private readonly IReadOnlyList<DiscoveredModelDependency> _dependencies;

        /// <param name="dependencies">Manifest dependencies to read defaults from. Null reads the installed manifests.</param>
        public ProjectModelRecipeSource(IReadOnlyList<DiscoveredModelDependency> dependencies = null)
        {
            _dependencies = dependencies;
        }

        public EffectiveOnnxConversionRecipe GetEffectiveRecipe(string repository, string repoPath)
        {
            var projectOverride = ProjectSettings.instance.GetConversionOverride(repository, repoPath);
            if (projectOverride != null)
            {
                return new EffectiveOnnxConversionRecipe(projectOverride, "Project Override", true);
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
                    false);
            }

            return new EffectiveOnnxConversionRecipe(
                OnnxImportMetadataUtility.CreateDefaultRecipe(),
                defaults.Length == 0
                    ? "Built-in Default"
                    : $"Built-in Default (manifests disagree: {JoinOwners(defaults.SelectMany(group => group.Select(item => item.Owner)))})",
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

            return TextHash.Sha256(builder.ToString());
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
            ProjectSettings.instance.SetConversionOverride(
                repository,
                repoPath,
                IsSameRecipe(normalizedRecipe, GetManifestDefault(repository, repoPath).Recipe) ? null : normalizedRecipe);
        }

        public bool RemoveProjectOverride(string repository, string repoPath)
        {
            return ProjectSettings.instance.SetConversionOverride(repository, repoPath, null);
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
