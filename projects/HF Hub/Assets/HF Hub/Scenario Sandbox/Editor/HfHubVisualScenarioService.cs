using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;

namespace Doji.AI.ModelHub.Sandbox.Editor
{
    /// <summary>
    /// Materializes fake local HF Hub artifacts for visual scenario testing inside companion project only.
    /// </summary>
    public static class HfHubVisualScenarioService
    {
        private const string GeneratedRootAssetPath = "Assets/HF Hub/Scenario Sandbox/Generated";
        private const string GeneratedManifestFileNameSuffix = ".asset";
        private const string ImportStateRoot = "Library/Doji/HfHub/import-state";
        private const string ImportMetadataFileName = "hf-hub-import.json";
        private const string ProjectOwnerId = "project";
        private const string ProjectOwnerVersion = "project";
        private const string RuntimeStreamingAssetsPrefix = "Assets/StreamingAssets/";

        public static void PopulateWithRecommendedEntries(HfHubVisualScenarioAsset scenario)
        {
            if (scenario == null)
            {
                throw new ArgumentNullException(nameof(scenario));
            }

            scenario.Entries = new[]
            {
                new HfHubVisualScenarioEntry
                {
                    Repository = "sandbox/needs-download",
                    Revision = "main",
                    ResolvedCommitHash = "sandbox-download-001",
                    State = HfHubVisualScenarioState.DownloadNeeded
                },
                new HfHubVisualScenarioEntry
                {
                    Repository = "sandbox/needs-import",
                    Revision = "main",
                    ResolvedCommitHash = "sandbox-import-001",
                    State = HfHubVisualScenarioState.ImportNeeded
                },
                new HfHubVisualScenarioEntry
                {
                    Repository = "sandbox/update-needed",
                    Revision = "main",
                    ResolvedCommitHash = "sandbox-update-001",
                    State = HfHubVisualScenarioState.UpdateNeeded
                },
                new HfHubVisualScenarioEntry
                {
                    Repository = "sandbox/cache-removed-after-import",
                    Revision = "main",
                    ResolvedCommitHash = "sandbox-imported-only-001",
                    State = HfHubVisualScenarioState.ImportedCacheRemoved
                },
                new HfHubVisualScenarioEntry
                {
                    Repository = "sandbox/partial-conversion",
                    Revision = "main",
                    ResolvedCommitHash = "sandbox-partial-001",
                    State = HfHubVisualScenarioState.PartialConversionWarning
                },
                new HfHubVisualScenarioEntry
                {
                    Repository = "sandbox/runtime-file-missing",
                    Revision = "main",
                    ResolvedCommitHash = "sandbox-runtime-missing-001",
                    State = HfHubVisualScenarioState.RuntimeFileMissing
                },
                new HfHubVisualScenarioEntry
                {
                    Repository = "sandbox/import-metadata-invalid",
                    Revision = "main",
                    ResolvedCommitHash = "sandbox-invalid-import-001",
                    State = HfHubVisualScenarioState.InvalidImportMetadata
                },
                new HfHubVisualScenarioEntry
                {
                    Repository = "sandbox/raw-metadata-invalid",
                    Revision = "main",
                    ResolvedCommitHash = "sandbox-invalid-raw-001",
                    State = HfHubVisualScenarioState.InvalidRawMetadata
                },
                new HfHubVisualScenarioEntry
                {
                    Repository = "sandbox/shared-repo",
                    Revision = "main",
                    ResolvedCommitHash = "sandbox-shared-001",
                    State = HfHubVisualScenarioState.ImportNeeded
                },
                new HfHubVisualScenarioEntry
                {
                    Repository = "sandbox/shared-repo",
                    Revision = "main",
                    ResolvedCommitHash = "sandbox-shared-001",
                    State = HfHubVisualScenarioState.ImportNeeded
                },
                new HfHubVisualScenarioEntry
                {
                    Repository = "sandbox/revision-ambiguity",
                    Revision = "main",
                    ResolvedCommitHash = "sandbox-ambiguity-main-001",
                    State = HfHubVisualScenarioState.ImportNeeded
                },
                new HfHubVisualScenarioEntry
                {
                    Repository = "sandbox/revision-ambiguity",
                    Revision = "refs/pr/42",
                    ResolvedCommitHash = "sandbox-ambiguity-pr42-001",
                    State = HfHubVisualScenarioState.ImportNeeded
                }
            };

            EditorUtility.SetDirty(scenario);
            AssetDatabase.SaveAssets();
        }

