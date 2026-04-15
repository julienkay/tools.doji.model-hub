using System.IO;
using System.Linq;
using UnityEditor.PackageManager;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Scans installed Unity packages for `hf-hub-manifest.json` files, then delegates shared manifest handling to runtime-safe discovery code.
    /// </summary>
    internal static class HfHubPackageDiscovery
    {
        /// <summary>
        /// Reads all installed package manifests that match the Hub manifest filename.
        /// </summary>
        public static HfHubDiscoveryReport DiscoverInstalledPackageManifests()
        {
            var packages = PackageInfo
                .GetAllRegisteredPackages()
                .Select(package => new HfHubInstalledPackageSnapshot(
                    package.name,
                    package.displayName,
                    package.version,
                    package.resolvedPath))
                .ToArray();

            return HfHubManifestDiscovery.DiscoverPackageManifests(packages);
        }
    }
}
