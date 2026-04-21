using System;
using System.Linq;
using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers prompt summary text for first-run dependency attention window.
    /// </summary>
    internal sealed class AutoPromptTests
    {
        /// <summary>
        /// Verifies single-repository prompt copy names needed model directly.
        /// </summary>
        [Test]
        public void BuildViewModel_UsesSingleRepositoryHeadlineWhenOneModelNeedsAttention()
        {
            var report = new StatusReport(
                Array.Empty<ManifestDiscoveryIssue>(),
                new[]
                {
                    CreateStatus(DependencyState.Missing)
                });

            var viewModel = AutoPromptWindow.BuildViewModel(report);

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
            var report = new StatusReport(
                Array.Empty<ManifestDiscoveryIssue>(),
                new[]
                {
                    CreateStatus(DependencyState.Stale, hasRawCache: true)
                });

            var viewModel = AutoPromptWindow.BuildViewModel(report);

            Assert.That(viewModel.PrimaryActionLabel, Is.EqualTo("Import"));
            Assert.That(viewModel.PrimaryActionTooltip, Does.Contain("shared cache"));
        }

        /// <summary>
        /// Verifies missing import metadata still uses import wording when raw cache already exists.
        /// </summary>
        [Test]
        public void BuildViewModel_UsesImportActionWhenCacheExistsButImportIsMissing()
        {
            var report = new StatusReport(
                Array.Empty<ManifestDiscoveryIssue>(),
                new[]
                {
                    CreateStatus(DependencyState.Missing, hasRawCache: true)
                });

            var viewModel = AutoPromptWindow.BuildViewModel(report);

            Assert.That(viewModel.PrimaryActionLabel, Is.EqualTo("Import"));
            Assert.That(viewModel.PrimaryActionTooltip, Does.Contain("shared cache"));
        }

        /// <summary>
        /// Verifies multi-repository prompt falls back to count plus explicit repo list.
        /// </summary>
        [Test]
        public void BuildViewModel_UsesMultiRepositorySummaryWhenSeveralModelsNeedAttention()
        {
            var report = new StatusReport(
                new[]
                {
                    new ManifestDiscoveryIssue("project", "manifest", "Issue")
                },
                new[]
                {
                    CreateStatus(DependencyState.Missing, "alpha/model-a"),
                    CreateStatus(DependencyState.Stale, "beta/model-b")
                });

            var viewModel = AutoPromptWindow.BuildViewModel(report);

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

        private static DependencyStatus CreateStatus(
            DependencyState state,
            string repository = "doji/test-model",
            bool hasRawCache = false)
        {
            var dependency = new DiscoveredModelDependency(
                "com.doji.test",
                "Test Package",
                "1.0.0",
                "/tmp/com.doji.test",
                "/tmp/com.doji.test/hf-hub-manifest.json",
                "manifesthash",
                new ModelDependencyDefinition(
                    repository,
                    "main"));

            return new DependencyStatus(
                dependency,
                state,
                state == DependencyState.Missing ? DependencyReasonCode.ImportBindingMissing : DependencyReasonCode.ImportBindingStale,
                state.ToString(),
                hasRawCache ? "/tmp/cache/refs/main" : null,
                hasRawCache ? "/tmp/cache/snapshots/commit" : null,
                hasRawCache ? "/tmp/cache/snapshots/commit/.hf-hub.json" : null,
                hasRawCache ? new RawCacheMetadata
                {
                    Repository = repository,
                    RequestedRevision = "main",
                    ResolvedCommitHash = "commit",
                    CachedSnapshotRoot = "/tmp/cache/snapshots/commit",
                    Files = Array.Empty<RawCacheFileMetadata>()
                } : null,
                null,
                null,
                null);
        }
    }
}