        public static void ApplyScenario(HfHubVisualScenarioAsset scenario)
        {
            if (scenario == null)
            {
                throw new ArgumentNullException(nameof(scenario));
            }

            ValidateScenario(scenario);
            CleanupScenario(scenario);

            for (var index = 0; index < scenario.Entries.Length; index++)
            {
                var entry = scenario.Entries[index];
                var manifestAsset = CreateOrUpdateManifestAsset(scenario, index, entry);
                var manifestHash = ComputeManifestHash(BuildStableManifestJson(entry));
                ApplyEntry(manifestHash, entry);
                EditorUtility.SetDirty(manifestAsset);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        public static void CleanupScenario(HfHubVisualScenarioAsset scenario)
        {
            if (scenario == null)
            {
                throw new ArgumentNullException(nameof(scenario));
            }

            foreach (var repository in (scenario.Entries ?? Array.Empty<HfHubVisualScenarioEntry>())
                         .Select(entry => entry.Repository)
                         .Where(repository => !string.IsNullOrWhiteSpace(repository))
                         .Distinct(StringComparer.Ordinal))
            {
                DeleteAssetIfExists(GetRuntimeSnapshotFolderAssetPath(repository));
                DeleteDirectoryIfExists(GetCacheRepositoryRoot(repository));
                DeleteProjectDirectoryIfExists(GetImportStateFolder(ProjectOwnerId, repository));
            }

            var generatedFolderAssetPath = GetGeneratedScenarioFolderAssetPath(scenario);
            if (AssetDatabase.IsValidFolder(generatedFolderAssetPath))
            {
                AssetDatabase.DeleteAsset(generatedFolderAssetPath);
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        public static void OpenModelHubWindow()
        {
            EditorApplication.ExecuteMenuItem("Window/AI Model Hub");
        }

        public static string GetGeneratedOutputRootAssetPath(HfHubVisualScenarioAsset scenario)
        {
            return GetGeneratedScenarioFolderAssetPath(scenario);
        }

        public static string GetGeneratedManifestAssetPath(HfHubVisualScenarioAsset scenario, int entryIndex)
        {
            if (scenario == null)
            {
                throw new ArgumentNullException(nameof(scenario));
            }

            if (entryIndex < 0 || entryIndex >= (scenario.Entries?.Length ?? 0))
            {
                throw new ArgumentOutOfRangeException(nameof(entryIndex));
            }

            var entry = scenario.Entries[entryIndex];
            var fileName =
                $"{entryIndex + 1:00}-{SanitizePathSegment(entry.Repository)}-{SanitizePathSegment(entry.Revision)}{GeneratedManifestFileNameSuffix}";
            return $"{GetGeneratedScenarioFolderAssetPath(scenario)}/{fileName}";
        }

        public static string GetImportMetadataProjectRelativePath(HfHubVisualScenarioEntry entry)
        {
            return $"{GetImportStateFolder(ProjectOwnerId, entry.Repository)}/{ImportMetadataFileName}";
        }

        public static string GetRuntimeSentisAssetPath(HfHubVisualScenarioEntry entry)
        {
            var sanitizedRelativeRepoPath = SanitizeRelativePath(entry.RepoPath);
            var sentisRelativePath = Path.ChangeExtension(sanitizedRelativeRepoPath, ".sentis")?.Replace('\\', '/')
                ?? throw new InvalidOperationException($"Could not derive .sentis path from '{entry.RepoPath}'.");
            return $"{GetRuntimeSnapshotFolderAssetPath(entry.Repository)}/{sentisRelativePath}";
        }

        public static string GetRevisionRefPath(HfHubVisualScenarioEntry entry)
        {
            return Path.Combine(GetCacheRepositoryRoot(entry.Repository), "refs", SanitizeCacheSegment(entry.Revision));
        }

        private static void ApplyEntry(string manifestHash, HfHubVisualScenarioEntry entry)
        {
            var rawFilePath = GetSnapshotFilePath(entry.Repository, entry.ResolvedCommitHash, entry.RepoPath);
            var snapshotRoot = GetSnapshotRoot(entry.Repository, entry.ResolvedCommitHash);
            var rawMetadataPath = GetSnapshotMetadataPath(entry.Repository, entry.ResolvedCommitHash);
            var revisionRefPath = GetRevisionRefPath(entry);
            var runtimeAssetPath = GetRuntimeSentisAssetPath(entry);
            var runtimeAbsolutePath = AssetPathToAbsolutePath(runtimeAssetPath);
            var importMetadataProjectRelativePath = GetImportMetadataProjectRelativePath(entry);
            var importMetadataAbsolutePath = ProjectRelativeToAbsolutePath(importMetadataProjectRelativePath);

            if (entry.State != HfHubVisualScenarioState.DownloadNeeded &&
                entry.State != HfHubVisualScenarioState.ImportedCacheRemoved)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(rawFilePath) ?? throw new InvalidOperationException("Missing raw file directory."));
                Directory.CreateDirectory(Path.GetDirectoryName(revisionRefPath) ?? throw new InvalidOperationException("Missing revision ref directory."));
                File.WriteAllText(rawFilePath, "fake onnx");
                File.WriteAllText(revisionRefPath, entry.ResolvedCommitHash);
            }

            if (entry.State != HfHubVisualScenarioState.DownloadNeeded &&
                entry.State != HfHubVisualScenarioState.ImportedCacheRemoved &&
                entry.State != HfHubVisualScenarioState.InvalidRawMetadata)
            {
                WriteJson(
                    rawMetadataPath,
                    new RawCacheMetadataDocument
                    {
                        Repository = entry.Repository,
                        RequestedRevision = entry.Revision,
                        ResolvedCommitHash = entry.ResolvedCommitHash,
                        CachedSnapshotRoot = snapshotRoot,
                        Files = new[]
                        {
                            new RawCacheFileDocument
                            {
                                RepoPath = entry.RepoPath,
                                CachedFilePath = rawFilePath,
                                SizeInBytes = new FileInfo(rawFilePath).Length
                            }
                        },
                        DownloadedAtUtc = DateTime.UtcNow.ToString("O")
                    });
            }

            if (entry.State == HfHubVisualScenarioState.InvalidRawMetadata)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(rawMetadataPath) ?? throw new InvalidOperationException("Missing raw metadata directory."));
                File.WriteAllText(rawMetadataPath, "{ invalid json");
            }

            if (ShouldWriteRuntimeFile(entry.State))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(runtimeAbsolutePath) ?? throw new InvalidOperationException("Missing runtime directory."));
                File.WriteAllText(runtimeAbsolutePath, "fake sentis");
            }

