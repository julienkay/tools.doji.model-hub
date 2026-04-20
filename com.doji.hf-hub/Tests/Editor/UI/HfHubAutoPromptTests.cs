using System;
using System.Linq;
using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers prompt summary text for first-run dependency attention window.
    /// </summary>
    internal sealed class HfHubAutoPromptTests
    {
        /// <summary>
        /// Verifies single-repository prompt copy names needed model directly.
        /// </summary>
        [Test]
        public void BuildViewModel_UsesSingleRepositoryHeadlineWhenOneModelNeedsAttention()
        {
            var report = new HfHubStatusReport(
                Array.Empty<HfHubManifestDiscoveryIssue>(),
                new[]
                {
                    CreateStatus(HfHubDependencyState.Missing)
                });

            var viewModel = HfHubAutoPromptWindow.BuildViewModel(report);

            Assert.That(viewModel.Headline, Is.EqualTo("AI Model Required"));
            Assert.That(viewModel.Body, Does.Contain("download it into shared cache"));
            Assert.That(viewModel.Dependents.Count, Is.EqualTo(1));
            Assert.That(viewModel.Dependents[0].DisplayLabel, Is.EqualTo("com.doji.test"));
            Assert.That(viewModel.Dependents[0].PackageDisplayName, Is.EqualTo("Test Package"));
            Assert.That(viewModel.Details, Is.EqualTo(string.Empty));
            Assert.That(viewModel.PrimaryActionLabel, Is.EqualTo("Download"));
            Assert.That(viewModel.PrimaryActionTooltip, Does.Contain("Downloads this model"));
        }

        /// <summary>
        /// Verifies single-repository prompt uses import wording when files already exist in cache.
        /// </summary>
        [Test]
        public void BuildViewModel_UsesImportActionWhenOneModelIsAlreadyDownloaded()
        {
            var report = new HfHubStatusReport(
                Array.Empty<HfHubManifestDiscoveryIssue>(),
                new[]
                {
                    CreateStatus(HfHubDependencyState.Stale, hasRawCache: true)
                });

            var viewModel = HfHubAutoPromptWindow.BuildViewModel(report);

            Assert.That(viewModel.PrimaryActionLabel, Is.EqualTo("Import"));
            Assert.That(viewModel.PrimaryActionTooltip, Does.Contain("shared cache"));
        }

        /// <summary>
        /// Verifies missing import metadata still uses import wording when raw cache already exists.
        /// </summary>
        [Test]
        public void BuildViewModel_UsesImportActionWhenCacheExistsButImportIsMissing()
        {
            var report = new HfHubStatusReport(
                Array.Empty<HfHubManifestDiscoveryIssue>(),
                new[]
                {
                    CreateStatus(HfHubDependencyState.Missing, hasRawCache: true)
                });

            var viewModel = HfHubAutoPromptWindow.BuildViewModel(report);

            Assert.That(viewModel.PrimaryActionLabel, Is.EqualTo("Import"));
            Assert.That(viewModel.PrimaryActionTooltip, Does.Contain("shared cache"));
        }

        /// <summary>
        /// Verifies multi-repository prompt falls back to count plus explicit repo list.
        /// </summary>
        [Test]
        public void BuildViewModel_UsesMultiRepositorySummaryWhenSeveralModelsNeedAttention()
        {
            var report = new HfHubStatusReport(
                new[]
                {
                    new HfHubManifestDiscoveryIssue("project", "manifest", "Issue")
                },
                new[]
                {
                    CreateStatus(HfHubDependencyState.Missing, "alpha/model-a"),
                    CreateStatus(HfHubDependencyState.Stale, "beta/model-b")
                });

            var viewModel = HfHubAutoPromptWindow.BuildViewModel(report);

            Assert.That(viewModel.Headline, Is.EqualTo("2 manifest dependencies need AI models."));
            Assert.That(viewModel.Dependents.Select(entry => entry.DisplayLabel).ToArray(), Is.EqualTo(new[]
            {
                "com.doji.test",
                "com.doji.test"
            }));
            Assert.That(viewModel.Dependents.Select(entry => entry.Repository).ToArray(), Is.EqualTo(new[]
            {
                "alpha/model-a",
                "beta/model-b"
            }));
            Assert.That(viewModel.Details, Does.Contain("Needs attention in 2 manifest dependencies."));
            Assert.That(viewModel.Details, Does.Contain("1 discovery issues also need attention."));
            Assert.That(viewModel.PrimaryActionLabel, Is.EqualTo("Import All"));
            Assert.That(viewModel.PrimaryActionTooltip, Does.Contain("all unresolved models"));
        }

        private static HfHubDependencyStatus CreateStatus(
            HfHubDependencyState state,
            string repository = "doji/test-model",
            bool hasRawCache = false)
        {
            var dependency = new HfHubDiscoveredModelDependency(
                "com.doji.test",
                "Test Package",
                "1.0.0",
                "/tmp/com.doji.test",
                "/tmp/com.doji.test/hf-hub-manifest.json",
                "manifesthash",
                new HfHubModelDependencyDefinition(
                    repository,
                    "main"));

            return new HfHubDependencyStatus(
                dependency,
                state,
                state.ToString(),
                hasRawCache ? "/tmp/cache/refs/main" : null,
                hasRawCache ? "/tmp/cache/snapshots/commit" : null,
                hasRawCache ? "/tmp/cache/snapshots/commit/.hf-hub.json" : null,
                hasRawCache ? new HfHubRawCacheMetadata
                {
                    Repository = repository,
                    RequestedRevision = "main",
                    ResolvedCommitHash = "commit",
                    CachedSnapshotRoot = "/tmp/cache/snapshots/commit",
                    Files = Array.Empty<HfHubRawCacheFileMetadata>()
                } : null,
                null,
                null,
                null);
        }
    }
}
