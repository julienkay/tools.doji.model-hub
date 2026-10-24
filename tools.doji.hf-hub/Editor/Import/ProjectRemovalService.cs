using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Removes this project's import records and models pulled into the project, and removes files from the shared
    /// Hugging Face cache.
    /// </summary>
    internal sealed class ProjectRemovalService
    {
        /// <summary>
        /// Deletes this project's import records of one repository and its models pulled into
        /// <c>Assets/StreamingAssets</c>. Converted models stay in the shared converted folder, where other projects
        /// may use them, so importing again is fast.
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

            // Pulled copies belong to the repository, not to one owner: keep them while another owner still imports it.
            var pulledFolder = $"Assets/StreamingAssets/{Hub.RuntimeStreamingRelativeRoot}/models/{Hub.SanitizeRelativePath(item.Repository)}";
            if (!HasAnyBindingForRepository(item.Repository) && AssetDatabase.IsValidFolder(pulledFolder))
            {
                AssetDatabase.DeleteAsset(pulledFolder);
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            EditorRuntimeCatalog.Invalidate();
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
                var linkedBlobs = CollectLinkedBlobs(snapshotRootPath);
                if (Directory.Exists(snapshotRootPath))
                {
                    Directory.Delete(snapshotRootPath, true);
                }

                var indexPath = CachePaths.GetSnapshotMetadataPath(item.Repository, resolvedCommitHash);
                if (File.Exists(indexPath))
                {
                    File.Delete(indexPath);
                }

                DeleteUnusedBlobs(item.Repository, linkedBlobs);
            }

            DeleteEmptyParentFolders(revisionRefPath, Path.Combine(CachePaths.GetRepositoryRoot(item.Repository), "refs"));
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

        private static bool HasAnyBindingForRepository(string repository)
        {
            var bindingsRoot = ProjectPaths.ToProjectAbsolutePath("Library/Doji/HfHub/imports/bindings");
            return Directory.Exists(bindingsRoot) &&
                Directory.EnumerateFiles(bindingsRoot, "hf-hub-binding.json", SearchOption.AllDirectories)
                    .Any(path => ProjectImportService.TryReadBindingMetadata(path, out var binding) &&
                        string.Equals(binding.Repository, repository, StringComparison.OrdinalIgnoreCase));
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

        /// <summary>
        /// Returns the blobs that the snapshot's files link to. Plain-file snapshots (no symlinks) link to none.
        /// </summary>
        internal static HashSet<string> CollectLinkedBlobs(string snapshotRootPath)
        {
            var blobs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(snapshotRootPath))
            {
                return blobs;
            }

            foreach (var filePath in Directory.EnumerateFiles(snapshotRootPath, "*", SearchOption.AllDirectories))
            {
                var target = CacheFiles.TryGetLinkTarget(filePath);
                if (!string.IsNullOrWhiteSpace(target))
                {
                    blobs.Add(Path.GetFullPath(target));
                }
            }

            return blobs;
        }

        /// <summary>
        /// Deletes the removed snapshot's blobs that no remaining snapshot links to, like <c>delete_revisions</c> in
        /// <c>huggingface_hub</c>. Snapshots from other tools keep their blobs, and other files in <c>blobs/</c>, such
        /// as another program's partial download, are never touched.
        /// </summary>
        private static void DeleteUnusedBlobs(string repository, ICollection<string> removedSnapshotBlobs)
        {
            if (removedSnapshotBlobs.Count == 0)
            {
                return;
            }

            var blobsRoot = Path.GetFullPath(CachePaths.GetBlobsRoot(repository)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var stillLinked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var snapshotsRoot = Path.Combine(CachePaths.GetRepositoryRoot(repository), "snapshots");
            if (Directory.Exists(snapshotsRoot))
            {
                foreach (var snapshot in Directory.EnumerateDirectories(snapshotsRoot))
                {
                    stillLinked.UnionWith(CollectLinkedBlobs(snapshot));
                }
            }

            foreach (var blob in removedSnapshotBlobs)
            {
                if (stillLinked.Contains(blob) ||
                    !blob.StartsWith(blobsRoot, StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(blob))
                {
                    continue;
                }

                File.Delete(blob);
            }
        }

        /// <summary>
        /// Deletes empty folders between a removed nested ref, such as <c>refs/pr/1</c>, and the refs root.
        /// </summary>
        private static void DeleteEmptyParentFolders(string filePath, string stopAt)
        {
            var folder = Path.GetDirectoryName(filePath);
            var stopFullPath = Path.GetFullPath(stopAt).TrimEnd(Path.DirectorySeparatorChar);
            while (!string.IsNullOrWhiteSpace(folder) &&
                   !string.Equals(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar), stopFullPath, StringComparison.OrdinalIgnoreCase))
            {
                DeleteDirectoryIfEmpty(folder);
                if (Directory.Exists(folder))
                {
                    return;
                }

                folder = Path.GetDirectoryName(folder);
            }
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
            DeleteDirectoryIfEmpty(Path.Combine(repositoryRoot, "blobs"));
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
    }
}
