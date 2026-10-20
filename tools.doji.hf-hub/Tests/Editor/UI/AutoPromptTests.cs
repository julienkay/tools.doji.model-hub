using System;
using System.IO;
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
            Assert.That(viewModel.Dependents[0].DisplayLabel, Is.EqualTo("tools.doji.test"));
            Assert.That(viewModel.Dependents[0].PackageDisplayName, Is.EqualTo("Test Package"));
            Assert.That(viewModel.Details, Is.EqualTo(string.Empty));
            Assert.That(viewModel.PrimaryActionLabel, Is.EqualTo("Download + Import"));
            Assert.That(viewModel.PrimaryActionTooltip, Does.Contain("Downloads this model"));
        }

        /// <summary>
        /// Verifies stable project owner ids still render as Project in prompt UI.
        /// </summary>
        [Test]
        public void BuildViewModel_UsesProjectLabelForProjectManifestOwners()
        {
            var report = new StatusReport(
                Array.Empty<ManifestDiscoveryIssue>(),
                new[]
                {
                    CreateStatus(
                        DependencyState.Missing,
                        packageId: ProjectManifestDiscovery.GetOwnerId(
                            "5a899f24b6fd4a1ba127ab1a1b267c7e",
                            "Assets/HF Hub/HF Hub Project Manifest.asset",
                            null),
                        packageDisplayName: "HF Hub Project Manifest",
                        packageVersion: "project",
                        packageResolvedPath: "/tmp/project",
                        manifestPath: "/tmp/project/Assets/HF Hub/HF Hub Project Manifest.asset")
                });

            var viewModel = AutoPromptWindow.BuildViewModel(report);

            Assert.That(viewModel.Dependents.Count, Is.EqualTo(1));
            Assert.That(viewModel.Dependents[0].DisplayLabel, Is.EqualTo("HF Hub Project Manifest"));
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
                    new ManifestDiscoveryIssue(
                        ProjectManifestDiscovery.GetOwnerId(
                            "5a899f24b6fd4a1ba127ab1a1b267c7e",
                            "Assets/HF Hub/HF Hub Project Manifest.asset",
                            null),
                        "manifest",
                        "Issue")
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
                "tools.doji.test",
                "tools.doji.test"
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

        /// <summary>
        /// Verifies optional ONNX choices that have not been enabled stay out of startup prompt copy.
        /// </summary>
        [Test]
        public void BuildViewModel_IgnoresSelectionRequiredStatusesForStartupPrompt()
        {
            var report = new StatusReport(
                Array.Empty<ManifestDiscoveryIssue>(),
                new[]
                {
                    CreateStatus(
                        DependencyState.Missing,
                        repository: "alpha/optional-only",
                        reasonCode: DependencyReasonCode.SelectionRequired),
                    CreateStatus(
                        DependencyState.Missing,
                        repository: "beta/required-missing",
                        reasonCode: DependencyReasonCode.RawCacheRevisionRefMissing)
                });

            var viewModel = AutoPromptWindow.BuildViewModel(report);

            Assert.That(viewModel.Headline, Is.EqualTo("AI Model Required"));
            Assert.That(viewModel.Dependents.Count, Is.EqualTo(1));
            Assert.That(viewModel.Dependents[0].Repository, Is.EqualTo("beta/required-missing"));
            Assert.That(viewModel.PrimaryActionLabel, Is.EqualTo("Download + Import"));
        }

        /// <summary>
        /// Verifies startup prompt is suppressed when only unenabled optional ONNX selection is pending.
        /// </summary>
        [Test]
        public void ShouldPrompt_ReturnsFalseWhenOnlyOptionalSelectionIsRequired()
        {
            var report = new StatusReport(
                Array.Empty<ManifestDiscoveryIssue>(),
                new[]
                {
                    CreateStatus(
                        DependencyState.Missing,
                        reasonCode: DependencyReasonCode.SelectionRequired)
                });

            Assert.That(AutoPrompt.ShouldPrompt(report), Is.False);
        }

        /// <summary>
        /// Verifies enabled optional files behave like selected dependencies for startup prompts when missing.
        /// </summary>
        [Test]
        public void ShouldPrompt_ReturnsTrueWhenEnabledOptionalFileIsMissing()
        {
            var report = new StatusReport(
                Array.Empty<ManifestDiscoveryIssue>(),
                new[]
                {
                    CreateStatus(
                        DependencyState.Missing,
                        reasonCode: DependencyReasonCode.RawCacheRevisionRefMissing)
                });

            Assert.That(AutoPrompt.ShouldPrompt(report), Is.True);
        }

        /// <summary>
        /// Verifies conflict-only prompt opens Model Hub instead of offering blocked import work.
        /// </summary>
        [Test]
        public void BuildViewModel_UsesResolveConflictActionWhenOnlyRevisionConflictsNeedAttention()
        {
            var report = new StatusReport(
                Array.Empty<ManifestDiscoveryIssue>(),
                new[]
                {
                    CreateStatus(
                        DependencyState.Failed,
                        repository: "alpha/model-a",
                        reasonCode: DependencyReasonCode.RevisionConflict,
                        revision: "main"),
                    CreateStatus(
                        DependencyState.Failed,
                        repository: "alpha/model-a",
                        reasonCode: DependencyReasonCode.RevisionConflict,
                        revision: "refs/tags/v1")
                });

            var viewModel = AutoPromptWindow.BuildViewModel(report);

            Assert.That(viewModel.Body, Does.Contain("Resolve each repository to one revision"));
            Assert.That(viewModel.PrimaryActionLabel, Is.EqualTo("Resolve Conflict"));
            Assert.That(viewModel.PrimaryActionTooltip, Does.Contain("Open Model Hub"));
        }

        /// <summary>
        /// Verifies conflict-only prompt mentions package-version resolution when owners come from installed packages.
        /// </summary>
        [Test]
        public void BuildViewModel_UsesPackageConflictBodyWhenConflictOwnerIsInstalledPackage()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
            var packageRoot = Path.Combine(projectRoot, "Library", "PackageCache", "tools.doji.pkg@1.2.3");
            var report = new StatusReport(
                Array.Empty<ManifestDiscoveryIssue>(),
                new[]
                {
                    CreateStatus(
                        DependencyState.Failed,
                        repository: "alpha/model-a",
                        reasonCode: DependencyReasonCode.RevisionConflict,
                        revision: "main"),
                    CreateStatus(
                        DependencyState.Failed,
                        repository: "alpha/model-a",
                        reasonCode: DependencyReasonCode.RevisionConflict,
                        revision: "refs/tags/v1",
                        packageId: "tools.doji.pkg",
                        packageDisplayName: "Locked Package",
                        packageVersion: "1.2.3",
                        packageResolvedPath: packageRoot,
                        manifestPath: Path.Combine(packageRoot, "hf-hub-manifest.json"))
                });

            var viewModel = AutoPromptWindow.BuildViewModel(report);

            Assert.That(viewModel.Body, Does.Contain("updating package versions"));
            Assert.That(viewModel.PrimaryActionLabel, Is.EqualTo("Resolve Conflict"));
        }

        /// <summary>
        /// Verifies reminder logging is skipped while the prompt window is already open.
        /// </summary>
        [Test]
        public void ShouldLogReminder_ReturnsFalseWhenAutoPromptWindowIsOpen()
        {
            var window = UnityEngine.ScriptableObject.CreateInstance<AutoPromptWindow>();
            try
            {
                window.ShowUtility();
                var report = new StatusReport(
                    Array.Empty<ManifestDiscoveryIssue>(),
                    new[]
                    {
                        CreateStatus(DependencyState.Missing)
                    });

                Assert.That(AutoPromptWindow.IsOpen(), Is.True);
                Assert.That(AutoPrompt.ShouldLogReminder(report), Is.False);
            }
            finally
            {
                if (AutoPromptWindow.IsOpen())
                {
                    window.Close();
                }

                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        private static DependencyStatus CreateStatus(
            DependencyState state,
            string repository = "doji/test-model",
            bool hasRawCache = false,
            DependencyReasonCode? reasonCode = null,
            string revision = "main",
            string packageId = "tools.doji.test",
            string packageDisplayName = "Test Package",
            string packageVersion = "1.0.0",
            string packageResolvedPath = null,
            string manifestPath = null)
        {
            var dependency = new DiscoveredModelDependency(
                packageId,
                packageDisplayName,
                packageVersion,
                packageResolvedPath ?? $"/tmp/{packageId}",
                manifestPath ?? $"/tmp/{packageId}/hf-hub-manifest.json",
                "manifesthash",
                new ModelDependencyDefinition(
                    repository,
                    revision));

            return new DependencyStatus(
                dependency,
                state,
                reasonCode ?? (state == DependencyState.Stale
                    ? DependencyReasonCode.ImportBindingStale
                    : DependencyReasonCode.ImportBindingMissing),
                state.ToString(),
                hasRawCache ? "/tmp/cache/refs/main" : null,
                hasRawCache ? "/tmp/cache/snapshots/commit" : null,
                hasRawCache ? "/tmp/cache/snapshots/commit/.hf-hub.json" : null,
                hasRawCache ? new RawCacheMetadata
                {
                    Repository = repository,
                    RequestedRevision = revision,
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
