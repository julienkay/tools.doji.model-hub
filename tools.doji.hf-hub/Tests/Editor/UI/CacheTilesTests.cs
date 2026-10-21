using System.Linq;
using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers the rules of the Model Hub cache overview: tile kinds, file tiles, and which partial downloads the
    /// selection card offers to delete.
    /// </summary>
    internal sealed class CacheTilesTests
    {
        /// <summary>
        /// Verifies a model this project lists is used, ignoring letter case, and anything else is unused.
        /// </summary>
        [TestCase("model", "julienkay/sentis-MiDaS", CacheTileKind.Used)]
        [TestCase("model", "JULIENKAY/SENTIS-midas", CacheTileKind.Used)]
        [TestCase("model", "doji/other-model", CacheTileKind.Unused)]
        [TestCase("dataset", "julienkay/sentis-MiDaS", CacheTileKind.Unused)]
        public void Classify_MarksModelsThisProjectUses(string repoType, string repository, CacheTileKind expected)
        {
            var projectRepositories = CacheTiles.CreateProjectRepositorySet(new[] { "julienkay/sentis-midas", null, " " });

            var kind = CacheTiles.Classify(CreateRepository(repoType, repository, Complete(10)), projectRepositories);

            Assert.That(kind, Is.EqualTo(expected));
        }

        /// <summary>
        /// Verifies a repository with only partial downloads is partial even when this project uses it, and one with
        /// complete files keeps its kind.
        /// </summary>
        [Test]
        public void Classify_MarksRepositoriesWithOnlyPartialDownloadsAsPartial()
        {
            var projectRepositories = CacheTiles.CreateProjectRepositorySet(new[] { "doji/model" });

            Assert.That(CacheTiles.Classify(CreateRepository("model", "doji/model", Partial(5)), projectRepositories), Is.EqualTo(CacheTileKind.Partial));
            Assert.That(CacheTiles.Classify(CreateRepository("model", "doji/model", Complete(10), Partial(5)), projectRepositories), Is.EqualTo(CacheTileKind.Used));
        }

        /// <summary>
        /// Verifies a partial file tile is partial, and a complete one takes its repository's kind.
        /// </summary>
        [Test]
        public void ClassifyFile_MarksPartialFiles()
        {
            Assert.That(CacheTiles.ClassifyFile(Partial(5), CacheTileKind.Used), Is.EqualTo(CacheTileKind.Partial));
            Assert.That(CacheTiles.ClassifyFile(Complete(5), CacheTileKind.Unused), Is.EqualTo(CacheTileKind.Unused));
        }

        /// <summary>
        /// Verifies the largest files get their own tile and the rest merge into one with their count and size.
        /// </summary>
        [Test]
        public void SplitFiles_KeepsLargestFilesAndMergesTheRest()
        {
            var repository = CreateRepository("model", "doji/model", Complete(1, "a"), Complete(30, "b"), Complete(2, "c"), Complete(20, "d"));

            var (tiles, restCount, restBytes) = CacheTiles.SplitFiles(repository, maxTiles: 2);

            Assert.That(tiles.Select(file => file.Name), Is.EqualTo(new[] { "b", "d" }));
            Assert.That(restCount, Is.EqualTo(2));
            Assert.That(restBytes, Is.EqualTo(3));
        }

        /// <summary>
        /// Verifies which partial downloads the selection card offers to delete.
        /// </summary>
        [Test]
        public void GetPartialsToDelete_FollowsTheSelection()
        {
            var partial = Partial(5);
            var complete = Complete(10);
            var mixed = CreateRepository("model", "doji/model", complete, partial, Partial(7));
            var partialOnly = CreateRepository("model", "doji/model", Partial(5));

            Assert.That(CacheTiles.GetPartialsToDelete(mixed, partial), Is.EqualTo(new[] { partial }));
            Assert.That(CacheTiles.GetPartialsToDelete(mixed, complete), Is.Empty);
            Assert.That(CacheTiles.GetPartialsToDelete(mixed, null).Count, Is.EqualTo(2));
            Assert.That(CacheTiles.GetPartialsToDelete(partialOnly, null), Is.Empty, "Remove from Cache already covers a partial-only repository.");
        }

        /// <summary>
        /// Verifies the size text names the partial share only when complete files exist too.
        /// </summary>
        [Test]
        public void BuildSizeText_NamesPartialShare()
        {
            Assert.That(CacheTiles.BuildSizeText(CreateRepository("model", "doji/model", Complete(1024), Partial(1024))), Is.EqualTo("2 KB · 1 KB partial"));
            Assert.That(CacheTiles.BuildSizeText(CreateRepository("model", "doji/model", Partial(1024))), Is.EqualTo("1 KB"));
        }

        private static CacheRepositoryEntry CreateRepository(string repoType, string repository, params CacheFileEntry[] files)
        {
            return new CacheRepositoryEntry
            {
                RepoType = repoType,
                Repository = repository,
                FolderName = repoType + "s--" + repository.Replace("/", "--"),
                Files = files
            };
        }

        private static CacheFileEntry Complete(long size, string name = "model.onnx")
        {
            return new CacheFileEntry { Name = name, Path = name, SizeInBytes = size };
        }

        private static CacheFileEntry Partial(long size)
        {
            return new CacheFileEntry { Name = "partial", Path = "partial.incomplete", SizeInBytes = size, IsPartial = true };
        }
    }
}
