using System.Linq;
using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers the user-facing status, tone, and next action that every Model Hub surface shows.
    /// </summary>
    internal sealed class ModelStatusTests
    {
        private static readonly ImportedRuntimeFileMetadata[] ImportedFiles =
        {
            new()
            {
                RepoPath = "model.onnx",
                Kind = "sentis",
                Format = "onnx",
                FilePath = "/data/converted/models--doji--test-model/key.sentis"
            }
        };

        /// <summary>
        /// A failed setup with downloaded files shows an error and offers Try Again.
        /// </summary>
        [Test]
        public void Error_WithCache_OffersTryAgainWithErrorTone()
        {
            var status = ModelStatus.From(CreateItem(
                ModelHubItemState.Error,
                DependencyReasonCode.InvalidSelection,
                rawSnapshotRootPath: "/cache/doji/test-model/abc123",
                runtimeFiles: System.Array.Empty<ImportedRuntimeFileMetadata>()));

            Assert.That(status.Kind, Is.EqualTo(ModelStatusKind.Error));
            Assert.That(status.Tone, Is.EqualTo(UiTone.Error));
            Assert.That(status.Action, Is.EqualTo(ModelActionKind.Sync));
            Assert.That(status.ActionLabel, Is.EqualTo("Try Again"));
        }

        /// <summary>
        /// A model that is not downloaded offers Download.
        /// </summary>
        [Test]
        public void MissingCache_OffersDownload()
        {
            var status = ModelStatus.From(CreateItem(
                ModelHubItemState.ActionRequired,
                DependencyReasonCode.RawCacheRevisionRefMissing,
                rawSnapshotRootPath: null,
                runtimeFiles: System.Array.Empty<ImportedRuntimeFileMetadata>()));

            Assert.That(status.Kind, Is.EqualTo(ModelStatusKind.DownloadNeeded));
            Assert.That(status.Label, Is.EqualTo("Not downloaded"));
            Assert.That(status.Tone, Is.EqualTo(UiTone.Info));
            Assert.That(status.ActionLabel, Is.EqualTo("Download"));
        }

        /// <summary>
        /// A downloaded model whose converted models are all in the store is set up automatically, so it shows Setting
        /// up and offers Finish instead of a conversion.
        /// </summary>
        [TestCase(DependencyReasonCode.ImportBindingMissing)]
        [TestCase(DependencyReasonCode.ImportBindingInvalid)]
        [TestCase(DependencyReasonCode.SharedImportMetadataInvalid)]
        [TestCase(DependencyReasonCode.SharedImportMetadataStale)]
        public void CachedWithStoreHits_IsSettingUp(DependencyReasonCode reasonCode)
        {
            var status = ModelStatus.From(CreateItem(
                ModelHubItemState.ActionRequired,
                reasonCode,
                rawSnapshotRootPath: "/cache/doji/test-model/abc123",
                runtimeFiles: System.Array.Empty<ImportedRuntimeFileMetadata>()));

            Assert.That(status.Kind, Is.EqualTo(ModelStatusKind.Preparing));
            Assert.That(status.Label, Is.EqualTo("Setting up"));
            Assert.That(status.ActionLabel, Is.EqualTo("Finish"));
        }

        /// <summary>
        /// A downloaded model with ONNX files to convert asks for a conversion and says what it costs.
        /// </summary>
        [Test]
        public void CachedWithPendingConversion_OffersConvert()
        {
            var status = ModelStatus.From(CreateItem(
                ModelHubItemState.ActionRequired,
                DependencyReasonCode.ImportBindingMissing,
                rawSnapshotRootPath: "/cache/doji/test-model/abc123",
                runtimeFiles: System.Array.Empty<ImportedRuntimeFileMetadata>(),
                pendingConversions: new[] { "model.onnx" },
                conversionCause: ConversionCause.FirstConversion));

            Assert.That(status.Kind, Is.EqualTo(ModelStatusKind.ConversionNeeded));
            Assert.That(status.Label, Is.EqualTo("Needs conversion"));
            Assert.That(status.Action, Is.EqualTo(ModelActionKind.Sync));
            Assert.That(status.ActionLabel, Is.EqualTo("Convert"));
            Assert.That(status.Description, Does.Contain("1 ONNX file needs converting"));
            Assert.That(status.Description, Does.Contain("CPU, memory"));
        }

        /// <summary>
        /// The conversion description names the cause: a new Sentis model format, or changed import settings.
        /// </summary>
        [Test]
        public void ConversionDescription_NamesTheCause()
        {
            var formatChanged = ModelStatus.From(CreateItem(
                ModelHubItemState.ActionRequired,
                DependencyReasonCode.ImportBindingStale,
                "/cache/doji/test-model/abc123",
                ImportedFiles,
                new[] { "a.onnx", "b.onnx" },
                ConversionCause.FormatChanged));
            var settingsChanged = ModelStatus.From(CreateItem(
                ModelHubItemState.ActionRequired,
                DependencyReasonCode.SharedImportMetadataStale,
                "/cache/doji/test-model/abc123",
                ImportedFiles,
                new[] { "a.onnx" },
                ConversionCause.SettingsChanged));

            Assert.That(formatChanged.Kind, Is.EqualTo(ModelStatusKind.ConversionNeeded));
            Assert.That(formatChanged.Description, Does.Contain($"Sentis {StatusService.GetCurrentSentisVersion()} uses a new model format"));
            Assert.That(formatChanged.Description, Does.Contain("2 ONNX files need converting again"));
            Assert.That(settingsChanged.Description, Does.Contain("import settings changed"));
        }

        /// <summary>
        /// The toolbar names the most expensive pending work: downloads, then conversions, then setup.
        /// </summary>
        [Test]
        public void BuildBulkActionLabel_NamesTheMostExpensiveWork()
        {
            var download = ModelStatus.From(CreateItem(ModelHubItemState.ActionRequired, DependencyReasonCode.RawCacheRevisionRefMissing, null, System.Array.Empty<ImportedRuntimeFileMetadata>()));
            var convert = ModelStatus.From(CreateItem(ModelHubItemState.ActionRequired, DependencyReasonCode.ImportBindingMissing, "/cache/doji/test-model/abc123", System.Array.Empty<ImportedRuntimeFileMetadata>(), new[] { "model.onnx" }, ConversionCause.FirstConversion));
            var setUp = ModelStatus.From(CreateItem(ModelHubItemState.ActionRequired, DependencyReasonCode.ImportBindingMissing, "/cache/doji/test-model/abc123", System.Array.Empty<ImportedRuntimeFileMetadata>()));

            Assert.That(ModelStatus.BuildBulkActionLabel(new[] { setUp, convert, download }), Is.EqualTo("Download All"));
            Assert.That(ModelStatus.BuildBulkActionLabel(new[] { setUp, convert }), Is.EqualTo("Convert All"));
            Assert.That(ModelStatus.BuildBulkActionLabel(new[] { setUp }), Is.EqualTo("Set Up All"));
        }

        /// <summary>
        /// Newly selected files that are not downloaded yet offer Update.
        /// </summary>
        [Test]
        public void NewlySelectedFiles_OfferUpdate()
        {
            var status = ModelStatus.From(CreateItem(
                ModelHubItemState.ActionRequired,
                DependencyReasonCode.RawCacheFilesMissing,
                rawSnapshotRootPath: "/cache/doji/test-model/abc123",
                runtimeFiles: ImportedFiles,
                selectedFiles: new[] { "model.onnx", "model_q4.onnx" }));

            Assert.That(status.Kind, Is.EqualTo(ModelStatusKind.UpdateNeeded));
            Assert.That(status.ActionLabel, Is.EqualTo("Update"));
            Assert.That(status.Description, Does.Contain("More files are selected"));
        }

        /// <summary>
        /// A whole-repository model downloaded without a known file list asks to update, and says that the list is
        /// unknown instead of claiming files are missing.
        /// </summary>
        [Test]
        public void UnknownFileList_ExplainsTheMissingList()
        {
            var status = ModelStatus.From(CreateItem(
                ModelHubItemState.ActionRequired,
                DependencyReasonCode.RawCacheFilesMissing,
                rawSnapshotRootPath: "/cache/doji/test-model/abc123",
                runtimeFiles: System.Array.Empty<ImportedRuntimeFileMetadata>()));

            Assert.That(status.Kind, Is.EqualTo(ModelStatusKind.UpdateNeeded));
            Assert.That(status.Description, Does.Contain("file list"));
            Assert.That(status.Description, Does.Not.Contain("More files are selected"));
        }

        /// <summary>
        /// A model with only optional files and none chosen asks the user to choose files.
        /// </summary>
        [Test]
        public void SelectionRequired_AsksToChooseFiles()
        {
            var status = ModelStatus.From(CreateItem(
                ModelHubItemState.ActionRequired,
                DependencyReasonCode.SelectionRequired,
                rawSnapshotRootPath: null,
                runtimeFiles: System.Array.Empty<ImportedRuntimeFileMetadata>()));

            Assert.That(status.Kind, Is.EqualTo(ModelStatusKind.ChooseFiles));
            Assert.That(status.Action, Is.EqualTo(ModelActionKind.ChooseFiles));
            Assert.That(status.Tone, Is.EqualTo(UiTone.Warning));
        }

        /// <summary>
        /// A revision conflict shows an error and offers no import action.
        /// </summary>
        [Test]
        public void RevisionConflict_HasNoAction()
        {
            var status = ModelStatus.From(CreateItem(
                ModelHubItemState.Error,
                DependencyReasonCode.RevisionConflict,
                rawSnapshotRootPath: "/cache/doji/test-model/abc123",
                runtimeFiles: ImportedFiles));

            Assert.That(status.Kind, Is.EqualTo(ModelStatusKind.Conflict));
            Assert.That(status.Action, Is.EqualTo(ModelActionKind.None));
            Assert.That(status.Tone, Is.EqualTo(UiTone.Error));
        }

        /// <summary>
        /// Damaged cache bookkeeping asks for a repair download instead of fixing itself in the background.
        /// </summary>
        [TestCase(DependencyReasonCode.RawCacheRevisionRefUnreadable)]
        public void DamagedCache_OffersRepair(DependencyReasonCode reasonCode)
        {
            var status = ModelStatus.From(CreateItem(
                ModelHubItemState.Error,
                reasonCode,
                rawSnapshotRootPath: "/cache/doji/test-model/abc123",
                runtimeFiles: ImportedFiles));

            Assert.That(status.Kind, Is.EqualTo(ModelStatusKind.RepairNeeded));
            Assert.That(status.Action, Is.EqualTo(ModelActionKind.Sync));
            Assert.That(status.ActionLabel, Is.EqualTo("Repair"));
        }

        /// <summary>
        /// An imported model is Ready and needs no action.
        /// </summary>
        [Test]
        public void Ready_NeedsNoAction()
        {
            var status = ModelStatus.From(CreateItem(
                ModelHubItemState.Ready,
                DependencyReasonCode.None,
                rawSnapshotRootPath: "/cache/doji/test-model/abc123",
                runtimeFiles: ImportedFiles));

            Assert.That(status.Kind, Is.EqualTo(ModelStatusKind.Ready));
            Assert.That(status.Description, Is.EqualTo("Ready to use."));
            Assert.That(status.NeedsAction, Is.False);
            Assert.That(status.Tone, Is.EqualTo(UiTone.Success));
        }

        /// <summary>
        /// The model name drops the owner part of the repository id.
        /// </summary>
        [TestCase("julienkay/sentis-MiDaS", "sentis-MiDaS")]
        [TestCase("gpt2", "gpt2")]
        public void GetModelName_DropsOwner(string repository, string expected)
        {
            Assert.That(ModelStatus.GetModelName(repository), Is.EqualTo(expected));
        }

        private static ModelHubItem CreateItem(
            ModelHubItemState state,
            DependencyReasonCode reasonCode,
            string rawSnapshotRootPath,
            ImportedRuntimeFileMetadata[] runtimeFiles,
            string[] pendingConversions = null,
            ConversionCause conversionCause = ConversionCause.None,
            string[] selectedFiles = null)
        {
            var dependency = new DiscoveredModelDependency(
                "tools.doji.test",
                "Test Package",
                "1.0.0",
                "/tmp/tools.doji.test",
                "/tmp/tools.doji.test/model-hub-manifest.json",
                new ModelDependencyDefinition(
                    "doji/test-model",
                    "main",
                    (selectedFiles ?? System.Array.Empty<string>()).Select(file => new ModelDependencyFileDefinition(file)).ToArray()));

            return new ModelHubItem(
                "doji/test-model",
                "main",
                "abc123",
                rawSnapshotRootPath,
                null,
                null,
                null,
                null,
                null,
                state,
                new[]
                {
                    new DependencyStatus(
                        dependency,
                        state switch
                        {
                            ModelHubItemState.Error => DependencyState.Failed,
                            ModelHubItemState.ActionRequired => DependencyState.Missing,
                            _ => DependencyState.Ready
                        },
                        reasonCode,
                        "Status message.",
                        "/cache/doji/test-model/refs/main",
                        rawSnapshotRootPath,
                        "/cache/doji/test-model/abc123/snapshot.modelhub.json",
                        null,
                        null,
                        null,
                        null,
                        pendingConversions,
                        conversionCause)
                },
                System.Array.Empty<ModelHubDiagnostic>(),
                runtimeFiles,
                System.Array.Empty<ModelHubFileItem>());
        }
    }
}
