using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers user-facing console report formatting from editor operations.
    /// </summary>
    internal sealed class EditorOperationsTests
    {
        /// <summary>
        /// Verifies status console output hides stable project owner ids behind a human-readable label.
        /// </summary>
        [Test]
        public void BuildStatusConsoleReport_UsesProjectLabelForProjectManifestOwners()
        {
            var projectOwnerId = CreateProjectOwnerId();
            var report = new StatusReport(
                new[]
                {
                    new ManifestDiscoveryIssue(projectOwnerId, "Assets/AI Model Hub/AI Model Hub Project Manifest.asset", "Issue")
                },
                new[]
                {
                    CreateDependencyStatus(projectOwnerId, "Project", DependencyState.Missing, "Needs import")
                });

            var consoleReport = EditorOperations.BuildStatusConsoleReport(report);

            Assert.That(consoleReport, Does.Contain("- Project"));
            Assert.That(consoleReport, Does.Contain("- Project Manifest Asset: Issue"));
            Assert.That(consoleReport, Does.Not.Contain(projectOwnerId));
        }

        /// <summary>
        /// Verifies sync console output hides stable project owner ids behind the Project label.
        /// </summary>
        [Test]
        public void BuildSyncConsoleReport_UsesProjectLabelForProjectManifestOwners()
        {
            var projectOwnerId = CreateProjectOwnerId();
            var dependency = CreateDependency(projectOwnerId, "Project");
            var report = new SyncReport(
                new[]
                {
                    new ManifestDiscoveryIssue(projectOwnerId, "Assets/AI Model Hub/AI Model Hub Project Manifest.asset", "Issue")
                },
                new[]
                {
                    new ModelSyncResult(SyncOperationMode.Full, dependency, null, null, "Import failed")
                });

            var consoleReport = EditorOperations.BuildSyncConsoleReport(report);

            Assert.That(consoleReport, Does.Contain("- FAIL Project/doji/test-model"));
            Assert.That(consoleReport, Does.Contain("- Project Manifest Asset: Issue"));
            Assert.That(consoleReport, Does.Not.Contain(projectOwnerId));
        }

        /// <summary>
        /// Verifies unresolved-dependency reminder points users at the editor surfaces that can resolve it.
        /// </summary>
        [Test]
        public void BuildUnresolvedDependencyReminder_IncludesCountsAndResolutionLocations()
        {
            var projectOwnerId = CreateProjectOwnerId();
            var report = new StatusReport(
                new[]
                {
                    new ManifestDiscoveryIssue(projectOwnerId, "Assets/AI Model Hub/AI Model Hub Project Manifest.asset", "Issue")
                },
                new[]
                {
                    CreateDependencyStatus(projectOwnerId, "Project", DependencyState.Missing, "Needs import"),
                    CreateDependencyStatus("tools.doji.pkg", "Package", DependencyState.Stale, "Needs refresh")
                });

            var message = EditorOperations.BuildUnresolvedDependencyReminder(report);

            Assert.That(message, Does.Contain("AI Model Hub: some models need setup."));
            Assert.That(message, Does.Contain("(2 models, 1 manifest error)"));
            Assert.That(message, Does.Contain(EditorOperations.ProjectSettingsPath));
            Assert.That(message, Does.Contain("Window > AI Model Hub"));
        }

        /// <summary>
        /// Verifies singular unresolved reminder copy does not duplicate the trailing y.
        /// </summary>
        [Test]
        public void BuildUnresolvedDependencyReminder_UsesSingularDependencyLabel()
        {
            var projectOwnerId = CreateProjectOwnerId();
            var report = new StatusReport(
                System.Array.Empty<ManifestDiscoveryIssue>(),
                new[]
                {
                    CreateDependencyStatus(projectOwnerId, "Project", DependencyState.Missing, "Needs import")
                });

            var message = EditorOperations.BuildUnresolvedDependencyReminder(report);

            Assert.That(message, Does.Contain("(1 model)"));
            Assert.That(message, Does.Not.Contain("modell"));
        }

        /// <summary>
        /// Verifies startup reminder ignores optional ONNX choices that have not been enabled by the project.
        /// </summary>
        [Test]
        public void BuildUnresolvedDependencyReminder_IgnoresSelectionRequiredOnlyStatuses()
        {
            var projectOwnerId = CreateProjectOwnerId();
            var report = new StatusReport(
                System.Array.Empty<ManifestDiscoveryIssue>(),
                new[]
                {
                    CreateDependencyStatus(
                        projectOwnerId,
                        "Project",
                        DependencyState.Missing,
                        "Choose optional ONNX file.",
                        DependencyReasonCode.SelectionRequired)
                });

            var message = EditorOperations.BuildUnresolvedDependencyReminder(report);

            Assert.That(message, Is.EqualTo("AI Model Hub: every model is ready."));
        }

        /// <summary>
        /// Verifies enabled optional files still contribute to startup reminders once they are missing imports.
        /// </summary>
        [Test]
        public void BuildUnresolvedDependencyReminder_IncludesEnabledOptionalMissingStatuses()
        {
            var projectOwnerId = CreateProjectOwnerId();
            var report = new StatusReport(
                System.Array.Empty<ManifestDiscoveryIssue>(),
                new[]
                {
                    CreateDependencyStatus(
                        projectOwnerId,
                        "Project",
                        DependencyState.Missing,
                        "Not downloaded on this machine.",
                        DependencyReasonCode.RawCacheRevisionRefMissing)
                });

            var message = EditorOperations.BuildUnresolvedDependencyReminder(report);

            Assert.That(message, Does.Contain("(1 model)"));
        }

        private static DependencyStatus CreateDependencyStatus(
            string packageId,
            string packageDisplayName,
            DependencyState state,
            string maintainerMessage,
            DependencyReasonCode? reasonCode = null)
        {
            return new DependencyStatus(
                CreateDependency(packageId, packageDisplayName),
                state,
                reasonCode ?? (state == DependencyState.Ready ? DependencyReasonCode.None : DependencyReasonCode.RawCacheRevisionRefMissing),
                maintainerMessage,
                null,
                null,
                null,
                null,
                null,
                null,
                null);
        }

        private static DiscoveredModelDependency CreateDependency(string packageId, string packageDisplayName)
        {
            return new DiscoveredModelDependency(
                packageId,
                packageDisplayName,
                "project",
                $"/tmp/{packageId}",
                "Assets/AI Model Hub/AI Model Hub Project Manifest.asset",
                new ModelDependencyDefinition(
                    "doji/test-model",
                    "main"));
        }

        private static string CreateProjectOwnerId()
        {
            return ProjectManifestDiscovery.GetOwnerId(
                "5a899f24b6fd4a1ba127ab1a1b267c7e",
                "Assets/AI Model Hub/AI Model Hub Project Manifest.asset",
                null);
        }
    }
}
