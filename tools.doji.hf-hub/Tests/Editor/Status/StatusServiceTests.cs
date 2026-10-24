using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEditor;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers repo-level local dependency state classification across raw cache, owner bindings, and shared imports.
    /// </summary>
    internal sealed class StatusServiceTests
    {
        private TestCacheScope _cacheScope;
        private TestProjectStateScope _projectScope;
        private string _rootDirectory;

        [SetUp]
        public void SetUp()
        {
            _rootDirectory = Path.Combine(
                Path.GetTempPath(),
                "Doji.ModelHub.StatusTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rootDirectory);
            _cacheScope = new TestCacheScope(Path.Combine(_rootDirectory, "SharedCache"));
            _projectScope = new TestProjectStateScope();
        }

        [TearDown]
        public void TearDown()
        {
            _projectScope?.Dispose();
            _projectScope = null;

            if (Directory.Exists(_rootDirectory))
            {
                Directory.Delete(_rootDirectory, true);
            }

            _cacheScope?.Dispose();
            _cacheScope = null;
        }

        [Test]
        public void BuildStatusReport_ReturnsMissingWhenRawCacheIsAbsent()
        {
            var dependency = CreateDependency();
            var report = BuildStatusReport(dependency);

            Assert.That(report.MissingCount, Is.EqualTo(1));
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(DependencyReasonCode.RawCacheRevisionRefMissing));
            Assert.That(report.DependencyStatuses[0].MaintainerMessage, Is.EqualTo("Raw cache revision ref is missing."));
        }

        [Test]
        public void BuildStatusReport_ReturnsFailedWhenRepositoryIsKnownInvalid()
        {
            var dependency = CreateDependency(repository: $"doji/invalid-{Guid.NewGuid():N}");
            RepositoryValidationCacheService.WriteCachedRepositoryExists(dependency.Repository, false);

            var report = BuildStatusReport(dependency);

            Assert.That(report.FailedCount, Is.EqualTo(1));
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(DependencyReasonCode.InvalidRepository));
            Assert.That(report.DependencyStatuses[0].MaintainerMessage, Is.EqualTo("Repository was not found on Hugging Face."));
        }

        [Test]
        public void BuildStatusReport_ReturnsMissingWhenImportBindingIsAbsent()
        {
            var dependency = CreateDependency();
            WriteRawCache(dependency);

            var report = BuildStatusReport(dependency);

            Assert.That(report.MissingCount, Is.EqualTo(1));
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(DependencyReasonCode.ImportBindingMissing));
            Assert.That(report.DependencyStatuses[0].MaintainerMessage, Is.EqualTo("Import binding metadata file is missing."));
        }

        /// <summary>
        /// Verifies selected-file dependencies become missing when the raw cache lacks one selected file.
        /// </summary>
        [Test]
        public void BuildStatusReport_ReturnsMissingWhenSelectedRawCacheFileIsAbsent()
        {
            var dependency = CreateDependency(selectedFiles: new[] { "models/model.onnx", "tokenizer.json" });
            WriteRawCache(dependency, includeTokenizer: false);

            var report = BuildStatusReport(dependency);

            Assert.That(report.MissingCount, Is.EqualTo(1));
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(DependencyReasonCode.RawCacheFilesMissing));
            Assert.That(report.DependencyStatuses[0].MaintainerMessage, Does.Contain("tokenizer.json"));
        }

        /// <summary>
        /// Verifies whole-repo dependencies fail when raw inventory has files but none of them are ONNX.
        /// </summary>
        [Test]
        public void BuildStatusReport_ReturnsFailedWhenWholeRepositorySelectionContainsNoOnnxFiles()
        {
            var dependency = CreateDependency();
            WriteRawCacheWithoutOnnx(dependency);

            var report = BuildStatusReport(dependency);

            Assert.That(report.FailedCount, Is.EqualTo(1));
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(DependencyReasonCode.InvalidSelection));
            Assert.That(report.DependencyStatuses[0].MaintainerMessage, Does.Contain("does not select any model file"));
        }

        /// <summary>
        /// Verifies optional-only ONNX dependencies prompt for selection before any cache download is required.
        /// </summary>
        [Test]
        public void BuildStatusReport_ReturnsSelectionRequiredWhenAllOnnxFilesAreOptionalAndNoneAreEnabled()
        {
            var dependency = CreateDependency(
                fileEntries: new[]
                {
                    new ModelDependencyFileDefinition("models/model.onnx", true),
                    new ModelDependencyFileDefinition("models/model_q4.onnx", true)
                });

            var report = BuildStatusReport(dependency);

            Assert.That(report.MissingCount, Is.EqualTo(1));
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(DependencyReasonCode.SelectionRequired));
            Assert.That(report.DependencyStatuses[0].MaintainerMessage, Is.EqualTo("This dependency has optional model files, but none are currently enabled for this project owner."));
        }

        /// <summary>
        /// Verifies enabled optional ONNX files become part of the effective selection and report missing cache files.
        /// </summary>
        [Test]
        public void BuildStatusReport_ReturnsMissingWhenEnabledOptionalRawCacheFileIsAbsent()
        {
            var dependency = CreateDependency(
                fileEntries: new[]
                {
                    new ModelDependencyFileDefinition("models/model.onnx", true),
                    new ModelDependencyFileDefinition("models/model_q4.onnx", true)
                },
                enabledOptionalFiles: new[] { "models/model.onnx", "models/model_q4.onnx" });
            WriteRawCache(dependency);

            var report = BuildStatusReport(dependency);

            Assert.That(report.MissingCount, Is.EqualTo(1));
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(DependencyReasonCode.RawCacheFilesMissing));
            Assert.That(report.DependencyStatuses[0].MaintainerMessage, Does.Contain("models/model_q4.onnx"));
        }

        /// <summary>
        /// A partial conversion, where Sentis skipped one selected ONNX file, is imported and not out of date.
        /// </summary>
        [Test]
        public void BuildStatusReport_ReturnsReadyWhenSkippedOnnxFileIsRecorded()
        {
            var selected = new[] { "models/model.onnx", "models/model_bnb4.onnx" };
            var dependency = CreateDependency(selectedFiles: selected);
            WriteRawCacheWithMissingAdditionalOnnx(dependency, cacheAdditionalOnnx: true);
            WriteGeneratedArtifactsForExpandedInventory(dependency, selectedOnnxRepoPaths: selected, skippedRepoPaths: new[] { "models/model_bnb4.onnx" });

            var report = BuildStatusReport(dependency);

            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(DependencyReasonCode.None), report.DependencyStatuses[0].MaintainerMessage);
            Assert.That(report.DependencyStatuses[0].State, Is.EqualTo(DependencyState.Ready));
        }

        /// <summary>
        /// A selected ONNX file with neither a .sentis output nor a skip record still needs an import.
        /// </summary>
        [Test]
        public void BuildStatusReport_ReturnsStaleWhenSelectedOnnxFileWasNeitherImportedNorSkipped()
        {
            var selected = new[] { "models/model.onnx", "models/model_bnb4.onnx" };
            var dependency = CreateDependency(selectedFiles: selected);
            WriteRawCacheWithMissingAdditionalOnnx(dependency, cacheAdditionalOnnx: true);
            WriteGeneratedArtifactsForExpandedInventory(dependency, selectedOnnxRepoPaths: selected);

            var report = BuildStatusReport(dependency);

            Assert.That(report.DependencyStatuses[0].State, Is.Not.EqualTo(DependencyState.Ready));
        }

        [Test]
        public void BuildStatusReport_ReturnsReadyWhenRawBindingAndSharedArtifactsMatch()
        {
            var dependency = CreateDependency();
            WriteRawCache(dependency);
            WriteGeneratedArtifacts(dependency);

            var report = BuildStatusReport(dependency);

            Assert.That(report.ReadyCount, Is.EqualTo(1));
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(DependencyReasonCode.None));
            Assert.That(report.DependencyStatuses[0].MaintainerMessage, Is.EqualTo("Raw repo snapshot, import binding, and generated runtime files are current."));
        }

        /// <summary>
        /// Verifies an Inference Engine update that keeps the format generation keeps the import ready, so converted
        /// models are reused instead of converted again.
        /// </summary>
        [Test]
        public void BuildStatusReport_StaysReadyAfterAnUpdateWithinTheFormatGeneration()
        {
            var dependency = CreateDependency();
            WriteRawCache(dependency);
            WriteGeneratedArtifacts(dependency, sentisVersion: "2.2.0");

            var report = BuildStatusReport(dependency);

            Assert.That(report.DependencyStatuses[0].State, Is.EqualTo(DependencyState.Ready));
        }

        /// <summary>
        /// Verifies an import made with a version of an older format generation needs importing again, and says why.
        /// </summary>
        [Test]
        public void BuildStatusReport_IsStaleAfterAnUpdateAcrossFormatGenerations()
        {
            var dependency = CreateDependency();
            WriteRawCache(dependency);
            WriteGeneratedArtifacts(dependency, sentisVersion: "2.1.0");

            var status = BuildStatusReport(dependency).DependencyStatuses[0];

            Assert.That(status.State, Is.EqualTo(DependencyState.Stale));
            Assert.That(status.MaintainerMessage, Does.Contain("Sentis format generation"));
        }

        /// <summary>
        /// Verifies adding a broader same-repository dependency later leaves existing owner ready but marks the new owner as cache-update-needed.
        /// </summary>
        [Test]
        public void BuildStatusReport_ReturnsReadyAndMissingForExistingAndNewSameRepositoryOwnersWhenSelectionGrows()
        {
            var existingDependency = CreateDependency(packageId: "tools.doji.existing", selectedFiles: new[] { "models/model.onnx" });
            var newDependency = CreateDependency(packageId: "tools.doji.new", selectedFiles: new[] { "models/model.onnx", "models/model_bnb4.onnx" });
            WriteRawCacheWithMissingAdditionalOnnx(existingDependency);
            WriteGeneratedArtifactsForExpandedInventory(existingDependency);

            var report = BuildStatusReport(existingDependency, newDependency);

            Assert.That(report.DependencyStatuses, Has.Length.EqualTo(2));

            var existingStatus = report.DependencyStatuses.Single(status => status.Dependency.PackageId == "tools.doji.existing");
            Assert.That(existingStatus.State, Is.EqualTo(DependencyState.Ready));
            Assert.That(existingStatus.ReasonCode, Is.EqualTo(DependencyReasonCode.None));

            var newStatus = report.DependencyStatuses.Single(status => status.Dependency.PackageId == "tools.doji.new");
            Assert.That(newStatus.State, Is.EqualTo(DependencyState.Missing));
            Assert.That(newStatus.ReasonCode, Is.EqualTo(DependencyReasonCode.RawCacheFilesMissing));
            Assert.That(newStatus.MaintainerMessage, Does.Contain("models/model_bnb4.onnx"));
        }

        /// <summary>
        /// Verifies same repository declared at multiple revisions blocks both dependencies before cache or import checks run.
        /// </summary>
        [Test]
        public void BuildStatusReport_ReturnsFailedWhenRepositoryHasMultipleRequestedRevisions()
        {
            var mainDependency = CreateDependency(packageId: "tools.doji.main", revision: "main");
            var releaseDependency = CreateDependency(packageId: "tools.doji.release", revision: "refs/tags/v1");

            var report = BuildStatusReport(mainDependency, releaseDependency);

            Assert.That(report.FailedCount, Is.EqualTo(2));
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(DependencyReasonCode.RevisionConflict));
            Assert.That(report.DependencyStatuses[1].ReasonCode, Is.EqualTo(DependencyReasonCode.RevisionConflict));
            Assert.That(report.DependencyStatuses[0].MaintainerMessage, Does.Contain("multiple requested revisions"));
            Assert.That(report.DependencyStatuses[0].MaintainerMessage, Does.Contain("main"));
            Assert.That(report.DependencyStatuses[0].MaintainerMessage, Does.Contain("refs/tags/v1"));
        }

        [Test]
        public void BuildStatusReport_ReturnsReadyWhenRawCacheWasRemovedButImportedFilesRemain()
        {
            var dependency = CreateDependency();
            WriteRawCache(dependency);
            WriteGeneratedArtifacts(dependency);
            DeleteRawCache(dependency);

            var report = BuildStatusReport(dependency);

            Assert.That(report.ReadyCount, Is.EqualTo(1));
            Assert.That(
                report.DependencyStatuses[0].MaintainerMessage,
                Is.EqualTo("Generated runtime files are present. Raw cache has been removed from this machine."));
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(DependencyReasonCode.RawCacheRemovedAfterImport));
        }

        [Test]
        public void BuildStatusReport_ReturnsStaleWhenBindingManifestHashDiffers()
        {
            var dependency = CreateDependency();
            WriteRawCache(dependency);
            WriteGeneratedArtifacts(dependency, bindingManifestHash: "old-manifest-hash");

            var report = BuildStatusReport(dependency);

            Assert.That(report.StaleCount, Is.EqualTo(1));
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(DependencyReasonCode.ImportBindingStale));
            Assert.That(
                report.DependencyStatuses[0].MaintainerMessage,
                Is.EqualTo("Import binding metadata is stale: manifest hash expected `manifesthash` but found `old-manifest-hash`."));
        }

        [Test]
        public void BuildStatusReport_ReturnsMissingWhenSharedImportMetadataIsAbsent()
        {
            var dependency = CreateDependency();
            WriteRawCache(dependency);
            WriteGeneratedArtifacts(dependency);
            DeleteSharedMetadata(dependency);

            var report = BuildStatusReport(dependency);

            Assert.That(report.MissingCount, Is.EqualTo(1));
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(DependencyReasonCode.SharedImportMetadataMissing));
            Assert.That(report.DependencyStatuses[0].MaintainerMessage, Is.EqualTo("Shared import metadata file is missing."));
        }

        [Test]
        public void BuildStatusReport_ReturnsFailedWhenSharedImportMetadataIsInvalid()
        {
            var dependency = CreateDependency();
            WriteRawCache(dependency);
            WriteGeneratedArtifacts(dependency);
            WriteInvalidSharedMetadata(dependency);

            var report = BuildStatusReport(dependency);

            Assert.That(report.FailedCount, Is.EqualTo(1));
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(DependencyReasonCode.SharedImportMetadataInvalid));
            Assert.That(report.DependencyStatuses[0].MaintainerMessage, Is.EqualTo("Shared import metadata is invalid."));
        }

        [Test]
        public void BuildStatusReport_ReturnsMissingWhenGeneratedRuntimeFileIsAbsent()
        {
            var dependency = CreateDependency();
            WriteRawCache(dependency);
            WriteGeneratedArtifacts(dependency);
            DeleteGeneratedRuntimeFiles(dependency);

            var report = BuildStatusReport(dependency);

            Assert.That(report.MissingCount, Is.EqualTo(1));
            Assert.That(
                report.DependencyStatuses[0].MaintainerMessage,
                Does.StartWith("Runtime model is missing: models/model.onnx"));
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(DependencyReasonCode.RuntimeFileMissing));
        }

        [Test]
        public void BuildStatusReport_ReturnsFailedWhenBindingMetadataIsInvalid()
        {
            var dependency = CreateDependency();
            WriteRawCache(dependency);
            WriteRuntimeFiles(dependency);

            var bindingMetadataAbsolutePath = ProjectPaths.ToProjectAbsolutePath(
                ProjectPaths.GetImportBindingMetadataFilePath(dependency.PackageId, dependency.ModelId));
            Directory.CreateDirectory(Path.GetDirectoryName(bindingMetadataAbsolutePath) ?? throw new InvalidOperationException());
            File.WriteAllText(bindingMetadataAbsolutePath, "{ invalid json");

            var report = BuildStatusReport(dependency);

            Assert.That(report.FailedCount, Is.EqualTo(1));
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(DependencyReasonCode.ImportBindingInvalid));
            Assert.That(report.DependencyStatuses[0].MaintainerMessage, Is.EqualTo("Import binding metadata is invalid."));
        }

        [Test]
        public void BuildStatusReport_ReturnsStaleWhenRecipeSetHashDiffers()
        {
            var dependency = CreateDependency();
            WriteRawCache(dependency);
            WriteGeneratedArtifacts(dependency, bindingRecipeSetHash: "old-recipe-hash");

            var report = BuildStatusReport(dependency);

            Assert.That(report.StaleCount, Is.EqualTo(1));
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(DependencyReasonCode.ImportBindingStale));
            Assert.That(report.DependencyStatuses[0].MaintainerMessage, Does.Contain("recipe set hash"));
        }

        /// <summary>
        /// Verifies import binding becomes stale when owner file selection changed even if raw cache still contains the files.
        /// </summary>
        [Test]
        public void BuildStatusReport_ReturnsStaleWhenBindingSelectionHashDiffers()
        {
            var dependency = CreateDependency(selectedFiles: new[] { "models/model.onnx" });
            WriteRawCache(dependency);
            WriteGeneratedArtifacts(dependency);

            var bindingMetadataAbsolutePath = ProjectPaths.ToProjectAbsolutePath(
                ProjectPaths.GetImportBindingMetadataFilePath(dependency.PackageId, dependency.ModelId));
            var bindingMetadata = JsonConvert.DeserializeObject<ImportBindingMetadata>(File.ReadAllText(bindingMetadataAbsolutePath));
            bindingMetadata.DependencySelectionHash = "old-selection-hash";
            File.WriteAllText(bindingMetadataAbsolutePath, JsonConvert.SerializeObject(bindingMetadata, Formatting.Indented));

            var report = BuildStatusReport(dependency);

            Assert.That(report.StaleCount, Is.EqualTo(1));
            Assert.That(report.DependencyStatuses[0].ReasonCode, Is.EqualTo(DependencyReasonCode.ImportBindingStale));
            Assert.That(report.DependencyStatuses[0].MaintainerMessage, Does.Contain("selection hash"));
        }

        /// <summary>
        /// Verifies a dependency on a Sentis file is ready after download and import, without any conversion, and stops
        /// being ready when the downloaded file is gone.
        /// </summary>
        [Test]
        public async System.Threading.Tasks.Task BuildStatusReport_IsReadyForSentisFilesWithoutConversion()
        {
            var dependency = CreateDependency(selectedFiles: new[] { "sentis/model.sentis" });
            WriteMixedFormatCache(dependency);
            var cached = new RawCacheService().GetCachedOrThrow(dependency);

            var artifact = await new ProjectImportService().ImportAsync(dependency, cached, System.Threading.CancellationToken.None);
            var ready = BuildStatusReport(dependency).DependencyStatuses.Single();
            File.Delete(CachePaths.GetSnapshotFilePath(dependency.Repository, "abc123", "sentis/model.sentis"));
            var missing = BuildStatusReport(dependency).DependencyStatuses.Single();

            Assert.That(artifact.RuntimeFiles.Single().Format, Is.EqualTo("sentis"));
            Assert.That(ready.State, Is.EqualTo(DependencyState.Ready), ready.MaintainerMessage);
            Assert.That(missing.State, Is.Not.EqualTo(DependencyState.Ready));
        }

        /// <summary>
        /// Verifies a whole-repository dependency on a repository with both formats fails instead of picking one.
        /// </summary>
        [Test]
        public void BuildStatusReport_FailsWholeRepositoryOfMixedFormats()
        {
            var dependency = CreateDependency();
            WriteMixedFormatCache(dependency);

            var status = BuildStatusReport(dependency).DependencyStatuses.Single();

            Assert.That(status.State, Is.EqualTo(DependencyState.Failed));
            Assert.That(status.ReasonCode, Is.EqualTo(DependencyReasonCode.InvalidSelection));
            Assert.That(status.MaintainerMessage, Does.Contain("both ONNX and Sentis files"));
        }

        private static void WriteMixedFormatCache(DiscoveredModelDependency dependency, string commitHash = "abc123")
        {
            var files = new[] { ("onnx/model.onnx", "etag-onnx"), ("sentis/model.sentis", "etag-sentis") };
            foreach (var (repoPath, _) in files)
            {
                var path = CachePaths.GetSnapshotFilePath(dependency.Repository, commitHash, repoPath);
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new InvalidOperationException());
                File.WriteAllText(path, "model");
            }

            var refPath = CachePaths.GetRevisionRefPath(dependency.Repository, dependency.Revision);
            Directory.CreateDirectory(Path.GetDirectoryName(refPath) ?? throw new InvalidOperationException());
            File.WriteAllText(refPath, commitHash);
            var metadataPath = CachePaths.GetSnapshotMetadataPath(dependency.Repository, commitHash);
            Directory.CreateDirectory(Path.GetDirectoryName(metadataPath) ?? throw new InvalidOperationException());
            File.WriteAllText(metadataPath, JsonConvert.SerializeObject(new RawCacheMetadata
            {
                Repository = dependency.Repository,
                RequestedRevision = dependency.Revision,
                ResolvedCommitHash = commitHash,
                RepositoryFiles = files.Select(file => new RepositoryFileMetadata { RepoPath = file.Item1, SizeInBytes = 5 }).ToArray(),
                Files = files.Select(file => new RawCacheFileMetadata
                {
                    RepoPath = file.Item1,
                    EntityTag = file.Item2,
                    CachedFilePath = CachePaths.GetSnapshotFilePath(dependency.Repository, commitHash, file.Item1),
                    SizeInBytes = 5
                }).ToArray()
            }));
        }

        private static StatusReport BuildStatusReport(params DiscoveredModelDependency[] dependencies)
        {
            var discoveryReport = new DiscoveryReport(
                dependencies,
                Array.Empty<ManifestDiscoveryIssue>());
            return new StatusService().BuildStatusReport(discoveryReport);
        }

        private static DiscoveredModelDependency CreateDependency(
            string packageId = "tools.doji.test",
            string revision = "main",
            string repository = "doji/test-model",
            string[] selectedFiles = null,
            ModelDependencyFileDefinition[] fileEntries = null,
            string[] enabledOptionalFiles = null)
        {
            return new DiscoveredModelDependency(
                packageId,
                "Test Package",
                "1.0.0",
                $"/tmp/{packageId}",
                $"/tmp/{packageId}/hf-hub-manifest.json",
                "manifesthash",
                new ModelDependencyDefinition(
                    repository,
                    revision,
                    fileEntries ?? ToRequiredFileEntries(selectedFiles)),
                enabledOptionalFiles);
        }

        private static ModelDependencyFileDefinition[] ToRequiredFileEntries(string[] selectedFiles)
        {
            return (selectedFiles ?? Array.Empty<string>())
                .Select(path => new ModelDependencyFileDefinition(path))
                .ToArray();
        }

        private static void WriteRawCache(DiscoveredModelDependency dependency, string commitHash = "abc123", bool includeTokenizer = true)
        {
            var rawFilePath = CachePaths.GetSnapshotFilePath(dependency.Repository, commitHash, "models/model.onnx");
            var rawMetadataPath = CachePaths.GetSnapshotMetadataPath(dependency.Repository, commitHash);
            var revisionRefPath = CachePaths.GetRevisionRefPath(dependency.Repository, dependency.Revision);
            var tokenizerPath = CachePaths.GetSnapshotFilePath(dependency.Repository, commitHash, "tokenizer.json");

            Directory.CreateDirectory(Path.GetDirectoryName(rawFilePath) ?? throw new InvalidOperationException());
            Directory.CreateDirectory(Path.GetDirectoryName(revisionRefPath) ?? throw new InvalidOperationException());

            File.WriteAllText(rawFilePath, "onnx");
            if (includeTokenizer)
            {
                File.WriteAllText(tokenizerPath, "tokenizer");
            }

            File.WriteAllText(revisionRefPath, commitHash);
            Directory.CreateDirectory(Path.GetDirectoryName(rawMetadataPath));
            File.WriteAllText(
                rawMetadataPath,
                JsonConvert.SerializeObject(new RawCacheMetadata
                {
                    Repository = dependency.Repository,
                    RequestedRevision = dependency.Revision,
                    ResolvedCommitHash = commitHash,
                    CachedSnapshotRoot = CachePaths.GetSnapshotRoot(dependency.Repository, commitHash),
                    RepositoryFiles = new[]
                    {
                        new RepositoryFileMetadata
                        {
                            RepoPath = "models/model.onnx",
                            SizeInBytes = 4
                        },
                        new RepositoryFileMetadata
                        {
                            RepoPath = "tokenizer.json",
                            SizeInBytes = 9
                        }
                    },
                    Files = new[]
                    {
                        new RawCacheFileMetadata
                        {
                            RepoPath = "models/model.onnx",
                            CachedFilePath = rawFilePath
                        },
                        new RawCacheFileMetadata
                        {
                            RepoPath = "tokenizer.json",
                            CachedFilePath = tokenizerPath
                        }
                    },
                    DownloadedAtUtc = DateTime.UtcNow.ToString("O")
                }, Formatting.Indented));
        }

        private static void WriteGeneratedArtifacts(
            DiscoveredModelDependency dependency,
            string commitHash = "abc123",
            string bindingManifestHash = null,
            string bindingRecipeSetHash = null,
            string sentisVersion = null)
        {
            var runtimeFiles = WriteRuntimeFiles(dependency, commitHash);
            sentisVersion ??= StatusService.GetCurrentSentisVersion();
            var recipeSetHash = bindingRecipeSetHash ?? OnnxImportMetadataUtility.ComputeRecipeSetHash(new[] { "models/model.onnx" });
            var sharedMetadataPath = ProjectPaths.GetSharedImportMetadataFilePath(dependency.Repository, commitHash, sentisVersion, recipeSetHash);
            var sharedMetadataAbsolutePath = ProjectPaths.ToProjectAbsolutePath(sharedMetadataPath);
            var bindingMetadataAbsolutePath = ProjectPaths.ToProjectAbsolutePath(
                ProjectPaths.GetImportBindingMetadataFilePath(dependency.PackageId, dependency.ModelId));

            Directory.CreateDirectory(Path.GetDirectoryName(sharedMetadataAbsolutePath) ?? throw new InvalidOperationException());
            Directory.CreateDirectory(Path.GetDirectoryName(bindingMetadataAbsolutePath) ?? throw new InvalidOperationException());

            File.WriteAllText(
                sharedMetadataAbsolutePath,
                JsonConvert.SerializeObject(new SharedImportMetadata
                {
                    Repository = dependency.Repository,
                    ResolvedCommitHash = commitHash,
                    RequestedRevisionHint = dependency.Revision,
                    RawSnapshotRootPath = CachePaths.GetSnapshotRoot(dependency.Repository, commitHash),
                    RawMetadataPath = CachePaths.GetSnapshotMetadataPath(dependency.Repository, commitHash),
                    RepositoryFiles = new[]
                    {
                        new RepositoryFileMetadata
                        {
                            RepoPath = "models/model.onnx",
                            SizeInBytes = 4
                        },
                        new RepositoryFileMetadata
                        {
                            RepoPath = "tokenizer.json",
                            SizeInBytes = 9
                        }
                    },
                    RawCacheSizeInBytes = 4,
                    SentisVersion = sentisVersion,
                    UnityVersion = UnityEngine.Application.unityVersion,
                    RecipeSetHash = recipeSetHash,
                    RuntimeFiles = runtimeFiles,
                    ImportedAtUtc = DateTime.UtcNow.ToString("O")
                }, Formatting.Indented));

            File.WriteAllText(
                bindingMetadataAbsolutePath,
                JsonConvert.SerializeObject(new ImportBindingMetadata
                {
                    PackageId = dependency.PackageId,
                    PackageVersion = dependency.PackageVersion,
                    ModelId = dependency.ModelId,
                    ManifestHash = bindingManifestHash ?? dependency.ManifestHash,
                    Repository = dependency.Repository,
                    RequestedRevision = dependency.Revision,
                    ResolvedCommitHash = commitHash,
                    SentisVersion = sentisVersion,
                    RecipeSetHash = recipeSetHash,
                    DependencySelectionHash = dependency.DependencySelectionHash,
                    SharedImportMetadataPath = sharedMetadataPath,
                    BoundAtUtc = DateTime.UtcNow.ToString("O")
                }, Formatting.Indented));
        }

        private static void WriteRawCacheWithoutOnnx(DiscoveredModelDependency dependency, string commitHash = "abc123")
        {
            var rawMetadataPath = CachePaths.GetSnapshotMetadataPath(dependency.Repository, commitHash);
            var revisionRefPath = CachePaths.GetRevisionRefPath(dependency.Repository, dependency.Revision);
            var tokenizerPath = CachePaths.GetSnapshotFilePath(dependency.Repository, commitHash, "tokenizer.json");

            Directory.CreateDirectory(Path.GetDirectoryName(revisionRefPath) ?? throw new InvalidOperationException());
            Directory.CreateDirectory(Path.GetDirectoryName(tokenizerPath) ?? throw new InvalidOperationException());

            File.WriteAllText(tokenizerPath, "tokenizer");
            File.WriteAllText(revisionRefPath, commitHash);
            Directory.CreateDirectory(Path.GetDirectoryName(rawMetadataPath));
            File.WriteAllText(
                rawMetadataPath,
                JsonConvert.SerializeObject(new RawCacheMetadata
                {
                    Repository = dependency.Repository,
                    RequestedRevision = dependency.Revision,
                    ResolvedCommitHash = commitHash,
                    CachedSnapshotRoot = CachePaths.GetSnapshotRoot(dependency.Repository, commitHash),
                    RepositoryFiles = new[]
                    {
                        new RepositoryFileMetadata
                        {
                            RepoPath = "tokenizer.json",
                            SizeInBytes = 9
                        }
                    },
                    Files = new[]
                    {
                        new RawCacheFileMetadata
                        {
                            RepoPath = "tokenizer.json",
                            CachedFilePath = tokenizerPath
                        }
                    },
                    DownloadedAtUtc = DateTime.UtcNow.ToString("O")
                }, Formatting.Indented));
        }

        private static void WriteRawCacheWithMissingAdditionalOnnx(DiscoveredModelDependency dependency, string commitHash = "abc123", bool cacheAdditionalOnnx = false)
        {
            var rawModelPath = CachePaths.GetSnapshotFilePath(dependency.Repository, commitHash, "models/model.onnx");
            var additionalModelPath = CachePaths.GetSnapshotFilePath(dependency.Repository, commitHash, "models/model_bnb4.onnx");
            var rawMetadataPath = CachePaths.GetSnapshotMetadataPath(dependency.Repository, commitHash);
            var revisionRefPath = CachePaths.GetRevisionRefPath(dependency.Repository, dependency.Revision);

            Directory.CreateDirectory(Path.GetDirectoryName(rawModelPath) ?? throw new InvalidOperationException());
            Directory.CreateDirectory(Path.GetDirectoryName(revisionRefPath) ?? throw new InvalidOperationException());

            File.WriteAllText(rawModelPath, "onnx");
            if (cacheAdditionalOnnx)
            {
                File.WriteAllText(additionalModelPath, "onnxbnb4");
            }

            File.WriteAllText(revisionRefPath, commitHash);
            Directory.CreateDirectory(Path.GetDirectoryName(rawMetadataPath));
            var cachedFiles = new List<RawCacheFileMetadata>
            {
                new RawCacheFileMetadata
                {
                    RepoPath = "models/model.onnx",
                    CachedFilePath = rawModelPath
                }
            };
            if (cacheAdditionalOnnx)
            {
                cachedFiles.Add(new RawCacheFileMetadata
                {
                    RepoPath = "models/model_bnb4.onnx",
                    CachedFilePath = additionalModelPath
                });
            }

            File.WriteAllText(
                rawMetadataPath,
                JsonConvert.SerializeObject(new RawCacheMetadata
                {
                    Repository = dependency.Repository,
                    RequestedRevision = dependency.Revision,
                    ResolvedCommitHash = commitHash,
                    CachedSnapshotRoot = CachePaths.GetSnapshotRoot(dependency.Repository, commitHash),
                    RepositoryFiles = new[]
                    {
                        new RepositoryFileMetadata
                        {
                            RepoPath = "models/model.onnx",
                            SizeInBytes = 4
                        },
                        new RepositoryFileMetadata
                        {
                            RepoPath = "models/model_bnb4.onnx",
                            SizeInBytes = 8
                        }
                    },
                    Files = cachedFiles.ToArray(),
                    DownloadedAtUtc = DateTime.UtcNow.ToString("O")
                }, Formatting.Indented));
        }

        private static void WriteGeneratedArtifactsForExpandedInventory(
            DiscoveredModelDependency dependency,
            string commitHash = "abc123",
            string[] selectedOnnxRepoPaths = null,
            string[] skippedRepoPaths = null)
        {
            var runtimeFiles = WriteRuntimeFiles(dependency, commitHash);
            var sentisVersion = StatusService.GetCurrentSentisVersion();
            var recipeSetHash = OnnxImportMetadataUtility.ComputeRecipeSetHash(selectedOnnxRepoPaths ?? new[] { "models/model.onnx" });
            var sharedMetadataPath = ProjectPaths.GetSharedImportMetadataFilePath(dependency.Repository, commitHash, sentisVersion, recipeSetHash);
            var sharedMetadataAbsolutePath = ProjectPaths.ToProjectAbsolutePath(sharedMetadataPath);
            var bindingMetadataAbsolutePath = ProjectPaths.ToProjectAbsolutePath(
                ProjectPaths.GetImportBindingMetadataFilePath(dependency.PackageId, dependency.ModelId));

            Directory.CreateDirectory(Path.GetDirectoryName(sharedMetadataAbsolutePath) ?? throw new InvalidOperationException());
            Directory.CreateDirectory(Path.GetDirectoryName(bindingMetadataAbsolutePath) ?? throw new InvalidOperationException());

            File.WriteAllText(
                sharedMetadataAbsolutePath,
                JsonConvert.SerializeObject(new SharedImportMetadata
                {
                    Repository = dependency.Repository,
                    ResolvedCommitHash = commitHash,
                    RequestedRevisionHint = dependency.Revision,
                    RawSnapshotRootPath = CachePaths.GetSnapshotRoot(dependency.Repository, commitHash),
                    RawMetadataPath = CachePaths.GetSnapshotMetadataPath(dependency.Repository, commitHash),
                    RepositoryFiles = new[]
                    {
                        new RepositoryFileMetadata
                        {
                            RepoPath = "models/model.onnx",
                            SizeInBytes = 4
                        },
                        new RepositoryFileMetadata
                        {
                            RepoPath = "models/model_bnb4.onnx",
                            SizeInBytes = 8
                        }
                    },
                    RawCacheSizeInBytes = 4,
                    SentisVersion = sentisVersion,
                    UnityVersion = UnityEngine.Application.unityVersion,
                    RecipeSetHash = recipeSetHash,
                    RuntimeFiles = runtimeFiles,
                    SkippedRepoPaths = skippedRepoPaths ?? Array.Empty<string>(),
                    ImportedAtUtc = DateTime.UtcNow.ToString("O")
                }, Formatting.Indented));

            File.WriteAllText(
                bindingMetadataAbsolutePath,
                JsonConvert.SerializeObject(new ImportBindingMetadata
                {
                    PackageId = dependency.PackageId,
                    PackageVersion = dependency.PackageVersion,
                    ModelId = dependency.ModelId,
                    ManifestHash = dependency.ManifestHash,
                    Repository = dependency.Repository,
                    RequestedRevision = dependency.Revision,
                    ResolvedCommitHash = commitHash,
                    SentisVersion = sentisVersion,
                    RecipeSetHash = recipeSetHash,
                    DependencySelectionHash = dependency.DependencySelectionHash,
                    SharedImportMetadataPath = sharedMetadataPath,
                    BoundAtUtc = DateTime.UtcNow.ToString("O")
                }, Formatting.Indented));
        }

        private static string GetConvertedModelPath(DiscoveredModelDependency dependency)
        {
            return ConvertedModelStore.GetModelPath(dependency.Repository, "status-test-key");
        }

        private static ImportedRuntimeFileMetadata[] WriteRuntimeFiles(DiscoveredModelDependency dependency, string commitHash = "abc123")
        {
            var sentisAbsolutePath = GetConvertedModelPath(dependency);

            Directory.CreateDirectory(Path.GetDirectoryName(sentisAbsolutePath) ?? throw new InvalidOperationException());
            File.WriteAllText(sentisAbsolutePath, "sentis");

            return new[]
            {
                new ImportedRuntimeFileMetadata
                {
                    RepoPath = "models/model.onnx",
                    Kind = "sentis",
                    Format = "onnx",
                    FilePath = sentisAbsolutePath,
                    SizeInBytes = 6,
                    RecipeHash = OnnxImportMetadataUtility.ComputeRecipeHash(new OnnxConversionRecipe()),
                    ConversionPipelineVersion = OnnxImportMetadataUtility.DefaultConversionPipelineVersion
                }
            };
        }

        private static void DeleteRawCache(DiscoveredModelDependency dependency)
        {
            var repositoryRoot = CachePaths.GetRepositoryRoot(dependency.Repository);
            if (Directory.Exists(repositoryRoot))
            {
                Directory.Delete(repositoryRoot, true);
            }
        }

        private static void DeleteSharedMetadata(DiscoveredModelDependency dependency, string commitHash = "abc123")
        {
            var sharedMetadataAbsolutePath = ProjectPaths.ToProjectAbsolutePath(
                ProjectPaths.GetSharedImportMetadataFilePath(
                    dependency.Repository,
                    commitHash,
                    StatusService.GetCurrentSentisVersion(),
                    OnnxImportMetadataUtility.ComputeRecipeSetHash(new[] { "models/model.onnx" })));
            if (File.Exists(sharedMetadataAbsolutePath))
            {
                File.Delete(sharedMetadataAbsolutePath);
            }
        }

        private static void WriteInvalidSharedMetadata(DiscoveredModelDependency dependency, string commitHash = "abc123")
        {
            var sharedMetadataAbsolutePath = ProjectPaths.ToProjectAbsolutePath(
                ProjectPaths.GetSharedImportMetadataFilePath(
                    dependency.Repository,
                    commitHash,
                    StatusService.GetCurrentSentisVersion(),
                    OnnxImportMetadataUtility.ComputeRecipeSetHash(new[] { "models/model.onnx" })));
            File.WriteAllText(sharedMetadataAbsolutePath, "{ invalid json");
        }

        private static void DeleteGeneratedRuntimeFiles(DiscoveredModelDependency dependency)
        {
            var sentisAbsolutePath = GetConvertedModelPath(dependency);
            if (File.Exists(sentisAbsolutePath))
            {
                File.Delete(sentisAbsolutePath);
            }
        }
    }
}
