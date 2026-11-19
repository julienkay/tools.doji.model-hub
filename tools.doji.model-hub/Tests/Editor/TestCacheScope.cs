using System;
using System.IO;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Points the Hugging Face cache and AI Model Hub's index at temporary folders for one test,
    /// and restores the previous values on dispose. Tests never touch the real caches.
    /// </summary>
    internal sealed class TestCacheScope : IDisposable
    {
        private readonly string _previousHubCache;
        private readonly string _previousDataRoot;
        private readonly IDisposable _settingsOverride;

        public TestCacheScope(string cacheRoot)
        {
            CacheRoot = cacheRoot;
            DataRoot = cacheRoot + "-modelhub-data";
            _previousHubCache = Environment.GetEnvironmentVariable(CachePaths.HubCacheEnvironmentVariable);
            _previousDataRoot = Environment.GetEnvironmentVariable(CachePaths.DataRootEnvironmentVariable);
            Environment.SetEnvironmentVariable(CachePaths.HubCacheEnvironmentVariable, CacheRoot);
            Environment.SetEnvironmentVariable(CachePaths.DataRootEnvironmentVariable, DataRoot);
            _settingsOverride = CacheFolderSettings.PushOverride(null, null);
        }

        public string CacheRoot { get; }
        public string DataRoot { get; }

        public void Dispose()
        {
            _settingsOverride.Dispose();
            Environment.SetEnvironmentVariable(CachePaths.HubCacheEnvironmentVariable, _previousHubCache);
            Environment.SetEnvironmentVariable(CachePaths.DataRootEnvironmentVariable, _previousDataRoot);
            if (Directory.Exists(DataRoot))
            {
                Directory.Delete(DataRoot, true);
            }
        }
    }
}
