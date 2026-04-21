using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;
using Doji.AI.ModelHub.Editor;

namespace Doji.AI.ModelHub.Sandbox.Editor
{
    /// <summary>
    /// Materializes fake local HF Hub artifacts for visual scenario testing inside companion project only.
    /// </summary>
    public static class VisualScenarioService
    {
        private const string GeneratedRootAssetPath = "Assets/HF Hub/Scenario Sandbox/Generated";
        private const string GeneratedManifestFileNameSuffix = ".asset";
        private const string ImportsRoot = "Library/Doji/HfHub/imports";
        private const string BindingImportsRoot = ImportsRoot + "/bindings";
        private const string SharedImportsRoot = ImportsRoot + "/shared";
        private const string BindingMetadataFileName = "hf-hub-binding.json";
        private const string SharedMetadataFileName = "hf-hub-shared-import.json";
        private const string ProjectOwnerId = "project";
        private const string ProjectOwnerVersion = "project";
        private const string RuntimeStreamingAssetsPrefix = "Assets/StreamingAssets/";

        public static void PopulateWithRecommendedEntries(VisualScenarioAsset scenario)
        {
            if (scenario == null)
            {
                throw new ArgumentNullException(nameof(scenario));
            }

            scenario.Entries = new[]
            {
                new VisualScenarioEntry
                {
                    Repository = "sandbox/needs-download",
                    Revision = "main",
                    ResolvedCommitHash = "sandbox-download-001",
                    State = VisualScenarioState.DownloadNeeded
                },
                new VisualScenarioEntry
                {
                    Repository = "sandbox/needs-import",
                    Revision = "main",
                    ResolvedCommitHash = "sandbox-import-001",
                    State = VisualScenarioState.ImportNeeded
                },
                new VisualScenarioEntry
                {
                    Repository = "sandbox/update-needed",
                    Revision = "main",
                    ResolvedCommitHash = "sandbox-update-001",
                    State = VisualScenarioState.UpdateNeeded
                },
                new VisualScenarioEntry
                {
                    Repository = "sandbox/cache-removed-after-import",
                    Revision = "main",
                    ResolvedCommitHash = "sandbox-imported-only-001",
                    State = VisualScenarioState.ImportedCacheRemoved
                },
                new VisualScenarioEntry
                {
                    Repository = "sandbox/partial-conversion",
                    Revision = "main",
                    ResolvedCommitHash = "sandbox-partial-001",
                    State = VisualScenarioState.PartialConversionWarning
                },
                new VisualScenarioEntry
                {
                    Repository = "sandbox/runtime-file-missing",
                    Revision = "main",
                    ResolvedCommitHash = "sandbox-runtime-missing-001",
                    State = VisualScenarioState.RuntimeFileMissing
                },
                new VisualScenarioEntry
                {
                    Repository = "sandbox/import-metadata-invalid",
                    Revision = "main",
                    ResolvedCommitHash = "sandbox-invalid-import-001",
                    State = VisualScenarioState.InvalidImportMetadata
                },
                new VisualScenarioEntry
                {
                    Repository = "sandbox/raw-metadata-invalid",
                    Revision = "main",
                    ResolvedCommitHash = "sandbox-invalid-raw-001",
                    State = VisualScenarioState.InvalidRawMetadata
                },
                new VisualScenarioEntry
                {
                    Repository = "sandbox/shared-repo",
                    Revision = "main",
                    ResolvedCommitHash = "sandbox-shared-001",
                    State = VisualScenarioState.ImportNeeded
                },
                new VisualScenarioEntry
                {
                    Repository = "sandbox/shared-repo",
                    Revision = "main",
                    ResolvedCommitHash = "sandbox-shared-001",
                    State = VisualScenarioState.ImportNeeded
                },
                new VisualScenarioEntry
                {
                    Repository = "sandbox/revision-ambiguity",
                    Revision = "main",
                    ResolvedCommitHash = "sandbox-ambiguity-main-001",
                    State = VisualScenarioState.ImportNeeded
                },
                new VisualScenarioEntry
                {
                    Repository = "sandbox/revision-ambiguity",
                    Revision = "refs/pr/42",
                    ResolvedCommitHash = "sandbox-ambiguity-pr42-001",
                    State = VisualScenarioState.ImportNeeded
                }
            };

            EditorUtility.SetDirty(scenario);
            AssetDatabase.SaveAssets();
        }

        public static void ApplyScenario(VisualScenarioAsset scenario)
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

