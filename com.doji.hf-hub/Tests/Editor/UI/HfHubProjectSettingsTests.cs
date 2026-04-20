using NUnit.Framework;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Covers project-scoped Model Hub settings that affect cache path resolution.
    /// </summary>
    internal sealed class HfHubProjectSettingsTests
    {
        /// <summary>
        /// Verifies cache root uses project settings when environment override is absent.
        /// </summary>
        [Test]
        public void GetCacheRoot_ReturnsProjectSettingOverrideWhenConfigured()
        {
            var previousEnvironment = System.Environment.GetEnvironmentVariable("DOJI_HF_HUB_CACHE_ROOT");
            System.Environment.SetEnvironmentVariable("DOJI_HF_HUB_CACHE_ROOT", null);

            using (HfHubProjectSettings.PushOverride(false, "Library/../Temp/HfHubCache"))
            {
                var cacheRoot = HfHubCachePaths.GetCacheRoot();

                Assert.That(cacheRoot.Replace('\\', '/'), Does.EndWith("/Temp/HfHubCache"));
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

            using (HfHubProjectSettings.PushOverride(false, "/tmp/project-cache"))
            {
                Assert.That(HfHubCachePaths.GetCacheRoot(), Is.EqualTo("/tmp/env-cache"));
            }

            System.Environment.SetEnvironmentVariable("DOJI_HF_HUB_CACHE_ROOT", previousEnvironment);
        }
    }
}
