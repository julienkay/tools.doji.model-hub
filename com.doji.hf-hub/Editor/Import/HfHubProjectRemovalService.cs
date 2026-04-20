using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Removes project-owned runtime artifacts for one repository item and rebuilds runtime registries from remaining imports.
    /// </summary>
    internal sealed class HfHubProjectRemovalService
    {
        /// <summary>
        /// Deletes generated runtime assets and editor import metadata for one repository entry.
        /// </summary>
        public void Remove(HfHubModelHubItem item)
        {
            if (item == null)
            {
                return;
            }

            foreach (var dependent in item.Dependents)
            {
                if (!string.IsNullOrWhiteSpace(dependent.ImportMetadataPath))
                {
                    var metadataAbsolutePath = HfHubProjectPaths.ToProjectAbsolutePath(dependent.ImportMetadataPath);
                    if (File.Exists(metadataAbsolutePath))
                    {
                        File.Delete(metadataAbsolutePath);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(item.RuntimeSnapshotAssetPath))
            {
                AssetDatabase.DeleteAsset(item.RuntimeSnapshotAssetPath);
            }

            CleanupEmptyImportStateFolders(item);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            RebuildRuntimeRegistries();
        }

        /// <summary>
        /// Removes one repository entry's raw cache mapping for this machine while keeping generated project assets.
        /// </summary>
        public void RemoveRawCache(HfHubModelHubItem item)
        {
            if (item == null)
            {
                return;
            }

            var revisionRefPath = HfHubCachePaths.GetRevisionRefPath(item.Repository, item.RequestedRevision);
            var resolvedCommitHash = ResolveCommitHash(item, revisionRefPath);
            if (File.Exists(revisionRefPath))
            {
                File.Delete(revisionRefPath);
            }

            if (!string.IsNullOrWhiteSpace(resolvedCommitHash) &&
                !HasOtherRevisionRefForCommit(item.Repository, resolvedCommitHash))
            {
                var snapshotRootPath = HfHubCachePaths.GetSnapshotRoot(item.Repository, resolvedCommitHash);
                if (Directory.Exists(snapshotRootPath))
                {
                    Directory.Delete(snapshotRootPath, true);
                }
            }

            CleanupEmptyCacheFolders(item.Repository);
        }

        private static void CleanupEmptyImportStateFolders(HfHubModelHubItem item)
        {
            foreach (var dependent in item.Dependents)
            {
                var importStateFolder = HfHubProjectPaths.GetImportStateFolder(
                    dependent.Dependency.PackageId,
                    dependent.Dependency.ModelId);
                var importStateAbsolutePath = HfHubProjectPaths.ToProjectAbsolutePath(importStateFolder);
                if (Directory.Exists(importStateAbsolutePath) && !Directory.EnumerateFileSystemEntries(importStateAbsolutePath).Any())
                {
                    Directory.Delete(importStateAbsolutePath, false);
                }
            }
        }

        private static string ResolveCommitHash(HfHubModelHubItem item, string revisionRefPath)
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
            var refsRoot = Path.Combine(HfHubCachePaths.GetRepositoryRoot(repository), "refs");
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
            var repositoryRoot = HfHubCachePaths.GetRepositoryRoot(repository);
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
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path, false);
            }
        }

        private static void RebuildRuntimeRegistries()
        {
            var discoveryReport = HfHubEditorManifestDiscovery.DiscoverInstalledManifests();
            var statusReport = new HfHubStatusService().BuildStatusReport(discoveryReport);
            var readyResults = statusReport.DependencyStatuses
                .Where(status => status.State == HfHubDependencyState.Ready && status.ImportedMetadata != null)
                .Select(status => new HfHubModelSyncResult(
                    status.Dependency,
                    null,
                    new HfHubImportedModelArtifact(
                        HfHubProjectPaths.GetRuntimeSnapshotFolder(status.Dependency.Repository, status.ImportedMetadata.ResolvedCommitHash),
                        status.ImportMetadataPath,
                        status.ImportedMetadata),
                    null))
                .ToArray();

            var registryService = new HfHubRuntimeLocatorRegistryService();
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

                var registryAssetPath = HfHubProjectPaths.GetRuntimeRegistryAssetPath(packageId);
                AssetDatabase.DeleteAsset(registryAssetPath);
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }
    }
}
