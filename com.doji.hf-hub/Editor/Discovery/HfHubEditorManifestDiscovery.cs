using System.Linq;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Combines package and project manifest discovery into the editor-facing dependency scan.
    /// </summary>
    internal static class HfHubEditorManifestDiscovery
    {
        public static HfHubDiscoveryReport DiscoverInstalledManifests()
        {
            var packageReport = HfHubPackageDiscovery.DiscoverInstalledPackageManifests();
            var projectReport = HfHubProjectManifestDiscovery.DiscoverProjectManifests();

            return new HfHubDiscoveryReport(
                packageReport.Dependencies
                    .Concat(projectReport.Dependencies)
                    .OrderBy(dependency => dependency.PackageId, System.StringComparer.Ordinal)
                    .ThenBy(dependency => dependency.ModelId, System.StringComparer.Ordinal)
                    .ToArray(),
                packageReport.Issues
                    .Concat(projectReport.Issues)
                    .OrderBy(issue => issue.PackageId, System.StringComparer.Ordinal)
                    .ThenBy(issue => issue.ManifestPath, System.StringComparer.Ordinal)
                    .ToArray());
        }
    }
}
