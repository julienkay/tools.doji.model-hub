using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers project-scoped Model Hub settings that affect cache path resolution.
    /// </summary>
    internal sealed class ProjectSettingsTests
    {
        /// <summary>
        /// Verifies cache root uses project settings when environment override is absent.
        /// </summary>
        [Test]
        public void GetCacheRoot_ReturnsProjectSettingOverrideWhenConfigured()
        {
            var previousEnvironment = System.Environment.GetEnvironmentVariable("DOJI_HF_HUB_CACHE_ROOT");
            System.Environment.SetEnvironmentVariable("DOJI_HF_HUB_CACHE_ROOT", null);

            using (ProjectSettings.PushOverride(false, "Library/../Temp/Cache"))
            {
                var cacheRoot = CachePaths.GetCacheRoot();

                Assert.That(cacheRoot.Replace('\\', '/'), Does.EndWith("/Temp/Cache"));
            }

            System.Environment.SetEnvironmentVariable("DOJI_HF_HUB_CACHE_ROOT", previousEnvironment);
        }

        /// <summary>
        /// Verifies environment variable still takes precedence over project settings.
        /// </summary>
        [Test]
        public void GetCacheRoot_PrefersEnvironmentVariableOverProjectSetting()
        {
            var previousEnvironment = System.Environment.GetEnvironmentVariable("DOJI_HF_HUB_CACHE_ROOT");
            System.Environment.SetEnvironmentVariable("DOJI_HF_HUB_CACHE_ROOT", "/tmp/env-cache");

            using (ProjectSettings.PushOverride(false, "/tmp/project-cache"))
            {
                Assert.That(CachePaths.GetCacheRoot(), Is.EqualTo("/tmp/env-cache"));
            }

            System.Environment.SetEnvironmentVariable("DOJI_HF_HUB_CACHE_ROOT", previousEnvironment);
        }
    }
}
