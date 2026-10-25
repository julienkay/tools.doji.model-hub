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
    // TODO: Rename package-oriented members here to owner-oriented names
    // (`PackageId`, `PackageDisplayName`, `PackageVersion`, `PackageResolvedPath`)
    // so project-manifest owners read naturally too.
    internal sealed class DiscoveredModelDependency
    {
        private readonly ModelDependencyFileDefinition[] _fileEntries;
        private readonly string[] _enabledOptionalFilePaths;

        /// <summary>
        /// Captures one normalized model dependency from one package or project manifest source.
        /// </summary>
        public DiscoveredModelDependency(
            string packageId,
            string packageDisplayName,
            string packageVersion,
            string packageResolvedPath,
            string manifestPath,
            string manifestHash,
            ModelDependencyDefinition model,
            IReadOnlyList<string> enabledOptionalFiles = null)
        {
            PackageId = packageId;
            PackageDisplayName = packageDisplayName;
            PackageVersion = packageVersion;
            PackageResolvedPath = packageResolvedPath;
            ManifestPath = manifestPath;
            ManifestHash = manifestHash;
            ModelId = model.Id;
            Repository = model.Id;
            Revision = model.EffectiveRevision;
            _fileEntries = ModelDependencyDefinition.NormalizeFileEntries(model.FileEntries?.ToArray());

            var enabledOptionalLookup = new HashSet<string>(
                ModelDependencyDefinition.NormalizeSelectedFiles(enabledOptionalFiles),
                StringComparer.Ordinal);
            _enabledOptionalFilePaths = _fileEntries
                .Where(file => file.Optional && enabledOptionalLookup.Contains(file.Path))
                .Select(file => file.Path)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            DependencySelectionHash = ModelDependencyDefinition.ComputeSelectionHash(
                SelectedFiles,
                SelectsWholeRepository);
        }

        /// <summary>
        /// Stable manifest owner identifier used by sync and runtime locator lookup, for example
        /// <c>com.example.sample-package</c> for a package or one generated project-manifest owner id.
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
        /// Absolute owner path used to anchor related project or package context, for example
        /// <c>C:/dev/com.example.sample-package</c> or <c>C:/work/AI Model Hub/Assets/AI Model Hub</c>.
        /// </summary>
        public string PackageResolvedPath { get; }

        /// <summary>
        /// Absolute path to manifest source that declared dependency, for example
        /// <c>C:/dev/com.example.sample-package/model-hub-manifest.json</c>.
        /// </summary>
        public string ManifestPath { get; }

        /// <summary>
        /// SHA-256 hash of manifest contents used for future staleness checks, for example
        /// <c>7f83b1657ff1fc53b92dc18148a1d65dfa135014...</c>.
        /// </summary>
        public string ManifestHash { get; }

        /// <summary>
        /// Canonical HuggingFace model id declared in manifest, for example
        /// <c>example/sample-model</c>.
        /// </summary>
        public string ModelId { get; }

        /// <summary>
        /// Canonical HuggingFace model id reused as repo snapshot key and runtime lookup id, for example
        /// <c>example/sample-model</c>.
        /// </summary>
        public string Repository { get; }

        /// <summary>
        /// Branch, tag, or commit to resolve from Hub, for example <c>main</c>,
        /// <c>refs/pr/12</c>, or one commit SHA.
        /// </summary>
        public string Revision { get; }

        /// <summary>
        /// Declared repo-relative file selections with optional intent stored beside each path.
        /// Empty means whole-repository scope.
        /// </summary>
        public IReadOnlyList<ModelDependencyFileDefinition> FileEntries => _fileEntries;

        /// <summary>
        /// Effective exact repo-relative file paths selected for this dependency after applying project-owned optional-file overrides.
        /// </summary>
        public IReadOnlyList<string> SelectedFiles => _fileEntries
            .Where(file => !file.Optional || _enabledOptionalFilePaths.Contains(file.Path, StringComparer.Ordinal))
            .Select(file => file.Path)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        /// <summary>
        /// Stable owner-scoped fingerprint of dependency selection intent.
        /// Raw cache metadata must not be used as a substitute for this value.
        /// </summary>
        public string DependencySelectionHash { get; }

        /// <summary>
        /// Whether this dependency selects explicit repo paths instead of requiring the full repository.
        /// </summary>
        public bool HasExplicitFileSelection => _fileEntries.Length > 0;

        /// <summary>
        /// Whether this dependency still means the whole repository rather than an explicit required/optional file set.
        /// </summary>
        public bool SelectsWholeRepository => !HasExplicitFileSelection;

        /// <summary>
        /// Whether this dependency declares any optional repo file paths.
        /// </summary>
        public bool HasOptionalFiles => _fileEntries.Any(file => file.Optional);

        /// <summary>
        /// Whether the manifest declares optional model files but none are currently enabled for this owner.
        /// </summary>
        public bool RequiresOptionalModelSelection => !SelectsWholeRepository &&
            !SelectedFiles.Any(ModelFiles.IsModelFile) &&
            _fileEntries.Any(file => file.Optional && ModelFiles.IsModelFile(file.Path));

        internal IReadOnlyList<string> GetOptionalRepoPaths()
        {
            return _fileEntries
                .Where(file => file.Optional)
                .Select(file => file.Path)
                .ToArray();
        }

        internal IReadOnlyList<string> GetEnabledOptionalRepoPaths()
        {
            return _enabledOptionalFilePaths;
        }

        /// <summary>
        /// Returns every repository file this dependency needs: the listed files plus the companion files of listed ONNX
        /// files, or the whole repository.
        /// </summary>
        internal IReadOnlyList<string> GetSelectedRepoPaths(
            IEnumerable<string> repositoryFiles,
            IEnumerable<string> fallbackFiles = null)
        {
            var knownFiles = (repositoryFiles ?? fallbackFiles ?? Array.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(ModelDependencyDefinition.NormalizeRepoPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (!HasExplicitFileSelection)
            {
                return knownFiles.OrderBy(path => path, StringComparer.Ordinal).ToArray();
            }

            return SelectedFiles
                .Concat(SelectedFiles.SelectMany(path => ModelFiles.GetCompanionFiles(path, knownFiles)))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Returns the selected model files: ONNX files to convert and Sentis files to load as downloaded.
        /// </summary>
        internal IReadOnlyList<string> GetSelectedModelRepoPaths(
            IEnumerable<string> repositoryFiles,
            IEnumerable<string> fallbackFiles = null)
        {
            return GetSelectedRepoPaths(repositoryFiles, fallbackFiles)
                .Where(ModelFiles.IsModelFile)
                .ToArray();
        }

        internal IReadOnlyList<string> GetSelectedOnnxRepoPaths(
            IEnumerable<string> repositoryFiles,
            IEnumerable<string> fallbackFiles = null)
        {
            return GetSelectedRepoPaths(repositoryFiles, fallbackFiles)
                .Where(path => ModelFiles.GetFormat(path) == ModelFileFormat.Onnx)
                .ToArray();
        }

        /// <summary>
        /// Returns why this selection cannot load, or null. A whole-repository dependency on a repository with both ONNX
        /// and Sentis files must list its files, so AI Model Hub never picks a format silently.
        /// </summary>
        internal string GetSelectionProblem(IEnumerable<string> repositoryFiles, IEnumerable<string> fallbackFiles = null)
        {
            var selected = GetSelectedRepoPaths(repositoryFiles, fallbackFiles);
            if (SelectsWholeRepository && ModelFiles.Describe(selected) == RepositoryModelFormat.Mixed)
            {
                return $"Repository `{Repository}` has both ONNX and Sentis files. List the files you use in the manifest.";
            }

            return selected.Any(ModelFiles.IsModelFile)
                ? null
                : $"Dependency `{ModelId}` does not select any model file. Select at least one `.onnx` or `.sentis` file.";
        }

        internal bool RequiresRepoPath(
            string repoPath,
            IEnumerable<string> repositoryFiles,
            IEnumerable<string> fallbackFiles = null)
        {
            if (string.IsNullOrWhiteSpace(repoPath))
            {
                return false;
            }

            if (HasExplicitFileSelection)
            {
                return SelectedFiles.Contains(repoPath, StringComparer.Ordinal);
            }

            return GetSelectedRepoPaths(repositoryFiles, fallbackFiles)
                .Contains(repoPath, StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Captures one manifest discovery problem without preventing rest of scan from continuing.
    /// </summary>
    internal sealed class ManifestDiscoveryIssue
    {
        /// <summary>
        /// Records one package-specific manifest discovery error or validation failure.
        /// </summary>
        public ManifestDiscoveryIssue(string packageId, string manifestPath, string message)
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
    internal sealed class DiscoveryReport
    {
        private readonly DiscoveredModelDependency[] _dependencies;
        private readonly ManifestDiscoveryIssue[] _issues;

        /// <summary>
        /// Stores discovered dependencies and any non-fatal scan issues.
        /// </summary>
        public DiscoveryReport(
            IReadOnlyList<DiscoveredModelDependency> dependencies,
            IReadOnlyList<ManifestDiscoveryIssue> issues)
        {
            _dependencies = dependencies?.ToArray() ?? Array.Empty<DiscoveredModelDependency>();
            _issues = issues?.ToArray() ?? Array.Empty<ManifestDiscoveryIssue>();
        }

        /// <summary>
        /// Valid model dependencies discovered from package or project manifests.
        /// </summary>
        public IReadOnlyList<DiscoveredModelDependency> Dependencies => _dependencies;

        /// <summary>
        /// Manifest discovery problems that require maintainer attention.
        /// </summary>
        public IReadOnlyList<ManifestDiscoveryIssue> Issues => _issues;
    }

    /// <summary>
    /// Captures package metadata needed by manifest discovery in both editor and runtime flows.
    /// </summary>
    internal sealed class InstalledPackageSnapshot
    {
        /// <summary>
        /// Stores normalized package metadata used to locate and label one package manifest.
        /// </summary>
        public InstalledPackageSnapshot(string name, string displayName, string version, string resolvedPath)
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
    internal static class ManifestDiscovery
    {
        /// <summary>
        /// Discovers manifests from supplied package snapshots.
        /// </summary>
        public static DiscoveryReport DiscoverPackageManifests(IReadOnlyList<InstalledPackageSnapshot> packages)
        {
            var dependencies = new List<DiscoveredModelDependency>();
            var issues = new List<ManifestDiscoveryIssue>();

            if (packages == null)
            {
                return new DiscoveryReport(dependencies, issues);
            }

            foreach (var package in packages.OrderBy(info => info.Name, StringComparer.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(package.ResolvedPath))
                {
                    continue;
                }

                var manifestPath = Path.Combine(package.ResolvedPath, Hub.ManifestFileName);
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
                    issues.Add(new ManifestDiscoveryIssue(
                        package.Name,
                        manifestPath,
                        $"Failed to read manifest: {exception.Message}"));
                    continue;
                }

                if (!ManifestParser.TryParse(contents, out var manifest, out var errorMessage))
                {
                    issues.Add(new ManifestDiscoveryIssue(package.Name, manifestPath, errorMessage));
                    continue;
                }

                var manifestHash = ComputeManifestHash(contents);
                var packageDisplayName = string.IsNullOrWhiteSpace(package.DisplayName) ? package.Name : package.DisplayName;
                var packageVersion = string.IsNullOrWhiteSpace(package.Version) ? "unknown" : package.Version;

                foreach (var model in manifest.Models)
                {
                    dependencies.Add(new DiscoveredModelDependency(
                        package.Name,
                        packageDisplayName,
                        packageVersion,
                        package.ResolvedPath,
                        manifestPath,
                        manifestHash,
                        model));
                }
            }

            return new DiscoveryReport(dependencies, issues);
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
    internal static class ManifestParser
    {
        private static readonly string[] RootProperties = { "version", "models" };
        private static readonly string[] ModelProperties = { "id", "revision", "files" };
        private static readonly string[] FileProperties = { "path", "optional" };

        /// <summary>
        /// Parses one manifest JSON document into immutable runtime manifest model.
        /// </summary>
        public static bool TryParse(string json, out PackageManifest manifest, out string errorMessage)
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
                if (!TryRejectUnknownProperties(rootObject, RootProperties, "Manifest root", out errorMessage))
                {
                    return false;
                }

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

                var models = new List<ManifestModelDraft>();

                var modelIndex = 0;
                foreach (var modelToken in modelsToken.Children())
                {
                    if (modelToken.Type != JTokenType.Object)
                    {
                        errorMessage = $"Manifest model at index {modelIndex} must be a JSON object.";
                        return false;
                    }

                    var modelElement = (JObject)modelToken;
                    if (!TryRejectUnknownProperties(
                            modelElement,
                            ModelProperties,
                            $"Manifest model at index {modelIndex}",
                            out errorMessage))
                    {
                        return false;
                    }

                    var id =ReadRequiredString(modelElement, "id", modelIndex, out errorMessage);
                    if (errorMessage != null)
                    {
                        return false;
                    }

                    var revision = ReadOptionalString(modelElement, "revision");
                    var files = ReadOptionalFileArray(modelElement, "files", modelIndex, out errorMessage);
                    if (errorMessage != null)
                    {
                        return false;
                    }

                    models.Add(new ManifestModelDraft(id, revision, files));
                    modelIndex++;
                }

                return ManifestFactory.TryCreateManifest(version, models, out manifest, out errorMessage);
            }
            catch (JsonException exception)
            {
                errorMessage = $"Manifest contains invalid JSON: {exception.Message}";
                return false;
            }
        }

        /// <summary>
        /// Rejects properties outside the v1 schema. A misspelled or outdated key must fail loudly:
        /// a silently ignored key can turn a file-selective dependency into a whole-repository download.
        /// </summary>
        private static bool TryRejectUnknownProperties(
            JObject element,
            IReadOnlyCollection<string> allowedProperties,
            string location,
            out string errorMessage)
        {
            errorMessage = null;
            foreach (var property in element.Properties())
            {
                if (allowedProperties.Contains(property.Name, StringComparer.Ordinal))
                {
                    continue;
                }

                errorMessage = $"{location} has unknown property `{property.Name}`. " +
                    $"Allowed properties: {string.Join(", ", allowedProperties.Select(name => $"`{name}`"))}.";
                if (string.Equals(property.Name, "optionalFiles", StringComparison.Ordinal))
                {
                    errorMessage += " `optionalFiles` was replaced by `files` entries with an `optional` flag, " +
                        "for example `\"files\": [{ \"path\": \"onnx/model.onnx\", \"optional\": true }]`.";
                }

                return false;
            }

            return true;
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

        private static ModelDependencyFileDefinition[] ReadOptionalFileArray(
            JObject modelElement,
            string propertyName,
            int modelIndex,
            out string errorMessage)
        {
            errorMessage = null;
            if (!modelElement.TryGetValue(propertyName, StringComparison.Ordinal, out var propertyToken))
            {
                return null;
            }

            if (propertyToken.Type != JTokenType.Array)
            {
                errorMessage = $"Manifest model at index {modelIndex} must define `{propertyName}` as an array when present.";
                return null;
            }

            var values = new List<ModelDependencyFileDefinition>();
            var fileIndex = 0;
            foreach (var child in propertyToken.Children())
            {
                if (child.Type != JTokenType.Object)
                {
                    errorMessage = $"Manifest model at index {modelIndex} must define `{propertyName}[{fileIndex}]` as a file object.";
                    return null;
                }

                var fileObject = (JObject)child;
                if (!TryRejectUnknownProperties(
                        fileObject,
                        FileProperties,
                        $"Manifest model at index {modelIndex} `{propertyName}[{fileIndex}]`",
                        out errorMessage))
                {
                    return null;
                }

                var path = ReadRequiredString(fileObject, "path", modelIndex, out errorMessage);
                if (errorMessage != null)
                {
                    errorMessage = $"Manifest model at index {modelIndex} must define `{propertyName}[{fileIndex}].path` as a non-empty string.";
                    return null;
                }

                var optional = false;
                if (fileObject.TryGetValue("optional", StringComparison.Ordinal, out var optionalToken))
                {
                    if (optionalToken.Type != JTokenType.Boolean)
                    {
                        errorMessage = $"Manifest model at index {modelIndex} must define `{propertyName}[{fileIndex}].optional` as a boolean when present.";
                        return null;
                    }

                    optional = optionalToken.Value<bool>();
                }

                values.Add(new ModelDependencyFileDefinition(path, optional));
                fileIndex++;
            }

            return values.ToArray();
        }
    }

    /// <summary>
    /// Captures one manifest model entry before it is validated into immutable package data.
    /// </summary>
    internal sealed class ManifestModelDraft
    {
        public ManifestModelDraft(
            string id,
            string revision,
            IReadOnlyList<ModelDependencyFileDefinition> files = null)
        {
            Id = id;
            Revision = revision;
            FileEntries = files?.Count > 0 ? files.ToArray() : null;
        }

        public string Id { get; }
        public string Revision { get; }
        public IReadOnlyList<ModelDependencyFileDefinition> FileEntries { get; }
    }

    /// <summary>
    /// Validates normalized manifest data regardless of whether it came from JSON or a Unity asset.
    /// </summary>
    internal static class ManifestFactory
    {
        public static bool TryCreateManifest(
            int version,
            IReadOnlyList<ManifestModelDraft> models,
            out PackageManifest manifest,
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

            var finalizedModels = new List<ModelDependencyDefinition>(models.Count);
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

                var revision = ManifestRevision.NormalizeForResolution(model.Revision);
                var files = ValidateSelectedFiles(model?.FileEntries, id, modelIndex, out errorMessage);
                if (errorMessage != null)
                {
                    return false;
                }

                finalizedModels.Add(new ModelDependencyDefinition(id, revision, files));
            }

            manifest = new PackageManifest(version, finalizedModels);
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

        private static ModelDependencyFileDefinition[] ValidateSelectedFiles(
            IReadOnlyList<ModelDependencyFileDefinition> files,
            string id,
            int modelIndex,
            out string errorMessage)
        {
            errorMessage = null;
            if (files == null)
            {
                return null;
            }

            var validatedFiles = new List<ModelDependencyFileDefinition>();
            var seenFiles = new HashSet<string>(StringComparer.Ordinal);
            for (var fileIndex = 0; fileIndex < files.Count; fileIndex++)
            {
                if (!TryValidateRequiredString(files[fileIndex]?.Path, "files", modelIndex, out var filePath, out errorMessage))
                {
                    errorMessage = $"Manifest model at index {modelIndex} must define `files[{fileIndex}].path` as a non-empty string.";
                    return null;
                }

                filePath = ModelDependencyDefinition.NormalizeRepoPath(filePath);
                if (!seenFiles.Add(filePath))
                {
                    errorMessage = $"Manifest model `{id}` declares file `{filePath}` more than once.";
                    return null;
                }

                validatedFiles.Add(new ModelDependencyFileDefinition(filePath, files[fileIndex].Optional));
            }

            if (validatedFiles.Count == 0)
            {
                return null;
            }

            return validatedFiles
                .OrderBy(file => file.Path, StringComparer.Ordinal)
                .ToArray();
        }
    }
}
