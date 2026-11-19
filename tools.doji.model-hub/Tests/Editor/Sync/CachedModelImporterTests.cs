using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Doji.Downloads;
using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers which models are set up from the cache without a download: only cheap setups without consent, every
    /// cached model when a user action allows conversions, and failures reported per model.
    /// </summary>
    internal sealed class CachedModelImporterTests
    {
        /// <summary>
        /// Verifies automatic setup imports only models that need no conversion and never touches downloads.
        /// </summary>
        [Test]
        public void Import_WithoutConversion_ImportsOnlyCheapSetups()
        {
            var cheap = Status("doji/cheap", DependencyReasonCode.ImportBindingMissing);
            var convert = Status("doji/convert", DependencyReasonCode.ImportBindingStale, new[] { "model.onnx" });
            var download = Status("doji/download", DependencyReasonCode.RawCacheRevisionRefMissing);
            var ready = Status("doji/ready", DependencyReasonCode.None, state: DependencyState.Ready);
            var rawCache = new FakeRawCacheService();
            var importer = new FakeImportService();

            var result = CachedModelImporter.Import(Report(cheap, convert, download, ready), allowConversion: false, rawCache, importer);

            Assert.That(result.Imported, Is.EqualTo(new[] { cheap.Dependency }));
            Assert.That(result.Failures, Is.Empty);
            Assert.That(importer.Calls, Is.EqualTo(new[] { ("doji/cheap", false) }));
            Assert.That(rawCache.CachedRepositories, Is.EqualTo(new[] { "doji/cheap" }));
            Assert.That(rawCache.DownloadCount, Is.Zero);
        }

        /// <summary>
        /// Verifies a user action, such as a build, converts every cached model but still downloads nothing.
        /// </summary>
        [Test]
        public void Import_WithConversion_ConvertsCachedModelsWithoutDownloading()
        {
            var cheap = Status("doji/cheap", DependencyReasonCode.SharedImportMetadataInvalid);
            var convert = Status("doji/convert", DependencyReasonCode.ImportBindingMissing, new[] { "model.onnx" });
            var download = Status("doji/download", DependencyReasonCode.RawCacheFilesMissing);
            var rawCache = new FakeRawCacheService();
            var importer = new FakeImportService();
            var progress = new List<(string Repository, int Index, int Count)>();

            var result = CachedModelImporter.Import(
                Report(cheap, convert, download),
                allowConversion: true,
                rawCache,
                importer,
                (dependency, index, count) => progress.Add((dependency.Repository, index, count)));

            Assert.That(result.Imported.Select(dependency => dependency.Repository), Is.EqualTo(new[] { "doji/cheap", "doji/convert" }));
            Assert.That(importer.Calls, Is.EqualTo(new[] { ("doji/cheap", true), ("doji/convert", true) }));
            Assert.That(progress, Is.EqualTo(new[] { ("doji/cheap", 0, 2), ("doji/convert", 1, 2) }));
            Assert.That(rawCache.DownloadCount, Is.Zero);
        }

        /// <summary>
        /// Verifies a model whose store entry vanished after the status check is left for the prompt, not converted.
        /// </summary>
        [Test]
        public void Import_WithoutConversion_LeavesModelsThatTurnOutToNeedConversion()
        {
            var cheap = Status("doji/cheap", DependencyReasonCode.ImportBindingMissing);
            var importer = new FakeImportService { RequireConversion = { "doji/cheap" } };

            var result = CachedModelImporter.Import(Report(cheap), allowConversion: false, new FakeRawCacheService(), importer);

            Assert.That(result.Imported, Is.Empty);
            Assert.That(result.NeedConversion, Is.EqualTo(new[] { cheap.Dependency }));
            Assert.That(result.Failures, Is.Empty);
        }

        /// <summary>
        /// Verifies one failing model is reported with its name and reason, and the others still import.
        /// </summary>
        [Test]
        public void Import_ReportsFailuresPerModelAndContinues()
        {
            var broken = Status("doji/broken", DependencyReasonCode.ImportBindingMissing);
            var fine = Status("doji/fine", DependencyReasonCode.ImportBindingMissing);
            var importer = new FakeImportService { Failing = { "doji/broken" } };

            var result = CachedModelImporter.Import(Report(broken, fine), allowConversion: false, new FakeRawCacheService(), importer);

            Assert.That(result.Imported, Is.EqualTo(new[] { fine.Dependency }));
            Assert.That(result.Failures.Single(), Does.Contain("doji/broken"));
            Assert.That(result.Failures.Single(), Does.Contain("Simulated import failure"));
        }

        /// <summary>
        /// Verifies an empty or missing report imports nothing.
        /// </summary>
        [Test]
        public void Import_HandlesAnEmptyReport()
        {
            var importer = new FakeImportService();

            var result = CachedModelImporter.Import(null, allowConversion: true, new FakeRawCacheService(), importer);

            Assert.That(result.Imported, Is.Empty);
            Assert.That(importer.Calls, Is.Empty);
        }

        /// <summary>
        /// Verifies automatic setup does nothing while suspended, which keeps tests in control of import records.
        /// </summary>
        [Test]
        public void AutoImport_DoesNothingWhileSuspended()
        {
            Assert.That(AutoImport.Suspended, Is.True, "The test assembly suspends automatic imports.");
            Assert.That(AutoImport.Run(), Is.Zero);
        }

        private static StatusReport Report(params DependencyStatus[] statuses)
        {
            return new StatusReport(Array.Empty<ManifestDiscoveryIssue>(), statuses);
        }

        private static DependencyStatus Status(
            string repository,
            DependencyReasonCode reasonCode,
            string[] pendingConversions = null,
            DependencyState state = DependencyState.Missing)
        {
            var dependency = new DiscoveredModelDependency(
                "tools.doji.test",
                "Test Package",
                "1.0.0",
                "/tmp/tools.doji.test",
                "/tmp/tools.doji.test/model-hub-manifest.json",
                new ModelDependencyDefinition(repository, "main"));
            return new DependencyStatus(
                dependency,
                state,
                reasonCode,
                reasonCode.ToString(),
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                pendingConversions,
                ConversionCause.FirstConversion);
        }

        private sealed class FakeRawCacheService : IRawCacheService
        {
            public List<string> CachedRepositories { get; } = new();
            public int DownloadCount { get; private set; }

            public Task<CachedSnapshot> GetOrDownloadAsync(
                DiscoveredModelDependency dependency,
                IProgress<DownloadProgress> progress,
                CancellationToken cancellationToken)
            {
                DownloadCount++;
                return Task.FromResult(CreateSnapshot(dependency));
            }

            public CachedSnapshot GetCachedOrThrow(DiscoveredModelDependency dependency)
            {
                CachedRepositories.Add(dependency.Repository);
                return CreateSnapshot(dependency);
            }

            private static CachedSnapshot CreateSnapshot(DiscoveredModelDependency dependency)
            {
                return new CachedSnapshot(
                    "/cache/snapshot",
                    "/cache/snapshot/snapshot.modelhub.json",
                    new RawCacheMetadata
                    {
                        Repository = dependency.Repository,
                        RequestedRevision = dependency.Revision,
                        ResolvedCommitHash = "abc123",
                        CachedSnapshotRoot = "/cache/snapshot",
                        Files = Array.Empty<RawCacheFileMetadata>()
                    });
            }
        }

        private sealed class FakeImportService : IProjectImportService
        {
            public List<(string Repository, bool AllowConversion)> Calls { get; } = new();
            public HashSet<string> Failing { get; } = new(StringComparer.Ordinal);
            public HashSet<string> RequireConversion { get; } = new(StringComparer.Ordinal);

            public Task<ImportedModelArtifact> ImportAsync(
                DiscoveredModelDependency dependency,
                CachedSnapshot cachedSnapshot,
                CancellationToken cancellationToken,
                bool allowConversion = true)
            {
                Calls.Add((dependency.Repository, allowConversion));
                if (Failing.Contains(dependency.Repository))
                {
                    throw new InvalidOperationException("Simulated import failure");
                }

                if (!allowConversion && RequireConversion.Contains(dependency.Repository))
                {
                    throw new ConversionRequiredException(dependency.Repository, new[] { "model.onnx" });
                }

                return Task.FromResult(new ImportedModelArtifact(
                    "binding.json",
                    new ImportedModelMetadata { Repository = dependency.Repository, RuntimeFiles = Array.Empty<ImportedRuntimeFileMetadata>() },
                    "shared.json"));
            }
        }
    }
}
