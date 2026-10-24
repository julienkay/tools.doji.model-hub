using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Adds HF Hub's registry and bundled runtime models to the build's StreamingAssets, straight from the shared
    /// caches. Nothing is copied into <c>Assets</c>, so a failed build leaves nothing behind. A model that is not
    /// imported fails the build with a list, instead of shipping without it.
    /// </summary>
    internal sealed class HfHubBuildProcessor : BuildPlayerProcessor
    {
        private const string BuildFolder = "Library/Doji/HfHub/build";

        public override int callbackOrder => 0;

        public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
        {
            var plan = CreatePlan();
            if (plan.Registry.packages.Length == 0)
            {
                return;
            }

            if (plan.Problems.Count > 0)
            {
                throw new BuildFailedException(
                    "HF Hub cannot build these models:\n- " + string.Join("\n- ", plan.Problems));
            }

            foreach (var (sourcePath, relativePath) in plan.BundledFiles)
            {
                buildPlayerContext.AddAdditionalPathToStreamingAssets(sourcePath, relativePath);
            }

            buildPlayerContext.AddAdditionalPathToStreamingAssets(WriteRegistry(plan.Registry), RuntimeModels.RegistryRelativePath);
            Debug.Log($"HF Hub added {plan.BundledFiles.Count} bundled models and its registry to the build.");
        }

        internal static RuntimeRegistryBuilder.Plan CreatePlan()
        {
            var recipes = new ProjectModelRecipeSource();
            return RuntimeRegistryBuilder.Build(
                EditorOperations.CreateStatusReport(),
                ProjectSettings.instance.GetEffectiveDelivery,
                (repository, repoPath) => recipes.GetEffectiveRecipe(repository, repoPath).Recipe,
                forEditor: false,
                GetPlayerOnnxConverterId(),
                GetProjectRoot());
        }

        /// <summary>
        /// Converter id of <c>tools.doji.onnx-model-loader</c> in this project, or null when it is not installed.
        /// </summary>
        internal static string GetPlayerOnnxConverterId()
        {
            var loader = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                .FirstOrDefault(package => package.name == "tools.doji.onnx-model-loader");
            return loader == null
                ? null
                : $"{RuntimeRegistryBuilder.PlayerOnnxPipelineName}@{loader.version}+{SentisCompatibility.GetGeneration(ProjectImportService.GetSentisVersion())}";
        }

        private static string WriteRegistry(RuntimeRegistry registry)
        {
            var folder = Path.Combine(GetProjectRoot(), BuildFolder);
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "hf-hub-runtime.json");
            File.WriteAllText(path, registry.ToJson());
            return path;
        }

        private static string GetProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }
    }
}