        public static void CleanupScenario(VisualScenarioAsset scenario)
        {
            if (scenario == null)
            {
                throw new ArgumentNullException(nameof(scenario));
            }

            foreach (var repository in (scenario.Entries ?? Array.Empty<VisualScenarioEntry>())
                         .Select(entry => entry.Repository)
                         .Where(repository => !string.IsNullOrWhiteSpace(repository))
                         .Distinct(StringComparer.Ordinal))
            {
                DeleteAssetIfExists(GetRuntimeSnapshotFolderAssetPath(repository));
                DeleteDirectoryIfExists(GetCacheRepositoryRoot(repository));
                DeleteProjectDirectoryIfExists(GetImportBindingFolder(ProjectOwnerId, repository));
                DeleteProjectDirectoryIfExists(GetSharedImportFolder(repository, entryResolvedCommitHash: null, deleteAllForRepository: true));
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
            ModelHubWindow.OpenWindow(resetFilters: true);
        }

        public static string GetGeneratedOutputRootAssetPath(VisualScenarioAsset scenario)
        {
            return GetGeneratedScenarioFolderAssetPath(scenario);
        }

        public static string GetGeneratedManifestAssetPath(VisualScenarioAsset scenario, int entryIndex)
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

        public static string GetImportMetadataProjectRelativePath(VisualScenarioEntry entry)
        {
            return $"{GetImportBindingFolder(ProjectOwnerId, entry.Repository)}/{BindingMetadataFileName}";
        }

        public static string GetSharedImportMetadataProjectRelativePath(VisualScenarioEntry entry)
        {
            return $"{GetSharedImportFolder(entry.Repository, entry.ResolvedCommitHash)}/{SharedMetadataFileName}";
        }

        public static string GetRuntimeSentisAssetPath(VisualScenarioEntry entry)
        {
            var sanitizedRelativeRepoPath = SanitizeRelativePath(entry.RepoPath);
            var sentisRelativePath = Path.ChangeExtension(sanitizedRelativeRepoPath, ".sentis")?.Replace('\\', '/')
                ?? throw new InvalidOperationException($"Could not derive .sentis path from '{entry.RepoPath}'.");
            return $"{GetRuntimeSnapshotFolderAssetPath(entry.Repository)}/{sentisRelativePath}";
        }

        public static string GetRevisionRefPath(VisualScenarioEntry entry)
        {
            return Path.Combine(GetCacheRepositoryRoot(entry.Repository), "refs", SanitizeCacheSegment(entry.Revision));
        }

        private static void ApplyEntry(string manifestHash, VisualScenarioEntry entry)
        {
            var rawFilePath = GetSnapshotFilePath(entry.Repository, entry.ResolvedCommitHash, entry.RepoPath);
            var snapshotRoot = GetSnapshotRoot(entry.Repository, entry.ResolvedCommitHash);
            var rawMetadataPath = GetSnapshotMetadataPath(entry.Repository, entry.ResolvedCommitHash);
            var revisionRefPath = GetRevisionRefPath(entry);
            var runtimeAssetPath = GetRuntimeSentisAssetPath(entry);
            var runtimeAbsolutePath = AssetPathToAbsolutePath(runtimeAssetPath);
            var bindingMetadataProjectRelativePath = GetImportMetadataProjectRelativePath(entry);
            var bindingMetadataAbsolutePath = ProjectRelativeToAbsolutePath(bindingMetadataProjectRelativePath);
            var sharedMetadataProjectRelativePath = GetSharedImportMetadataProjectRelativePath(entry);
            var sharedMetadataAbsolutePath = ProjectRelativeToAbsolutePath(sharedMetadataProjectRelativePath);

            if (entry.State != VisualScenarioState.DownloadNeeded &&
                entry.State != VisualScenarioState.ImportedCacheRemoved)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(rawFilePath) ?? throw new InvalidOperationException("Missing raw file directory."));
                Directory.CreateDirectory(Path.GetDirectoryName(revisionRefPath) ?? throw new InvalidOperationException("Missing revision ref directory."));
                File.WriteAllText(rawFilePath, "fake onnx");
                File.WriteAllText(revisionRefPath, entry.ResolvedCommitHash);
            }

            if (entry.State != VisualScenarioState.DownloadNeeded &&
                entry.State != VisualScenarioState.ImportedCacheRemoved &&
                entry.State != VisualScenarioState.InvalidRawMetadata)
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

