using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Turns this project's import records into the runtime registry: which models each package uses, where their
    /// runtime models come from, and which files a build bundles. The build and the Editor's Play mode both use it, so
    /// they agree on what a package can load.
    /// </summary>
    internal static class RuntimeRegistryBuilder
    {
        /// <summary>
        /// Converter id that a player with <c>tools.doji.onnx-model-loader</c> converts downloaded ONNX files with.
        /// </summary>
        internal const string PlayerOnnxPipelineName = "doji-onnx-model-loader";

        internal sealed class Plan
        {
            public RuntimeRegistry Registry { get; } = new();

            /// <summary>
            /// Runtime models the build adds to StreamingAssets: absolute source path and path inside StreamingAssets.
            /// </summary>
            public List<(string SourcePath, string RelativePath)> BundledFiles { get; } = new();

            /// <summary>
            /// Models that cannot be built, with the reason. A build fails when there are any.
            /// </summary>
            public List<string> Problems { get; } = new();
        }

        /// <summary>
        /// Builds the registry from a status report.
        /// </summary>
        /// <param name="getDelivery">Delivery of one repository from Project Settings.</param>
        /// <param name="getRecipe">Effective conversion settings of one ONNX file, for players that convert it.</param>
        /// <param name="forEditor">Set local paths for the Editor, and skip models that are not imported instead of
        /// reporting them.</param>
        /// <param name="playerOnnxConverterId">Converter id of the ONNX converter a player has, or null.</param>
        /// <param name="projectRoot">Project folder, to find models pulled into <c>Assets/StreamingAssets</c>.</param>
        public static Plan Build(
            StatusReport report,
            Func<string, ModelDelivery> getDelivery,
            Func<string, string, OnnxConversionRecipe> getRecipe,
            bool forEditor,
            string playerOnnxConverterId,
            string projectRoot)
        {
            var plan = new Plan();
            plan.Registry.onnxConverterId = playerOnnxConverterId;
            plan.Registry.formatGeneration = SentisCompatibility.GetGeneration(ProjectImportService.GetSentisVersion());
            var bundledPaths = new HashSet<string>(StringComparer.Ordinal);
            var packages = new List<RuntimePackageEntry>();

            foreach (var packageGroup in (report?.DependencyStatuses ?? Array.Empty<DependencyStatus>())
                         .GroupBy(status => status.Dependency.PackageId, StringComparer.Ordinal)
                         .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                var models = new List<RuntimeModelEntry>();
                foreach (var status in packageGroup.OrderBy(status => status.Dependency.ModelId, StringComparer.Ordinal))
                {
                    var dependency = status.Dependency;
                    var label = $"{dependency.Repository} ({dependency.PackageDisplayName ?? dependency.PackageId})";
                    var runtimeFiles = status.ImportedMetadata?.RuntimeFiles ?? Array.Empty<ImportedRuntimeFileMetadata>();
                    if (status.State != DependencyState.Ready || runtimeFiles.Length == 0)
                    {
                        if (!forEditor)
                        {
                            plan.Problems.Add($"{label}: not imported. Import it in Window > AI Model Hub. ({status.MaintainerMessage})");
                        }

                        continue;
                    }

                    var delivery = getDelivery?.Invoke(dependency.Repository) ?? ModelDelivery.Bundle;
                    var model = new RuntimeModelEntry
                    {
                        repoId = dependency.Repository,
                        revision = dependency.Revision,
                        commit = status.ImportedMetadata.ResolvedCommitHash,
                        delivery = delivery == ModelDelivery.Download ? "download" : "bundle",
                        cacheRepoId = GetCacheRepoId(dependency.Repository),
                        files = runtimeFiles
                            .OrderBy(file => file.RepoPath, StringComparer.Ordinal)
                            .Select(file => CreateFile(dependency, file, delivery, getRecipe, forEditor, projectRoot, plan, bundledPaths, label))
                            .Where(file => file != null)
                            .ToArray()
                    };
                    models.Add(model);
                }

                var first = packageGroup.First().Dependency;
                packages.Add(new RuntimePackageEntry
                {
                    packageId = first.PackageId,
                    packageVersion = first.PackageVersion,
                    models = models.ToArray()
                });
            }

            plan.Registry.packages = packages.ToArray();
            return plan;
        }

        /// <summary>
        /// The repository id as the cache folder on this machine spells it, which is the Hub's spelling when AI Model Hub
        /// or Python downloaded it.
        /// </summary>
        private static string GetCacheRepoId(string repository)
        {
            return CacheOverviewService.TryParseFolderName(CachePaths.ResolveRepositoryFolderName(repository), out _, out var cached)
                ? cached
                : repository;
        }

        private static RuntimeModelFile CreateFile(
            DiscoveredModelDependency dependency,
            ImportedRuntimeFileMetadata runtimeFile,
            ModelDelivery delivery,
            Func<string, string, OnnxConversionRecipe> getRecipe,
            bool forEditor,
            string projectRoot,
            Plan plan,
            HashSet<string> bundledPaths,
            string label)
        {
            var isOnnx = string.Equals(runtimeFile.Format, "onnx", StringComparison.Ordinal);
            var relativePath = Hub.GetRuntimeModelRelativePath(dependency.Repository, runtimeFile.RepoPath);
            var pulledPath = string.IsNullOrWhiteSpace(projectRoot)
                ? null
                : Path.Combine(projectRoot, "Assets", "StreamingAssets", relativePath.Replace('/', Path.DirectorySeparatorChar));
            var isPulled = pulledPath != null && File.Exists(pulledPath);
            var file = new RuntimeModelFile
            {
                repoPath = runtimeFile.RepoPath,
                format = isOnnx ? "onnx" : "sentis",
                relativePath = relativePath,
                source = new RuntimeSourceFile
                {
                    repoPath = runtimeFile.RepoPath,
                    etag = runtimeFile.SourceEtag,
                    size = runtimeFile.SourceSizeInBytes ?? 0
                },
                companions = (runtimeFile.Companions ?? Array.Empty<ImportedSourceFileMetadata>())
                    .Select(companion => new RuntimeSourceFile { repoPath = companion.RepoPath, etag = companion.Etag, size = companion.SizeInBytes ?? 0 })
                    .ToArray(),
                recipeHash = runtimeFile.RecipeHash
            };

            if (isOnnx)
            {
                var recipe = getRecipe?.Invoke(dependency.Repository, runtimeFile.RepoPath) ?? new OnnxConversionRecipe();
                file.quantization = OnnxConversionSettings.NormalizeQuantizationMode(recipe.QuantizationMode);
                file.inputShapes = (recipe.StaticInputShapeOverrides ?? Array.Empty<OnnxStaticInputShapeOverride>())
                    .Where(shape => shape != null)
                    .Select(shape => new RuntimeInputShape { inputName = shape.InputName, dimensions = shape.Dimensions })
                    .ToArray();
            }

            if (forEditor)
            {
                file.localPath = isPulled ? pulledPath : runtimeFile.FilePath;
                return file;
            }

            if (delivery == ModelDelivery.Download)
            {
                if (string.IsNullOrWhiteSpace(runtimeFile.SourceEtag))
                {
                    plan.Problems.Add($"{label}: {runtimeFile.RepoPath} has no etag to download it by. Download it again in Window > AI Model Hub.");
                }

                if (isOnnx && string.IsNullOrWhiteSpace(plan.Registry.onnxConverterId))
                {
                    plan.Problems.Add($"{label}: {runtimeFile.RepoPath} is an ONNX file set to download at runtime, so players must convert it. Install the tools.doji.onnx-model-loader package, or bundle this model.");
                }

                return file;
            }

            if (!isPulled && bundledPaths.Add(relativePath))
            {
                if (string.IsNullOrWhiteSpace(runtimeFile.FilePath) || !File.Exists(runtimeFile.FilePath))
                {
                    plan.Problems.Add($"{label}: the runtime model of {runtimeFile.RepoPath} is missing. Import it again in Window > AI Model Hub.");
                }
                else
                {
                    plan.BundledFiles.Add((runtimeFile.FilePath, relativePath));
                }
            }

            return file;
        }
    }
}
