using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Keeps the last manifest scan for lookups that run per file, such as manifest conversion defaults. A project
    /// change, a package change, or a manifest write clears it.
    /// </summary>
    [InitializeOnLoad]
    internal static class DiscoveredDependencyCache
    {
        private static IReadOnlyList<DiscoveredModelDependency> _dependencies;

        static DiscoveredDependencyCache()
        {
            EditorApplication.projectChanged += Invalidate;
            UnityEditor.PackageManager.Events.registeredPackages += _ => Invalidate();
        }

        public static IReadOnlyList<DiscoveredModelDependency> Dependencies =>
            _dependencies ??= EditorManifestDiscovery.DiscoverInstalledManifests().Dependencies.ToArray();

        public static void Invalidate()
        {
            _dependencies = null;
        }
    }
}