            if (entry.State == VisualScenarioState.InvalidRawMetadata)
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
                var importedManifestHash = entry.State == VisualScenarioState.UpdateNeeded
                    ? manifestHash + "-stale"
                    : manifestHash;
                var diagnostics = entry.State == VisualScenarioState.PartialConversionWarning
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
                    sharedMetadataAbsolutePath,
                    new SharedImportMetadataDocument
                    {
                        Repository = entry.Repository,
                        ResolvedCommitHash = entry.ResolvedCommitHash,
                        RequestedRevisionHint = entry.Revision,
                        RawSnapshotRootPath = snapshotRoot,
                        RawMetadataPath = rawMetadataPath,
                        RawCacheSizeInBytes = entry.State == VisualScenarioState.ImportedCacheRemoved ? null : new FileInfo(rawFilePath).Length,
                        SentisVersion = GetSentisVersion(),
                        UnityVersion = Application.unityVersion,
                        RuntimeSnapshotAssetPath = GetRuntimeSnapshotFolderAssetPath(entry.Repository),
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
                WriteJson(
                    bindingMetadataAbsolutePath,
                    new ImportBindingMetadataDocument
                    {
                        PackageId = ProjectOwnerId,
                        PackageVersion = ProjectOwnerVersion,
                        ModelId = entry.Repository,
                        ManifestHash = importedManifestHash,
                        Repository = entry.Repository,
                        RequestedRevision = entry.Revision,
                        ResolvedCommitHash = entry.ResolvedCommitHash,
                        SentisVersion = GetSentisVersion(),
                        SharedImportMetadataPath = sharedMetadataProjectRelativePath,
                        RuntimeRegistryAssetPath = $"Assets/StreamingAssets/hf-hub/registries/{ProjectOwnerId}/hf-hub-runtime.json",
                        RuntimeRegistryRelativePath = $"hf-hub/registries/{ProjectOwnerId}/hf-hub-runtime.json",
                        BoundAtUtc = DateTime.UtcNow.ToString("O")
                    });
            }
            else if (entry.State == VisualScenarioState.InvalidImportMetadata)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(bindingMetadataAbsolutePath) ?? throw new InvalidOperationException("Missing import metadata directory."));
                File.WriteAllText(bindingMetadataAbsolutePath, "{ invalid json");
            }
        }

        private static bool ShouldWriteRuntimeFile(VisualScenarioState state)
        {
            return state is VisualScenarioState.Imported or
                VisualScenarioState.ImportedCacheRemoved or
                VisualScenarioState.UpdateNeeded or
                VisualScenarioState.PartialConversionWarning or
                VisualScenarioState.InvalidImportMetadata;
        }

        private static bool ShouldWriteImportMetadata(VisualScenarioState state)
        {
            return state is VisualScenarioState.Imported or
                VisualScenarioState.ImportedCacheRemoved or
                VisualScenarioState.UpdateNeeded or
                VisualScenarioState.RuntimeFileMissing or
                VisualScenarioState.PartialConversionWarning;
        }

        private static ProjectManifestAsset CreateOrUpdateManifestAsset(
            VisualScenarioAsset scenario,
            int entryIndex,
            VisualScenarioEntry entry)
        {
            var manifestAssetPath = GetGeneratedManifestAssetPath(scenario, entryIndex);
            EnsureAssetFolderExists(Path.GetDirectoryName(manifestAssetPath)?.Replace('\\', '/')
                ?? throw new InvalidOperationException("Missing scenario manifest asset folder."));

            var manifestAsset = AssetDatabase.LoadAssetAtPath<ProjectManifestAsset>(manifestAssetPath);
            if (manifestAsset == null)
            {
                manifestAsset = ScriptableObject.CreateInstance<ProjectManifestAsset>();
                AssetDatabase.CreateAsset(manifestAsset, manifestAssetPath);
            }

            SetAutoPropertyBackingField(manifestAsset, "<Version>k__BackingField", 1);
            SetAutoPropertyBackingField(
                manifestAsset,
                "<Models>k__BackingField",
                new[]
                {
                    new ProjectManifestModelAssetEntry
                    {
                        Id = entry.Repository,
                        Revision = entry.Revision
                    }
                });
            return manifestAsset;
        }

        private static void ValidateScenario(VisualScenarioAsset scenario)
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

        private static string BuildStableManifestJson(VisualScenarioEntry entry)
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

        private static string GetGeneratedScenarioFolderAssetPath(VisualScenarioAsset scenario)
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

        private static string GetImportBindingFolder(string packageId, string modelId)
        {
            return $"{BindingImportsRoot}/{SanitizePathSegment(packageId)}/{SanitizePathSegment(modelId)}";
        }

        private static string GetSharedImportFolder(string repository, string entryResolvedCommitHash, bool deleteAllForRepository = false)
        {
            var root = $"{SharedImportsRoot}/{SanitizePathSegment(repository)}";
            if (deleteAllForRepository || string.IsNullOrWhiteSpace(entryResolvedCommitHash))
            {
                return root;
            }

            return $"{root}/{SanitizePathSegment(entryResolvedCommitHash)}/{SanitizePathSegment(GetSentisVersion())}";
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
        private sealed class SharedImportMetadataDocument
        {
            public string Repository;
            public string ResolvedCommitHash;
            public string RequestedRevisionHint;
            public string RawSnapshotRootPath;
            public string RawMetadataPath;
            public long? RawCacheSizeInBytes;
            public string SentisVersion;
            public string UnityVersion;
            public string RuntimeSnapshotAssetPath;
            public ImportDiagnosticDocument[] Diagnostics = Array.Empty<ImportDiagnosticDocument>();
            public ImportedRuntimeFileDocument[] RuntimeFiles = Array.Empty<ImportedRuntimeFileDocument>();
            public string ImportedAtUtc;
        }

        [Serializable]
        private sealed class ImportBindingMetadataDocument
        {
            public string PackageId;
            public string PackageVersion;
            public string ModelId;
            public string ManifestHash;
            public string Repository;
            public string RequestedRevision;
            public string ResolvedCommitHash;
            public string SentisVersion;
            public string SharedImportMetadataPath;
            public string RuntimeRegistryAssetPath;
            public string RuntimeRegistryRelativePath;
            public string BoundAtUtc;
        }
    }
}
