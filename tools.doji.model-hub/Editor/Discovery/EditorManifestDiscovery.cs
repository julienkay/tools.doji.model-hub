using System;
using System.Collections.Generic;
using System.Linq;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Resolves project-owned choices for one manifest owner and model id: whether an optional model is enabled, and
    /// which optional files are turned on.
    /// </summary>
    internal interface IProjectOptionalFileSelectionSource
    {
        bool IsModelEnabled(string ownerId, string modelId);
        void SetModelEnabled(string ownerId, string modelId, bool isEnabled);
        string[] GetEnabledOptionalFiles(string ownerId, string modelId, IReadOnlyList<string> declaredOptionalFiles);
        void SetEnabledOptionalFiles(string ownerId, string modelId, IReadOnlyList<string> enabledOptionalFiles);
    }

    /// <summary>
    /// Enables or disables one optional model of one owner. The Model Hub window, the load prompt, and the command line
    /// all change it through here.
    /// </summary>
    internal static class OptionalModelSelection
    {
        /// <summary>
        /// Enables or disables the optional model of <paramref name="dependency"/> for its owner. Returns false when the
        /// model already had this state. Throws for a model the manifest does not mark optional.
        /// </summary>
        public static bool SetModelEnabled(
            this IProjectOptionalFileSelectionSource source,
            DiscoveredModelDependency dependency,
            bool isEnabled)
        {
            if (!dependency.IsOptionalModel)
            {
                throw new InvalidOperationException(
                    $"Model '{dependency.ModelId}' of {dependency.PackageId} is not optional. Only a model that its manifest marks \"optional\": true can be enabled or disabled.");
            }

            if (dependency.IsEnabled == isEnabled)
            {
                return false;
            }

            source.SetModelEnabled(dependency.PackageId, dependency.ModelId, isEnabled);
            return true;
        }
    }

    /// <summary>
    /// Turns one optional file of one owner on or off. The Model Hub window, the model picker, and the command line
    /// all change selections through here.
    /// </summary>
    internal static class OptionalFileSelection
    {
        /// <summary>
        /// Turns <paramref name="repoPath"/> on or off for the owner of <paramref name="dependency"/> and keeps the
        /// owner's other optional files as they are. Returns false when the selection already matched.
        /// </summary>
        public static bool SetEnabled(
            this IProjectOptionalFileSelectionSource source,
            DiscoveredModelDependency dependency,
            string repoPath,
            bool isEnabled)
        {
            var enabledFiles = dependency.GetEnabledOptionalRepoPaths().ToList();
            var wasEnabled = enabledFiles.Contains(repoPath, StringComparer.Ordinal);
            if (wasEnabled == isEnabled)
            {
                return false;
            }

            enabledFiles.RemoveAll(existing => string.Equals(existing, repoPath, StringComparison.Ordinal));
            if (isEnabled)
            {
                enabledFiles.Add(repoPath);
            }

            source.SetEnabledOptionalFiles(dependency.PackageId, dependency.ModelId, enabledFiles);
            return true;
        }
    }

    /// <summary>
    /// Loads and saves owner-scoped optional file selections in <see cref="ProjectSettings"/>.
    /// </summary>
    internal sealed class ProjectOptionalFileSelectionSource : IProjectOptionalFileSelectionSource
    {
        public bool IsModelEnabled(string ownerId, string modelId)
        {
            return ProjectSettings.instance.IsOptionalModelEnabled(ownerId, modelId);
        }

        public void SetModelEnabled(string ownerId, string modelId, bool isEnabled)
        {
            if (string.IsNullOrWhiteSpace(ownerId))
            {
                throw new ArgumentException("Owner id is required.", nameof(ownerId));
            }

            if (string.IsNullOrWhiteSpace(modelId))
            {
                throw new ArgumentException("Model id is required.", nameof(modelId));
            }

            ProjectSettings.instance.SetOptionalModelEnabled(ownerId, modelId, isEnabled);
        }

        public string[] GetEnabledOptionalFiles(string ownerId, string modelId, IReadOnlyList<string> declaredOptionalFiles)
        {
            var declaredSet = new HashSet<string>(
                ModelDependencyDefinition.NormalizeSelectedFiles(declaredOptionalFiles),
                StringComparer.Ordinal);
            return ProjectSettings.instance.GetEnabledOptionalFiles(ownerId, modelId)
                .Where(path => declaredSet.Contains(path))
                .ToArray();
        }

        public void SetEnabledOptionalFiles(string ownerId, string modelId, IReadOnlyList<string> enabledOptionalFiles)
        {
            if (string.IsNullOrWhiteSpace(ownerId))
            {
                throw new ArgumentException("Owner id is required.", nameof(ownerId));
            }

            if (string.IsNullOrWhiteSpace(modelId))
            {
                throw new ArgumentException("Model id is required.", nameof(modelId));
            }

            ProjectSettings.instance.SetEnabledOptionalFiles(
                ownerId,
                modelId,
                ModelDependencyDefinition.NormalizeSelectedFiles(enabledOptionalFiles));
        }
    }

    /// <summary>
    /// Combines package and project manifest discovery into the editor-facing dependency scan.
    /// </summary>
    internal static class EditorManifestDiscovery
    {
        public static DiscoveryReport DiscoverInstalledManifests()
        {
            var packageReport = PackageDiscovery.DiscoverInstalledPackageManifests();
            var projectReport = ProjectManifestDiscovery.DiscoverProjectManifests();

            return ApplyProjectOptionalSelections(
                new DiscoveryReport(
                    packageReport.Dependencies
                        .Concat(projectReport.Dependencies)
                        .OrderBy(dependency => dependency.PackageId, StringComparer.Ordinal)
                        .ThenBy(dependency => dependency.ModelId, StringComparer.Ordinal)
                        .ToArray(),
                    packageReport.Issues
                        .Concat(projectReport.Issues)
                        .OrderBy(issue => issue.PackageId, StringComparer.Ordinal)
                        .ThenBy(issue => issue.ManifestPath, StringComparer.Ordinal)
                        .ToArray()),
                new ProjectOptionalFileSelectionSource());
        }

        internal static DiscoveryReport ApplyProjectOptionalSelections(
            DiscoveryReport report,
            IProjectOptionalFileSelectionSource selectionSource)
        {
            if (report == null)
            {
                return new DiscoveryReport(Array.Empty<DiscoveredModelDependency>(), Array.Empty<ManifestDiscoveryIssue>());
            }

            if (selectionSource == null)
            {
                return report;
            }

            var dependencies = report.Dependencies
                .Select(dependency =>
                {
                    if (dependency == null || (!dependency.HasOptionalFiles && !dependency.IsOptionalModel))
                    {
                        return dependency;
                    }

                    var model = new ModelDependencyDefinition(
                        dependency.ModelId,
                        dependency.Revision,
                        dependency.FileEntries.ToArray(),
                        dependency.IsOptionalModel);
                    return new DiscoveredModelDependency(
                        dependency.PackageId,
                        dependency.PackageDisplayName,
                        dependency.PackageVersion,
                        dependency.PackageResolvedPath,
                        dependency.ManifestPath,
                        model,
                        dependency.HasOptionalFiles
                            ? selectionSource.GetEnabledOptionalFiles(
                                dependency.PackageId,
                                dependency.ModelId,
                                dependency.GetOptionalRepoPaths())
                            : null,
                        dependency.IsOptionalModel &&
                        selectionSource.IsModelEnabled(dependency.PackageId, dependency.ModelId));
                })
                .OrderBy(dependency => dependency.PackageId, StringComparer.Ordinal)
                .ThenBy(dependency => dependency.ModelId, StringComparer.Ordinal)
                .ToArray();

            return new DiscoveryReport(dependencies, report.Issues.ToArray());
        }
    }
}
