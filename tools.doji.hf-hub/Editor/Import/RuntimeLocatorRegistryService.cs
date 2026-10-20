using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Writes per-package runtime locator registries for generated files under shared `StreamingAssets` root.
    /// </summary>
    internal sealed class RuntimeLocatorRegistryService : IRuntimeLocatorRegistryService
    {
        private const int RuntimeLocatorSchemaVersion = 1;

        /// <summary>
        /// Rebuilds runtime locator registries from successful sync results.
        /// </summary>
        public void WriteRegistries(IReadOnlyList<ModelSyncResult> modelResults)
        {
            var successfulResults = (modelResults ?? Array.Empty<ModelSyncResult>())
                .Where(result => result.Success && result.ImportedArtifact?.Metadata != null)
                .ToArray();

            foreach (var packageGroup in successfulResults.GroupBy(result => result.Dependency.PackageId, StringComparer.Ordinal))
            {
                var manifest = new RuntimeLocatorManifest(
                    RuntimeLocatorSchemaVersion,
                    packageGroup.Key,
                    packageGroup.First().Dependency.PackageVersion,
                    packageGroup
                        .OrderBy(result => result.Dependency.ModelId, StringComparer.Ordinal)
                        .Select(CreateEntry)
                        .ToArray());

                var registryAssetPath = ProjectPaths.GetRuntimeRegistryAssetPath(packageGroup.Key);
                var registryAbsolutePath = ProjectPaths.ToAbsolutePath(registryAssetPath);
                EnsureAssetFolderExists(Path.GetDirectoryName(registryAssetPath)?.Replace('\\', '/')
                    ?? throw new InvalidOperationException("Missing runtime registry directory."));

                Directory.CreateDirectory(Path.GetDirectoryName(registryAbsolutePath)
                    ?? throw new InvalidOperationException("Missing runtime registry directory."));
                File.WriteAllText(
                    registryAbsolutePath,
                    JsonUtility.ToJson(new RuntimeLocatorManifestDocument(manifest), true));

                AssetDatabase.ImportAsset(
                    registryAssetPath,
                    ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            }
        }

        private static RuntimeLocatorEntry CreateEntry(ModelSyncResult result)
        {
            var metadata = result.ImportedArtifact.Metadata;
            return new RuntimeLocatorEntry(
                result.Dependency.Repository,
                metadata.RuntimeFiles
                    .Select(file => new RuntimeLocatorFile(
                        file.RelativePath,
                        file.RepoPath,
                        file.Kind))
                    .ToArray());
        }

        private static void EnsureAssetFolderExists(string assetFolderPath)
        {
            if (AssetDatabase.IsValidFolder(assetFolderPath))
            {
                return;
            }

            var parts = assetFolderPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !string.Equals(parts[0], "Assets", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Expected Unity asset folder under Assets, got '{assetFolderPath}'.");
            }

            var currentFolder = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var nextFolder = $"{currentFolder}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(nextFolder))
                {
                    AssetDatabase.CreateFolder(currentFolder, parts[i]);
                }

                currentFolder = nextFolder;
            }
        }
    }
}
