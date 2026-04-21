using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// Resolves generated runtime model data from package-owned HF Hub locator registries.
    /// </summary>
    internal static class RuntimeLocatorService
    {
        /// <summary>
        /// Resolves one synchronized model by package id and canonical HuggingFace model id.
        /// </summary>
        public static bool TryResolveModel(
            string packageId,
            string repoId,
            out ResolvedRuntimeModel model)
        {
            model = null;
            if (string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(repoId))
            {
                return false;
            }

            if (!TryLoadManifest(packageId, out var manifest))
            {
                return false;
            }

            var entry = manifest.Models.FirstOrDefault(candidate =>
                string.Equals(candidate.RepoId, repoId, StringComparison.Ordinal));
            if (entry == null)
            {
                return false;
            }

            var files = entry.Files
                .Select(file => new ResolvedRuntimeFile(
                    ModelHub.GetRuntimeAbsolutePath(file.RelativePath),
                    file.RepoPath,
                    file.Kind))
                .ToArray();

            model = new ResolvedRuntimeModel(
                manifest.PackageId,
                manifest.PackageVersion,
                entry.RepoId,
                files);
            return true;
        }

        public static bool TryResolveModel(string repoId, out ResolvedRuntimeModel model)
        {
            model = null;
            if (string.IsNullOrWhiteSpace(repoId))
            {
                return false;
            }

            var matches = new List<ResolvedRuntimeModel>();
            foreach (var manifest in LoadAllManifests())
            {
                foreach (var entry in manifest.Models.Where(candidate =>
                             string.Equals(candidate.RepoId, repoId, StringComparison.Ordinal)))
                {
                    var files = entry.Files
                        .Select(file => new ResolvedRuntimeFile(
                            ModelHub.GetRuntimeAbsolutePath(file.RelativePath),
                            file.RepoPath,
                            file.Kind))
                        .ToArray();

                    matches.Add(new ResolvedRuntimeModel(
                        manifest.PackageId,
                        manifest.PackageVersion,
                        entry.RepoId,
                        files));
                }
            }

            if (matches.Count == 1)
            {
                model = matches[0];
                return true;
            }

            if (matches.Count > 1)
            {
                throw new InvalidOperationException(
                    $"HF Hub runtime model '{repoId}' is ambiguous across multiple registries. Use owner-specific lookup or keep model ids unique per owner when using global lookup.");
            }

            return false;
        }

        internal static bool TryLoadManifest(string packageId, out RuntimeLocatorManifest manifest)
        {
            manifest = null;

            var absolutePath = ModelHub.GetRuntimeRegistryAbsolutePath(packageId);
            if (!File.Exists(absolutePath))
            {
                return false;
            }

            try
            {
                var json = File.ReadAllText(absolutePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return false;
                }

                var document = JsonUtility.FromJson<RuntimeLocatorManifestDocument>(json);
                if (document == null)
                {
                    return false;
                }

                manifest = document.ToManifest();
                return manifest != null;
            }
            catch
            {
                return false;
            }
        }

        private static IEnumerable<RuntimeLocatorManifest> LoadAllManifests()
        {
            var registriesRoot = Path.Combine(Application.streamingAssetsPath, ModelHub.RuntimeStreamingRelativeRoot, "registries");
            if (!Directory.Exists(registriesRoot))
            {
                yield break;
            }

            var registryFiles = Directory.GetFiles(registriesRoot, "hf-hub-runtime.json", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal);

            foreach (var registryFile in registryFiles)
            {
                RuntimeLocatorManifest manifest = null;

                try
                {
                    var json = File.ReadAllText(registryFile);
                    if (string.IsNullOrWhiteSpace(json))
                    {
                        continue;
                    }

                    var document = JsonUtility.FromJson<RuntimeLocatorManifestDocument>(json);
                    manifest = document?.ToManifest();
                }
                catch
                {
                    manifest = null;
                }

                if (manifest != null)
                {
                    yield return manifest;
                }
            }
        }
    }
}
