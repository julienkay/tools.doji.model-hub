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

            Assert.That(viewModel.Headline, Is.EqualTo("Test Package needs an AI model"));
            Assert.That(viewModel.Body, Does.Contain("downloads it from Hugging Face"));
            Assert.That(viewModel.Body, Does.Contain("converts the model to Sentis"));
            Assert.That(viewModel.Models[0].WillConvert, Is.True, "A model with an unknown file list is assumed to convert.");
            Assert.That(viewModel.Models.Count, Is.EqualTo(1));
            Assert.That(viewModel.Models[0].Repository, Is.EqualTo("doji/test-model"));
            Assert.That(viewModel.Models[0].Owners, Is.EqualTo(new[] { "Test Package" }));
            Assert.That(viewModel.IssueCount, Is.EqualTo(0));
            Assert.That(viewModel.PrimaryActionLabel, Is.EqualTo("Download & Convert"));
            Assert.That(viewModel.PrimaryActionTooltip, Does.Contain("CPU, memory"));
            Assert.That(viewModel.PrimaryAction, Is.EqualTo(AutoPromptWindow.PromptPrimaryAction.StartSync));
        }

        /// <summary>
        /// A model that selects only Sentis files downloads without a conversion, so the prompt does not mention one.
        /// </summary>
        [Test]
        public void BuildViewModel_OffersPlainDownloadWhenNothingConverts()
        {
            var report = new StatusReport(
                Array.Empty<ManifestDiscoveryIssue>(),
                new[]
                {
                    CreateStatus(DependencyState.Missing)
                });
            var onnxSelections = new System.Collections.Generic.Dictionary<string, bool> { ["doji/test-model"] = false };

            var viewModel = AutoPromptWindow.BuildViewModel(report, null, null, onnxSelections);

            Assert.That(viewModel.Models[0].WillConvert, Is.False);
            Assert.That(viewModel.PrimaryActionLabel, Is.EqualTo("Download"));
            Assert.That(viewModel.Body, Does.Not.Contain("convert"));
            Assert.That(viewModel.PrimaryActionTooltip, Does.Not.Contain("convert"));
        }

        /// <summary>
        /// A downloaded model whose converted models are all in the store needs no prompt: AI Model Hub sets it up.
        /// </summary>
        [Test]
        public void ShouldPrompt_ReturnsFalseWhenOnlyCheapSetupIsPending()
        {
            var report = new StatusReport(
                Array.Empty<ManifestDiscoveryIssue>(),
                new[]
                {
                    CreateStatus(DependencyState.Missing, hasRawCache: true),
                    CreateStatus(DependencyState.Stale, repository: "alpha/model-a", hasRawCache: true)
                });

            Assert.That(AutoPrompt.ShouldPrompt(report), Is.False);
            Assert.That(AutoPromptWindow.BuildViewModel(report).Models, Is.Empty);
        }

        /// <summary>
        /// A downloaded model that needs a conversion is listed with a Convert tag and no download size.
        /// </summary>
        [Test]
        public void BuildViewModel_OffersConvertWhenOnlyConversionIsPending()
        {
            var report = new StatusReport(
                Array.Empty<ManifestDiscoveryIssue>(),
                new[]
                {
                    CreateStatus(DependencyState.Missing, hasRawCache: true, pendingConversions: new[] { "model.onnx" }),
                    CreateStatus(DependencyState.Missing, repository: "alpha/cheap", hasRawCache: true)
                });
            var sizes = new System.Collections.Generic.Dictionary<string, long?> { ["doji/test-model"] = 1024L };

            var viewModel = AutoPromptWindow.BuildViewModel(report, sizes);

            Assert.That(AutoPrompt.ShouldPrompt(report), Is.True);
            Assert.That(viewModel.Models.Select(model => model.Repository), Is.EqualTo(new[] { "doji/test-model" }));
            Assert.That(viewModel.Models[0].NeedsDownload, Is.False);
            Assert.That(viewModel.Models[0].WillConvert, Is.True);
            Assert.That(viewModel.PrimaryActionLabel, Is.EqualTo("Convert"), "A conversion shows no download size.");
            Assert.That(viewModel.Body, Does.Contain("already downloaded"));
            Assert.That(viewModel.Body, Does.Contain("CPU, memory"));
        }

        /// <summary>
        /// After a Sentis update with a new model format, the prompt explains why downloaded models convert again.
        /// </summary>
        [Test]
        public void BuildViewModel_ExplainsFormatChange()
        {
            var report = new StatusReport(
                Array.Empty<ManifestDiscoveryIssue>(),
                new[]
                {
                    CreateStatus(DependencyState.Stale, hasRawCache: true, pendingConversions: new[] { "model.onnx" }, conversionCause: ConversionCause.FormatChanged),
                    CreateStatus(DependencyState.Stale, repository: "alpha/model-a", hasRawCache: true, pendingConversions: new[] { "model.onnx" }, conversionCause: ConversionCause.FormatChanged)
                });

            var viewModel = AutoPromptWindow.BuildViewModel(report);

            Assert.That(viewModel.PrimaryActionLabel, Is.EqualTo("Convert All"));
            Assert.That(viewModel.Body, Does.Contain($"Sentis {StatusService.GetCurrentSentisVersion()} uses a new model format"));
            Assert.That(viewModel.Body, Does.Contain("they need converting again"));
        }

        /// <summary>
        /// Changed import settings make downloaded models convert again, and the prompt says so.
        /// </summary>
        [Test]
        public void BuildViewModel_ExplainsSettingsChange()
        {
            var report = new StatusReport(
                Array.Empty<ManifestDiscoveryIssue>(),
                new[]
                {
                    CreateStatus(DependencyState.Stale, hasRawCache: true, pendingConversions: new[] { "model.onnx" }, conversionCause: ConversionCause.SettingsChanged)
                });

            var viewModel = AutoPromptWindow.BuildViewModel(report);

            Assert.That(viewModel.Body, Does.Contain("import settings changed, so it needs converting again"));
        }

        /// <summary>
        /// Verifies each model row carries its license link, and a model whose card is unknown links the model card.
        /// </summary>
        [Test]
        public void BuildViewModel_AddsLicenseLinks()
        {
            var report = new StatusReport(
                Array.Empty<ManifestDiscoveryIssue>(),
                new[]
                {
                    CreateStatus(DependencyState.Missing)
                });
            var licenseLinks = new System.Collections.Generic.Dictionary<string, ModelLink>
            {
                ["doji/test-model"] = ModelLinks.GetLicenseLink("doji/test-model", "abc123", new ModelCardMetadata { License = "mit" }, new[] { "LICENSE" })
            };

            var withCard = AutoPromptWindow.BuildViewModel(report, null, licenseLinks);
            var withoutCard = AutoPromptWindow.BuildViewModel(report);

            Assert.That(withCard.Models[0].LicenseLink.Label, Is.EqualTo("License: mit"));
            Assert.That(withCard.Models[0].LicenseLink.Url, Is.EqualTo("https://huggingface.co/doji/test-model/blob/abc123/LICENSE"));
            Assert.That(withoutCard.Models[0].LicenseLink.Label, Is.EqualTo("License"));
            Assert.That(withoutCard.Models[0].LicenseLink.Url, Is.EqualTo("https://huggingface.co/doji/test-model"));
        }

        /// <summary>
        /// Files selected after the first download still need a download, so the prompt does not offer a plain Convert.
        /// </summary>
        [Test]
        public void BuildViewModel_TreatsMissingCacheFilesAsDownload()
        {
            var report = new StatusReport(
                Array.Empty<ManifestDiscoveryIssue>(),
                new[]
                {
                    CreateStatus(DependencyState.Missing, hasRawCache: true, reasonCode: DependencyReasonCode.RawCacheFilesMissing)
                });

            var viewModel = AutoPromptWindow.BuildViewModel(report);

            Assert.That(viewModel.Models[0].NeedsDownload, Is.True);
            Assert.That(viewModel.PrimaryActionLabel, Is.EqualTo("Download & Convert"));
        }

        /// <summary>
        /// Damaged cache needs a fresh download, so the prompt does not offer a plain Convert.
        /// </summary>
        [Test]
        public void BuildViewModel_TreatsDamagedCacheAsDownload()
        {
            var report = new StatusReport(
                Array.Empty<ManifestDiscoveryIssue>(),
                new[]
                {
                    CreateStatus(DependencyState.Failed, hasRawCache: true, reasonCode: DependencyReasonCode.RawCacheRevisionRefUnreadable)
                });

            var viewModel = AutoPromptWindow.BuildViewModel(report);

            Assert.That(viewModel.Models[0].NeedsDownload, Is.True);
            Assert.That(viewModel.PrimaryActionLabel, Is.EqualTo("Download & Convert"));
        }

        /// <summary>
        /// The primary button shows the total download size when every size is known.
        /// </summary>
        [Test]
        public void BuildViewModel_ShowsTotalDownloadSizeOnPrimaryAction()
        {
            var report = new StatusReport(
                Array.Empty<ManifestDiscoveryIssue>(),
                new[]
                {
                    CreateStatus(DependencyState.Missing, "alpha/model-a"),
                    CreateStatus(DependencyState.Missing, "beta/model-b")
                });
            var sizes = new System.Collections.Generic.Dictionary<string, long?>
            {
                ["alpha/model-a"] = 1024L * 1024L,
                ["beta/model-b"] = 2L * 1024L * 1024L
            };

            var viewModel = AutoPromptWindow.BuildViewModel(report, sizes);

            Assert.That(viewModel.PrimaryActionLabel, Does.StartWith("Download & Convert All"));
            Assert.That(viewModel.PrimaryActionLabel, Does.EndWith("3 MB"));
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
                            "Assets/AI Model Hub/AI Model Hub Project Manifest.asset",
                            null),
                        packageDisplayName: "AI Model Hub Project Manifest",
                        packageVersion: "project",
                        packageResolvedPath: "/tmp/project",
                        manifestPath: "/tmp/project/Assets/AI Model Hub/AI Model Hub Project Manifest.asset")
                });

            var viewModel = AutoPromptWindow.BuildViewModel(report);

            Assert.That(viewModel.Models.Count, Is.EqualTo(1));
            Assert.That(viewModel.Models[0].Owners, Is.EqualTo(new[] { "AI Model Hub Project Manifest" }));
        }

        /// <summary>
        /// A mix of downloads and conversions shows the download size of the downloads only.
        /// </summary>
        [Test]
        public void BuildViewModel_SumsOnlyDownloadSizesWhenConversionsArePending()
        {
            var report = new StatusReport(
                Array.Empty<ManifestDiscoveryIssue>(),
                new[]
                {
                    CreateStatus(DependencyState.Missing, "alpha/model-a"),
                    CreateStatus(DependencyState.Missing, "beta/model-b", hasRawCache: true, pendingConversions: new[] { "model.onnx" })
                });
            var sizes = new System.Collections.Generic.Dictionary<string, long?>
            {
                ["alpha/model-a"] = 1024L * 1024L,
                ["beta/model-b"] = 5L * 1024L * 1024L
            };

            var viewModel = AutoPromptWindow.BuildViewModel(report, sizes);

            Assert.That(viewModel.PrimaryActionLabel, Does.StartWith("Download & Convert All"));
            Assert.That(viewModel.PrimaryActionLabel, Does.EndWith("1 MB"));
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
                            "Assets/AI Model Hub/AI Model Hub Project Manifest.asset",
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

            Assert.That(viewModel.Headline, Is.EqualTo("Test Package needs 2 AI models"));
            Assert.That(viewModel.Models.Select(entry => entry.Repository).ToArray(), Is.EqualTo(new[]
            {
                "alpha/model-a",
                "beta/model-b"
            }));
            Assert.That(viewModel.IssueCount, Is.EqualTo(1));
            Assert.That(viewModel.PrimaryActionLabel, Is.EqualTo("Download & Convert All"));
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

            Assert.That(viewModel.Headline, Is.EqualTo("Test Package needs an AI model"));
            Assert.That(viewModel.Models.Count, Is.EqualTo(1));
            Assert.That(viewModel.Models[0].Repository, Is.EqualTo("beta/required-missing"));
            Assert.That(viewModel.PrimaryActionLabel, Is.EqualTo("Download & Convert"));
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

            Assert.That(viewModel.Headline, Is.EqualTo("Model revisions conflict"));
            Assert.That(viewModel.Models.Count, Is.EqualTo(1));
            Assert.That(viewModel.Models[0].IsConflict, Is.True);
            Assert.That(viewModel.Body, Does.Contain("use one revision"));
            Assert.That(viewModel.PrimaryAction, Is.EqualTo(AutoPromptWindow.PromptPrimaryAction.OpenModelHub));
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
                        manifestPath: Path.Combine(packageRoot, "model-hub-manifest.json"))
                });

            var viewModel = AutoPromptWindow.BuildViewModel(report);

            Assert.That(viewModel.Body, Does.Contain("Update a package"));
            Assert.That(viewModel.PrimaryAction, Is.EqualTo(AutoPromptWindow.PromptPrimaryAction.OpenModelHub));
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
            string manifestPath = null,
            string[] pendingConversions = null,
            ConversionCause conversionCause = ConversionCause.FirstConversion)
        {
            var dependency = new DiscoveredModelDependency(
                packageId,
                packageDisplayName,
                packageVersion,
                packageResolvedPath ?? $"/tmp/{packageId}",
                manifestPath ?? $"/tmp/{packageId}/model-hub-manifest.json",
                "manifesthash",
                new ModelDependencyDefinition(
                    repository,
                    revision));

            return new DependencyStatus(
                dependency,
                state,
                reasonCode ?? (!hasRawCache
                    ? DependencyReasonCode.RawCacheRevisionRefMissing
                    : state == DependencyState.Stale
                        ? DependencyReasonCode.ImportBindingStale
                        : DependencyReasonCode.ImportBindingMissing),
                state.ToString(),
                hasRawCache ? "/tmp/cache/refs/main" : null,
                hasRawCache ? "/tmp/cache/snapshots/commit" : null,
                hasRawCache ? "/tmp/cache/snapshots/commit/.model-hub.json" : null,
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
                null,
                pendingConversions,
                conversionCause);
        }
    }
}
