using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Copies runtime models into <c>Assets/StreamingAssets/hf-hub/models</c>, for projects that want a model's file
    /// in the project, for example to pin it for a build. Builds then ship the copy instead of adding the model, and
    /// Play mode loads the copy.
    /// </summary>
    internal static class PulledModels
    {
        public static string GetFolderAssetPath(string repository)
        {
            return $"Assets/StreamingAssets/{Hub.RuntimeStreamingRelativeRoot}/models/{Hub.SanitizeRelativePath(repository)}";
        }

        /// <summary>
        /// Whether any runtime model of a repository has a copy in the project.
        /// </summary>
        public static bool HasCopies(string repository, IEnumerable<ImportedRuntimeFileMetadata> runtimeFiles)
        {
            return (runtimeFiles ?? Array.Empty<ImportedRuntimeFileMetadata>())
                .Any(file => File.Exists(ProjectPaths.ToAbsolutePath(ProjectPaths.GetPulledModelAssetPath(repository, file.RepoPath))));
        }

        /// <summary>
        /// Copies every runtime model of a repository into the project. Returns the asset paths written.
        /// </summary>
        public static IReadOnlyList<string> Pull(string repository, IEnumerable<ImportedRuntimeFileMetadata> runtimeFiles)
        {
            var written = new List<string>();
            foreach (var file in runtimeFiles ?? Array.Empty<ImportedRuntimeFileMetadata>())
            {
                if (string.IsNullOrWhiteSpace(file?.FilePath) || !File.Exists(file.FilePath))
                {
                    throw new FileNotFoundException($"The runtime model of {file?.RepoPath} is missing. Import {repository} first.", file?.FilePath);
                }

                var assetPath = ProjectPaths.GetPulledModelAssetPath(repository, file.RepoPath);
                var absolutePath = ProjectPaths.ToAbsolutePath(assetPath);
                Directory.CreateDirectory(Path.GetDirectoryName(absolutePath) ?? string.Empty);
                File.Copy(file.FilePath, absolutePath, true);
                written.Add(assetPath);
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            EditorRuntimeCatalog.Invalidate();
            return written;
        }

        /// <summary>
        /// Deletes a repository's copies from the project. Builds add the model from the caches again.
        /// </summary>
        public static bool Remove(string repository)
        {
            var folder = GetFolderAssetPath(repository);
            var removed = AssetDatabase.IsValidFolder(folder) && AssetDatabase.DeleteAsset(folder);
            EditorRuntimeCatalog.Invalidate();
            return removed;
        }
    }
}
