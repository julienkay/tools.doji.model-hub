using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Stores project-scoped editor configuration for Model Hub surfaces and cache resolution.
    /// </summary>
    [FilePath("ProjectSettings/Doji.AI.ModelHubSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class ProjectSettings : ScriptableSingleton<ProjectSettings>
    {
        [SerializeField]
        private bool _useDefaultCacheLocation = true;

        [SerializeField]
        private string _customCacheLocation = string.Empty;

        private static OverrideScope _overrideScope;

        /// <summary>
        /// Indicates whether cache resolution should use the platform default path.
        /// </summary>
        public bool UseDefaultCacheLocation => _overrideScope?.UseDefaultCacheLocation ?? _useDefaultCacheLocation;

        /// <summary>
        /// Stores custom project cache root when default cache location is disabled.
        /// </summary>
        public string CustomCacheLocation => _overrideScope?.CustomCacheLocation ?? _customCacheLocation;

        /// <summary>
        /// Stores cache settings and persists them under ProjectSettings.
        /// </summary>
        public void UpdateCacheLocation(bool useDefaultCacheLocation, string customCacheLocation)
        {
            _useDefaultCacheLocation = useDefaultCacheLocation;
            _customCacheLocation = NormalizeCustomCacheLocation(customCacheLocation);
            Save(true);
        }

        /// <summary>
        /// Returns effective cache root after applying project settings.
        /// </summary>
        public string GetEffectiveCacheLocation()
        {
            if (UseDefaultCacheLocation || string.IsNullOrWhiteSpace(CustomCacheLocation))
            {
                return CachePaths.GetDefaultCacheRoot();
            }

            return Path.GetFullPath(CustomCacheLocation);
        }

        internal static IDisposable PushOverride(bool useDefaultCacheLocation, string customCacheLocation)
        {
            var previous = _overrideScope;
            _overrideScope = new OverrideScope(useDefaultCacheLocation, NormalizeCustomCacheLocation(customCacheLocation));
            return new RestoreOverrideScope(previous);
        }

        private static string NormalizeCustomCacheLocation(string customCacheLocation)
        {
            return string.IsNullOrWhiteSpace(customCacheLocation)
                ? string.Empty
                : customCacheLocation.Trim();
        }

        private sealed class OverrideScope
        {
            public OverrideScope(bool useDefaultCacheLocation, string customCacheLocation)
            {
                UseDefaultCacheLocation = useDefaultCacheLocation;
                CustomCacheLocation = customCacheLocation;
            }

            public bool UseDefaultCacheLocation { get; }
            public string CustomCacheLocation { get; }
        }

        private sealed class RestoreOverrideScope : IDisposable
        {
            private readonly OverrideScope _previous;

            public RestoreOverrideScope(OverrideScope previous)
            {
                _previous = previous;
            }

            public void Dispose()
            {
                _overrideScope = _previous;
            }
        }
    }
}
