using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers project-scoped Model Hub settings that affect cache path resolution.
    /// </summary>
    internal sealed class ProjectSettingsTests
    {
        /// <summary>
        /// A custom folder in Project Settings is the cache root, even when HF_HUB_CACHE is set.
        /// </summary>
        [Test]
        public void GetCacheRoot_ReturnsProjectSettingOverrideWhenConfigured()
        {
            var previous = System.Environment.GetEnvironmentVariable(CachePaths.HubCacheEnvironmentVariable);
            System.Environment.SetEnvironmentVariable(CachePaths.HubCacheEnvironmentVariable, "/tmp/env-cache");
            try
            {
                using (ProjectSettings.PushOverride(false, "/tmp/custom-cache"))
                {
                    Assert.That(CachePaths.GetCacheRoot(), Is.EqualTo(System.IO.Path.GetFullPath("/tmp/custom-cache")));
                }
            }
            finally
            {
                System.Environment.SetEnvironmentVariable(CachePaths.HubCacheEnvironmentVariable, previous);
            }
        }

        /// <summary>
        /// The default folder follows HF_HUB_CACHE, like the huggingface_hub Python library.
        /// </summary>
        [Test]
        public void GetCacheRoot_UsesHubCacheEnvironmentVariableForDefault()
        {
            var previous = System.Environment.GetEnvironmentVariable(CachePaths.HubCacheEnvironmentVariable);
            System.Environment.SetEnvironmentVariable(CachePaths.HubCacheEnvironmentVariable, "/tmp/env-cache");
            try
            {
                using (ProjectSettings.PushOverride(true, null))
                {
                    Assert.That(CachePaths.GetCacheRoot(), Is.EqualTo("/tmp/env-cache"));
                    Assert.That(CachePaths.GetDefaultCacheRootSource(), Is.EqualTo(CachePaths.HubCacheEnvironmentVariable));
                }
            }
            finally
            {
                System.Environment.SetEnvironmentVariable(CachePaths.HubCacheEnvironmentVariable, previous);
            }
        }

        /// <summary>
        /// Without HF_HUB_CACHE, the default folder is the hub folder inside HF_HOME.
        /// </summary>
        [Test]
        public void GetDefaultCacheRoot_UsesHubFolderInsideHfHome()
        {
            var previousCache = System.Environment.GetEnvironmentVariable(CachePaths.HubCacheEnvironmentVariable);
            var previousLegacy = System.Environment.GetEnvironmentVariable(CachePaths.LegacyHubCacheEnvironmentVariable);
            var previousHome = System.Environment.GetEnvironmentVariable(CachePaths.HomeEnvironmentVariable);
            System.Environment.SetEnvironmentVariable(CachePaths.HubCacheEnvironmentVariable, null);
            System.Environment.SetEnvironmentVariable(CachePaths.LegacyHubCacheEnvironmentVariable, null);
            System.Environment.SetEnvironmentVariable(CachePaths.HomeEnvironmentVariable, "/tmp/hf-home");
            try
            {
                Assert.That(CachePaths.GetDefaultCacheRoot(), Is.EqualTo(System.IO.Path.Combine("/tmp/hf-home", "hub")));
            }
            finally
            {
                System.Environment.SetEnvironmentVariable(CachePaths.HubCacheEnvironmentVariable, previousCache);
                System.Environment.SetEnvironmentVariable(CachePaths.LegacyHubCacheEnvironmentVariable, previousLegacy);
                System.Environment.SetEnvironmentVariable(CachePaths.HomeEnvironmentVariable, previousHome);
            }
        }
    }
}
