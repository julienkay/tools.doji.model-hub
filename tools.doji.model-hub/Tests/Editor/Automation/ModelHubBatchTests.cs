using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers the exit codes and argument parsing of the <c>-executeMethod</c> entry points.
    /// </summary>
    internal sealed class ModelHubBatchTests
    {
        /// <summary>
        /// Verifies a sync that ran exits with 0 only when it succeeded.
        /// </summary>
        [TestCase(SyncRunStates.Succeeded, ModelHubBatch.Success)]
        [TestCase(SyncRunStates.Failed, ModelHubBatch.NotReady)]
        [TestCase(SyncRunStates.Canceled, ModelHubBatch.NotReady)]
        [TestCase(SyncRunStates.Interrupted, ModelHubBatch.NotReady)]
        public void SyncExitCode_FollowsRunState(string state, int expected)
        {
            var result = new AutomationSyncStartResult { Started = true, Run = new SyncRunRecord { State = state } };

            Assert.That(ModelHubBatch.GetExitCode(result), Is.EqualTo(expected));
        }

        /// <summary>
        /// Verifies a sync that started nothing exits with 0 when every model is ready, and with 1 when a model was
        /// left out for another reason.
        /// </summary>
        [TestCase("ready", ModelHubBatch.Success)]
        [TestCase("not_enabled", ModelHubBatch.Success)]
        [TestCase("revision_conflict", ModelHubBatch.NotReady)]
        [TestCase("selection_required", ModelHubBatch.NotReady)]
        public void SyncExitCode_WhenNothingStarted_FollowsSkipReason(string reason, int expected)
        {
            var result = new AutomationSyncStartResult
            {
                Dependencies = new[]
                {
                    new AutomationSyncPlanItem { Action = "skip", Reason = "ready" },
                    new AutomationSyncPlanItem { Action = "skip", Reason = reason }
                }
            };

            Assert.That(ModelHubBatch.GetExitCode(result), Is.EqualTo(expected));
        }

        /// <summary>
        /// Verifies sync refuses <c>-quit</c>: Unity then waits without Editor updates, and the sync hangs until
        /// Unity's 300-second timeout.
        /// </summary>
        [TestCase("-quit")]
        [TestCase("-Quit")]
        public void SyncArguments_WithQuit_AreRejected(string quit)
        {
            var args = new[] { "Unity.exe", "-batchmode", quit, "-executeMethod", "Doji.AI.ModelHub.Editor.ModelHubBatch.Sync" };

            Assert.That(ModelHubBatch.GetSyncArgumentProblem(args), Does.Contain("without -quit"));
        }

        /// <summary>
        /// Verifies sync accepts the documented arguments, where the method exits the Editor itself.
        /// </summary>
        [Test]
        public void SyncArguments_WithoutQuit_AreAccepted()
        {
            var args = new[] { "Unity.exe", "-batchmode", "-executeMethod", "Doji.AI.ModelHub.Editor.ModelHubBatch.Sync", "-modelHubOutput", "sync.json" };

            Assert.That(ModelHubBatch.GetSyncArgumentProblem(args), Is.Null);
        }

        /// <summary>
        /// Verifies status exits with 0 when every enabled model is ready, ignoring optional models that are off.
        /// </summary>
        [Test]
        public void StatusExitCode_IgnoresModelsThatAreNotEnabled()
        {
            var result = new AutomationStatusResult
            {
                Dependencies = new[]
                {
                    new AutomationDependencyInfo { Enabled = true, State = "Ready" },
                    new AutomationDependencyInfo { Enabled = false, State = "NotEnabled" }
                }
            };

            Assert.That(ModelHubBatch.GetExitCode(result), Is.EqualTo(ModelHubBatch.Success));
        }

        /// <summary>
        /// Verifies status exits with 1 when an enabled model is not ready or a manifest could not be read.
        /// </summary>
        [Test]
        public void StatusExitCode_WhenModelMissingOrManifestBroken_IsNotReady()
        {
            var missing = new AutomationStatusResult
            {
                Dependencies = new[] { new AutomationDependencyInfo { Enabled = true, State = "Missing" } }
            };
            var broken = new AutomationStatusResult
            {
                DiscoveryIssues = new[] { new AutomationIssueInfo { Message = "bad json" } }
            };

            Assert.That(ModelHubBatch.GetExitCode(missing), Is.EqualTo(ModelHubBatch.NotReady));
            Assert.That(ModelHubBatch.GetExitCode(broken), Is.EqualTo(ModelHubBatch.NotReady));
        }

        /// <summary>
        /// Verifies -modelHubEnabled takes true or false in any case, and refuses a missing or other value.
        /// </summary>
        [Test]
        public void ParseEnabled_AcceptsTrueOrFalseOnly()
        {
            Assert.That(ModelHubBatch.ParseEnabled("true"), Is.True);
            Assert.That(ModelHubBatch.ParseEnabled("False"), Is.False);
            Assert.Throws<ModelHubAutomationException>(() => ModelHubBatch.ParseEnabled(null));
            Assert.Throws<ModelHubAutomationException>(() => ModelHubBatch.ParseEnabled("yes"));
        }

        /// <summary>
        /// Verifies an argument's value is read case-insensitively, and a missing or trailing argument gives null.
        /// </summary>
        [Test]
        public void GetArgument_ReadsValueAfterName()
        {
            var args = new[] { "Unity.exe", "-batchmode", "-ModelHubRepo", "owner/model", "-modelHubMode" };

            Assert.That(ModelHubBatch.GetArgument(args, "-modelHubRepo"), Is.EqualTo("owner/model"));
            Assert.That(ModelHubBatch.GetArgument(args, "-modelHubMode"), Is.Null);
            Assert.That(ModelHubBatch.GetArgument(args, "-modelHubOutput"), Is.Null);
        }
    }
}
