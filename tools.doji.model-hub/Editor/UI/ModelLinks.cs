using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// One link of the model links row: a label, the page it opens, and a tooltip.
    /// </summary>
    internal readonly struct ModelLink
    {
        public ModelLink(string label, string url, string tooltip)
        {
            Label = label;
            Url = url;
            Tooltip = tooltip;
        }

        public string Label { get; }
        public string Url { get; }
        public string Tooltip { get; }
    }

    /// <summary>
    /// Builds the links shown for a model, like Package Manager's Documentation, Changelog, and Licenses links:
    /// the model card, the repository files, and the license.
    /// </summary>
    internal static class ModelLinks
    {
        private static readonly string[] LicenseFileNames = { "LICENSE", "LICENCE", "LICENSE.md", "LICENSE.txt", "LICENCE.md", "LICENCE.txt" };

        /// <summary>
        /// Model card, Files, and License links for one repository revision.
        /// </summary>
        /// <param name="revision">The commit when known, else the requested revision. Files and license links open it.</param>
        /// <param name="repoPaths">Repository file paths, to find a license file. May be empty.</param>
        public static IReadOnlyList<ModelLink> Build(
            string repository,
            string revision,
            ModelCardMetadata modelCard,
            IEnumerable<string> repoPaths)
        {
            return new[]
            {
                new ModelLink("Model card", GetModelCardUrl(repository), "Open the model card on Hugging Face."),
                new ModelLink("Files", $"{GetModelCardUrl(repository)}/tree/{EscapeRevision(revision)}", "Browse the model's files on Hugging Face."),
                GetLicenseLink(repository, revision, modelCard, repoPaths)
            };
        }

        /// <summary>
        /// The license link. Its label names the license the model card declares, and it opens the license text:
        /// the card's <c>license_link</c>, else a license file in the repository, else the model card.
        /// </summary>
        public static ModelLink GetLicenseLink(
            string repository,
            string revision,
            ModelCardMetadata modelCard,
            IEnumerable<string> repoPaths)
        {
            var modelCardUrl = GetModelCardUrl(repository);
            if (modelCard == null)
            {
                return new ModelLink("License", modelCardUrl, "Open the model card to read the license.");
            }

            var url = GetLicenseTextUrl(repository, revision, modelCard.LicenseLink, repoPaths) ?? modelCardUrl;
            if (string.IsNullOrWhiteSpace(modelCard.License))
            {
                return new ModelLink(
                    "No license",
                    url,
                    "The model card declares no license. Check the model card before you use or ship this model.");
            }

            var name = string.Equals(modelCard.License, "other", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(modelCard.LicenseName)
                ? modelCard.LicenseName
                : modelCard.License;
            return new ModelLink(
                $"License: {name}",
                url,
                url == modelCardUrl
                    ? "The license declared by the model card. Open the model card to read it."
                    : "The license declared by the model card. Open the license text.");
        }

        public static string GetModelCardUrl(string repository)
        {
            return $"{HubEndpoint.Url}/{repository}";
        }

        private static string GetLicenseTextUrl(string repository, string revision, string licenseLink, IEnumerable<string> repoPaths)
        {
            if (!string.IsNullOrWhiteSpace(licenseLink))
            {
                var link = licenseLink.Trim();
                if (Uri.TryCreate(link, UriKind.Absolute, out var absolute) &&
                    (absolute.Scheme == Uri.UriSchemeHttps || absolute.Scheme == Uri.UriSchemeHttp))
                {
                    return absolute.AbsoluteUri;
                }

                var repoPath = link.Replace('\\', '/').TrimStart('.', '/');
                if (repoPath.Length > 0 && !repoPath.Contains(':'))
                {
                    return GetBlobUrl(repository, revision, repoPath);
                }
            }

            var licenseFile = (repoPaths ?? Array.Empty<string>())
                .Where(path => !string.IsNullOrEmpty(path) && path.IndexOf('/') < 0)
                .FirstOrDefault(path => LicenseFileNames.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase));
            return licenseFile == null ? null : GetBlobUrl(repository, revision, licenseFile);
        }

        private static string GetBlobUrl(string repository, string revision, string repoPath)
        {
            var escapedPath = string.Join("/", repoPath.Split('/').Select(Uri.EscapeDataString));
            return $"{GetModelCardUrl(repository)}/blob/{EscapeRevision(revision)}/{escapedPath}";
        }

        private static string EscapeRevision(string revision)
        {
            return Uri.EscapeDataString(string.IsNullOrWhiteSpace(revision) ? "main" : revision);
        }
    }
}
