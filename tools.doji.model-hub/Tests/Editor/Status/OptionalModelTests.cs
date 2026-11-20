using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers model-level opt-in: the manifest's <c>optional</c> key on a model entry, the project's enabled state,
    /// the neutral <c>NotEnabled</c> status, the sync skip, the <c>model_hub_set_model</c> tool, the registry, and the
    /// load error.
    /// </summary>
    internal sealed class OptionalModelTests
    {
        private const string Repository = "doji/optional-model";
        private const string ModelFile = "sentis/model.sentis";

        private TestCacheScope _cacheScope;
        private string _rootDirectory;

        [SetUp]
        public void SetUp()
        {
            _rootDirectory = Path.Combine(Path.GetTempPath(), "Doji.ModelHub.OptionalModelTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rootDirectory);
            _cacheScope = new TestCacheScope(Path.Combine(_rootDirectory, "SharedCache"));
        }

        [TearDown]
        public void TearDown()
        {
            ModelHubAutomation.StatusReportOverride = null;
            ModelHubAutomation.SetModelEnabledOverride = null;
            RuntimeModels.RegistryOverride = null;
            RuntimeModels.EditorOfferEnableModel = OptionalModelPrompt.OfferEnable;
            RuntimeModels.ResetForTests();
            _cacheScope?.Dispose();
            _cacheScope = null;
            if (Directory.Exists(_rootDirectory))
            {
                Directory.Delete(_rootDirectory, true);
            }
        }

        // ---------------------------------------------------------------- Manifest

        [Test]
        public void Parse_ReadsModelLevelOptional()
        {
            const string json = @"{
  ""version"": 1,
  ""models"": [
    { ""id"": ""doji/required"" },
    { ""id"": ""doji/optional-model"", ""revision"": ""main"", ""optional"": true, ""files"": [ { ""path"": ""sentis/model.sentis"" } ] },
    { ""id"": ""doji/explicit-false"", ""optional"": false }
  ]
}";

            Assert.That(ManifestParser.TryParse(json, out var manifest, out var error), Is.True, error);

            var models = manifest.Models.ToDictionary(model => model.Id);
            Assert.That(models["doji/required"].Optional, Is.False);
            Assert.That(models["doji/optional-model"].Optional, Is.True);
            Assert.That(models["doji/explicit-false"].Optional, Is.False);
            Assert.That(models["doji/optional-model"].FileEntries.Single().Optional, Is.False, "File-level optional keeps its own meaning.");
        }

        [Test]
        public void Parse_RejectsNonBooleanModelOptional()
        {
            const string json = @"{ ""version"": 1, ""models"": [ { ""id"": ""doji/model"", ""optional"": ""yes"" } ] }";

            Assert.That(ManifestParser.TryParse(json, out _, out var error), Is.False);
            Assert.That(error, Does.Contain("`optional`").And.Contain("boolean"));
        }

        [Test]
        public void Parse_StillRejectsUnknownModelProperties()
        {
            const string json = @"{ ""version"": 1, ""models"": [ { ""id"": ""doji/model"", ""enabled"": true } ] }";

            Assert.That(ManifestParser.TryParse(json, out _, out var error), Is.False);
            Assert.That(error, Does.Contain("unknown property `enabled`").And.Contain("`optional`"));
        }

        [Test]
        public void Writer_WritesOptionalOnlyWhenSet()
        {
            var json = ManifestWriter.ToJson(1, new[]
            {
                new ManifestModelDraft("doji/required", null),
                new ManifestModelDraft("doji/optional-model", "main", null, optional: true)
            });

            Assert.That(ManifestParser.TryParse(json, out var manifest, out var error), Is.True, error);
            Assert.That(manifest.Models.Single(model => model.Id == "doji/optional-model").Optional, Is.True);
            Assert.That(manifest.Models.Single(model => model.Id == "doji/required").Optional, Is.False);
            Assert.That(json.Split('\n').Count(line => line.Contains("\"optional\"")), Is.EqualTo(1));
        }

        [Test]
        public void ProjectManifestAsset_CarriesOptional()
        {
            var draft = new ManifestModelDraft("doji/optional-model", "main", null, optional: true);

            Assert.That(ManifestFactory.TryCreateManifest(1, new[] { draft }, out var manifest, out var error), Is.True, error);
            Assert.That(manifest.Models.Single().Optional, Is.True);
        }

        // ---------------------------------------------------------------- Enabled state

        [Test]
        public void Dependency_NotEnabled_SelectsNothing()
        {
            var dependency = CreateDependency();

            Assert.That(dependency.IsOptionalModel, Is.True);
            Assert.That(dependency.IsEnabled, Is.False);
            Assert.That(dependency.SelectedFiles, Is.Empty);
            Assert.That(dependency.GetSelectedRepoPaths(new[] { ModelFile, "config.json" }), Is.Empty);
            Assert.That(dependency.GetSelectedRepoPathsWhenEnabled(new[] { ModelFile, "config.json" }), Is.EqualTo(new[] { ModelFile }));
            Assert.That(dependency.RequiresOptionalModelSelection, Is.False);
        }

        [Test]
        public void Dependency_Enabled_SelectsRequiredFilesAndKeepsFileLevelOptional()
        {
            var dependency = CreateDependency(
                modelEnabled: true,
                files: new[]
                {
                    new ModelDependencyFileDefinition(ModelFile),
                    new ModelDependencyFileDefinition("sentis/model_large.sentis", true)
                });

            Assert.That(dependency.IsEnabled, Is.True);
            Assert.That(dependency.SelectedFiles, Is.EqualTo(new[] { ModelFile }));
        }

        [Test]
        public void ApplyProjectOptionalSelections_ReadsEnabledStatePerOwner()
        {
            var report = new DiscoveryReport(
                new[]
                {
                    CreateDependency(packageId: "tools.doji.a"),
                    CreateDependency(packageId: "tools.doji.b")
                },
                Array.Empty<ManifestDiscoveryIssue>());
            var source = new FakeSource();
            source.Enabled.Add(("tools.doji.a", Repository));

            var applied = EditorManifestDiscovery.ApplyProjectOptionalSelections(report, source);

            Assert.That(applied.Dependencies.Single(dependency => dependency.PackageId == "tools.doji.a").IsEnabled, Is.True);
            Assert.That(applied.Dependencies.Single(dependency => dependency.PackageId == "tools.doji.b").IsEnabled, Is.False);
            Assert.That(applied.Dependencies.All(dependency => dependency.IsOptionalModel), Is.True);
        }

        [Test]
        public void SetModelEnabled_WritesOnlyChangesAndRefusesRequiredModels()
        {
            var source = new FakeSource();
            var dependency = CreateDependency();

            Assert.That(source.SetModelEnabled(dependency, true), Is.True);
            Assert.That(source.Enabled, Does.Contain(("tools.doji.optional", Repository)));
            Assert.That(source.SetModelEnabled(dependency, false), Is.False, "The dependency already reads as disabled.");
            Assert.Throws<InvalidOperationException>(() => source.SetModelEnabled(CreateDependency(optional: false), true));
        }

        [Test]
        public void ProjectSettings_StoresEnabledOptionalModels()
        {
            using (ProjectSettings.PushEmptyProjectOverrides())
            {
                var settings = ProjectSettings.instance;
                Assert.That(settings.IsOptionalModelEnabled("tools.doji.optional", Repository), Is.False);

                Assert.That(settings.SetOptionalModelEnabled("tools.doji.optional", Repository, true), Is.True);
                Assert.That(settings.SetOptionalModelEnabled("tools.doji.optional", Repository, true), Is.False);
                Assert.That(settings.IsOptionalModelEnabled("tools.doji.optional", Repository), Is.True);
                Assert.That(settings.IsOptionalModelEnabled("tools.doji.other", Repository), Is.False, "The state is per owner.");

                var source = new ProjectOptionalFileSelectionSource();
                Assert.That(source.IsModelEnabled("tools.doji.optional", Repository), Is.True);

                Assert.That(settings.SetOptionalModelEnabled("tools.doji.optional", Repository, false), Is.True);
                Assert.That(settings.IsOptionalModelEnabled("tools.doji.optional", Repository), Is.False);
            }
        }

        // ---------------------------------------------------------------- Status and sync

        [Test]
        public void Status_NotEnabled_IsNeutral()
        {
            var report = new StatusService().BuildStatusReport(Discovery(CreateDependency()));

            var status = report.DependencyStatuses.Single();
            Assert.That(status.State, Is.EqualTo(DependencyState.NotEnabled));
            Assert.That(status.ReasonCode, Is.EqualTo(DependencyReasonCode.NotEnabled));
            Assert.That(status.NeedsAttention, Is.False);
            Assert.That(status.NeedsStartupPrompt, Is.False);
            Assert.That(report.NeedsAttention, Is.False);
            Assert.That(report.NotEnabledCount, Is.EqualTo(1));
            Assert.That(status.MaintainerMessage, Does.Contain("model_hub_set_model"));

            var item = new ModelHubService().BuildReport(report).Items.Single();
            Assert.That(item.State, Is.EqualTo(ModelHubItemState.NotEnabled));
            Assert.That(item.NeedsAttention, Is.False);
            Assert.That(ModelStatus.From(item).Kind, Is.EqualTo(ModelStatusKind.NotEnabled));
            Assert.That(ModelStatus.From(item).Action, Is.EqualTo(ModelActionKind.EnableModel));
            Assert.That(ModelStatus.From(item).NeedsAction, Is.False);
        }

        [Test]
        public void Status_NotEnabledModel_TakesNoPartInRevisionConflicts()
        {
            var required = CreateDependency(packageId: "tools.doji.main", optional: false, revision: "main");
            var optional = CreateDependency(packageId: "tools.doji.optional", revision: "refs/tags/v1");

            var report = new StatusService().BuildStatusReport(Discovery(required, optional));

            Assert.That(report.DependencyStatuses.Any(status => status.ReasonCode == DependencyReasonCode.RevisionConflict), Is.False);
            Assert.That(RevisionConflictDetector.Detect(new[] { required, optional }), Is.Empty);

            var enabled = CreateDependency(packageId: "tools.doji.optional", revision: "refs/tags/v1", modelEnabled: true);
            Assert.That(RevisionConflictDetector.Detect(new[] { required, enabled }).Keys, Is.EqualTo(new[] { Repository }),
                "Once enabled, the model takes part in the conflict check.");
        }

        [Test]
        public void SyncPlan_SkipsNotEnabledModelWithoutError()
        {
            var plan = BackgroundSyncManager.BuildSyncPlan(Discovery(CreateDependency()), null, SyncOperationMode.Full);

            var entry = plan.Single();
            Assert.That(entry.IsPending, Is.False);
            Assert.That(entry.SkipReason, Is.EqualTo(SyncSkipReason.NotEnabled));
            Assert.That(ModelHubAutomation.ToReasonName(entry.SkipReason), Is.EqualTo("not_enabled"));
            Assert.That(BackgroundSyncManager.BuildNothingToSyncMessage(plan), Does.Contain("optional model, not enabled"));
        }

        [Test]
        public void SyncPlan_IncludesModelOnceEnabled()
        {
            var plan = BackgroundSyncManager.BuildSyncPlan(Discovery(CreateDependency(modelEnabled: true)), null, SyncOperationMode.Full);

            Assert.That(plan.Single().IsPending, Is.True);
        }

        // ---------------------------------------------------------------- Automation (model_hub_set_model)

        [Test]
        public void GetStatus_ReportsOptionalNotEnabledModel()
        {
            UseDependencies(CreateDependency());

            var dependency = ModelHubAutomation.GetStatus().Dependencies.Single();

            Assert.That(dependency.State, Is.EqualTo("NotEnabled"));
            Assert.That(dependency.Optional, Is.True);
            Assert.That(dependency.Enabled, Is.False);
            Assert.That(dependency.Files.Single().Enabled, Is.False);
        }

        [Test]
        public void SetModel_EnablesEveryOwnerThatDeclaresTheModelOptional()
        {
            UseDependencies(CreateDependency(packageId: "tools.doji.a"), CreateDependency(packageId: "tools.doji.b"));
            var recorded = new List<(string Owner, bool Enabled)>();
            ModelHubAutomation.SetModelEnabledOverride = (dependencies, enabled) =>
            {
                recorded.AddRange(dependencies.Select(dependency => (dependency.PackageId, enabled)));
                return dependencies.Select(dependency => $"Enabled {dependency.PackageId}").ToArray();
            };

            var result = ModelHubAutomation.SetModel(Repository, true);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Enabled, Is.True);
            Assert.That(recorded, Is.EquivalentTo(new[] { ("tools.doji.a", true), ("tools.doji.b", true) }));
        }

        [Test]
        public void SetModel_OwnerLimitsTheChange()
        {
            UseDependencies(CreateDependency(packageId: "tools.doji.a"), CreateDependency(packageId: "tools.doji.b"));
            IReadOnlyList<DiscoveredModelDependency> passed = null;
            ModelHubAutomation.SetModelEnabledOverride = (dependencies, _) =>
            {
                passed = dependencies;
                return Array.Empty<string>();
            };

            var result = ModelHubAutomation.SetModel(Repository, true, "tools.doji.b");

            Assert.That(passed.Select(dependency => dependency.PackageId), Is.EqualTo(new[] { "tools.doji.b" }));
            Assert.That(result.Changed, Is.False);
            Assert.That(result.Changes.Single(), Does.Contain("already enabled"));
            Assert.Throws<ModelHubAutomationException>(() => ModelHubAutomation.SetModel(Repository, true, "tools.doji.unknown"));
        }

        [Test]
        public void SetModel_RefusesUnknownAndRequiredModels()
        {
            UseDependencies(CreateDependency(optional: false));
            ModelHubAutomation.SetModelEnabledOverride = (_, _) => throw new AssertionException("Nothing may be written.");

            Assert.Throws<ModelHubAutomationException>(() => ModelHubAutomation.SetModel("doji/unknown", true));
            var disable = Assert.Throws<ModelHubAutomationException>(() => ModelHubAutomation.SetModel(Repository, false));
            Assert.That(disable.Message, Does.Contain("not an optional model"));
            Assert.Throws<ModelHubAutomationException>(() => ModelHubAutomation.SetModel(null, true));
        }

        [Test]
        public void SetFile_RefusesFileOfNotEnabledModel()
        {
            UseDependencies(CreateDependency(files: new[] { new ModelDependencyFileDefinition(ModelFile, true) }));

            var exception = Assert.Throws<ModelHubAutomationException>(() => ModelHubAutomation.SetFile(Repository, ModelFile, true, null));
            Assert.That(exception.Message, Does.Contain("model_hub_set_model"));
        }

        // ---------------------------------------------------------------- Build and load

        [Test]
        public void RuntimeRegistry_ListsNotEnabledModelWithoutBuildProblem()
        {
            var report = new StatusService().BuildStatusReport(Discovery(CreateDependency()));

            var plan = RuntimeRegistryBuilder.Build(report, _ => ModelDelivery.Bundle, null, forEditor: false, playerOnnxConverterId: null, _rootDirectory);

            Assert.That(plan.Problems, Is.Empty);
            Assert.That(plan.BundledFiles, Is.Empty);
            Assert.That(plan.Registry.notEnabledModels.Single().repoId, Is.EqualTo(Repository));
            Assert.That(plan.Registry.packages.SelectMany(package => package.models), Is.Empty);
            var restored = RuntimeRegistry.FromJson(plan.Registry.ToJson());
            Assert.That(restored.IsNotEnabled(null, Repository), Is.True);
            Assert.That(restored.IsNotEnabled("tools.doji.other", Repository), Is.False);
        }

        [Test]
        public void Load_NotEnabledModel_ThrowsErrorThatSaysHowToEnable()
        {
            UseRegistry();
            RuntimeModels.EditorOfferEnableModel = (_, _) => false;

            var exception = Assert.Throws<InvalidOperationException>(() => Hub.LoadModel(Repository, ModelFile));

            Assert.That(exception.Message, Does.Contain(Repository).And.Contain("not enabled").And.Contain("model_hub_set_model"));
        }

        [Test]
        public void Load_NotEnabledModel_AfterConsent_SaysDownloadStarted()
        {
            UseRegistry();
            string offeredRepository = null;
            RuntimeModels.EditorOfferEnableModel = (_, repoId) =>
            {
                offeredRepository = repoId;
                return true;
            };

            var exception = Assert.Throws<InvalidOperationException>(() => Hub.ReadFile(Repository, "config.json"));

            Assert.That(offeredRepository, Is.EqualTo(Repository));
            Assert.That(exception.Message, Does.Contain("started its download"));
        }

        [Test]
        public void NotEnabledError_ForPlayers_NamesModelAndHowToEnable()
        {
            var exception = RuntimeModels.CreateNotEnabledException("tools.doji.optional", Repository, false);

            Assert.That(exception.Message, Does.Contain(Repository).And.Contain("tools.doji.optional").And.Contain("Window > AI Model Hub"));
        }

        // ---------------------------------------------------------------- Enable dialog

        /// <summary>
        /// Verifies the download size counts only files that are not in the cache: a complete blob counts as cached
        /// even when the snapshot does not link to it, a short blob does not.
        /// </summary>
        [Test]
        public void PendingDownloadSize_SkipsCompleteBlobsWithoutSnapshotLinks()
        {
            var files = new[]
            {
                new RepositoryFileMetadata { RepoPath = "unet/model.onnx", SizeInBytes = 10, EntityTag = "\"aaa\"" },
                new RepositoryFileMetadata { RepoPath = "unet/weights.pb", SizeInBytes = 20, EntityTag = "bbb" },
                new RepositoryFileMetadata { RepoPath = "vae/model.onnx", SizeInBytes = 30, EntityTag = "ccc" },
            };
            var selected = new HashSet<string>(files.Select(file => file.RepoPath), StringComparer.Ordinal);
            WriteBlob("aaa", 10);
            WriteBlob("bbb", 20);
            WriteBlob("ccc", 5);

            Assert.That(ModelHubService.CalculatePendingDownloadSize(Repository, null, files, selected), Is.EqualTo(30));

            WriteBlob("ccc", 30);
            Assert.That(ModelHubService.CalculatePendingDownloadSize(Repository, null, files, selected), Is.EqualTo(0));
            Assert.That(ModelHubService.CalculatePendingDownloadSize(Repository, null, Array.Empty<RepositoryFileMetadata>(), selected), Is.Null);
        }

        /// <summary>
        /// Verifies the converted size covers the selected ONNX files and their external weights, and nothing else.
        /// </summary>
        [Test]
        public void ConvertedSize_CountsOnnxFilesAndTheirWeights()
        {
            var files = new[]
            {
                new RepositoryFileMetadata { RepoPath = "unet/model.onnx", SizeInBytes = 10 },
                new RepositoryFileMetadata { RepoPath = "unet/weights.pb", SizeInBytes = 20 },
                new RepositoryFileMetadata { RepoPath = "tokenizer/vocab.json", SizeInBytes = 40 },
            };
            var selected = new HashSet<string>(files.Select(file => file.RepoPath), StringComparer.Ordinal);

            Assert.That(ModelHubService.CalculateConvertedSize(files, selected), Is.EqualTo(30));
            Assert.That(ModelHubService.CalculateConvertedSize(files, new HashSet<string> { "tokenizer/vocab.json" }), Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies the dialog names no owner, says when nothing needs downloading, and gives the converted size.
        /// </summary>
        [Test]
        public void EnableDialog_SaysWhenFilesAreAlreadyDownloaded()
        {
            var cached = OptionalModelPrompt.BuildMessage(Repository, 0, 2048);
            Assert.That(cached, Does.StartWith($"{Repository} is an optional model. This project does not use it yet."));
            Assert.That(cached, Does.Contain("Its files are already on this machine, so nothing needs to download."));
            Assert.That(cached, Does.Not.Contain("Enabling it downloads"));
            Assert.That(cached, Does.Contain("The converted model takes about 2 KB more."));

            var missing = OptionalModelPrompt.BuildMessage(Repository, 1024, 0);
            Assert.That(missing, Does.Contain("Enabling it downloads 1 KB from Hugging Face."));
            Assert.That(missing, Does.Not.Contain("converted model"));

            Assert.That(OptionalModelPrompt.BuildMessage(Repository, null, null), Does.Contain("Enabling it downloads it from Hugging Face."));
        }

        private static void WriteBlob(string etag, int length)
        {
            var path = CachePaths.GetBlobPath(Repository, etag);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, new byte[length]);
        }

        // ---------------------------------------------------------------- Helpers

        private void UseRegistry()
        {
            RuntimeModels.RegistryOverride = new RuntimeRegistry
            {
                notEnabledModels = new[] { new RuntimeNotEnabledModel { packageId = "tools.doji.optional", repoId = Repository } }
            };
        }

        private static DiscoveryReport Discovery(params DiscoveredModelDependency[] dependencies)
        {
            return new DiscoveryReport(dependencies, Array.Empty<ManifestDiscoveryIssue>());
        }

        private static void UseDependencies(params DiscoveredModelDependency[] dependencies)
        {
            ModelHubAutomation.StatusReportOverride = () => new StatusService().BuildStatusReport(Discovery(dependencies));
        }

        private static DiscoveredModelDependency CreateDependency(
            string packageId = "tools.doji.optional",
            bool optional = true,
            bool modelEnabled = false,
            string revision = "main",
            ModelDependencyFileDefinition[] files = null)
        {
            return new DiscoveredModelDependency(
                packageId,
                "Optional Package",
                "1.0.0",
                $"/tmp/{packageId}",
                $"/tmp/{packageId}/model-hub-manifest.json",
                new ModelDependencyDefinition(
                    Repository,
                    revision,
                    files ?? new[] { new ModelDependencyFileDefinition(ModelFile) },
                    optional),
                null,
                modelEnabled);
        }

        private sealed class FakeSource : IProjectOptionalFileSelectionSource
        {
            public HashSet<(string OwnerId, string ModelId)> Enabled { get; } = new();

            public bool IsModelEnabled(string ownerId, string modelId)
            {
                return Enabled.Contains((ownerId, modelId));
            }

            public void SetModelEnabled(string ownerId, string modelId, bool isEnabled)
            {
                if (isEnabled)
                {
                    Enabled.Add((ownerId, modelId));
                }
                else
                {
                    Enabled.Remove((ownerId, modelId));
                }
            }

            public string[] GetEnabledOptionalFiles(string ownerId, string modelId, IReadOnlyList<string> declaredOptionalFiles)
            {
                return Array.Empty<string>();
            }

            public void SetEnabledOptionalFiles(string ownerId, string modelId, IReadOnlyList<string> enabledOptionalFiles)
            {
            }
        }
    }
}
