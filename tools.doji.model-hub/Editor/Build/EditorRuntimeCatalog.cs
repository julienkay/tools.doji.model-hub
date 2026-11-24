using System.IO;
using UnityEditor;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Lets <see cref="Hub"/> find models in the Editor from this project's import records: no StreamingAssets files are
    /// needed for Play mode. The catalog is rebuilt after a sync or a project change.
    /// </summary>
    [InitializeOnLoad]
    internal static class EditorRuntimeCatalog
    {
        private static RuntimeRegistry _catalog;

        static EditorRuntimeCatalog()
        {
            RuntimeModels.EditorCatalog = Get;
            RuntimeModels.EditorOfferEnableModel = OptionalModelPrompt.OfferEnable;
            RuntimeModels.EditorWaitForSync = OptionalModelPrompt.WaitForSyncAsync;
            BackgroundSyncManager.SyncStateChanged += Invalidate;
            EditorApplication.projectChanged += Invalidate;
        }

        public static void Invalidate()
        {
            _catalog = null;
        }

        private static RuntimeRegistry Get()
        {
            if (_catalog != null)
            {
                return _catalog;
            }

            var recipes = new ProjectModelRecipeSource();
            _catalog = RuntimeRegistryBuilder.Build(
                EditorOperations.CreateStatusReport(),
                ProjectSettings.instance.GetEffectiveDelivery,
                (repository, repoPath) => recipes.GetEffectiveRecipe(repository, repoPath).Recipe,
                forEditor: true,
                ProjectImportService.GetConverterId(),
                Path.GetFullPath(Path.Combine(Application.dataPath, ".."))).Registry;
            return _catalog;
        }
    }
}
