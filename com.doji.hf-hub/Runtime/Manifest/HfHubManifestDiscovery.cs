using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// Represents one resolved HuggingFace model dependency discovered from one manifest owner.
    /// </summary>
    internal sealed class HfHubDiscoveredModelDependency
    {
        /// <summary>
        /// Captures one normalized model dependency from one package or project manifest source.
        /// </summary>
        public HfHubDiscoveredModelDependency(
            string packageId,
            string packageDisplayName,
            string packageVersion,
            string packageResolvedPath,
            string manifestPath,
            string manifestHash,
            HfHubModelDependencyDefinition model)
        {
            PackageId = packageId;
            PackageDisplayName = packageDisplayName;
            PackageVersion = packageVersion;
            PackageResolvedPath = packageResolvedPath;
            ManifestPath = manifestPath;
            ManifestHash = manifestHash;
            ModelId = model.Id;
            Repository = model.Id;
            Revision = model.Revision;
        }

        /// <summary>
        /// Stable manifest owner identifier used by sync and runtime locator lookup.
        /// </summary>
        public string PackageId { get; }

        /// <summary>
        /// User-facing manifest owner name used in prompts and status output.
        /// </summary>
        public string PackageDisplayName { get; }

        /// <summary>
        /// Manifest owner version string or project marker.
        /// </summary>
        public string PackageVersion { get; }

        /// <summary>
        /// Absolute owner path used to anchor related project or package context.
        /// </summary>
        public string PackageResolvedPath { get; }

        /// <summary>
        /// Absolute path to manifest source that declared dependency.
        /// </summary>
        public string ManifestPath { get; }

        /// <summary>
        /// SHA-256 hash of manifest contents used for future staleness checks.
        /// </summary>
        public string ManifestHash { get; }

        /// <summary>
        /// Canonical HuggingFace model id declared in manifest.
        /// </summary>
        public string ModelId { get; }

        /// <summary>
        /// Canonical HuggingFace model id reused as repo snapshot key and runtime lookup id.
        /// </summary>
        public string Repository { get; }

        /// <summary>
        /// Branch, tag, or commit to resolve from Hub.
        /// </summary>
        public string Revision { get; }

    }

    /// <summary>
    /// Captures one manifest discovery problem without preventing rest of scan from continuing.
    /// </summary>
    internal sealed class HfHubManifestDiscoveryIssue
    {
        /// <summary>
        /// Records one package-specific manifest discovery error or validation failure.
        /// </summary>
        public HfHubManifestDiscoveryIssue(string packageId, string manifestPath, string message)
        {
            PackageId = packageId;
            ManifestPath = manifestPath;
            Message = message;
        }

        /// <summary>
        /// Package identifier associated with issue.
        /// </summary>
        public string PackageId { get; }

        /// <summary>
        /// Manifest path associated with issue.
        /// </summary>
        public string ManifestPath { get; }

        /// <summary>
        /// Human-readable validation or I/O failure message.
        /// </summary>
        public string Message { get; }
    }

        /// <summary>
        /// Summarizes manifest discovery across one scan.
        /// </summary>
    internal sealed class HfHubDiscoveryReport
    {
        private readonly HfHubDiscoveredModelDependency[] _dependencies;
        private readonly HfHubManifestDiscoveryIssue[] _issues;

        /// <summary>
        /// Stores discovered dependencies and any non-fatal scan issues.
        /// </summary>
        public HfHubDiscoveryReport(
            IReadOnlyList<HfHubDiscoveredModelDependency> dependencies,
            IReadOnlyList<HfHubManifestDiscoveryIssue> issues)
        {
            _dependencies = dependencies?.ToArray() ?? Array.Empty<HfHubDiscoveredModelDependency>();
            _issues = issues?.ToArray() ?? Array.Empty<HfHubManifestDiscoveryIssue>();
        }

        /// <summary>
        /// Valid model dependencies discovered from package or project manifests.
        /// </summary>
        public IReadOnlyList<HfHubDiscoveredModelDependency> Dependencies => _dependencies;

        /// <summary>
        /// Manifest discovery problems that require maintainer attention.
        /// </summary>
        public IReadOnlyList<HfHubManifestDiscoveryIssue> Issues => _issues;
    }

    /// <summary>
    /// Captures package metadata needed by manifest discovery in both editor and runtime flows.
    /// </summary>
    internal sealed class HfHubInstalledPackageSnapshot
    {
        /// <summary>
        /// Stores normalized package metadata used to locate and label one package manifest.
        /// </summary>
        public HfHubInstalledPackageSnapshot(string name, string displayName, string version, string resolvedPath)
        {
            Name = name;
            DisplayName = displayName;
            Version = version;
            ResolvedPath = resolvedPath;
        }

        /// <summary>
        /// Installed package identifier.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// User-facing package display name.
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// Installed package version string.
        /// </summary>
        public string Version { get; }

        /// <summary>
        /// Absolute package path resolved by Unity.
        /// </summary>
        public string ResolvedPath { get; }
    }

    /// <summary>
    /// Provides runtime-safe manifest parsing and package-manifest normalization used by both editor and future runtime Hub downloads.
    /// </summary>
    internal static class HfHubManifestDiscovery
    {
        /// <summary>
        /// Discovers manifests from supplied package snapshots.
        /// </summary>
        public static HfHubDiscoveryReport DiscoverPackageManifests(IReadOnlyList<HfHubInstalledPackageSnapshot> packages)
        {
            var dependencies = new List<HfHubDiscoveredModelDependency>();
            var issues = new List<HfHubManifestDiscoveryIssue>();

            if (packages == null)
            {
                return new HfHubDiscoveryReport(dependencies, issues);
            }

            foreach (var package in packages.OrderBy(info => info.Name, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(package.ResolvedPath))
                {
                    continue;
                }

                var manifestPath = Path.Combine(package.ResolvedPath, ModelHub.ManifestFileName);
                if (!File.Exists(manifestPath))
                {
                    continue;
                }

                string contents;
                try
                {
                    contents = File.ReadAllText(manifestPath);
                }
                catch (Exception exception)
                {
                    issues.Add(new HfHubManifestDiscoveryIssue(
                        package.Name,
                        manifestPath,
                        $"Failed to read manifest: {exception.Message}"));
                    continue;
                }

                if (!HfHubManifestParser.TryParse(contents, out var manifest, out var errorMessage))
                {
                    issues.Add(new HfHubManifestDiscoveryIssue(package.Name, manifestPath, errorMessage));
                    continue;
                }

                var manifestHash = ComputeManifestHash(contents);
                var packageDisplayName = string.IsNullOrWhiteSpace(package.DisplayName) ? package.Name : package.DisplayName;
                var packageVersion = string.IsNullOrWhiteSpace(package.Version) ? "unknown" : package.Version;

                foreach (var model in manifest.Models)
                {
                    dependencies.Add(new HfHubDiscoveredModelDependency(
                        package.Name,
                        packageDisplayName,
                        packageVersion,
                        package.ResolvedPath,
                        manifestPath,
                        manifestHash,
                        model));
                }
            }

            return new HfHubDiscoveryReport(dependencies, issues);
        }

        private static string ComputeManifestHash(string contents)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(contents);
            var hashBytes = sha256.ComputeHash(bytes);
            var builder = new StringBuilder(hashBytes.Length * 2);
            foreach (var hashByte in hashBytes)
            {
                builder.Append(hashByte.ToString("x2"));
            }

            return builder.ToString();
        }
    }

    /// <summary>
    /// Parses and validates v1 package manifest schema used by editor and future runtime Hub flows.
    /// </summary>
    internal static class HfHubManifestParser
    {
        private const string DefaultRevision = "main";

        /// <summary>
        /// Parses one manifest JSON document into immutable runtime manifest model.
        /// </summary>
        public static bool TryParse(string json, out HfHubPackageManifest manifest, out string errorMessage)
        {
            manifest = null;
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                errorMessage = "Manifest file is empty.";
                return false;
            }

            try
            {
                var root = JToken.Parse(json);
                if (root.Type != JTokenType.Object)
                {
                    errorMessage = "Manifest root must be a JSON object.";
                    return false;
                }

                var rootObject = (JObject)root;
                if (!TryReadRequiredInt(rootObject, "version", out var version, out errorMessage))
                {
                    return false;
                }

                if (version != 1)
                {
                    errorMessage = $"Unsupported manifest version `{version}`. Only version `1` is supported.";
                    return false;
                }

                if (!rootObject.TryGetValue("models", StringComparison.Ordinal, out var modelsToken) ||
                    modelsToken.Type != JTokenType.Array)
                {
                    errorMessage = "Manifest must define a `models` array.";
                    return false;
                }

                var models = new List<HfHubManifestModelDraft>();

                var modelIndex = 0;
                foreach (var modelToken in modelsToken.Children())
                {
                    if (modelToken.Type != JTokenType.Object)
                    {
                        errorMessage = $"Manifest model at index {modelIndex} must be a JSON object.";
                        return false;
                    }

                    var modelElement = (JObject)modelToken;
                    var id = ReadRequiredString(modelElement, "id", modelIndex, out errorMessage);
                    if (errorMessage != null)
                    {
                        return false;
                    }

                    var revision = ReadOptionalString(modelElement, "revision") ?? DefaultRevision;

                    models.Add(new HfHubManifestModelDraft(id, revision));
                    modelIndex++;
                }

                return HfHubManifestFactory.TryCreateManifest(version, models, out manifest, out errorMessage);
            }
            catch (JsonException exception)
            {
                errorMessage = $"Manifest contains invalid JSON: {exception.Message}";
                return false;
            }
        }

        private static bool TryReadRequiredInt(
            JObject element,
            string propertyName,
            out int value,
            out string errorMessage)
        {
            value = 0;
            errorMessage = null;

            if (!element.TryGetValue(propertyName, StringComparison.Ordinal, out var propertyToken) ||
                propertyToken.Type != JTokenType.Integer)
            {
                errorMessage = $"Manifest must define a numeric `{propertyName}`.";
                return false;
            }

            value = propertyToken.Value<int>();
            return true;
        }

        private static string ReadRequiredString(
            JObject modelElement,
            string propertyName,
            int modelIndex,
            out string errorMessage)
        {
            errorMessage = null;
            if (!modelElement.TryGetValue(propertyName, StringComparison.Ordinal, out var propertyToken) ||
                propertyToken.Type != JTokenType.String)
            {
                errorMessage = $"Manifest model at index {modelIndex} must define a string `{propertyName}`.";
                return null;
            }

            var value = propertyToken.Value<string>()?.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                errorMessage = $"Manifest model at index {modelIndex} must define a non-empty `{propertyName}`.";
                return null;
            }

            return value;
        }

        private static string ReadOptionalString(JObject modelElement, string propertyName)
        {
            if (!modelElement.TryGetValue(propertyName, StringComparison.Ordinal, out var propertyToken))
            {
                return null;
            }

            if (propertyToken.Type != JTokenType.String)
            {
                return null;
            }

            var value = propertyToken.Value<string>()?.Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }

    /// <summary>
    /// Captures one manifest model entry before it is validated into immutable package data.
    /// </summary>
    internal sealed class HfHubManifestModelDraft
    {
        public HfHubManifestModelDraft(
            string id,
            string revision)
        {
            Id = id;
            Revision = revision;
        }

        public string Id { get; }
        public string Revision { get; }
    }

    /// <summary>
    /// Validates normalized manifest data regardless of whether it came from JSON or a Unity asset.
    /// </summary>
    internal static class HfHubManifestFactory
    {
        private const string DefaultRevision = "main";

        public static bool TryCreateManifest(
            int version,
            IReadOnlyList<HfHubManifestModelDraft> models,
            out HfHubPackageManifest manifest,
            out string errorMessage)
        {
            manifest = null;
            errorMessage = null;

            if (version != 1)
            {
                errorMessage = $"Unsupported manifest version `{version}`. Only version `1` is supported.";
                return false;
            }

            if (models == null)
            {
                errorMessage = "Manifest must define a `models` array.";
                return false;
            }

            var finalizedModels = new List<HfHubModelDependencyDefinition>(models.Count);
            var seenModelIds = new HashSet<string>(StringComparer.Ordinal);
            for (var modelIndex = 0; modelIndex < models.Count; modelIndex++)
            {
                var model = models[modelIndex];
                if (!TryValidateRequiredString(model?.Id, "id", modelIndex, out var id, out errorMessage))
                {
                    return false;
                }

                if (!seenModelIds.Add(id))
                {
                    errorMessage = $"Manifest model id `{id}` is declared more than once.";
                    return false;
                }

                var revision = string.IsNullOrWhiteSpace(model.Revision)
                    ? DefaultRevision
                    : model.Revision.Trim();

                finalizedModels.Add(new HfHubModelDependencyDefinition(id, revision));
            }

            manifest = new HfHubPackageManifest(version, finalizedModels);
            return true;
        }

        private static bool TryValidateRequiredString(
            string value,
            string propertyName,
            int modelIndex,
            out string trimmedValue,
            out string errorMessage)
        {
            trimmedValue = value?.Trim();
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(trimmedValue))
            {
                errorMessage = $"Manifest model at index {modelIndex} must define a non-empty `{propertyName}`.";
                return false;
            }

            return true;
        }
    }
}
