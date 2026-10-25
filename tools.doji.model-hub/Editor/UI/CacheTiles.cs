using System;
using System.Collections.Generic;
using System.Linq;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// What a tile in the cache overview stands for, which decides its color.
    /// </summary>
    internal enum CacheTileKind
    {
        Used,
        Unused,
        Partial
    }

    /// <summary>
    /// Rules of the Model Hub cache overview, kept out of element-building code so tests can check them.
    /// </summary>
    internal static class CacheTiles
    {
        /// <summary>
        /// Most file tiles drawn per repository; the rest merge into one tile.
        /// </summary>
        public const int MaxFileTilesPerRepository = 120;

        /// <summary>
        /// Builds the set of repositories this project uses. Hub ids are not case-sensitive, so neither is the set.
        /// </summary>
        public static HashSet<string> CreateProjectRepositorySet(IEnumerable<string> repositories)
        {
            return new HashSet<string>(
                (repositories ?? Array.Empty<string>()).Where(repository => !string.IsNullOrWhiteSpace(repository)),
                StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// A repository with only partial downloads is partial. Otherwise it is used when it is a model this project
        /// lists; datasets, spaces, and other models are unused.
        /// </summary>
        public static CacheTileKind Classify(CacheRepositoryEntry repository, HashSet<string> projectRepositories)
        {
            if (repository.SizeInBytes == 0 && repository.HasPartialDownloads)
            {
                return CacheTileKind.Partial;
            }

            return repository.IsModel && projectRepositories != null && projectRepositories.Contains(repository.Repository)
                ? CacheTileKind.Used
                : CacheTileKind.Unused;
        }

        /// <summary>
        /// A partial file is partial; any other file takes its repository's kind.
        /// </summary>
        public static CacheTileKind ClassifyFile(CacheFileEntry file, CacheTileKind repositoryKind)
        {
            return file.IsPartial ? CacheTileKind.Partial : repositoryKind;
        }

        /// <summary>
        /// Splits a repository's files into the largest ones that get their own tile and the rest, merged into one.
        /// </summary>
        public static (IReadOnlyList<CacheFileEntry> Tiles, int RestCount, long RestBytes) SplitFiles(
            CacheRepositoryEntry repository,
            int maxTiles = MaxFileTilesPerRepository)
        {
            var ordered = repository.Files.OrderByDescending(file => file.SizeInBytes).ToArray();
            var rest = ordered.Skip(maxTiles).ToArray();
            return (ordered.Take(maxTiles).ToArray(), rest.Length, rest.Sum(file => file.SizeInBytes));
        }

        /// <summary>
        /// The partial downloads the selection card offers to delete: the selected partial file, or all partial files
        /// of a selected repository that also has complete files. A repository with only partial downloads has
        /// none, because Remove from Cache already deletes everything in it.
        /// </summary>
        public static IReadOnlyList<CacheFileEntry> GetPartialsToDelete(CacheRepositoryEntry repository, CacheFileEntry selectedFile)
        {
            if (selectedFile != null)
            {
                return selectedFile.IsPartial ? new[] { selectedFile } : Array.Empty<CacheFileEntry>();
            }

            return repository.HasPartialDownloads && repository.SizeInBytes > 0
                ? repository.Files.Where(file => file.IsPartial).ToArray()
                : Array.Empty<CacheFileEntry>();
        }

        /// <summary>
        /// Size text for a repository tile, with the partial share when it also has complete files.
        /// </summary>
        public static string BuildSizeText(CacheRepositoryEntry repository)
        {
            var size = UiKit.FormatBytes(repository.TotalSizeInBytes);
            return repository.HasPartialDownloads && repository.SizeInBytes > 0
                ? $"{size} · {UiKit.FormatBytes(repository.PartialSizeInBytes)} partial"
                : size;
        }

        public static string GetStatusLabel(CacheTileKind kind)
        {
            return kind switch
            {
                CacheTileKind.Used => "Used by this project",
                CacheTileKind.Partial => "Partial download",
                _ => "Not used by this project"
            };
        }

        public static string GetKindClass(CacheTileKind kind)
        {
            return kind switch
            {
                CacheTileKind.Used => "mh-tile--used",
                CacheTileKind.Partial => "mh-tile--partial",
                _ => "mh-tile--unused"
            };
        }
    }
}
