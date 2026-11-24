using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// One converted Sentis model in the converted folder of this machine or device.
    /// </summary>
    public sealed class ConvertedModelInfo
    {
        internal ConvertedModelInfo(string modelPath, ConvertedModelRecord record, long sizeInBytes, DateTime lastWriteTimeUtc)
        {
            ModelPath = modelPath;
            Repository = record?.repository;
            RepoPath = record?.repoPath;
            SourceEtag = record?.sourceEtag;
            ConverterId = record?.converterId;
            FormatGeneration = record?.formatGeneration;
            RecipeHash = record?.recipeHash;
            SizeInBytes = sizeInBytes;
            LastWriteTimeUtc = lastWriteTimeUtc;
        }

        /// <summary>Path of the <c>.sentis</c> file.</summary>
        public string ModelPath { get; }

        /// <summary>Repository id of the source file, or null when the record is missing.</summary>
        public string Repository { get; }

        /// <summary>Repository path of the source file, for example <c>onnx/model.onnx</c>.</summary>
        public string RepoPath { get; }

        public string SourceEtag { get; }

        /// <summary>The converter that produced the model, for example <c>unity-inference-editor-v3@sentis-f8</c>.</summary>
        public string ConverterId { get; }

        /// <summary>
        /// Inference Engine format generation the model was converted for (<see cref="SentisCompatibility"/>), or null
        /// for models converted before AI Model Hub recorded it.
        /// </summary>
        public string FormatGeneration { get; }

        public string RecipeHash { get; }

        public long SizeInBytes { get; }

        public DateTime LastWriteTimeUtc { get; }
    }

    /// <summary>
    /// Disk space AI Model Hub uses outside the Hugging Face cache. AI Model Hub never deletes these files on its own: the
    /// application or project decides when, for example after an Inference Engine update or an app update.
    /// </summary>
    public static class HubStorage
    {
        /// <summary>
        /// Format generation of the running build, or of the Editor's Inference Engine. Compare it with
        /// <see cref="ConvertedModelInfo.FormatGeneration"/> to find models this version no longer uses. On Android
        /// and the Web use <see cref="GetFormatGenerationAsync"/>.
        /// </summary>
        public static string FormatGeneration => RuntimeModels.GetCatalog().formatGeneration;

        public static async Task<string> GetFormatGenerationAsync()
        {
            return (await RuntimeModels.GetCatalogAsync()).formatGeneration;
        }

        /// <summary>
        /// Lists every converted model in the converted folder. In the Editor and on desktop players that folder is
        /// shared by all projects and applications of the user.
        /// </summary>
        public static IReadOnlyList<ConvertedModelInfo> GetConvertedModels()
        {
            var root = CachePaths.GetConvertedRoot();
            if (!Directory.Exists(root))
            {
                return Array.Empty<ConvertedModelInfo>();
            }

            return Directory.EnumerateFiles(root, "*.sentis", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .Select(file => new ConvertedModelInfo(file.FullName, ReadRecord(file.FullName), file.Length, file.LastWriteTimeUtc))
                .OrderBy(info => info.ModelPath, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Deletes the converted models that <paramref name="match"/> selects, with their records. A model that is
        /// being written is skipped. Returns the number deleted. A deleted model is converted again when it is needed.
        /// </summary>
        /// <example>
        /// <code>
        /// var current = HubStorage.FormatGeneration;
        /// HubStorage.DeleteConvertedModels(model => model.FormatGeneration != current);
        /// </code>
        /// </example>
        public static int DeleteConvertedModels(Func<ConvertedModelInfo, bool> match)
        {
            if (match == null)
            {
                throw new ArgumentNullException(nameof(match));
            }

            var deleted = 0;
            foreach (var model in GetConvertedModels().Where(match))
            {
                if (!BlobLock.TryAcquire(model.ModelPath + ".lock", out var modelLock))
                {
                    continue;
                }

                using (modelLock)
                {
                    File.Delete(model.ModelPath);
                    var recordPath = Path.ChangeExtension(model.ModelPath, ".json");
                    if (File.Exists(recordPath))
                    {
                        File.Delete(recordPath);
                    }
                }

                TryDelete(model.ModelPath + ".lock");
                TryDeleteEmptyFolder(Path.GetDirectoryName(model.ModelPath));
                deleted++;
            }

            PersistentStorage.Flush();
            return deleted;
        }

        /// <summary>
        /// Deletes copies of bundled models that Android players made for other app versions
        /// (<c>persistentDataPath/model-hub/bundled/&lt;version&gt;</c>). The copies of the running version stay. Call it once
        /// after an app update. On other platforms there are no copies and it returns 0.
        /// </summary>
        public static int DeleteBundledCopiesOfOtherVersions()
        {
            return DeleteBundledCopiesExcept(Application.version);
        }

        internal static int DeleteBundledCopiesExcept(string keptVersion)
        {
            var root = RuntimeModels.GetBundledCopiesRoot();
            if (!Directory.Exists(root))
            {
                return 0;
            }

            var kept = string.IsNullOrWhiteSpace(keptVersion) ? "0" : keptVersion;
            var deleted = 0;
            foreach (var folder in Directory.GetDirectories(root))
            {
                if (string.Equals(Path.GetFileName(folder), kept, StringComparison.Ordinal))
                {
                    continue;
                }

                Directory.Delete(folder, true);
                deleted++;
            }

            return deleted;
        }

        private static ConvertedModelRecord ReadRecord(string modelPath)
        {
            var recordPath = Path.ChangeExtension(modelPath, ".json");
            try
            {
                return File.Exists(recordPath) ? JsonUtility.FromJson<ConvertedModelRecord>(File.ReadAllText(recordPath)) : null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // Another writer holds it and deletes it itself.
            }
        }

        private static void TryDeleteEmptyFolder(string folder)
        {
            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
            }
        }
    }
}
