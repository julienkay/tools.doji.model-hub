using System;
using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers repository-level aggregation used by the Model Hub window.
    /// </summary>
    internal sealed class HfHubModelHubServiceTests
    {
        /// <summary>
        /// Verifies multiple manifest dependents for one repo collapse into one Model Hub item.
        /// </summary>
        [Test]
        public void BuildReport_GroupsDependentsByRepositoryAndRevision()
        {
            var report = new HfHubStatusReport(
                Array.Empty<HfHubManifestDiscoveryIssue>(),
                new[]
                {
                    CreateDependencyStatus("com.doji.pkg-a", "Package A", "model-a"),
                    CreateDependencyStatus("com.doji.pkg-b", "Package B", "model-b")
                });

            var hubReport = new HfHubModelHubService().BuildReport(report);

            Assert.That(hubReport.Items.Count, Is.EqualTo(1));
            Assert.That(hubReport.Items[0].Repository, Is.EqualTo("doji/test-model"));
            Assert.That(hubReport.Items[0].Dependents.Count, Is.EqualTo(2));
            Assert.That(hubReport.Items[0].ManifestOwnerCount, Is.EqualTo(2));
            Assert.That(hubReport.Items[0].ImportedFileCount, Is.EqualTo(1));
            Assert.That(hubReport.Items[0].State, Is.EqualTo(HfHubModelHubItemState.Ready));
            Assert.That(hubReport.Items[0].EstimatedCacheSizeBytes, Is.EqualTo(12));
            Assert.That(hubReport.Items[0].EstimatedRuntimeSizeBytes, Is.EqualTo(6));
        }

        /// <summary>
        /// Verifies runtime registry paths stay attached to each dependent instead of collapsing into one repo-level value.
        /// </summary>
        [Test]
        public void BuildReport_PreservesOwnerSpecificRuntimeRegistryPathsOnDependents()
        {
            var report = new HfHubStatusReport(
                Array.Empty<HfHubManifestDiscoveryIssue>(),
                new[]
                {
                    CreateDependencyStatus("project", "Project", "model-a"),
                    CreateDependencyStatus("com.doji.pkg-b", "Package B", "model-b")
                });

            var hubReport = new HfHubModelHubService().BuildReport(report);

            Assert.That(hubReport.Items.Count, Is.EqualTo(1));
            Assert.That(hubReport.Items[0].Dependents.Count, Is.EqualTo(2));
            Assert.That(
                hubReport.Items[0].Dependents[0].ImportedMetadata?.RuntimeRegistryAssetPath,
                Is.EqualTo("Assets/StreamingAssets/hf-hub/registries/com.doji.pkg-b/hf-hub-runtime.json"));
            Assert.That(
                hubReport.Items[0].Dependents[1].ImportedMetadata?.RuntimeRegistryAssetPath,
                Is.EqualTo("Assets/StreamingAssets/hf-hub/registries/project/hf-hub-runtime.json"));
        }

        /// <summary>
        /// Verifies missing dependencies promote the repo item into action-required state.
        /// </summary>
        [Test]
        public void BuildReport_MarksRepoAsActionRequiredWhenAnyDependentIsMissing()
        {
            var report = new HfHubStatusReport(
                Array.Empty<HfHubManifestDiscoveryIssue>(),
                new[]
                {
                    CreateDependencyStatus("com.doji.pkg-a", "Package A", "model-a", HfHubDependencyState.Ready),
                    CreateDependencyStatus("com.doji.pkg-b", "Package B", "model-b", HfHubDependencyState.Missing)
                });

            var hubReport = new HfHubModelHubService().BuildReport(report);

            Assert.That(hubReport.Items.Count, Is.EqualTo(1));
            Assert.That(hubReport.Items[0].State, Is.EqualTo(HfHubModelHubItemState.ActionRequired));
            Assert.That(hubReport.Items[0].Diagnostics.Count, Is.EqualTo(1));
            Assert.That(hubReport.Items[0].Diagnostics[0].Severity, Is.EqualTo("warning"));
            Assert.That(hubReport.Items[0].Diagnostics[0].Message, Does.Contain("Model needs import into this project."));
        }

        /// <summary>
        /// Verifies cache size estimate survives when imported files remain but raw cache metadata is gone.
        /// </summary>
        [Test]
        public void BuildReport_UsesImportMetadataCacheSizeWhenRawCacheWasRemoved()
        {
            var dependency = new HfHubDiscoveredModelDependency(
                "com.doji.pkg-a",
                "Package A",
                "1.0.0",
                "/tmp/com.doji.pkg-a",
                "/tmp/com.doji.pkg-a/hf-hub-manifest.json",
                "pkg-hash",
                new HfHubModelDependencyDefinition(
                    "doji/test-model",
                    "main"));
            var metadata = new HfHubImportedModelMetadata
            {
                PackageId = "com.doji.pkg-a",
                PackageVersion = "1.0.0",
                ModelId = "model-a",
                ManifestHash = "pkg-hash",
                Repository = "doji/test-model",
                RequestedRevision = "main",
                ResolvedCommitHash = "abc123",
                RawSnapshotRootPath = "/cache/doji/test-model/abc123",
                RawMetadataPath = "/cache/doji/test-model/abc123/snapshot.hfhub.json",
                RawCacheSizeInBytes = 12,
                RuntimeFiles = new[]
                {
                    new HfHubImportedRuntimeFileMetadata
                    {
                        RepoPath = "model.onnx",
                        Kind = "sentis",
                        AssetPath = "Assets/StreamingAssets/hf-hub/doji/test-model/model.sentis",
                        RelativePath = "hf-hub/doji/test-model/model.sentis",
                        SizeInBytes = 6
                    }
                }
            };

            var report = new HfHubStatusReport(
                Array.Empty<HfHubManifestDiscoveryIssue>(),
                new[]
                {
                    new HfHubDependencyStatus(
                        dependency,
                        HfHubDependencyState.Ready,
                        HfHubDependencyReasonCode.RawCacheRemovedAfterImport,
                        "Generated runtime files are present. Raw cache has been removed from this machine.",
                        null,
                        null,
                        null,
                        null,
                        "Assets/StreamingAssets/hf-hub/doji/test-model/model.sentis",
                        HfHubProjectPaths.GetImportBindingMetadataFilePath("com.doji.pkg-a", "model-a"),
                        metadata)
                });

            var hubReport = new HfHubModelHubService().BuildReport(report);

            Assert.That(hubReport.Items.Count, Is.EqualTo(1));
            Assert.That(hubReport.Items[0].State, Is.EqualTo(HfHubModelHubItemState.Ready));
            Assert.That(hubReport.Items[0].CacheSizeOnDiskBytes, Is.Null);
            Assert.That(hubReport.Items[0].EstimatedCacheSizeBytes, Is.EqualTo(12));
            Assert.That(hubReport.Items[0].EstimatedRuntimeSizeBytes, Is.EqualTo(6));
        }

        /// <summary>
        /// Verifies repository rows warn when same repo is declared at multiple requested revisions.
        /// </summary>
        [Test]
        public void BuildReport_AddsRevisionAmbiguityWarningWhenRepositoryExistsAtMultipleRevisions()
        {
            var report = new HfHubStatusReport(
                Array.Empty<HfHubManifestDiscoveryIssue>(),
                new[]
                {
                    CreateDependencyStatus("com.doji.pkg-a", "Package A", "model-a", revision: "main"),
                    CreateDependencyStatus("com.doji.pkg-b", "Package B", "model-b", revision: "refs/pr/7")
                });

            var hubReport = new HfHubModelHubService().BuildReport(report);

            Assert.That(hubReport.Items.Count, Is.EqualTo(2));
            Assert.That(hubReport.Items[0].State, Is.EqualTo(HfHubModelHubItemState.Ready));
            Assert.That(hubReport.Items[1].State, Is.EqualTo(HfHubModelHubItemState.Ready));
            Assert.That(hubReport.Items[0].Diagnostics.Count, Is.EqualTo(1));
            Assert.That(hubReport.Items[1].Diagnostics.Count, Is.EqualTo(1));
            Assert.That(hubReport.Items[0].Diagnostics[0].Severity, Is.EqualTo("warning"));
            Assert.That(hubReport.Items[1].Diagnostics[0].Severity, Is.EqualTo("warning"));
            Assert.That(hubReport.Items[0].Diagnostics[0].Message, Does.Contain("revision ambiguity"));
            Assert.That(hubReport.Items[0].Diagnostics[0].Message, Does.Contain("main"));
            Assert.That(hubReport.Items[0].Diagnostics[0].Message, Does.Contain("refs/pr/7"));
            Assert.That(hubReport.Items[1].Diagnostics[0].Message, Is.EqualTo(hubReport.Items[0].Diagnostics[0].Message));
        }

        /// <summary>
        /// Verifies ready dependencies can still surface persisted import warnings in Model Hub diagnostics.
        /// </summary>
        [Test]
        public void BuildReport_IncludesPersistedImportWarningsForReadyRepositoryItems()
        {
            var report = new HfHubStatusReport(
                Array.Empty<HfHubManifestDiscoveryIssue>(),
                new[]
                {
                    CreateDependencyStatus(
                        "com.doji.pkg-a",
                        "Package A",
                        "model-a",
                        importedDiagnostics: new[]
                        {
                            new HfHubImportDiagnostic
                            {
                                Severity = "warning",
                                Message = "Partial ONNX conversion: skipped model_bnb4.onnx because unsupported operators remain."
                            }
                        })
                });

            var hubReport = new HfHubModelHubService().BuildReport(report);

            Assert.That(hubReport.Items.Count, Is.EqualTo(1));
            Assert.That(hubReport.Items[0].State, Is.EqualTo(HfHubModelHubItemState.Ready));
            Assert.That(hubReport.Items[0].Diagnostics.Count, Is.EqualTo(1));
            Assert.That(hubReport.Items[0].Diagnostics[0].Severity, Is.EqualTo("warning"));
            Assert.That(hubReport.Items[0].Diagnostics[0].Message, Does.Contain("Partial ONNX conversion"));
            Assert.That(hubReport.Items[0].Diagnostics[0].Message, Does.Contain("model_bnb4.onnx"));
        }

        private static HfHubDependencyStatus CreateDependencyStatus(
            string packageId,
            string packageDisplayName,
            string modelId,
            HfHubDependencyState state = HfHubDependencyState.Ready,
            string revision = "main",
            HfHubImportDiagnostic[] importedDiagnostics = null)
        {
            var dependency = new HfHubDiscoveredModelDependency(
                packageId,
                packageDisplayName,
                "1.0.0",
                $"/tmp/{packageId}",
                $"/tmp/{packageId}/hf-hub-manifest.json",
                $"{packageId}-hash",
                new HfHubModelDependencyDefinition(
                    "doji/test-model",
                    revision));

            var metadata = new HfHubImportedModelMetadata
            {
                PackageId = packageId,
                PackageVersion = "1.0.0",
                ModelId = modelId,
                ManifestHash = $"{packageId}-hash",
                Repository = "doji/test-model",
                RequestedRevision = revision,
                ResolvedCommitHash = "abc123",
                RawSnapshotRootPath = "/cache/doji/test-model/abc123",
                RawMetadataPath = "/cache/doji/test-model/abc123/snapshot.hfhub.json",
                RawCacheSizeInBytes = 12,
                RuntimeRegistryAssetPath = $"Assets/StreamingAssets/hf-hub/registries/{packageId}/hf-hub-runtime.json",
                Diagnostics = importedDiagnostics ?? Array.Empty<HfHubImportDiagnostic>(),
                RuntimeFiles = new[]
                {
                    new HfHubImportedRuntimeFileMetadata
                    {
                        RepoPath = "model.onnx",
                        Kind = "sentis",
                        AssetPath = "Assets/StreamingAssets/hf-hub/doji/test-model/model.sentis",
                        RelativePath = "hf-hub/doji/test-model/model.sentis",
                        SizeInBytes = 6
                    }
                }
            };

            return new HfHubDependencyStatus(
                dependency,
                state,
                state == HfHubDependencyState.Ready ? HfHubDependencyReasonCode.None : HfHubDependencyReasonCode.ImportBindingMissing,
                state == HfHubDependencyState.Ready ? "Ready." : "Import binding metadata file is missing.",
                $"/cache/doji/test-model/refs/{revision}",
                "/cache/doji/test-model/abc123",
                "/cache/doji/test-model/abc123/snapshot.hfhub.json",
                new HfHubRawCacheMetadata
                {
                    Repository = "doji/test-model",
                    RequestedRevision = revision,
                    ResolvedCommitHash = "abc123",
                    Files = new[]
                    {
                        new HfHubRawCacheFileMetadata
                        {
                            RepoPath = "model.onnx",
                            CachedFilePath = "/cache/doji/test-model/abc123/model.onnx",
                            SizeInBytes = 12
                        }
                    }
                },
                "Assets/StreamingAssets/hf-hub/doji/test-model/model.sentis",
                HfHubProjectPaths.GetImportBindingMetadataFilePath(packageId, modelId),
                state == HfHubDependencyState.Ready ? metadata : null);
        }
    }
}
