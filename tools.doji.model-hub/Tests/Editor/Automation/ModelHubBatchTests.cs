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
