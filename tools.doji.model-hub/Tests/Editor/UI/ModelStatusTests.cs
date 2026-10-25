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
        /// A failed import with downloaded files shows an error and offers Retry Import.
        /// </summary>
        [Test]
        public void Error_WithCache_OffersRetryWithErrorTone()
        {
            var status = ModelStatus.From(CreateItem(
                ModelHubItemState.Error,
                DependencyReasonCode.SharedImportMetadataInvalid,
                rawSnapshotRootPath: "/cache/doji/test-model/abc123",
                runtimeFiles: System.Array.Empty<ImportedRuntimeFileMetadata>()));

            Assert.That(status.Kind, Is.EqualTo(ModelStatusKind.Error));
            Assert.That(status.Tone, Is.EqualTo(UiTone.Error));
            Assert.That(status.Action, Is.EqualTo(ModelActionKind.Sync));
            Assert.That(status.ActionLabel, Is.EqualTo("Retry Import"));
        }

        /// <summary>
        /// A model that is not downloaded offers Download & Import.
        /// </summary>
        [Test]
        public void MissingCache_OffersDownloadAndImport()
        {
            var status = ModelStatus.From(CreateItem(
                ModelHubItemState.ActionRequired,
                DependencyReasonCode.RawCacheRevisionRefMissing,
                rawSnapshotRootPath: null,
                runtimeFiles: System.Array.Empty<ImportedRuntimeFileMetadata>()));

            Assert.That(status.Kind, Is.EqualTo(ModelStatusKind.DownloadNeeded));
            Assert.That(status.Label, Is.EqualTo("Not downloaded"));
            Assert.That(status.Tone, Is.EqualTo(UiTone.Info));
            Assert.That(status.ActionLabel, Is.EqualTo("Download & Import"));
        }

        /// <summary>
        /// A downloaded but not imported model offers Import.
        /// </summary>
        [Test]
        public void CachedWithoutImport_OffersImport()
        {
            var status = ModelStatus.From(CreateItem(
                ModelHubItemState.ActionRequired,
                DependencyReasonCode.ImportBindingMissing,
                rawSnapshotRootPath: "/cache/doji/test-model/abc123",
                runtimeFiles: System.Array.Empty<ImportedRuntimeFileMetadata>()));

            Assert.That(status.Kind, Is.EqualTo(ModelStatusKind.ImportNeeded));
            Assert.That(status.ActionLabel, Is.EqualTo("Import"));
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
                runtimeFiles: ImportedFiles));

            Assert.That(status.Kind, Is.EqualTo(ModelStatusKind.UpdateNeeded));
            Assert.That(status.ActionLabel, Is.EqualTo("Update"));
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
            ImportedRuntimeFileMetadata[] runtimeFiles)
        {
            var dependency = new DiscoveredModelDependency(
                "tools.doji.test",
                "Test Package",
                "1.0.0",
                "/tmp/tools.doji.test",
                "/tmp/tools.doji.test/model-hub-manifest.json",
                "manifesthash",
                new ModelDependencyDefinition("doji/test-model", "main"));

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
                        state == ModelHubItemState.Error ? DependencyState.Failed : DependencyState.Ready,
                        reasonCode,
                        "Status message.",
                        "/cache/doji/test-model/refs/main",
                        rawSnapshotRootPath,
                        "/cache/doji/test-model/abc123/snapshot.modelhub.json",
                        null,
                        null,
                        null,
                        null)
                },
                System.Array.Empty<ModelHubDiagnostic>(),
                runtimeFiles,
                System.Array.Empty<ModelHubFileItem>());
        }
    }
}
