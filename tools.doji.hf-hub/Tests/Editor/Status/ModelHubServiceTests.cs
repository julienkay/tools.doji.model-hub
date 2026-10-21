using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers repository-level aggregation used by the Model Hub window.
    /// </summary>
    internal sealed class ModelHubServiceTests
    {
        private readonly List<string> _tempPaths = new();
        private TestProjectStateScope _projectScope;

        [SetUp]
        public void SetUp()
        {
            _projectScope = new TestProjectStateScope();
        }

        [TearDown]
        public void TearDown()
        {
            _projectScope?.Dispose();
            _projectScope = null;

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
                    CreateDependencyStatus("tools.doji.pkg-a", "Package A", "model-a"),
                    CreateDependencyStatus("tools.doji.pkg-b", "Package B", "model-b")
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
                    CreateDependencyStatus("tools.doji.pkg-b", "Package B", "model-b")
                });

            var hubReport = new ModelHubService().BuildReport(report);

            Assert.That(hubReport.Items.Count, Is.EqualTo(1));
            Assert.That(hubReport.Items[0].Dependents.Count, Is.EqualTo(2));
            Assert.That(
                hubReport.Items[0].Dependents[0].ImportedMetadata?.RuntimeRegistryAssetPath,
                Is.EqualTo("Assets/StreamingAssets/hf-hub/registries/tools.doji.pkg-b/hf-hub-runtime.json"));
            Assert.That(
                hubReport.Items[0].Dependents[1].ImportedMetadata?.RuntimeRegistryAssetPath,
                Is.EqualTo(ProjectPaths.GetRuntimeRegistryAssetPath(projectOwnerId)));
        }

        /// <summary>
        /// Verifies manifest owner count reflects distinct manifest sources, including Project Settings manifests.
        /// </summary>
        [Test]
        public void BuildReport_CountsDistinctManifestSourcesForManifestOwners()
        {
            var report = new StatusReport(
                Array.Empty<ManifestDiscoveryIssue>(),
                new[]
                {
                    CreateDependencyStatus(
                        ProjectManifestDiscovery.ProjectSettingsManifestOwnerId,
                        "Project Settings",
                        "model-a",
                        manifestPath: "/tmp/HF Hub/ProjectSettings/hf-hub-manifest.json"),
                    CreateDependencyStatus(
                        "tools.doji.pkg-b",
                        "Package B",
                        "model-b",
                        manifestPath: "/tmp/HF Hub/Packages/tools.doji.pkg-b/hf-hub-manifest.json")
                });

            var hubReport = new ModelHubService().BuildReport(report);

            Assert.That(hubReport.Items.Count, Is.EqualTo(1));
            Assert.That(hubReport.Items[0].ManifestOwnerCount, Is.EqualTo(2));
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
                    CreateDependencyStatus("tools.doji.pkg-a", "Package A", "model-a", DependencyState.Ready),
                    CreateDependencyStatus("tools.doji.pkg-b", "Package B", "model-b", DependencyState.Missing)
                });

            var hubReport = new ModelHubService().BuildReport(report);

            Assert.That(hubReport.Items.Count, Is.EqualTo(1));
            Assert.That(hubReport.Items[0].State, Is.EqualTo(ModelHubItemState.ActionRequired));
            Assert.That(hubReport.Items[0].Diagnostics.Count, Is.EqualTo(1));
            Assert.That(hubReport.Items[0].Diagnostics[0].Severity, Is.EqualTo("warning"));
            Assert.That(hubReport.Items[0].Diagnostics[0].Message, Does.Contain("Model needs import into this project."));
        }

        /// <summary>
        /// Verifies selection-required dependencies surface a configure-first diagnostic instead of download-needed text.
        /// </summary>
        [Test]
        public void BuildReport_ShowsSelectionRequiredDiagnosticWhenOptionalOnnxNeedsOwnerChoice()
        {
            var report = new StatusReport(
                Array.Empty<ManifestDiscoveryIssue>(),
                new[]
                {
                    CreateDependencyStatus(
                        "tools.doji.pkg-a",
                        "Package A",
                        "model-a",
                        DependencyState.Missing,
                        reasonCode: DependencyReasonCode.SelectionRequired,
                        maintainerMessage: "This dependency has optional ONNX files, but none are currently enabled for this project owner.",
                        fileEntries: new[]
                        {
                            new ModelDependencyFileDefinition("model.onnx", true),
                            new ModelDependencyFileDefinition("model_q4.onnx", true)
                        },
                        includeRawMetadata: false)
                });

            var hubReport = new ModelHubService().BuildReport(report);

            Assert.That(hubReport.Items.Count, Is.EqualTo(1));
            Assert.That(hubReport.Items[0].State, Is.EqualTo(ModelHubItemState.ActionRequired));
            Assert.That(hubReport.Items[0].Diagnostics.Count, Is.EqualTo(1));
            Assert.That(hubReport.Items[0].Diagnostics[0].Severity, Is.EqualTo("warning"));
            Assert.That(hubReport.Items[0].Diagnostics[0].Message, Is.EqualTo("Choose at least one optional ONNX file for this manifest owner before downloading or importing."));
        }

        /// <summary>
        /// Verifies when a second owner later broadens same-repository selection, Model Hub shows one update-needed row and keeps already-imported files visible.
        /// </summary>
        [Test]
        public void BuildReport_ShowsUpdateNeededWhenLaterOwnerAddsMoreSameRepositoryFiles()
        {
            var report = new StatusReport(
                Array.Empty<ManifestDiscoveryIssue>(),
                new[]
                {
                    CreateDependencyStatus(
                        "tools.doji.pkg-a",
                        "Package A",
                        "model-a",
                        selectedFiles: new[] { "model.onnx" },
                        extraRepositoryFiles: new[]
                        {
                            new RepositoryFileMetadata { RepoPath = "model_bnb4.onnx", SizeInBytes = 7 }
                        }),
                    CreateDependencyStatus(
                        "tools.doji.pkg-b",
                        "Package B",
                        "model-b",
                        DependencyState.Missing,
                        reasonCode: DependencyReasonCode.RawCacheFilesMissing,
                        maintainerMessage: "Raw cache is missing selected file(s): model_bnb4.onnx.",
                        selectedFiles: new[] { "model.onnx", "model_bnb4.onnx" },
                        extraRepositoryFiles: new[]
                        {
                            new RepositoryFileMetadata { RepoPath = "model_bnb4.onnx", SizeInBytes = 7 }
                        })
                });

            var hubReport = new ModelHubService(new FakeRecipeSource()).BuildReport(report);

            Assert.That(hubReport.Items.Count, Is.EqualTo(1));
            var item = hubReport.Items[0];
            Assert.That(item.State, Is.EqualTo(ModelHubItemState.ActionRequired));
            Assert.That(item.HasMissingCacheFiles, Is.True);
            Assert.That(item.ImportedFileCount, Is.EqualTo(1));
            Assert.That(item.SelectedFileCount, Is.EqualTo(2));
            Assert.That(item.RepositoryFileCount, Is.EqualTo(2));
            Assert.That(item.Diagnostics.Any(diagnostic => diagnostic.Severity == "info" && diagnostic.Message.Contains("Update the model cache.")), Is.True);

            var firstFile = item.Files.Single(file => file.RepoPath == "model.onnx");
            Assert.That(firstFile.IsDependencySelected, Is.True);
            Assert.That(firstFile.SelectedOwnerCount, Is.EqualTo(2));
            Assert.That(firstFile.HasRuntimeFile, Is.True);

            var secondFile = item.Files.Single(file => file.RepoPath == "model_bnb4.onnx");
            Assert.That(secondFile.IsDependencySelected, Is.True);
            Assert.That(secondFile.SelectedOwnerCount, Is.EqualTo(1));
            Assert.That(secondFile.HasRuntimeFile, Is.False);
            Assert.That(secondFile.SupportsImportRecipeEditing, Is.True);
        }

        /// <summary>
        /// Verifies cache size estimate survives when imported files remain but raw cache metadata is gone.
        /// </summary>
        [Test]
        public void BuildReport_UsesImportMetadataCacheSizeWhenRawCacheWasRemoved()
        {
            var dependency = new DiscoveredModelDependency(
                "tools.doji.pkg-a",
                "Package A",
                "1.0.0",
                "/tmp/tools.doji.pkg-a",
                "/tmp/tools.doji.pkg-a/hf-hub-manifest.json",
                "pkg-hash",
                new ModelDependencyDefinition(
                    "doji/test-model",
                    "main"));
            var metadata = new ImportedModelMetadata
            {
                PackageId = "tools.doji.pkg-a",
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
                        ProjectPaths.GetImportBindingMetadataFilePath("tools.doji.pkg-a", "model-a"),
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
                        "tools.doji.pkg-a",
                        "Package A",
                        "model-a",
                        DependencyState.Missing,
                        reasonCode: DependencyReasonCode.RawCacheRevisionRefMissing,
                        maintainerMessage: "Raw cache revision ref is missing.",
                        rawCachedFilePath: null,
                        includeRawMetadata: false)
                });

            var hubReport = new ModelHubService(
                remoteSnapshotSizeEstimateService: new FakeRemoteSnapshotSizeEstimateService(
                    42,
                    new[]
                    {
                        new RepositoryFileMetadata
                        {
                            RepoPath = "model.onnx",
                            SizeInBytes = 42
                        }
                    }))
                .BuildReport(report);

            Assert.That(hubReport.Items.Count, Is.EqualTo(1));
            Assert.That(hubReport.Items[0].CacheSizeOnDiskBytes, Is.Null);
            Assert.That(hubReport.Items[0].EstimatedCacheSizeBytes, Is.EqualTo(42));
        }

        [Test]
        public void BuildReport_FiltersOutInvalidRepositoryStatuses()
        {
            var report = new StatusReport(
                Array.Empty<ManifestDiscoveryIssue>(),
                new[]
                {
                    CreateDependencyStatus(
                        "tools.doji.pkg-a",
                        "Package A",
                        "model-a",
                        DependencyState.Failed,
                        reasonCode: DependencyReasonCode.InvalidRepository,
                        maintainerMessage: "Repository was not found on Hugging Face.")
                });

            var hubReport = new ModelHubService().BuildReport(report);

            Assert.That(hubReport.Items, Is.Empty);
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
                        "tools.doji.pkg-a",
                        "Package A",
                        "model-a",
                        DependencyState.Failed,
                        revision: "main",
                        reasonCode: DependencyReasonCode.RevisionConflict,
                        maintainerMessage: "Repository revision conflict."),
                    CreateDependencyStatus(
                        "tools.doji.pkg-b",
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
                        "tools.doji.pkg-a",
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
                    CreateDependencyStatus("tools.doji.pkg-a", "Package A", "model-a")
                });

            var hubReport = new ModelHubService(new FakeRecipeSource()).BuildReport(report);

            Assert.That(hubReport.Items.Count, Is.EqualTo(1));
            Assert.That(hubReport.Items[0].Files.Count, Is.EqualTo(1));
            Assert.That(hubReport.Items[0].Files[0].RepoPath, Is.EqualTo("model.onnx"));
            Assert.That(hubReport.Items[0].Files[0].RawCachedFilePath, Is.EqualTo("/cache/doji/test-model/abc123/model.onnx"));
            Assert.That(hubReport.Items[0].Files[0].IsDependencySelected, Is.True);
            Assert.That(hubReport.Items[0].Files[0].RecipeSourceLabel, Is.EqualTo("Project Override"));
            Assert.That(hubReport.Items[0].Files[0].Recipe.QuantizationMode, Is.EqualTo("int8"));
        }

        /// <summary>
        /// Verifies file inventory includes unselected repository files while keeping recipe editing only on selected ONNX rows.
        /// </summary>
        [Test]
        public void BuildReport_ShowsUnselectedRepositoryFilesAsReadOnlyRows()
        {
            var report = new StatusReport(
                Array.Empty<ManifestDiscoveryIssue>(),
                new[]
                {
                    CreateDependencyStatus(
                        "tools.doji.pkg-a",
                        "Package A",
                        "model-a",
                        selectedFiles: new[] { "model.onnx" },
                        includeExtraRepositoryFile: true)
                });

            var hubReport = new ModelHubService(new FakeRecipeSource()).BuildReport(report);

            Assert.That(hubReport.Items.Count, Is.EqualTo(1));
            Assert.That(hubReport.Items[0].Files.Count, Is.EqualTo(2));
            Assert.That(hubReport.Items[0].SelectedFileCount, Is.EqualTo(1));
            Assert.That(hubReport.Items[0].RepositoryFileCount, Is.EqualTo(2));
            Assert.That(hubReport.Items[0].Files[0].RepoPath, Is.EqualTo("config.json"));
            Assert.That(hubReport.Items[0].Files[0].IsDependencySelected, Is.False);
            Assert.That(hubReport.Items[0].Files[0].SupportsImportRecipeEditing, Is.False);
            Assert.That(hubReport.Items[0].Files[1].RepoPath, Is.EqualTo("model.onnx"));
            Assert.That(hubReport.Items[0].Files[1].IsDependencySelected, Is.True);
            Assert.That(hubReport.Items[0].Files[1].SupportsImportRecipeEditing, Is.True);
        }

        /// <summary>
        /// Verifies file rows report raw-cache and runtime disk usage for the Storage tab.
        /// </summary>
        [Test]
        public void BuildReport_ReportsPerFileDiskUsage()
        {
            var rawPath = CreateTempFileWithSize(12);
            var runtimeAssetPath = CreateRuntimeAssetFileWithSize("Assets/StreamingAssets/hf-hub/tests/model.sentis", 7);
            var dependency = CreateDependencyStatus(
                "tools.doji.pkg-a",
                "Package A",
                "model-a",
                rawCachedFilePath: rawPath,
                runtimeAssetPath: runtimeAssetPath);
            var report = new StatusReport(Array.Empty<ManifestDiscoveryIssue>(), new[] { dependency });

            var hubReport = new ModelHubService().BuildReport(report);

            var file = hubReport.Items.Single().Files.Single(item => item.RepoPath == "model.onnx");
            Assert.That(file.RawSizeOnDiskBytes, Is.EqualTo(12));
            Assert.That(file.RuntimeSizeOnDiskBytes, Is.EqualTo(7));
        }

        /// <summary>
        /// Verifies .gitattributes, .gitignore, and README.md are excluded from file rows even
        /// when they appear in the raw cache metadata — they are repo-hygiene files with no runtime
        /// value in Unity and must not pollute the Files / Storage tabs.
        /// </summary>
        [Test]
        public void BuildReport_ExcludesRepoMetadataFilesFromFileRows()
        {
            var report = new StatusReport(
                Array.Empty<ManifestDiscoveryIssue>(),
                new[]
                {
                    CreateDependencyStatus(
                        "tools.doji.pkg-a",
                        "Package A",
                        "model-a",
                        extraRepositoryFiles: new[]
                        {
                            new RepositoryFileMetadata { RepoPath = ".gitattributes", SizeInBytes = 1 },
                            new RepositoryFileMetadata { RepoPath = ".gitignore", SizeInBytes = 1 },
                            new RepositoryFileMetadata { RepoPath = "README.md", SizeInBytes = 2 },
                            new RepositoryFileMetadata { RepoPath = "README_en.md", SizeInBytes = 2 },
                        })
                });

            var hubReport = new ModelHubService().BuildReport(report);

            Assert.That(hubReport.Items.Count, Is.EqualTo(1));
            var repoPaths = hubReport.Items[0].Files.Select(f => f.RepoPath).ToArray();
            Assert.That(repoPaths, Has.No.Member(".gitattributes"));
            Assert.That(repoPaths, Has.No.Member(".gitignore"));
            Assert.That(repoPaths, Has.No.Member("README.md"));
            Assert.That(repoPaths, Has.No.Member("README_en.md"));
            Assert.That(repoPaths, Has.Member("model.onnx"));
        }

        /// <summary>
        /// Verifies .gitattributes / README.md are not counted toward repository file or selected-file totals.
        /// </summary>
        [Test]
        public void BuildReport_RepoMetadataFilesDoNotInflateFileCounts()
        {
            var report = new StatusReport(
                Array.Empty<ManifestDiscoveryIssue>(),
                new[]
                {
                    CreateDependencyStatus(
                        "tools.doji.pkg-a",
                        "Package A",
                        "model-a",
                        extraRepositoryFiles: new[]
                        {
                            new RepositoryFileMetadata { RepoPath = ".gitattributes", SizeInBytes = 1 },
                            new RepositoryFileMetadata { RepoPath = "README.md", SizeInBytes = 2 },
                        })
                });

            var hubReport = new ModelHubService().BuildReport(report);

            Assert.That(hubReport.Items[0].RepositoryFileCount, Is.EqualTo(1));
            Assert.That(hubReport.Items[0].SelectedFileCount, Is.EqualTo(1));
        }

        private static DependencyStatus CreateDependencyStatus(
            string packageId,
            string packageDisplayName,
            string modelId,
            DependencyState state = DependencyState.Ready,
            string manifestPath = null,
            string revision = "main",
            ImportDiagnostic[] importedDiagnostics = null,
            DependencyReasonCode? reasonCode = null,
            string maintainerMessage = null,
            string rawCachedFilePath = "/cache/doji/test-model/abc123/model.onnx",
            string runtimeAssetPath = "Assets/StreamingAssets/hf-hub/doji/test-model/model.sentis",
            bool includeRawMetadata = true,
            string[] selectedFiles = null,
            ModelDependencyFileDefinition[] fileEntries = null,
            bool includeExtraRepositoryFile = false,
            RepositoryFileMetadata[] extraRepositoryFiles = null)
        {
            var dependency = new DiscoveredModelDependency(
                packageId,
                packageDisplayName,
                "1.0.0",
                $"/tmp/{packageId}",
                manifestPath ?? $"/tmp/{packageId}/hf-hub-manifest.json",
                $"{packageId}-hash",
                new ModelDependencyDefinition(
                    "doji/test-model",
                    revision,
                    fileEntries ?? ToRequiredFileEntries(selectedFiles)));

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
                RepositoryFiles = BuildRepositoryFiles(includeExtraRepositoryFile, extraRepositoryFiles),
                DependencySelectionHash = dependency.DependencySelectionHash,
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
                        RepositoryFiles = BuildRepositoryFiles(includeExtraRepositoryFile, extraRepositoryFiles),
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

        private static RepositoryFileMetadata[] BuildRepositoryFiles(
            bool includeExtraRepositoryFile,
            RepositoryFileMetadata[] additionalFiles = null)
        {
            var baseFiles = includeExtraRepositoryFile
                ? new[]
                {
                    new RepositoryFileMetadata { RepoPath = "config.json", SizeInBytes = 3 },
                    new RepositoryFileMetadata { RepoPath = "model.onnx", SizeInBytes = 12 }
                }
                : new[]
                {
                    new RepositoryFileMetadata { RepoPath = "model.onnx", SizeInBytes = 12 }
                };

            if (additionalFiles == null || additionalFiles.Length == 0)
            {
                return baseFiles;
            }

            return baseFiles.Concat(additionalFiles).ToArray();
        }

        private static ModelDependencyFileDefinition[] ToRequiredFileEntries(string[] selectedFiles)
        {
            return (selectedFiles ?? Array.Empty<string>())
                .Select(path => new ModelDependencyFileDefinition(path))
                .ToArray();
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

    internal sealed class IsRepoMetadataFileTests
    {
        [TestCase(".gitattributes")]
        [TestCase(".gitignore")]
        [TestCase("README.md")]
        [TestCase("README_en.md")]
        [TestCase("README_zh-Hans.md")]
        public void IsRepoMetadataFile_ReturnsTrue_ForKnownMetadataFiles(string repoPath)
        {
            Assert.That(ModelDependencyDefinition.IsRepoMetadataFile(repoPath), Is.True);
        }

        [TestCase("model.onnx")]
        [TestCase("config.json")]
        [TestCase("tokenizer.json")]
        [TestCase("subfolder/model.onnx")]
        [TestCase("")]
        [TestCase(null)]
        public void IsRepoMetadataFile_ReturnsFalse_ForNonMetadataFiles(string repoPath)
        {
            Assert.That(ModelDependencyDefinition.IsRepoMetadataFile(repoPath), Is.False);
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
        private readonly RepositoryFileMetadata[] _repositoryFiles;

        public FakeRemoteSnapshotSizeEstimateService(long? estimatedCacheSizeBytes, RepositoryFileMetadata[] repositoryFiles = null)
        {
            _estimatedCacheSizeBytes = estimatedCacheSizeBytes;
            _repositoryFiles = repositoryFiles ?? Array.Empty<RepositoryFileMetadata>();
        }

        public bool TryGetEstimatedCacheSize(string repository, string requestedRevision, out long? estimatedCacheSizeBytes)
        {
            estimatedCacheSizeBytes = _estimatedCacheSizeBytes;
            return _estimatedCacheSizeBytes.HasValue;
        }

        public bool TryGetRepositoryInventory(string repository, string requestedRevision, out RepositoryFileMetadata[] repositoryFiles)
        {
            repositoryFiles = _repositoryFiles;
            return _repositoryFiles.Length > 0;
        }

        public void EnsureEstimateLookup(DiscoveredModelDependency dependency, Action onCompleted = null)
        {
        }
    }
}
