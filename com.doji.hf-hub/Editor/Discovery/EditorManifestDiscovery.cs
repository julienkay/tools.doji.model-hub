using System.Linq;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Combines package and project manifest discovery into the editor-facing dependency scan.
    /// </summary>
    internal static class EditorManifestDiscovery
    {
        public static DiscoveryReport DiscoverInstalledManifests()
        {
            var packageReport = PackageDiscovery.DiscoverInstalledPackageManifests();
            var projectReport = ProjectManifestDiscovery.DiscoverProjectManifests();

            return new DiscoveryReport(
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
