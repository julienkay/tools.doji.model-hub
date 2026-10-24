using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Removes what older HF Hub versions generated in <c>Assets/StreamingAssets/hf-hub</c>: per-package registries
    /// and converted models in <c>hf-hub/&lt;owner&gt;/&lt;name&gt;</c>. Builds now add models from the shared caches,
    /// so these copies would only ship twice. Models pulled into the project live in <c>hf-hub/models</c> and are kept,
    /// and a folder with any file other than <c>.sentis</c> files is kept and reported, because it may be the user's.
    /// </summary>
    [InitializeOnLoad]
    internal static class LegacyProjectOutputCleanup
    {
        private const string SessionKey = "Doji.AI.ModelHub.LegacyProjectOutputChecked";
        internal const string RootAssetPath = "Assets/StreamingAssets/" + Hub.RuntimeStreamingRelativeRoot;

        static LegacyProjectOutputCleanup()
        {
            if (Application.isBatchMode || SessionState.GetBool(SessionKey, false))
            {
                return;
            }

            SessionState.SetBool(SessionKey, true);
            EditorApplication.delayCall += () =>
            {
                var result = Clean(RootAssetPath);
                if (result.Deleted.Count > 0)
                {
                    Debug.Log($"HF Hub removed files an older version generated in {RootAssetPath}; builds now add models from the shared cache: {string.Join(", ", result.Deleted)}.");
                }

                if (result.Kept.Count > 0)
                {
                    Debug.LogWarning($"HF Hub left these folders in {RootAssetPath} because they hold files it did not generate. Delete them if they are left over from an older HF Hub version: {string.Join(", ", result.Kept)}.");
                }
            };
        }

        internal sealed class Result
        {
            public List<string> Deleted { get; } = new();
            public List<string> Kept { get; } = new();
        }

        /// <summary>
        /// Deletes the old layout under <paramref name="rootAssetPath"/> and reports what it deleted and kept.
        /// </summary>
        internal static Result Clean(string rootAssetPath)
        {
            var result = new Result();
            var root = ProjectPaths.ToAbsolutePath(rootAssetPath);
            if (!Directory.Exists(root))
            {
                return result;
            }

            foreach (var folder in Directory.GetDirectories(root))
            {
                var name = Path.GetFileName(folder);
                if (string.Equals(name, "models", StringComparison.Ordinal))
                {
                    continue;
                }

                var assetPath = $"{rootAssetPath}/{name}";
                var isRegistries = string.Equals(name, "registries", StringComparison.Ordinal);
                var onlyGenerated = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                    .All(file => file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) ||
                                 file.EndsWith(".sentis", StringComparison.OrdinalIgnoreCase) ||
                                 (isRegistries && file.EndsWith("hf-hub-runtime.json", StringComparison.OrdinalIgnoreCase)));
                if (!onlyGenerated)
                {
                    result.Kept.Add(assetPath);
                    continue;
                }

                if (AssetDatabase.DeleteAsset(assetPath) || DeleteOnDisk(folder))
                {
                    result.Deleted.Add(assetPath);
                }
            }

            return result;
        }

        private static bool DeleteOnDisk(string folder)
        {
            if (!Directory.Exists(folder))
            {
                return false;
            }

            Directory.Delete(folder, true);
            var meta = folder + ".meta";
            if (File.Exists(meta))
            {
                File.Delete(meta);
            }

            return true;
        }
    }
}