            if (ShouldWriteImportMetadata(entry.State))
            {
                var importedManifestHash = entry.State == HfHubVisualScenarioState.UpdateNeeded
                    ? manifestHash + "-stale"
                    : manifestHash;
                var diagnostics = entry.State == HfHubVisualScenarioState.PartialConversionWarning
                    ? new[]
                    {
                        new ImportDiagnosticDocument
                        {
                            Severity = "warning",
                            Message = $"Partial ONNX conversion: skipped {entry.PartialWarningRepoPath} because {entry.PartialWarningDetail}"
                        }
                    }
                    : Array.Empty<ImportDiagnosticDocument>();

                WriteJson(
                    importMetadataAbsolutePath,
                    new ImportedModelMetadataDocument
                    {
                        PackageId = ProjectOwnerId,
                        PackageVersion = ProjectOwnerVersion,
                        ModelId = entry.Repository,
                        ManifestHash = importedManifestHash,
                        Repository = entry.Repository,
                        RequestedRevision = entry.Revision,
                        ResolvedCommitHash = entry.ResolvedCommitHash,
                        RawSnapshotRootPath = snapshotRoot,
                        RawMetadataPath = rawMetadataPath,
                        RawCacheSizeInBytes = entry.State == HfHubVisualScenarioState.ImportedCacheRemoved ? null : new FileInfo(rawFilePath).Length,
                        SentisVersion = GetSentisVersion(),
                        UnityVersion = Application.unityVersion,
                        Diagnostics = diagnostics,
                        RuntimeFiles = new[]
                        {
                            new ImportedRuntimeFileDocument
                            {
                                RepoPath = entry.RepoPath,
                                Kind = "sentis",
                                AssetPath = runtimeAssetPath,
                                RelativePath = ToStreamingAssetsRelativePath(runtimeAssetPath),
                                SizeInBytes = ShouldWriteRuntimeFile(entry.State)
                                    ? new FileInfo(runtimeAbsolutePath).Length
                                    : null
                            }
                        },
                        ImportedAtUtc = DateTime.UtcNow.ToString("O")
                    });
            }
            else if (entry.State == HfHubVisualScenarioState.InvalidImportMetadata)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(importMetadataAbsolutePath) ?? throw new InvalidOperationException("Missing import metadata directory."));
                File.WriteAllText(importMetadataAbsolutePath, "{ invalid json");
            }
        }

        private static bool ShouldWriteRuntimeFile(HfHubVisualScenarioState state)
        {
            return state is HfHubVisualScenarioState.Imported or
                HfHubVisualScenarioState.ImportedCacheRemoved or
                HfHubVisualScenarioState.UpdateNeeded or
                HfHubVisualScenarioState.PartialConversionWarning or
                HfHubVisualScenarioState.InvalidImportMetadata;
        }

        private static bool ShouldWriteImportMetadata(HfHubVisualScenarioState state)
        {
            return state is HfHubVisualScenarioState.Imported or
                HfHubVisualScenarioState.ImportedCacheRemoved or
                HfHubVisualScenarioState.UpdateNeeded or
                HfHubVisualScenarioState.RuntimeFileMissing or
                HfHubVisualScenarioState.PartialConversionWarning;
        }

        private static HfHubProjectManifestAsset CreateOrUpdateManifestAsset(
            HfHubVisualScenarioAsset scenario,
            int entryIndex,
            HfHubVisualScenarioEntry entry)
        {
            var manifestAssetPath = GetGeneratedManifestAssetPath(scenario, entryIndex);
            EnsureAssetFolderExists(Path.GetDirectoryName(manifestAssetPath)?.Replace('\\', '/')
                ?? throw new InvalidOperationException("Missing scenario manifest asset folder."));

            var manifestAsset = AssetDatabase.LoadAssetAtPath<HfHubProjectManifestAsset>(manifestAssetPath);
            if (manifestAsset == null)
            {
                manifestAsset = ScriptableObject.CreateInstance<HfHubProjectManifestAsset>();
                AssetDatabase.CreateAsset(manifestAsset, manifestAssetPath);
            }

            SetAutoPropertyBackingField(manifestAsset, "<Version>k__BackingField", 1);
            SetAutoPropertyBackingField(
                manifestAsset,
                "<Models>k__BackingField",
                new[]
                {
                    new HfHubProjectManifestModelAssetEntry
                    {
                        Id = entry.Repository,
                        Revision = entry.Revision
                    }
                });
            return manifestAsset;
        }

        private static void ValidateScenario(HfHubVisualScenarioAsset scenario)
        {
            if (scenario.Entries == null || scenario.Entries.Length == 0)
            {
                throw new InvalidOperationException("Visual scenario needs at least one entry.");
            }

            foreach (var entry in scenario.Entries)
            {
                if (entry == null)
                {
                    throw new InvalidOperationException("Visual scenario contains null entry.");
                }

                if (string.IsNullOrWhiteSpace(entry.Repository))
                {
                    throw new InvalidOperationException("Visual scenario entry repository cannot be empty.");
                }

                if (string.IsNullOrWhiteSpace(entry.Revision))
                {
                    throw new InvalidOperationException($"Visual scenario revision cannot be empty for '{entry.Repository}'.");
                }

                if (string.IsNullOrWhiteSpace(entry.ResolvedCommitHash))
                {
                    throw new InvalidOperationException($"Visual scenario resolved commit cannot be empty for '{entry.Repository}'.");
                }

                if (string.IsNullOrWhiteSpace(entry.RepoPath))
                {
                    throw new InvalidOperationException($"Visual scenario repo path cannot be empty for '{entry.Repository}'.");
                }
            }
        }

        private static string BuildStableManifestJson(HfHubVisualScenarioEntry entry)
        {
            return JsonUtility.ToJson(
                new ManifestAssetDocument
                {
                    _version = 1,
                    _models = new[]
                    {
                        new ManifestAssetEntryDocument
                        {
                            _id = entry.Repository,
                            _revision = entry.Revision
                        }
                    }
                });
        }

        private static string ComputeManifestHash(string contents)
        {
            using var sha256 = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(contents ?? string.Empty);
            var hashBytes = sha256.ComputeHash(bytes);
            return string.Concat(hashBytes.Select(hashByte => hashByte.ToString("x2")));
        }

        private static string GetSentisVersion()
        {
            return UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ModelAsset).Assembly)?.version ?? "unknown";
        }

        private static string GetGeneratedScenarioFolderAssetPath(HfHubVisualScenarioAsset scenario)
        {
            var folderName = SanitizePathSegment(scenario == null ? "Scenario" : scenario.name);
            return $"{GeneratedRootAssetPath}/{folderName}";
        }

        private static string GetRuntimeSnapshotFolderAssetPath(string repository)
        {
            return $"{ModelHub.RuntimeStreamingAssetsRoot}/{SanitizeRelativePath(repository)}";
        }

        private static string GetCacheRepositoryRoot(string repository)
        {
            return Path.Combine(GetCacheRoot(), "raw", repository.Replace("/", "--", StringComparison.Ordinal));
        }

        private static string GetSnapshotRoot(string repository, string commitHash)
        {
            return Path.Combine(GetCacheRepositoryRoot(repository), "snapshots", SanitizeCacheSegment(commitHash));
        }

        private static string GetSnapshotMetadataPath(string repository, string commitHash)
        {
            return Path.Combine(GetSnapshotRoot(repository, commitHash), "snapshot.hfhub.json");
        }

        private static string GetSnapshotFilePath(string repository, string commitHash, string repoPath)
        {
            return Path.Combine(GetSnapshotRoot(repository, commitHash), repoPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string GetImportStateFolder(string packageId, string modelId)
        {
            return $"{ImportStateRoot}/{SanitizePathSegment(packageId)}/{SanitizePathSegment(modelId)}";
        }

        private static string GetCacheRoot()
        {
            var overrideRoot = Environment.GetEnvironmentVariable("DOJI_HF_HUB_CACHE_ROOT");
            if (!string.IsNullOrWhiteSpace(overrideRoot))
            {
                return overrideRoot;
            }

            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Doji",
                    "HfHub");
            }

            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Personal),
                    "Library",
                    "Application Support",
                    "Doji",
                    "HfHub");
            }

            var xdgCacheHome = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            if (!string.IsNullOrWhiteSpace(xdgCacheHome))
            {
                return Path.Combine(xdgCacheHome, "doji", "hf-hub");
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Personal),
                ".cache",
                "doji",
                "hf-hub");
        }

        private static string SanitizeCacheSegment(string value)
        {
            var sanitized = (value ?? string.Empty).Trim().Replace("/", "_", StringComparison.Ordinal);
            foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
            {
                sanitized = sanitized.Replace(invalidCharacter, '_');
            }

            return string.IsNullOrWhiteSpace(sanitized) ? "unnamed" : sanitized;
        }

        private static string SanitizePathSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "unnamed";
            }

            var invalidCharacters = Path.GetInvalidFileNameChars();
            var sanitized = new string(value
                .Select(character => invalidCharacters.Contains(character) || character == '/' || character == '\\'
                    ? '-'
                    : character)
                .ToArray());
            return sanitized.Replace(" ", "-");
        }

        private static string SanitizeRelativePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "unnamed";
            }

            return string.Join(
                "/",
                value.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(SanitizePathSegment));
        }

        private static string ToStreamingAssetsRelativePath(string assetPath)
        {
            if (!assetPath.StartsWith(RuntimeStreamingAssetsPrefix, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Expected asset path under Assets/StreamingAssets, got '{assetPath}'.");
            }

            return assetPath.Substring(RuntimeStreamingAssetsPrefix.Length);
        }

        private static string AssetPathToAbsolutePath(string assetPath)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }

        private static string ProjectRelativeToAbsolutePath(string projectRelativePath)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, projectRelativePath));
        }

        private static void DeleteAssetIfExists(string assetPath)
        {
            if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null || Directory.Exists(AssetPathToAbsolutePath(assetPath)))
            {
                AssetDatabase.DeleteAsset(assetPath);
            }
        }

        private static void DeleteDirectoryIfExists(string absolutePath)
        {
            if (Directory.Exists(absolutePath))
            {
                Directory.Delete(absolutePath, true);
            }
        }

        private static void DeleteProjectDirectoryIfExists(string projectRelativePath)
        {
            DeleteDirectoryIfExists(ProjectRelativeToAbsolutePath(projectRelativePath));
        }

        private static void EnsureAssetFolderExists(string assetFolderPath)
        {
            if (AssetDatabase.IsValidFolder(assetFolderPath))
            {
                return;
            }

            var parts = assetFolderPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !string.Equals(parts[0], "Assets", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Expected Unity asset folder under Assets, got '{assetFolderPath}'.");
            }

            var currentFolder = parts[0];
            for (var index = 1; index < parts.Length; index++)
            {
                var nextFolder = $"{currentFolder}/{parts[index]}";
                if (!AssetDatabase.IsValidFolder(nextFolder))
                {
                    AssetDatabase.CreateFolder(currentFolder, parts[index]);
                }

                currentFolder = nextFolder;
            }
        }

        private static void SetAutoPropertyBackingField<TTarget, TValue>(TTarget target, string fieldName, TValue value)
            where TTarget : UnityEngine.Object
        {
            var field = typeof(TTarget).GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field == null)
            {
                throw new InvalidOperationException($"Could not find backing field '{fieldName}' on {typeof(TTarget).Name}.");
            }

            field.SetValue(target, value);
        }

        private static void WriteJson<TDocument>(string absolutePath, TDocument document)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath) ?? throw new InvalidOperationException("Missing JSON output directory."));
            File.WriteAllText(absolutePath, JsonUtility.ToJson(document, true));
        }

        [Serializable]
        private sealed class ManifestAssetDocument
        {
            [SerializeField]
            public int _version;

            [SerializeField]
            public ManifestAssetEntryDocument[] _models = Array.Empty<ManifestAssetEntryDocument>();
        }

        [Serializable]
        private sealed class ManifestAssetEntryDocument
        {
            [SerializeField]
            public string _id;

            [SerializeField]
            public string _revision;
        }

        [Serializable]
        private sealed class RawCacheFileDocument
        {
            public string RepoPath;
            public string CachedFilePath;
            public long SizeInBytes;
        }

        [Serializable]
        private sealed class RawCacheMetadataDocument
        {
            public string Repository;
            public string RequestedRevision;
            public string ResolvedCommitHash;
            public string CachedSnapshotRoot;
            public RawCacheFileDocument[] Files = Array.Empty<RawCacheFileDocument>();
            public string DownloadedAtUtc;
        }

        [Serializable]
        private sealed class ImportDiagnosticDocument
        {
            public string Severity;
            public string Message;
        }

        [Serializable]
        private sealed class ImportedRuntimeFileDocument
        {
            public string RepoPath;
            public string Kind;
            public string AssetPath;
            public string RelativePath;
            public long? SizeInBytes;
        }

        [Serializable]
        private sealed class ImportedModelMetadataDocument
        {
            public string PackageId;
            public string PackageVersion;
            public string ModelId;
            public string ManifestHash;
            public string Repository;
            public string RequestedRevision;
            public string ResolvedCommitHash;
            public string RawSnapshotRootPath;
            public string RawMetadataPath;
            public long? RawCacheSizeInBytes;
            public string SentisVersion;
            public string UnityVersion;
            public ImportDiagnosticDocument[] Diagnostics = Array.Empty<ImportDiagnosticDocument>();
            public ImportedRuntimeFileDocument[] RuntimeFiles = Array.Empty<ImportedRuntimeFileDocument>();
            public string ImportedAtUtc;
        }
    }
}
