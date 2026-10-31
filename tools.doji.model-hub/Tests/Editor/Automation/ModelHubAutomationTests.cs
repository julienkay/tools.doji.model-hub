using System;
using System.IO;
using System.Linq;
using Doji.Downloads;
using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers the tool surface behind the Unity CLI commands: status output, argument checks, and refusals that
    /// never open a dialog.
    /// </summary>
    internal sealed class ModelHubAutomationTests
    {
        private const string Repository = "doji/automation-model";
        private const string RequiredFile = "sentis/model_fp32.sentis";
        private const string OptionalFile = "sentis/model_large_fp32.sentis";

        private TestCacheScope _cacheScope;
        private string _rootDirectory;

        [SetUp]
        public void SetUp()
        {
            _rootDirectory = Path.Combine(
                Path.GetTempPath(),
                "Doji.ModelHub.AutomationTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rootDirectory);
            _cacheScope = new TestCacheScope(Path.Combine(_rootDirectory, "SharedCache"));
        }

        [TearDown]
        public void TearDown()
        {
            ModelHubAutomation.StatusReportOverride = null;
            SyncRunStore.PathOverride = null;
            _cacheScope?.Dispose();
            _cacheScope = null;
            if (Directory.Exists(_rootDirectory))
            {
                Directory.Delete(_rootDirectory, true);
            }
        }

        /// <summary>
        /// Verifies status lists the owner, the requested revision, the state, and each listed file with whether it is
        /// optional, turned on, and cached.
        /// </summary>
        [Test]
        public void GetStatus_ListsOwnerRevisionAndFiles()
        {
            UseDependencies(CreateDependency());

            var status = ModelHubAutomation.GetStatus();

            var dependency = status.Dependencies.Single();
            Assert.That(dependency.Owner, Is.EqualTo("tools.doji.automation"));
            Assert.That(dependency.Repository, Is.EqualTo(Repository));
            Assert.That(dependency.RequestedRevision, Is.EqualTo("v1"));
            Assert.That(dependency.ResolvedCommit, Is.Null);
            Assert.That(dependency.State, Is.EqualTo("Missing"));
            Assert.That(dependency.ReasonCode, Is.EqualTo(nameof(DependencyReasonCode.RawCacheRevisionRefMissing)));
            Assert.That(dependency.Files.Select(file => file.Path), Is.EqualTo(new[] { RequiredFile, OptionalFile }));

            var required = dependency.Files.Single(file => file.Path == RequiredFile);
            Assert.That(required.Optional, Is.False);
            Assert.That(required.Enabled, Is.True);
            Assert.That(required.Cached, Is.False);
            Assert.That(required.Quantization, Is.Null, "Quantization applies to ONNX files only.");

            var optional = dependency.Files.Single(file => file.Path == OptionalFile);
            Assert.That(optional.Optional, Is.True);
            Assert.That(optional.Enabled, Is.False);
        }

        /// <summary>
        /// Verifies a file in the shared cache reads as cached, and the commit the revision resolved to is reported.
        /// </summary>
        [Test]
        public void GetStatus_ReportsCachedFileAndResolvedCommit()
        {
            const string commitHash = "0123456789abcdef";
            var revisionRefPath = CachePaths.GetRevisionRefPath(Repository, "v1");
            Directory.CreateDirectory(Path.GetDirectoryName(revisionRefPath));
            File.WriteAllText(revisionRefPath, commitHash);
            var filePath = CachePaths.GetSnapshotFilePath(Repository, commitHash, RequiredFile);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, "sentis");
            UseDependencies(CreateDependency());

            var dependency = ModelHubAutomation.GetStatus(Repository).Dependencies.Single();

            Assert.That(dependency.ResolvedCommit, Is.EqualTo(commitHash));
            Assert.That(dependency.Files.Single(file => file.Path == RequiredFile).Cached, Is.True);
            Assert.That(dependency.Files.Single(file => file.Path == OptionalFile).Cached, Is.False);
        }

        /// <summary>
        /// Verifies an optional file the project turned on reads as enabled.
        /// </summary>
        [Test]
        public void GetStatus_ReportsEnabledOptionalFile()
        {
            UseDependencies(CreateDependency(enabledOptionalFiles: new[] { OptionalFile }));

            var optional = ModelHubAutomation.GetStatus().Dependencies.Single().Files.Single(file => file.Path == OptionalFile);

            Assert.That(optional.Optional, Is.True);
            Assert.That(optional.Enabled, Is.True);
        }

        /// <summary>
        /// Verifies status filtered by repository leaves out other models.
        /// </summary>
        [Test]
        public void GetStatus_FiltersByRepository()
        {
            UseDependencies(CreateDependency(), CreateDependency(packageId: "tools.doji.other", repository: "doji/other-model"));

            var status = ModelHubAutomation.GetStatus("doji/other-model");

            Assert.That(status.Dependencies.Select(dependency => dependency.Repository), Is.EqualTo(new[] { "doji/other-model" }));
        }

        /// <summary>
        /// Verifies an unknown repository id is refused with the list of declared models, and a wrong case gets a hint.
        /// </summary>
        [Test]
        public void GetStatus_RefusesUnknownRepository()
        {
            UseDependencies(CreateDependency());

            var unknown = Assert.Throws<ModelHubAutomationException>(() => ModelHubAutomation.GetStatus("doji/nothing"));
            var wrongCase = Assert.Throws<ModelHubAutomationException>(() => ModelHubAutomation.GetStatus("DOJI/automation-model"));

            Assert.That(unknown.Message, Is.EqualTo($"Unknown model 'doji/nothing'. Declared models: {Repository}."));
            Assert.That(wrongCase.Message, Does.Contain($"Did you mean '{Repository}'?"));
        }

        /// <summary>
        /// Verifies a sync request with an unknown mode or repository is refused before anything starts.
        /// </summary>
        [Test]
        public void StartSync_RefusesUnknownModeAndRepository()
        {
            UseDependencies(CreateDependency());

            var mode = Assert.Throws<ModelHubAutomationException>(() => ModelHubAutomation.StartSync(null, "everything"));
            var repository = Assert.Throws<ModelHubAutomationException>(() => ModelHubAutomation.StartSync("doji/nothing", "full"));

            Assert.That(mode.Message, Is.EqualTo("Unknown mode 'everything'. Use 'full' (download and import) or 'download'."));
            Assert.That(repository.Message, Does.StartWith("Unknown model 'doji/nothing'."));
            Assert.That(BackgroundSyncManager.IsRunning, Is.False);
        }

        /// <summary>
        /// Verifies file changes need an argument, and a bad quantization name is refused.
        /// </summary>
        [Test]
        public void SetFile_RefusesMissingAndInvalidArguments()
        {
            UseDependencies(CreateDependency());

            var nothing = Assert.Throws<ModelHubAutomationException>(() => ModelHubAutomation.SetFile(Repository, OptionalFile, null, null));
            var quantization = Assert.Throws<ModelHubAutomationException>(() => ModelHubAutomation.SetFile(Repository, OptionalFile, null, "int4"));
            var noRepository = Assert.Throws<ModelHubAutomationException>(() => ModelHubAutomation.SetFile(" ", OptionalFile, true, null));

            Assert.That(nothing.Message, Does.StartWith("Pass --enabled"));
            Assert.That(quantization.Message, Is.EqualTo("Unknown quantization 'int4'. Use none, float16, or uint8."));
            Assert.That(noRepository.Message, Does.StartWith("Pass --repo"));
        }

        /// <summary>
        /// Verifies a required file cannot be turned off, and a file no manifest lists cannot be turned on.
        /// </summary>
        [Test]
        public void SetFile_RefusesRequiredAndUnlistedFiles()
        {
            UseDependencies(CreateDependency());

            var required = Assert.Throws<ModelHubAutomationException>(() => ModelHubAutomation.SetFile(Repository, RequiredFile, false, null));
            var unlisted = Assert.Throws<ModelHubAutomationException>(() => ModelHubAutomation.SetFile(Repository, "onnx/other.onnx", true, null));

            Assert.That(required.Message, Is.EqualTo($"'{RequiredFile}' is a required file of tools.doji.automation. Only optional files can be turned off."));
            Assert.That(unlisted.Message, Does.Contain("Only files a manifest marks optional can be turned on or off."));
            Assert.That(unlisted.Message, Does.Contain($"{OptionalFile} (optional)"));
        }

        /// <summary>
        /// Verifies quantization is refused for a Sentis file, which is loaded as downloaded.
        /// </summary>
        [Test]
        public void SetFile_RefusesQuantizationOfSentisFile()
        {
            UseDependencies(CreateDependency());

            var exception = Assert.Throws<ModelHubAutomationException>(() => ModelHubAutomation.SetFile(Repository, RequiredFile, null, "float16"));

            Assert.That(exception.Message, Is.EqualTo($"Quantization applies to ONNX files only. '{RequiredFile}' is loaded as downloaded."));
        }

        /// <summary>
        /// Verifies cache removal refuses without the user's confirmation and leaves the cache alone.
        /// </summary>
        [Test]
        public void RemoveCache_RefusesWithoutConfirmation()
        {
            const string commitHash = "0123456789abcdef";
            var revisionRefPath = CachePaths.GetRevisionRefPath(Repository, "v1");
            Directory.CreateDirectory(Path.GetDirectoryName(revisionRefPath));
            File.WriteAllText(revisionRefPath, commitHash);
            UseDependencies(CreateDependency());

            var exception = Assert.Throws<ModelHubAutomationException>(() => ModelHubAutomation.RemoveCache(Repository, confirm: false));

            Assert.That(exception.Message, Does.EndWith("Ask the user, then pass --confirm true."));
            Assert.That(File.Exists(revisionRefPath), Is.True);
        }

        /// <summary>
        /// Verifies cancel says so when no sync runs.
        /// </summary>
        [Test]
        public void Cancel_ReportsWhenNoSyncRuns()
        {
            Assume.That(BackgroundSyncManager.IsRunning, Is.False);

            var result = ModelHubAutomation.Cancel();

            Assert.That(result.Canceled, Is.False);
            Assert.That(result.Message, Is.EqualTo("No sync is running."));
        }

        /// <summary>
        /// Verifies the mode argument reads full and download, and full when empty.
        /// </summary>
        [Test]
        public void SyncModes_ParseNamesAndRejectOthers()
        {
            Assert.That(SyncModes.TryParse(null, out var empty) && empty == SyncOperationMode.Full, Is.True);
            Assert.That(SyncModes.TryParse(" FULL ", out var full) && full == SyncOperationMode.Full, Is.True);
            Assert.That(SyncModes.TryParse("download", out var download) && download == SyncOperationMode.DownloadOnly, Is.True);
            Assert.That(SyncModes.TryParse("import", out _), Is.False, "An import-only sync is chosen per model, not requested.");
            Assert.That(SyncModes.ToName(SyncOperationMode.MaterializeOnly), Is.EqualTo("import"));
        }

        /// <summary>
        /// Verifies the last sync record survives a save and load, so sync status can report it after a domain reload.
        /// </summary>
        [Test]
        public void SyncRunStore_SavesAndLoadsTheLastRun()
        {
            SyncRunStore.PathOverride = Path.Combine(_rootDirectory, "Library", "last-sync.json");
            var record = new SyncRunRecord
            {
                Id = "run-1",
                State = SyncRunStates.Failed,
                Mode = SyncModes.Full,
                Repository = Repository,
                Results = new[]
                {
                    new SyncRunResultItem { Owner = "tools.doji.automation", Repository = Repository, Revision = "v1", Ok = false, Message = "HTTP 404" }
                }
            };

            SyncRunStore.Save(record);
            var loaded = SyncRunStore.Load();

            Assert.That(loaded.Id, Is.EqualTo("run-1"));
            Assert.That(loaded.IsFinished, Is.True);
            Assert.That(loaded.Results.Single().Message, Is.EqualTo("HTTP 404"));
        }

        /// <summary>
        /// Verifies a sync report becomes one result per model, with the error of a failed one.
        /// </summary>
        [Test]
        public void SyncRunStore_ToResultItems_ReportsEachModel()
        {
            var ok = CreateDependency();
            var failed = CreateDependency(packageId: "tools.doji.other", repository: "doji/other-model");
            var metadata = new RawCacheMetadata { ResolvedCommitHash = "abc" };
            var report = new SyncReport(
                Array.Empty<ManifestDiscoveryIssue>(),
                new[]
                {
                    new ModelSyncResult(SyncOperationMode.Full, ok, new CachedSnapshot("/tmp/snapshot", "/tmp/index.json", metadata), null, null),
                    new ModelSyncResult(SyncOperationMode.DownloadOnly, failed, null, null, "Download failed.")
                });

            var items = SyncRunStore.ToResultItems(report);

            Assert.That(items[0].Ok, Is.True);
            Assert.That(items[0].ResolvedCommit, Is.EqualTo("abc"));
            Assert.That(items[0].Message, Is.Null);
            Assert.That(items[1].Ok, Is.False);
            Assert.That(items[1].Mode, Is.EqualTo("download"));
            Assert.That(items[1].Message, Is.EqualTo("Download failed."));
        }

        /// <summary>
        /// Verifies turning an optional file on keeps the owner's other optional files, and a repeat changes nothing.
        /// </summary>
        [Test]
        public void OptionalFileSelection_SetEnabled_KeepsOtherFiles()
        {
            var source = new RecordingSelectionSource();
            var dependency = CreateDependency(
                fileEntries: new[]
                {
                    new ModelDependencyFileDefinition(OptionalFile, true),
                    new ModelDependencyFileDefinition("sentis/model_small.sentis", true)
                },
                enabledOptionalFiles: new[] { "sentis/model_small.sentis" });

            var changed = source.SetEnabled(dependency, OptionalFile, true);
            var unchanged = source.SetEnabled(dependency, "sentis/model_small.sentis", true);

            Assert.That(changed, Is.True);
            Assert.That(unchanged, Is.False);
            Assert.That(source.LastOwner, Is.EqualTo("tools.doji.automation"));
            Assert.That(source.LastFiles, Is.EquivalentTo(new[] { OptionalFile, "sentis/model_small.sentis" }));
            Assert.That(source.WriteCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies the cache download reports each file before it starts, so sync status can name the current file.
        /// </summary>
        [Test]
        public void FileDownloadProgress_ForwardsFileStartsAndBytes()
        {
            string startedFile = null;
            long bytes = 0;
            var inner = new RecordingProgress();
            var progress = new FileDownloadProgress(
                inner,
                (repoPath, _, _, _) => startedFile = repoPath,
                value => bytes = value.DownloadedBytes);

            ((IFileDownloadProgress)progress).OnFileStarted(RequiredFile, 10, 0, 1);
            progress.Report(new DownloadProgress(4, 10));

            Assert.That(startedFile, Is.EqualTo(RequiredFile));
            Assert.That(bytes, Is.EqualTo(4));
            Assert.That(inner.Last.DownloadedBytes, Is.EqualTo(4));
        }

        private static void UseDependencies(params DiscoveredModelDependency[] dependencies)
        {
            ModelHubAutomation.StatusReportOverride = () => new StatusService().BuildStatusReport(
                new DiscoveryReport(dependencies, Array.Empty<ManifestDiscoveryIssue>()));
        }

        private static DiscoveredModelDependency CreateDependency(
            string packageId = "tools.doji.automation",
            string repository = Repository,
            ModelDependencyFileDefinition[] fileEntries = null,
            string[] enabledOptionalFiles = null)
        {
            return new DiscoveredModelDependency(
                packageId,
                "Automation Package",
                "1.0.0",
                $"/tmp/{packageId}",
                $"/tmp/{packageId}/model-hub-manifest.json",
                "manifesthash",
                new ModelDependencyDefinition(
                    repository,
                    "v1",
                    fileEntries ?? new[]
                    {
                        new ModelDependencyFileDefinition(RequiredFile),
                        new ModelDependencyFileDefinition(OptionalFile, true)
                    }),
                enabledOptionalFiles);
        }

        private sealed class RecordingSelectionSource : IProjectOptionalFileSelectionSource
        {
            public string LastOwner { get; private set; }
            public string[] LastFiles { get; private set; }
            public int WriteCount { get; private set; }

            public string[] GetEnabledOptionalFiles(string ownerId, string modelId, System.Collections.Generic.IReadOnlyList<string> declaredOptionalFiles)
            {
                return Array.Empty<string>();
            }

            public void SetEnabledOptionalFiles(string ownerId, string modelId, System.Collections.Generic.IReadOnlyList<string> enabledOptionalFiles)
            {
                LastOwner = ownerId;
                LastFiles = enabledOptionalFiles.ToArray();
                WriteCount++;
            }
        }

        private sealed class RecordingProgress : IProgress<DownloadProgress>
        {
            public DownloadProgress Last { get; private set; }

            public void Report(DownloadProgress value)
            {
                Last = value;
            }
        }
    }
}
