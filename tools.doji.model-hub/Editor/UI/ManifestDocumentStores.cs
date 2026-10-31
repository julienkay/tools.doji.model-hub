using System;
using System.Linq;
using UnityEditor;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Loads and saves one JSON-backed manifest document for a host editor surface.
    /// </summary>
    internal interface IManifestDocumentStore
    {
        string DisplayName { get; }
        bool IsEditable { get; }
        bool ShouldPersistValidationCache { get; }
        bool TryLoad(out int version, out ManifestModelEntry[] models, out string errorMessage);
        bool TrySave(int version, ManifestModelEntry[] models, out string errorMessage);
    }


    /// <summary>
    /// Binds the shared editor to one manifest file already represented by a Unity asset path.
    /// </summary>
    internal sealed class AssetManifestDocumentStore : IManifestDocumentStore
    {
        private readonly string _assetPath;
        private readonly string _displayName;
        private readonly string _absolutePath;

        public AssetManifestDocumentStore(string assetPath)
        {
            _assetPath = assetPath;
            _displayName = string.IsNullOrWhiteSpace(assetPath) ? Hub.ManifestFileName : assetPath;
            ModelManifestInspectorUtility.TryLoadManifest(assetPath, out _, out _, out _absolutePath, out _);
        }

        public string DisplayName => _displayName;
        public bool IsEditable => ModelManifestInspectorUtility.IsManifestEditable(_assetPath, _absolutePath);
        public bool ShouldPersistValidationCache =>
            !string.IsNullOrWhiteSpace(_assetPath) &&
            _assetPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase);

        public bool TryLoad(out int version, out ManifestModelEntry[] models, out string errorMessage)
        {
            return ModelManifestInspectorUtility.TryLoadManifestEntries(_assetPath, out version, out models, out errorMessage);
        }

        public bool TrySave(int version, ManifestModelEntry[] models, out string errorMessage)
        {
            errorMessage = null;
            if (!ModelManifestInspectorUtility.TryBuildManifestJson(version, models, out var serializedJson, out errorMessage))
            {
                return false;
            }

            try
            {
                System.IO.File.WriteAllText(_absolutePath, serializedJson);
                DiscoveredDependencyCache.Invalidate();
                AssetDatabase.ImportAsset(_assetPath, ImportAssetOptions.ForceUpdate);
                return true;
            }
            catch (Exception exception)
            {
                errorMessage = $"Failed to write manifest: {exception.Message}";
                return false;
            }
        }
    }

    /// <summary>
    /// Binds the shared editor to the project-scoped manifest stored under ProjectSettings.
    /// </summary>
    internal sealed class ProjectSettingsManifestDocumentStore : IManifestDocumentStore
    {
        public string DisplayName => ProjectSettingsModelManifestStore.GetManifestPath();
        public bool IsEditable => true;
        public bool ShouldPersistValidationCache => true;

        public bool TryLoad(out int version, out ManifestModelEntry[] models, out string errorMessage)
        {
            return ProjectSettingsModelManifestStore.TryLoadManifestEntries(null, out version, out models, out errorMessage);
        }

        public bool TrySave(int version, ManifestModelEntry[] models, out string errorMessage)
        {
            var drafts = (models ?? Array.Empty<ManifestModelEntry>())
                .Select(model => new ManifestModelDraft(
                    model?.ModelId,
                    model?.Revision,
                    model?.HasExplicitFileSelection == true ? model.FileEntries : null))
                .ToArray();
            return ProjectSettingsModelManifestStore.TryWriteManifest(version, drafts, out errorMessage);
        }
    }

    /// <summary>
    /// Binds shared manifest editor to one `ProjectManifestAsset` so ScriptableObject manifests get same UX as JSON manifests.
    /// </summary>
    internal sealed class ProjectManifestAssetDocumentStore : IManifestDocumentStore
    {
        private readonly ProjectManifestAsset _asset;
        private readonly string _assetPath;

        public ProjectManifestAssetDocumentStore(ProjectManifestAsset asset)
        {
            _asset = asset;
            _assetPath = asset == null ? string.Empty : AssetDatabase.GetAssetPath(asset);
        }

        public string DisplayName => string.IsNullOrWhiteSpace(_assetPath) ? _asset?.name ?? "Project Manifest Asset" : _assetPath;
        public bool IsEditable => _asset != null;
        public bool ShouldPersistValidationCache => true;

        public bool TryLoad(out int version, out ManifestModelEntry[] models, out string errorMessage)
        {
            return ModelManifestInspectorUtility.TryLoadProjectManifestAsset(_asset, out version, out models, out errorMessage);
        }

        public bool TrySave(int version, ManifestModelEntry[] models, out string errorMessage)
        {
            if (!ModelManifestInspectorUtility.TryWriteProjectManifestAssetDocument(_asset, version, models, out errorMessage))
            {
                return false;
            }

            DiscoveredDependencyCache.Invalidate();
            return true;
        }
    }
}
