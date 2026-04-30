using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers repository-level aggregation used by the Model Hub window.
    /// </summary>
    internal sealed class ModelHubServiceTests
    {
        private readonly List<string> _tempPaths = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var path in _tempPaths)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            _tempPaths.Clear();
        }

        /// <summary>
        /// Verifies multiple manifest dependents for one repo collapse into one Model Hub item.
        /// </summary>
        [Test]
        public void BuildReport_GroupsDependentsByRepositoryAndRevision()
        {
            var report = new StatusReport(
                Array.Empty<ManifestDiscoveryIssue>(),
                new[]
                {
                    CreateDependencyStatus("com.doji.pkg-a", "Package A", "model-a"),
                    CreateDependencyStatus("com.doji.pkg-b", "Package B", "model-b")
                });

            var hubReport = new ModelHubService().BuildReport(report);

            Assert.That(hubReport.Items.Count, Is.EqualTo(1));
            Assert.That(hubReport.Items[0].Repository, Is.EqualTo("doji/test-model"));
            Assert.That(hubReport.Items[0].Dependents.Count, Is.EqualTo(2));
            Assert.That(hubReport.Items[0].ManifestOwnerCount, Is.EqualTo(2));
            Assert.That(hubReport.Items[0].ImportedFileCount, Is.EqualTo(1));
            Assert.That(hubReport.Items[0].State, Is.EqualTo(ModelHubItemState.Ready));
            Assert.That(hubReport.Items[0].EstimatedCacheSizeBytes, Is.EqualTo(12));
            Assert.That(hubReport.Items[0].RuntimeSizeOnDiskBytes, Is.Null);
        }

        /// <summary>
        /// Verifies runtime registry paths stay attached to each dependent instead of collapsing into one repo-level value.
        /// </summary>
        [Test]
        public void BuildReport_PreservesOwnerSpecificRuntimeRegistryPathsOnDependents()
        {
            var projectOwnerId = ProjectManifestDiscovery.GetOwnerId(
                "5a899f24b6fd4a1ba127ab1a1b267c7e",
                "Assets/HF Hub/HF Hub Project Manifest.asset",
                null);
            var report = new StatusReport(
                Array.Empty<ManifestDiscoveryIssue>(),
                new[]
                {
                    CreateDependencyStatus(projectOwnerId, "Project", "model-a"),
                    CreateDependencyStatus("com.doji.pkg-b", "Package B", "model-b")
                });

            var hubReport = new ModelHubService().BuildReport(report);

            Assert.That(hubReport.Items.Count, Is.EqualTo(1));
            Assert.That(hubReport.Items[0].Dependents.Count, Is.EqualTo(2));
            Assert.That(
                hubReport.Items[0].Dependents[0].ImportedMetadata?.RuntimeRegistryAssetPath,
                Is.EqualTo("Assets/StreamingAssets/hf-hub/registries/com.doji.pkg-b/hf-hub-runtime.json"));
            Assert.That(
                hubReport.Items[0].Dependents[1].ImportedMetadata?.RuntimeRegistryAssetPath,
                Is.EqualTo(ProjectPaths.GetRuntimeRegistryAssetPath(projectOwnerId)));
        }

        /// <summary>
        /// Verifies missing dependencies promote the repo item into action-required state.
        /// </summary>
        [Test]
        public void BuildReport_MarksRepoAsActionRequiredWhenAnyDependentIsMissing()
        {
            var report = new StatusReport(
                Array.Empty<ManifestDiscoveryIssue>(),
                new[]
                {
                    CreateDependencyStatus("com.doji.pkg-a", "Package A", "model-a", DependencyState.Ready),
                    CreateDependencyStatus("com.doji.pkg-b", "Package B", "model-b", DependencyState.Missing)
                });

            var hubReport = new ModelHubService().BuildReport(report);

            Assert.That(hubReport.Items.Count, Is.EqualTo(1));
            Assert.That(hubReport.Items[0].State, Is.EqualTo(ModelHubItemState.ActionRequired));
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
            var dependency = new DiscoveredModelDependency(
                "com.doji.pkg-a",
                "Package A",
                "1.0.0",
                "/tmp/com.doji.pkg-a",
                "/tmp/com.doji.pkg-a/hf-hub-manifest.json",
                "pkg-hash",
                new ModelDependencyDefinition(
                    "doji/test-model",
                    "main"));
            var metadata = new ImportedModelMetadata
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
                    new ImportedRuntimeFileMetadata
                    {
                        RepoPath = "model.onnx",
                        Kind = "sentis",
                        AssetPath = "Assets/StreamingAssets/hf-hub/doji/test-model/model.sentis",
                        RelativePath = "hf-hub/doji/test-model/model.sentis",
                        SizeInBytes = 6
                    }
                }
            };

            var report = new StatusReport(
                Array.Empty<ManifestDiscoveryIssue>(),
                new[]
                {
                    new DependencyStatus(
                        dependency,
                        DependencyState.Ready,
                        DependencyReasonCode.RawCacheRemovedAfterImport,
                        "Generated runtime files are present. Raw cache has been removed from this machine.",
                        null,
                        null,
                        null,
                        null,
                        "Assets/StreamingAssets/hf-hub/doji/test-model/model.sentis",
                        ProjectPaths.GetImportBindingMetadataFilePath("com.doji.pkg-a", "model-a"),
                        metadata)
                });

            var hubReport = new ModelHubService().BuildReport(report);

            Assert.That(hubReport.Items.Count, Is.EqualTo(1));
            Assert.That(hubReport.Items[0].State, Is.EqualTo(ModelHubItemState.Ready));
            Assert.That(hubReport.Items[0].CacheSizeOnDiskBytes, Is.Null);
            Assert.That(hubReport.Items[0].EstimatedCacheSizeBytes, Is.EqualTo(12));
            Assert.That(hubReport.Items[0].RuntimeSizeOnDiskBytes, Is.Null);
        }

        /// <summary>
        /// Verifies persisted pre-download cache estimates fill the repository size field before raw cache exists.
        /// </summary>
        [Test]
        public void BuildReport_UsesPersistedCacheEstimateWhenCacheAndImportMetadataAreMissing()
        {
            var report = new StatusReport(
                Array.Empty<ManifestDiscoveryIssue>(),
                new[]
                {
                    CreateDependencyStatus(
                        "com.doji.pkg-a",
                        "Package A",
                        "model-a",
                        DependencyState.Missing,
                        reasonCode: DependencyReasonCode.RawCacheRevisionRefMissing,
                        maintainerMessage: "Raw cache revision ref is missing.",
                        rawCachedFilePath: null,
                        includeRawMetadata: false)
                });

            var hubReport = new ModelHubService(
                remoteSnapshotSizeEstimateService: new FakeRemoteSnapshotSizeEstimateService(42))
                .BuildReport(report);

            Assert.That(hubReport.Items.Count, Is.EqualTo(1));
            Assert.That(hubReport.Items[0].CacheSizeOnDiskBytes, Is.Null);
            Assert.That(hubReport.Items[0].EstimatedCacheSizeBytes, Is.EqualTo(42));
        }

        /// <summary>
        /// Verifies repository rows become error entries when same repo is declared at multiple requested revisions.
        /// </summary>
        [Test]
        public void BuildReport_AddsRevisionConflictErrorWhenRepositoryExistsAtMultipleRevisions()
        {
            var report = new StatusReport(
                Array.Empty<ManifestDiscoveryIssue>(),
                new[]
                {
                    CreateDependencyStatus(
                        "com.doji.pkg-a",
                        "Package A",
                        "model-a",
                        DependencyState.Failed,
                        revision: "main",
                        reasonCode: DependencyReasonCode.RevisionConflict,
                        maintainerMessage: "Repository revision conflict."),
                    CreateDependencyStatus(
                        "com.doji.pkg-b",
                        "Package B",
                        "model-b",
                        DependencyState.Failed,
                        revision: "refs/pr/7",
                        reasonCode: DependencyReasonCode.RevisionConflict,
                        maintainerMessage: "Repository revision conflict.")
                });

            var hubReport = new ModelHubService().BuildReport(report);

            Assert.That(hubReport.Items.Count, Is.EqualTo(2));
            Assert.That(hubReport.Items[0].State, Is.EqualTo(ModelHubItemState.Error));
            Assert.That(hubReport.Items[1].State, Is.EqualTo(ModelHubItemState.Error));
            Assert.That(hubReport.Items[0].HasRevisionConflict, Is.True);
            Assert.That(hubReport.Items[1].HasRevisionConflict, Is.True);
            Assert.That(hubReport.Items[0].Diagnostics.Count, Is.EqualTo(1));
            Assert.That(hubReport.Items[1].Diagnostics.Count, Is.EqualTo(1));
            Assert.That(hubReport.Items[0].Diagnostics[0].Severity, Is.EqualTo("error"));
            Assert.That(hubReport.Items[1].Diagnostics[0].Severity, Is.EqualTo("error"));
            Assert.That(hubReport.Items[0].Diagnostics[0].Message, Does.Contain("revision conflict"));
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
            var report = new StatusReport(
                Array.Empty<ManifestDiscoveryIssue>(),
                new[]
                {
                    CreateDependencyStatus(
                        "com.doji.pkg-a",
                        "Package A",
                        "model-a",
                        importedDiagnostics: new[]
                        {
                            new ImportDiagnostic
                            {
                                Severity = "warning",
                                Message = "Partial ONNX conversion: skipped model_bnb4.onnx because unsupported operators remain."
                            }
                        })
                });

            var hubReport = new ModelHubService().BuildReport(report);

            Assert.That(hubReport.Items.Count, Is.EqualTo(1));
            Assert.That(hubReport.Items[0].State, Is.EqualTo(ModelHubItemState.Ready));
            Assert.That(hubReport.Items[0].Diagnostics.Count, Is.EqualTo(1));
            Assert.That(hubReport.Items[0].Diagnostics[0].Severity, Is.EqualTo("warning"));
            Assert.That(hubReport.Items[0].Diagnostics[0].Message, Does.Contain("Partial ONNX conversion"));
            Assert.That(hubReport.Items[0].Diagnostics[0].Message, Does.Contain("model_bnb4.onnx"));
        }

        [Test]
        public void BuildReport_IncludesRawOnnxFileRowsAndProjectOverrideSource()
        {
            var report = new StatusReport(
                Array.Empty<ManifestDiscoveryIssue>(),
                new[]
                {
                    CreateDependencyStatus("com.doji.pkg-a", "Package A", "model-a")
                });

            var hubReport = new ModelHubService(new FakeRecipeSource()).BuildReport(report);

            Assert.That(hubReport.Items.Count, Is.EqualTo(1));
            Assert.That(hubReport.Items[0].Files.Count, Is.EqualTo(1));
            Assert.That(hubReport.Items[0].Files[0].RepoPath, Is.EqualTo("model.onnx"));
            Assert.That(hubReport.Items[0].Files[0].RawCachedFilePath, Is.EqualTo("/cache/doji/test-model/abc123/model.onnx"));
            Assert.That(hubReport.Items[0].Files[0].RecipeSourceLabel, Is.EqualTo("Project Override"));
            Assert.That(hubReport.Items[0].Files[0].Recipe.QuantizationMode, Is.EqualTo("int8"));
        }

        [Test]
        public void BuildReport_BuildsStorageTreemapTilesFromCombinedRawAndRuntimeDiskUsage()
        {
            var rawPath = CreateTempFileWithSize(12);
            var runtimeAssetPath = CreateRuntimeAssetFileWithSize("Assets/StreamingAssets/hf-hub/tests/model.sentis", 7);
            var dependency = CreateDependencyStatus(
                "com.doji.pkg-a",
                "Package A",
                "model-a",
                rawCachedFilePath: rawPath,
                runtimeAssetPath: runtimeAssetPath);
            var report = new StatusReport(Array.Empty<ManifestDiscoveryIssue>(), new[] { dependency });

            var hubReport = new ModelHubService().BuildReport(report);

            Assert.That(hubReport.Items.Count, Is.EqualTo(1));
            Assert.That(hubReport.Items[0].StorageTreemapTiles.Count, Is.EqualTo(1));
            Assert.That(hubReport.Items[0].StorageTreemapTiles[0].RepoPath, Is.EqualTo("model.onnx"));
            Assert.That(hubReport.Items[0].StorageTreemapTiles[0].RawSizeOnDiskBytes, Is.EqualTo(12));
            Assert.That(hubReport.Items[0].StorageTreemapTiles[0].RuntimeSizeOnDiskBytes, Is.EqualTo(7));
            Assert.That(hubReport.Items[0].StorageTreemapTiles[0].TotalSizeOnDiskBytes, Is.EqualTo(19));
        }

        [Test]
        public void BuildReport_SkipsStorageTreemapTilesWhenFilesAreNotOnDisk()
        {
            var report = new StatusReport(
                Array.Empty<ManifestDiscoveryIssue>(),
                new[]
                {
                    CreateDependencyStatus("com.doji.pkg-a", "Package A", "model-a")
                });

            var hubReport = new ModelHubService().BuildReport(report);

            Assert.That(hubReport.Items.Count, Is.EqualTo(1));
            Assert.That(hubReport.Items[0].StorageTreemapTiles, Is.Empty);
        }

        private static DependencyStatus CreateDependencyStatus(
            string packageId,
            string packageDisplayName,
            string modelId,
            DependencyState state = DependencyState.Ready,
            string revision = "main",
            ImportDiagnostic[] importedDiagnostics = null,
            DependencyReasonCode? reasonCode = null,
            string maintainerMessage = null,
            string rawCachedFilePath = "/cache/doji/test-model/abc123/model.onnx",
            string runtimeAssetPath = "Assets/StreamingAssets/hf-hub/doji/test-model/model.sentis",
            bool includeRawMetadata = true)
        {
            var dependency = new DiscoveredModelDependency(
                packageId,
                packageDisplayName,
                "1.0.0",
                $"/tmp/{packageId}",
                $"/tmp/{packageId}/hf-hub-manifest.json",
                $"{packageId}-hash",
                new ModelDependencyDefinition(
                    "doji/test-model",
                    revision));

            var metadata = new ImportedModelMetadata
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
                Diagnostics = importedDiagnostics ?? Array.Empty<ImportDiagnostic>(),
                RuntimeFiles = new[]
                {
                    new ImportedRuntimeFileMetadata
                    {
                        RepoPath = "model.onnx",
                        Kind = "sentis",
                        AssetPath = runtimeAssetPath,
                        RelativePath = "hf-hub/doji/test-model/model.sentis",
                        SizeInBytes = 6
                    }
                }
            };

            return new DependencyStatus(
                dependency,
                state,
                reasonCode ?? (state == DependencyState.Ready ? DependencyReasonCode.None : DependencyReasonCode.ImportBindingMissing),
                maintainerMessage ?? (state == DependencyState.Ready ? "Ready." : "Import binding metadata file is missing."),
                $"/cache/doji/test-model/refs/{revision}",
                includeRawMetadata ? "/cache/doji/test-model/abc123" : null,
                includeRawMetadata ? "/cache/doji/test-model/abc123/snapshot.hfhub.json" : null,
                includeRawMetadata
                    ? new RawCacheMetadata
                    {
                        Repository = "doji/test-model",
                        RequestedRevision = revision,
                        ResolvedCommitHash = "abc123",
                        Files = new[]
                        {
                            new RawCacheFileMetadata
                            {
                                RepoPath = "model.onnx",
                                CachedFilePath = rawCachedFilePath,
                                SizeInBytes = 12
                            }
                        }
                    }
                    : null,
                runtimeAssetPath,
                ProjectPaths.GetImportBindingMetadataFilePath(packageId, modelId),
                state == DependencyState.Ready ? metadata : null);
        }

        private string CreateTempFileWithSize(int sizeInBytes)
        {
            var path = Path.Combine(Path.GetTempPath(), $"hf-hub-modelhub-test-{Guid.NewGuid():N}.bin");
            File.WriteAllBytes(path, new byte[sizeInBytes]);
            _tempPaths.Add(path);
            return path;
        }

        private string CreateRuntimeAssetFileWithSize(string assetPath, int sizeInBytes)
        {
            var absolutePath = ProjectPaths.ToAbsolutePath(assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath) ?? throw new InvalidOperationException("Expected runtime directory."));
            File.WriteAllBytes(absolutePath, new byte[sizeInBytes]);
            _tempPaths.Add(absolutePath);
            return assetPath;
        }
    }

    internal sealed class FakeRecipeSource : IProjectModelRecipeSource
    {
        public EffectiveOnnxConversionRecipe GetEffectiveRecipe(string repository, string repoPath)
        {
            return new EffectiveOnnxConversionRecipe(
                new OnnxConversionRecipe
                {
                    QuantizationMode = "int8"
                },
                "Project Override",
                "Assets/Doji/HfHub/HF Hub Conversion Overrides.asset",
                true);
        }

        public string ComputeRecipeSetHash(string repository, System.Collections.Generic.IEnumerable<string> repoPaths)
        {
            return "fake-recipe-set-hash";
        }

        public void SetProjectOverride(string repository, string repoPath, OnnxConversionRecipe recipe)
        {
        }

        public string EnsureProjectOverride(string repository, string repoPath)
        {
            return "Assets/Doji/HfHub/HF Hub Conversion Overrides.asset";
        }

        public bool RemoveProjectOverride(string repository, string repoPath)
        {
            return true;
        }
    }

    internal sealed class FakeRemoteSnapshotSizeEstimateService : IRemoteSnapshotSizeEstimateService
    {
        private readonly long? _estimatedCacheSizeBytes;

        public FakeRemoteSnapshotSizeEstimateService(long? estimatedCacheSizeBytes)
        {
            _estimatedCacheSizeBytes = estimatedCacheSizeBytes;
        }

        public bool TryGetEstimatedCacheSize(string repository, string requestedRevision, out long? estimatedCacheSizeBytes)
        {
            estimatedCacheSizeBytes = _estimatedCacheSizeBytes;
            return _estimatedCacheSizeBytes.HasValue;
        }

        public void EnsureEstimateLookup(DiscoveredModelDependency dependency, Action onCompleted = null)
        {
        }
    }
}
