using NUnit.Framework;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers stable status-rendering assets used by Model Hub rows.
    /// </summary>
    internal sealed class ModelHubWindowStatusRenderingTests
    {
        /// <summary>
        /// Ensures error and revision-conflict rows use the error icon instead of the info or warning icon.
        /// </summary>
        [Test]
        public void ErrorState_UsesErrorIconAsset()
        {
            Assert.That(ModelHubWindow.ErrorStateIconName, Is.EqualTo("console.erroricon"));
        }

        /// <summary>
        /// Ensures action-required rows use Unity's update-available icon.
        /// </summary>
        [Test]
        public void ActionRequiredState_UsesUpdateAvailableIconAsset()
        {
            Assert.That(ModelHubWindow.UpdateNeededStateIconName, Is.EqualTo("Update-Available@2x"));
        }

        /// <summary>
        /// Ensures failed rows use repair-needed label and red badge color even when cache exists.
        /// </summary>
        [Test]
        public void ErrorState_UsesRepairLabelAndErrorColor()
        {
            var item = CreateItem(
                ModelHubItemState.Error,
                DependencyReasonCode.RawCacheMetadataInvalid,
                rawSnapshotRootPath: "/cache/doji/test-model/abc123",
                runtimeFiles: System.Array.Empty<ImportedRuntimeFileMetadata>());

            Assert.That(ModelHubWindow.GetStatusBadgeLabel(item), Is.EqualTo("Needs Repair"));
            Assert.That(ModelHubWindow.GetStatusBadgeColor(item), Is.EqualTo(new Color(0.68f, 0.30f, 0.28f, 1f)));
        }

        /// <summary>
        /// Ensures download-needed rows use warning color instead of error red.
        /// </summary>
        [Test]
        public void DownloadNeededState_UsesWarningColor()
        {
            var item = CreateItem(
                ModelHubItemState.ActionRequired,
                DependencyReasonCode.RawCacheRevisionRefMissing,
                rawSnapshotRootPath: null,
                runtimeFiles: System.Array.Empty<ImportedRuntimeFileMetadata>());

            Assert.That(ModelHubWindow.GetStatusBadgeLabel(item), Is.EqualTo("Download Needed"));
            Assert.That(ModelHubWindow.GetStatusBadgeColor(item), Is.EqualTo(new Color(0.76f, 0.60f, 0.24f, 1f)));
        }

        /// <summary>
        /// Ensures state icon mapping distinguishes download, import, and update-needed rows.
        /// </summary>
        [Test]
        public void ActionRequiredState_UsesStateSpecificIconAssets()
        {
            var downloadItem = CreateItem(
                ModelHubItemState.ActionRequired,
                DependencyReasonCode.RawCacheRevisionRefMissing,
                rawSnapshotRootPath: null,
                runtimeFiles: System.Array.Empty<ImportedRuntimeFileMetadata>());
            var importItem = CreateItem(
                ModelHubItemState.ActionRequired,
                DependencyReasonCode.ImportBindingMissing,
                rawSnapshotRootPath: "/cache/doji/test-model/abc123",
                runtimeFiles: System.Array.Empty<ImportedRuntimeFileMetadata>());
            var updateItem = CreateItem(
                ModelHubItemState.ActionRequired,
                DependencyReasonCode.RawCacheFilesMissing,
                rawSnapshotRootPath: "/cache/doji/test-model/abc123",
                runtimeFiles: new[]
                {
                    new ImportedRuntimeFileMetadata
                    {
                        RepoPath = "model.onnx",
                        Kind = "sentis",
                        AssetPath = "Assets/StreamingAssets/hf-hub/doji/test-model/model.sentis",
                        RelativePath = "hf-hub/doji/test-model/model.sentis"
                    }
                });

            Assert.That(ModelHubWindow.GetStateIconName(downloadItem), Is.EqualTo("Download-Available@2x"));
            Assert.That(ModelHubWindow.GetStateIconName(importItem), Is.EqualTo("Import-Available@2x"));
            Assert.That(ModelHubWindow.GetStateIconName(updateItem), Is.EqualTo("Update-Available@2x"));
        }

        /// <summary>
        /// Ensures optional-selection rows use a distinct status badge before download starts.
        /// </summary>
        [Test]
        public void SelectionRequiredState_UsesSelectionNeededBadgeLabel()
        {
            var item = CreateItem(
                ModelHubItemState.ActionRequired,
                DependencyReasonCode.SelectionRequired,
                rawSnapshotRootPath: null,
                runtimeFiles: System.Array.Empty<ImportedRuntimeFileMetadata>());

            Assert.That(ModelHubWindow.GetStatusBadgeLabel(item), Is.EqualTo("Selection Needed"));
            Assert.That(ModelHubWindow.GetStateIconName(item), Is.EqualTo("FilterByLabel"));
        }

        /// <summary>
        /// Ensures primary action becomes Configure when no optional ONNX file is enabled yet.
        /// </summary>
        [Test]
        public void PrimaryActionContent_UsesConfigureLabelForSelectionRequiredState()
        {
            var item = CreateItem(
                ModelHubItemState.ActionRequired,
                DependencyReasonCode.SelectionRequired,
                rawSnapshotRootPath: null,
                runtimeFiles: System.Array.Empty<ImportedRuntimeFileMetadata>());

            Assert.That(ModelHubWindow.GetPrimaryActionContent(item, isSyncRunning: false).text, Is.EqualTo("Configure"));
        }

        /// <summary>
        /// Ensures missing-cache rows now advertise the combined download and import action honestly.
        /// </summary>
        [Test]
        public void PrimaryActionContent_UsesDownloadAndImportLabelWhenCacheIsMissing()
        {
            var item = CreateItem(
                ModelHubItemState.ActionRequired,
                DependencyReasonCode.RawCacheRevisionRefMissing,
                rawSnapshotRootPath: null,
                runtimeFiles: System.Array.Empty<ImportedRuntimeFileMetadata>());

            Assert.That(ModelHubWindow.GetPrimaryActionContent(item, isSyncRunning: false).text, Is.EqualTo("Download + Import"));
        }

        private static ModelHubItem CreateItem(
            ModelHubItemState state,
            DependencyReasonCode reasonCode,
            string rawSnapshotRootPath,
            ImportedRuntimeFileMetadata[] runtimeFiles)
        {
            var dependency = new DiscoveredModelDependency(
                "com.doji.test",
                "Test Package",
                "1.0.0",
                "/tmp/com.doji.test",
                "/tmp/com.doji.test/hf-hub-manifest.json",
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
                        "/cache/doji/test-model/abc123/snapshot.hfhub.json",
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
