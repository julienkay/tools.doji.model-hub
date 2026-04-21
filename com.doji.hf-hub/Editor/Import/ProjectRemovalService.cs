using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Removes project-owned owner bindings and garbage-collects shared runtime artifacts when no bindings remain.
    /// </summary>
    internal sealed class ProjectRemovalService
    {
        /// <summary>
        /// Deletes owner binding metadata for one repository entry and removes shared runtime artifacts only when no bindings still reference them.
        /// </summary>
        public void Remove(ModelHubItem item)
        {
            if (item == null || item.HasRevisionConflict)
            {
                return;
            }

            var sharedMetadataPaths = item.Dependents
                .Select(dependent => dependent.ImportedMetadata?.SharedImportMetadataPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            foreach (var dependent in item.Dependents)
            {
                if (string.IsNullOrWhiteSpace(dependent.BindingMetadataPath))
                {
                    continue;
                }

                var metadataAbsolutePath = ProjectPaths.ToProjectAbsolutePath(dependent.BindingMetadataPath);
                if (File.Exists(metadataAbsolutePath))
                {
                    File.Delete(metadataAbsolutePath);
                }
            }

            CleanupEmptyBindingFolders(item);

            foreach (var sharedMetadataPath in sharedMetadataPaths)
            {
                if (HasAnyBindingReferencing(sharedMetadataPath))
                {
                    continue;
                }

                DeleteSharedImport(sharedMetadataPath);
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            RebuildRuntimeRegistries();
        }

        /// <summary>
        /// Removes one repository entry's raw cache mapping for this machine while keeping generated project assets.
        /// </summary>
        public void RemoveRawCache(ModelHubItem item)
        {
            if (item == null || item.HasRevisionConflict)
            {
                return;
            }

            var revisionRefPath = CachePaths.GetRevisionRefPath(item.Repository, item.RequestedRevision);
            var resolvedCommitHash = ResolveCommitHash(item, revisionRefPath);
            if (File.Exists(revisionRefPath))
            {
                File.Delete(revisionRefPath);
            }

            if (!string.IsNullOrWhiteSpace(resolvedCommitHash) &&
                !HasOtherRevisionRefForCommit(item.Repository, resolvedCommitHash))
            {
                var snapshotRootPath = CachePaths.GetSnapshotRoot(item.Repository, resolvedCommitHash);
                if (Directory.Exists(snapshotRootPath))
                {
                    Directory.Delete(snapshotRootPath, true);
                }
            }

            CleanupEmptyCacheFolders(item.Repository);
        }

        private static void CleanupEmptyBindingFolders(ModelHubItem item)
        {
            foreach (var dependent in item.Dependents)
            {
                var bindingFolder = ProjectPaths.GetImportBindingFolder(
                    dependent.Dependency.PackageId,
                    dependent.Dependency.ModelId);
                var bindingAbsolutePath = ProjectPaths.ToProjectAbsolutePath(bindingFolder);
                if (Directory.Exists(bindingAbsolutePath) && !Directory.EnumerateFileSystemEntries(bindingAbsolutePath).Any())
                {
                    Directory.Delete(bindingAbsolutePath, false);
                }
            }
        }

        private static bool HasAnyBindingReferencing(string sharedMetadataPath)
        {
            var bindingsRoot = ProjectPaths.ToProjectAbsolutePath("Library/Doji/HfHub/imports/bindings");
            if (!Directory.Exists(bindingsRoot))
            {
                return false;
            }

            foreach (var bindingPath in Directory.EnumerateFiles(bindingsRoot, "hf-hub-binding.json", SearchOption.AllDirectories))
            {
                if (!ProjectImportService.TryReadBindingMetadata(bindingPath, out var bindingMetadata))
                {
                    continue;
                }

                if (string.Equals(bindingMetadata.SharedImportMetadataPath, sharedMetadataPath, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void DeleteSharedImport(string sharedMetadataPath)
        {
            var sharedMetadataAbsolutePath = ProjectPaths.ToProjectAbsolutePath(sharedMetadataPath);
            if (!ProjectImportService.TryReadSharedMetadata(sharedMetadataAbsolutePath, out var sharedMetadata))
            {
                if (File.Exists(sharedMetadataAbsolutePath))
                {
                    File.Delete(sharedMetadataAbsolutePath);
                }

                DeleteDirectoryIfEmpty(Path.GetDirectoryName(sharedMetadataAbsolutePath));
                return;
            }

            if (!string.IsNullOrWhiteSpace(sharedMetadata.RuntimeSnapshotAssetPath))
            {
                AssetDatabase.DeleteAsset(sharedMetadata.RuntimeSnapshotAssetPath);
            }

            if (File.Exists(sharedMetadataAbsolutePath))
            {
                File.Delete(sharedMetadataAbsolutePath);
            }

            DeleteDirectoryIfEmpty(Path.GetDirectoryName(sharedMetadataAbsolutePath));
            DeleteDirectoryIfEmpty(Path.GetDirectoryName(Path.GetDirectoryName(sharedMetadataAbsolutePath)));
            DeleteDirectoryIfEmpty(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(sharedMetadataAbsolutePath))));
        }

        private static string ResolveCommitHash(ModelHubItem item, string revisionRefPath)
        {
            if (!string.IsNullOrWhiteSpace(item.ResolvedCommitHash))
            {
                return item.ResolvedCommitHash;
            }

            if (File.Exists(revisionRefPath))
            {
                return File.ReadAllText(revisionRefPath).Trim();
            }

            return item.Dependents
                .Select(dependent => dependent.ImportedMetadata?.ResolvedCommitHash ?? dependent.RawMetadata?.ResolvedCommitHash)
                .FirstOrDefault(commitHash => !string.IsNullOrWhiteSpace(commitHash));
        }

        private static bool HasOtherRevisionRefForCommit(string repository, string resolvedCommitHash)
        {
            var refsRoot = Path.Combine(CachePaths.GetRepositoryRoot(repository), "refs");
            if (!Directory.Exists(refsRoot))
            {
                return false;
            }

            foreach (var refPath in Directory.EnumerateFiles(refsRoot, "*", SearchOption.AllDirectories))
            {
                try
                {
                    if (string.Equals(File.ReadAllText(refPath).Trim(), resolvedCommitHash, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            return false;
        }

        private static void CleanupEmptyCacheFolders(string repository)
        {
            var repositoryRoot = CachePaths.GetRepositoryRoot(repository);
            if (!Directory.Exists(repositoryRoot))
            {
                return;
            }

            DeleteDirectoryIfEmpty(Path.Combine(repositoryRoot, "refs"));
            DeleteDirectoryIfEmpty(Path.Combine(repositoryRoot, "snapshots"));
            DeleteDirectoryIfEmpty(repositoryRoot);
        }

        private static void DeleteDirectoryIfEmpty(string path)
        {
            if (!string.IsNullOrWhiteSpace(path) &&
                Directory.Exists(path) &&
                !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path, false);
            }
        }

        private static void RebuildRuntimeRegistries()
        {
            var discoveryReport = EditorManifestDiscovery.DiscoverInstalledManifests();
            var statusReport = new StatusService().BuildStatusReport(discoveryReport);
            var readyResults = statusReport.DependencyStatuses
                .Where(status => status.State == DependencyState.Ready && status.ImportedMetadata != null)
                .Select(status => new ModelSyncResult(
                    status.Dependency,
                    null,
                    new ImportedModelArtifact(
                        ProjectPaths.GetRuntimeSnapshotFolder(status.Dependency.Repository, status.ImportedMetadata.ResolvedCommitHash),
                        status.BindingMetadataPath,
                        status.ImportedMetadata,
                        status.ImportedMetadata.SharedImportMetadataPath),
                    null))
                .ToArray();

            var registryService = new RuntimeLocatorRegistryService();
            registryService.WriteRegistries(readyResults);

            var readyPackageIds = new HashSet<string>(
                readyResults.Select(result => result.Dependency.PackageId),
                StringComparer.Ordinal);

            foreach (var packageId in discoveryReport.Dependencies.Select(dependency => dependency.PackageId).Distinct(StringComparer.Ordinal))
            {
                if (readyPackageIds.Contains(packageId))
                {
                    continue;
                }

                var registryAssetPath = ProjectPaths.GetRuntimeRegistryAssetPath(packageId);
                AssetDatabase.DeleteAsset(registryAssetPath);
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }
    }
}
