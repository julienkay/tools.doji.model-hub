using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers user-facing console report formatting from editor operations.
    /// </summary>
    internal sealed class EditorOperationsTests
    {
        /// <summary>
        /// Verifies status console output hides stable project owner ids behind the Project label.
        /// </summary>
        [Test]
        public void BuildStatusConsoleReport_UsesProjectLabelForProjectManifestOwners()
        {
            var projectOwnerId = CreateProjectOwnerId();
            var report = new StatusReport(
                new[]
                {
                    new ManifestDiscoveryIssue(projectOwnerId, "Assets/HF Hub/HF Hub Project Manifest.asset", "Issue")
                },
                new[]
                {
                    CreateDependencyStatus(projectOwnerId, "Project", DependencyState.Missing, "Needs import")
                });

            var consoleReport = EditorOperations.BuildStatusConsoleReport(report);

            Assert.That(consoleReport, Does.Contain("- Project"));
            Assert.That(consoleReport, Does.Contain("- Project: Issue"));
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
                    new ManifestDiscoveryIssue(projectOwnerId, "Assets/HF Hub/HF Hub Project Manifest.asset", "Issue")
                },
                new[]
                {
                    new ModelSyncResult(SyncOperationMode.Full, dependency, null, null, "Import failed")
                });

            var consoleReport = EditorOperations.BuildSyncConsoleReport(report);

            Assert.That(consoleReport, Does.Contain("- FAIL Project/doji/test-model"));
            Assert.That(consoleReport, Does.Contain("- Project: Issue"));
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
                    new ManifestDiscoveryIssue(projectOwnerId, "Assets/HF Hub/HF Hub Project Manifest.asset", "Issue")
                },
                new[]
                {
                    CreateDependencyStatus(projectOwnerId, "Project", DependencyState.Missing, "Needs import"),
                    CreateDependencyStatus("com.doji.pkg", "Package", DependencyState.Stale, "Needs refresh")
                });

            var message = EditorOperations.BuildUnresolvedDependencyReminder(report);

            Assert.That(message, Does.Contain("HF Hub: unresolved model dependencies."));
            Assert.That(message, Does.Contain("(2 dependencies, 1 discovery issue)"));
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

            Assert.That(message, Does.Contain("(1 dependency)"));
            Assert.That(message, Does.Not.Contain("dependencyy"));
        }

        private static DependencyStatus CreateDependencyStatus(
            string packageId,
            string packageDisplayName,
            DependencyState state,
            string maintainerMessage)
        {
            return new DependencyStatus(
                CreateDependency(packageId, packageDisplayName),
                state,
                state == DependencyState.Ready ? DependencyReasonCode.None : DependencyReasonCode.ImportBindingMissing,
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
                "Assets/HF Hub/HF Hub Project Manifest.asset",
                $"{packageId}-hash",
                new ModelDependencyDefinition(
                    "doji/test-model",
                    "main"));
        }

        private static string CreateProjectOwnerId()
        {
            return ProjectManifestDiscovery.GetOwnerId(
                "5a899f24b6fd4a1ba127ab1a1b267c7e",
                "Assets/HF Hub/HF Hub Project Manifest.asset",
                null);
        }
    }
}
