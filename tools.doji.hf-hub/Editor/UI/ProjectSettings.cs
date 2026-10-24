using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        [SerializeField]
        private ModelDelivery _defaultDelivery = ModelDelivery.Bundle;

        [SerializeField]
        private List<DeliveryOverride> _deliveryOverrides = new();

        private static OverrideScope _overrideScope;

        /// <summary>
        /// How builds deliver models that have no override: bundled into StreamingAssets, or downloaded at runtime.
        /// </summary>
        public ModelDelivery DefaultDelivery => _defaultDelivery;

        public void SetDefaultDelivery(ModelDelivery delivery)
        {
            _defaultDelivery = delivery;
            Save(true);
        }

        /// <summary>
        /// Returns the delivery override of one repository, or null when it follows the default.
        /// </summary>
        public ModelDelivery? GetDeliveryOverride(string repository)
        {
            var entry = _deliveryOverrides?.FirstOrDefault(item =>
                string.Equals(item?.repository, repository, StringComparison.OrdinalIgnoreCase));
            return entry?.delivery;
        }

        /// <summary>
        /// Sets or clears (with null) the delivery override of one repository.
        /// </summary>
        public void SetDeliveryOverride(string repository, ModelDelivery? delivery)
        {
            _deliveryOverrides ??= new List<DeliveryOverride>();
            _deliveryOverrides.RemoveAll(item => string.Equals(item?.repository, repository, StringComparison.OrdinalIgnoreCase));
            if (delivery.HasValue)
            {
                _deliveryOverrides.Add(new DeliveryOverride { repository = repository, delivery = delivery.Value });
                _deliveryOverrides.Sort((left, right) => string.CompareOrdinal(left.repository, right.repository));
            }

            Save(true);
        }

        public ModelDelivery GetEffectiveDelivery(string repository)
        {
            return GetDeliveryOverride(repository) ?? DefaultDelivery;
        }

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

        [Serializable]
        private sealed class DeliveryOverride
        {
            public string repository;
            public ModelDelivery delivery;
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

    /// <summary>
    /// Connects the runtime cache paths to this project's cache folder setting.
    /// </summary>
    [InitializeOnLoad]
    internal static class EditorCacheRoot
    {
        static EditorCacheRoot()
        {
            Register();
        }

        /// <summary>
        /// Makes <see cref="CachePaths.GetCacheRoot"/> use the Project Settings folder. Safe to call more than once.
        /// </summary>
        public static void Register()
        {
            CachePaths.CacheRootOverride ??= () => ProjectSettings.instance.GetEffectiveCacheLocation();
        }
    }
}
