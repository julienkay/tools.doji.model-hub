using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Unity.InferenceEngine;
using UnityEngine;

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// Describes one converted model next to its <c>.sentis</c> file.
    /// </summary>
    [Serializable]
    public sealed class ConvertedModelRecord
    {
        public string repository;
        public string repoPath;
        public string sourceEtag;
        public string converterId;
        public string recipeHash;
        public string createdAtUtc;
    }

    /// <summary>
    /// Converted Sentis models, shared by every project on this machine (or by the application on a device).
    /// A model is found by what produced it: the source file's etag, the converter and its version, and the
    /// conversion settings. It never goes into the Hugging Face cache, so Python tools never see it.
    /// </summary>
    public static class ConvertedModelStore
    {
        /// <summary>
        /// Key of one conversion: a hash of the source etag, the converter id, and the conversion settings hash.
        /// </summary>
        public static string ComputeKey(string sourceEtag, string converterId, string recipeHash)
        {
            if (string.IsNullOrWhiteSpace(sourceEtag))
            {
                throw new ArgumentException("The source etag is required.", nameof(sourceEtag));
            }

            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes($"{sourceEtag}\n{converterId}\n{recipeHash}"));
            return string.Concat(bytes.Take(16).Select(value => value.ToString("x2")));
        }

        /// <summary>
        /// Folder of one repository's conversions. The name is lower case, so manifests that spell an id differently
        /// share it.
        /// </summary>
        public static string GetRepositoryFolder(string repository)
        {
            return Path.Combine(CachePaths.GetConvertedRoot(), CachePaths.GetRepositoryFolderName(repository).ToLowerInvariant());
        }

        public static string GetModelPath(string repository, string key)
        {
            return Path.Combine(GetRepositoryFolder(repository), key + ".sentis");
        }

        public static string GetRecordPath(string repository, string key)
        {
            return Path.Combine(GetRepositoryFolder(repository), key + ".json");
        }

        /// <summary>
        /// Returns the converted model for a key when it exists.
        /// </summary>
        public static bool TryGet(string repository, string key, out string modelPath)
        {
            modelPath = GetModelPath(repository, key);
            return File.Exists(modelPath);
        }

        /// <summary>
        /// Saves a converted model. It is written under a temporary name and renamed, so a reader never sees a
        /// partial file, and a lock keeps two writers from converting the same key at once.
        /// </summary>
        public static string Save(string repository, string key, Model model, ConvertedModelRecord record)
        {
            var modelPath = GetModelPath(repository, key);
            Directory.CreateDirectory(Path.GetDirectoryName(modelPath));
            if (!BlobLock.TryAcquire(modelPath + ".lock", out var blobLock))
            {
                throw new IOException($"Another program is converting {record?.repoPath ?? key}. Try again when it is done.");
            }

            using (blobLock)
            {
                var temporaryPath = modelPath + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".incomplete";
                try
                {
                    ModelWriter.Save(temporaryPath, model);
                    if (File.Exists(modelPath))
                    {
                        File.Delete(modelPath);
                    }

                    File.Move(temporaryPath, modelPath);
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                    {
                        File.Delete(temporaryPath);
                    }
                }

                if (record != null)
                {
                    record.createdAtUtc ??= DateTime.UtcNow.ToString("O");
                    File.WriteAllText(GetRecordPath(repository, key), JsonUtility.ToJson(record, true));
                }
            }

            TryDelete(modelPath + ".lock");
            return modelPath;
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
                // Another writer holds it; it deletes the file itself.
            }
        }
    }
}
