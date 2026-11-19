using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Doji.AI.ModelHub
{
    /// <summary>
    /// How a model reaches the device.
    /// </summary>
    public enum ModelDelivery
    {
        /// <summary>
        /// The build adds the runtime model to StreamingAssets.
        /// </summary>
        Bundle,

        /// <summary>
        /// The player downloads the model from Hugging Face on first use, into the device cache.
        /// </summary>
        Download
    }

    /// <summary>
    /// One file that a downloaded model needs: the model itself or an ONNX companion file.
    /// </summary>
    [Serializable]
    public sealed class RuntimeSourceFile
    {
        public string repoPath;
        public string etag;
        public long size;
    }

    /// <summary>
    /// One input shape setting of an ONNX conversion.
    /// </summary>
    [Serializable]
    public sealed class RuntimeInputShape
    {
        public string inputName;
        public int[] dimensions = Array.Empty<int>();
    }

    /// <summary>
    /// One loadable model file in the registry, addressed by the repository path the package declared.
    /// </summary>
    [Serializable]
    public sealed class RuntimeModelFile
    {
        /// <summary>
        /// Repository path the package uses to load it, for example <c>onnx/model.onnx</c>.
        /// </summary>
        public string repoPath;

        /// <summary>
        /// Name the package manifest gives the file for model pickers. Empty when it sets none.
        /// </summary>
        public string label;

        /// <summary>
        /// Format of the source file: <c>onnx</c> or <c>sentis</c>.
        /// </summary>
        public string format;

        /// <summary>
        /// Bundled models: path of the runtime model inside StreamingAssets.
        /// </summary>
        public string relativePath;

        /// <summary>
        /// Downloaded models: the source file and its ONNX companion files.
        /// </summary>
        public RuntimeSourceFile source;

        public RuntimeSourceFile[] companions = Array.Empty<RuntimeSourceFile>();

        /// <summary>
        /// Downloaded ONNX models: the conversion settings, applied when the device converts the file.
        /// </summary>
        public string quantization;

        public RuntimeInputShape[] inputShapes = Array.Empty<RuntimeInputShape>();

        public string recipeHash;

        /// <summary>
        /// Editor only: the runtime model on this machine. Never written to a build.
        /// </summary>
        [NonSerialized]
        public string localPath;

        public bool IsOnnx => string.Equals(format, "onnx", StringComparison.Ordinal);

        internal OnnxConversionRecipe ToRecipe()
        {
            return new OnnxConversionRecipe
            {
                QuantizationMode = OnnxConversionSettings.NormalizeQuantizationMode(quantization),
                StaticInputShapeOverrides = (inputShapes ?? Array.Empty<RuntimeInputShape>())
                    .Where(shape => shape != null)
                    .Select(shape => new OnnxStaticInputShapeOverride { InputName = shape.inputName, Dimensions = shape.dimensions })
                    .ToArray()
            };
        }
    }

    /// <summary>
    /// One file of a model that is not a model itself, such as <c>config.json</c> or a tokenizer file, addressed by its
    /// repository path. It reaches the device like the model's runtime models: bundled in StreamingAssets, or downloaded.
    /// </summary>
    [Serializable]
    public sealed class RuntimeDataFile
    {
        /// <summary>
        /// Repository path the package uses to read it, for example <c>scheduler/scheduler_config.json</c>.
        /// </summary>
        public string repoPath;

        /// <summary>
        /// Bundled models: path of the file inside StreamingAssets.
        /// </summary>
        public string relativePath;

        /// <summary>
        /// Downloaded models: the file on Hugging Face.
        /// </summary>
        public RuntimeSourceFile source;

        /// <summary>
        /// Editor only: the file in the Hugging Face cache of this machine. Never written to a build.
        /// </summary>
        [NonSerialized]
        public string localPath;
    }

    /// <summary>
    /// One model of one package: its repository, pinned commit, delivery, and files.
    /// </summary>
    [Serializable]
    public sealed class RuntimeModelEntry
    {
        public string repoId;
        public string revision;

        /// <summary>
        /// The commit resolved in the Editor. Downloads use it, so players get exactly the tested files.
        /// </summary>
        public string commit;

        public string delivery;

        /// <summary>
        /// The repository id as the Hugging Face cache spells it, for example <c>julienkay/sentis-MiDaS</c> when a
        /// manifest wrote <c>julienkay/sentis-midas</c>. Downloads use it, so players share the folder Python uses.
        /// </summary>
        public string cacheRepoId;

        public RuntimeModelFile[] files = Array.Empty<RuntimeModelFile>();

        /// <summary>
        /// The selected files that are not models: configs, tokenizers, and other files the package reads.
        /// </summary>
        public RuntimeDataFile[] dataFiles = Array.Empty<RuntimeDataFile>();

        public string CacheRepoId => string.IsNullOrWhiteSpace(cacheRepoId) ? repoId : cacheRepoId;

        public ModelDelivery Delivery => string.Equals(delivery, "download", StringComparison.Ordinal)
            ? ModelDelivery.Download
            : ModelDelivery.Bundle;

        public RuntimeModelFile FindFile(string repoPath)
        {
            return (files ?? Array.Empty<RuntimeModelFile>())
                .FirstOrDefault(file => string.Equals(file?.repoPath, repoPath, StringComparison.Ordinal));
        }

        public RuntimeDataFile FindDataFile(string repoPath)
        {
            return (dataFiles ?? Array.Empty<RuntimeDataFile>())
                .FirstOrDefault(file => string.Equals(file?.repoPath, repoPath, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// The models of one package.
    /// </summary>
    [Serializable]
    public sealed class RuntimePackageEntry
    {
        public string packageId;
        public string packageVersion;
        public RuntimeModelEntry[] models = Array.Empty<RuntimeModelEntry>();
    }

    /// <summary>
    /// The runtime registry the build writes to <c>StreamingAssets/model-hub/model-hub-runtime.json</c>: every model of
    /// every package, and where its runtime models come from.
    /// </summary>
    [Serializable]
    public sealed class RuntimeRegistry
    {
        public const int CurrentVersion = 2;

        public int version = CurrentVersion;

        /// <summary>
        /// Converter id of the ONNX converter in this build, for keys of models the player converts. Empty when the
        /// build has none.
        /// </summary>
        public string onnxConverterId;

        /// <summary>
        /// Inference Engine format generation the build was made with (<see cref="SentisCompatibility"/>).
        /// </summary>
        public string formatGeneration;

        public RuntimePackageEntry[] packages = Array.Empty<RuntimePackageEntry>();

        public static RuntimeRegistry FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new RuntimeRegistry();
            }

            var registry = JsonUtility.FromJson<RuntimeRegistry>(json) ?? new RuntimeRegistry();
            if (registry.version > CurrentVersion)
            {
                throw new InvalidOperationException(
                    $"The AI Model Hub runtime registry has version {registry.version}, but this player reads version {CurrentVersion}. Rebuild the player.");
            }

            return registry;
        }

        public string ToJson()
        {
            return JsonUtility.ToJson(this, true);
        }

        /// <summary>
        /// Finds a model file. With a package id, only that package's models are searched. Without one, the first model
        /// of that repository that has the file is used.
        /// </summary>
        public bool TryFind(string packageId, string repoId, string repoPath, out RuntimePackageEntry package, out RuntimeModelEntry model, out RuntimeModelFile file)
        {
            foreach (var candidatePackage in packages ?? Array.Empty<RuntimePackageEntry>())
            {
                if (packageId != null && !string.Equals(candidatePackage?.packageId, packageId, StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var candidateModel in candidatePackage?.models ?? Array.Empty<RuntimeModelEntry>())
                {
                    if (!string.Equals(candidateModel?.repoId, repoId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var candidateFile = repoPath == null ? null : candidateModel.FindFile(repoPath);
                    if (repoPath == null || candidateFile != null)
                    {
                        package = candidatePackage;
                        model = candidateModel;
                        file = candidateFile;
                        return true;
                    }
                }
            }

            package = null;
            model = null;
            file = null;
            return false;
        }

        /// <summary>
        /// Finds a data file, the same way <see cref="TryFind"/> finds a model file.
        /// </summary>
        public bool TryFindDataFile(string packageId, string repoId, string repoPath, out RuntimePackageEntry package, out RuntimeModelEntry model, out RuntimeDataFile file)
        {
            foreach (var candidatePackage in packages ?? Array.Empty<RuntimePackageEntry>())
            {
                if (packageId != null && !string.Equals(candidatePackage?.packageId, packageId, StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var candidateModel in candidatePackage?.models ?? Array.Empty<RuntimeModelEntry>())
                {
                    if (!string.Equals(candidateModel?.repoId, repoId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var candidateFile = candidateModel.FindDataFile(repoPath);
                    if (candidateFile != null)
                    {
                        package = candidatePackage;
                        model = candidateModel;
                        file = candidateFile;
                        return true;
                    }
                }
            }

            package = null;
            model = null;
            file = null;
            return false;
        }

        /// <summary>
        /// Lists the data file paths of every package entry of one repository, for error messages.
        /// </summary>
        public IReadOnlyList<string> GetKnownDataFiles(string repoId)
        {
            return (packages ?? Array.Empty<RuntimePackageEntry>())
                .SelectMany(package => package?.models ?? Array.Empty<RuntimeModelEntry>())
                .Where(model => string.Equals(model?.repoId, repoId, StringComparison.OrdinalIgnoreCase))
                .SelectMany(model => model.dataFiles ?? Array.Empty<RuntimeDataFile>())
                .Select(file => file.repoPath)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Lists the repository paths of every package entry of one repository, for error messages.
        /// </summary>
        public IReadOnlyList<string> GetKnownFiles(string repoId)
        {
            return (packages ?? Array.Empty<RuntimePackageEntry>())
                .SelectMany(package => package?.models ?? Array.Empty<RuntimeModelEntry>())
                .Where(model => string.Equals(model?.repoId, repoId, StringComparison.OrdinalIgnoreCase))
                .SelectMany(model => model.files ?? Array.Empty<RuntimeModelFile>())
                .Select(file => file.repoPath)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }
    }
}
