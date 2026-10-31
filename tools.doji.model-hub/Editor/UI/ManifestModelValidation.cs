using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Networking;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Keeps the split owner/model editing model while still round-tripping manifest ids as <c>owner/name</c>.
    /// </summary>
    internal sealed class ManifestModelEntry
    {
        public ManifestModelEntry(string owner, string name, string revision, ModelDependencyFileDefinition[] files = null)
        {
            Owner = owner ?? string.Empty;
            Name = name ?? string.Empty;
            Revision = revision ?? string.Empty;
            FileEntries = ModelDependencyDefinition.NormalizeFileEntries(files);
        }

        public string Owner { get; set; }
        public string Name { get; set; }
        public string Revision { get; set; }
        public ModelDependencyFileDefinition[] FileEntries { get; set; }

        public string ModelId => string.IsNullOrWhiteSpace(Owner)
            ? Name ?? string.Empty
            : $"{Owner?.Trim()}/{Name?.Trim()}";

        public bool HasAnyModelIdContent =>
            !string.IsNullOrWhiteSpace(Owner) ||
            !string.IsNullOrWhiteSpace(Name);

        public bool HasCompleteModelId =>
            !string.IsNullOrWhiteSpace(Owner) &&
            !string.IsNullOrWhiteSpace(Name);

        public bool HasExplicitFileSelection => (FileEntries ?? Array.Empty<ModelDependencyFileDefinition>()).Length > 0;
        public string FileSelectionSummary
        {
            get
            {
                if (!HasExplicitFileSelection)
                {
                    return "All files";
                }

                var optionalCount = FileEntries.Count(file => file.Optional);
                var requiredCount = FileEntries.Length - optionalCount;
                if (optionalCount == 0)
                {
                    return requiredCount == 1 ? "1 file" : $"{requiredCount} files";
                }

                return requiredCount == 0 ? $"{optionalCount} optional" : $"{requiredCount} + {optionalCount} optional";
            }
        }

        /// <summary>
        /// Whether a row has partial or empty model id content that cannot be saved.
        /// </summary>
        public static bool IsIncomplete(ManifestModelEntry entry)
        {
            return entry != null && (!entry.HasAnyModelIdContent || !entry.HasCompleteModelId);
        }

        /// <summary>
        /// Whether the file picker can open: it needs a model id that is known to exist when validation is available.
        /// </summary>
        public static bool CanOpenFileSelection(
            ManifestModelEntry entry,
            ManifestModelValidationState validationState,
            bool hasValidationProvider)
        {
            if (string.IsNullOrWhiteSpace(entry?.ModelId))
            {
                return false;
            }

            return !hasValidationProvider || validationState?.Status == ManifestModelValidationStatus.Valid;
        }

        /// <summary>
        /// Parses <c>owner/name</c> or a Hugging Face model URL into owner and name.
        /// </summary>
        public static bool TryParseModelId(string input, out string owner, out string name)
        {
            owner = string.Empty;
            name = string.Empty;
            var value = input?.Trim() ?? string.Empty;
            if (value.Length == 0)
            {
                return false;
            }

            const string host = "huggingface.co/";
            var hostIndex = value.IndexOf(host, StringComparison.OrdinalIgnoreCase);
            if (hostIndex >= 0)
            {
                value = value.Substring(hostIndex + host.Length);
            }

            var segments = value.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 2)
            {
                name = value.Trim('/');
                return false;
            }

            owner = segments[0].Trim();
            name = segments[1].Trim();
            var queryIndex = name.IndexOfAny(new[] { '?', '#' });
            if (queryIndex >= 0)
            {
                name = name.Substring(0, queryIndex);
            }

            return owner.Length > 0 && name.Length > 0;
        }

        /// <summary>
        /// Creates an editor row from a manifest model id such as <c>owner/name</c>. An id without an owner keeps
        /// everything in <see cref="Name"/>.
        /// </summary>
        public static ManifestModelEntry Create(string id, string revision, ModelDependencyFileDefinition[] files)
        {
            var identifier = id ?? string.Empty;
            var slashIndex = identifier.IndexOf('/');
            return slashIndex <= 0 || slashIndex >= identifier.Length - 1
                ? new ManifestModelEntry(string.Empty, identifier, revision ?? string.Empty, files)
                : new ManifestModelEntry(
                    identifier.Substring(0, slashIndex),
                    identifier.Substring(slashIndex + 1),
                    revision ?? string.Empty,
                    files);
        }
    }

    internal enum ManifestModelValidationStatus
    {
        Unknown,
        Checking,
        Valid,
        Invalid
    }

    /// <summary>
    /// Captures one repository validation result for one manifest row.
    /// </summary>
    internal sealed class ManifestModelValidationState
    {
        public ManifestModelValidationState(string modelId, string revision, ManifestModelValidationStatus status, string message)
        {
            ModelId = modelId;
            Revision = ManifestRevision.NormalizeForResolution(revision);
            Status = status;
            Message = message;
        }

        public string ModelId { get; }
        public string Revision { get; }
        public ManifestModelValidationStatus Status { get; }
        public string Message { get; }
    }

    /// <summary>
    /// Shares the package-manifest validation UX across manifest editing surfaces.
    /// </summary>
    internal sealed class ManifestModelValidationController
    {
        internal readonly struct ValidationTarget
        {
            public ValidationTarget(string modelId, string revision)
            {
                ModelId = modelId?.Trim();
                Revision = ManifestRevision.NormalizeForResolution(revision);
            }

            public string ModelId { get; }
            public string Revision { get; }
        }

        private readonly bool _writePersistentCache;
        private readonly Func<bool> _canValidate;
        private readonly Action _requestRepaint;
        private ManifestModelValidationState[] _states = Array.Empty<ManifestModelValidationState>();
        private int _validationVersion;
        private int _seenValidationCacheRevision = -1;
        private string _seenValidationTargetsKey = string.Empty;

        public ManifestModelValidationController(bool writePersistentCache, Func<bool> canValidate, Action requestRepaint)
        {
            _writePersistentCache = writePersistentCache;
            _canValidate = canValidate ?? (() => true);
            _requestRepaint = requestRepaint;
        }

        public void RefreshFromCache(IEnumerable<ValidationTarget> targets)
        {
            var distinctTargets = NormalizeTargets(targets);
            if (distinctTargets.Length == 0)
            {
                _states = Array.Empty<ManifestModelValidationState>();
                _seenValidationCacheRevision = RepositoryValidationCacheService.CacheRevision;
                _seenValidationTargetsKey = string.Empty;
                return;
            }

            _seenValidationTargetsKey = BuildTargetsKey(distinctTargets);
            var previousStates = _states.ToDictionary(state => BuildTargetKey(state.ModelId, state.Revision), StringComparer.Ordinal);
            _states = distinctTargets
                .Select(target =>
                {
                    if (!RepositoryValidationCacheService.TryGetCachedRepositoryRevisionExists(target.ModelId, target.Revision, out var exists))
                    {
                        return previousStates.TryGetValue(BuildTargetKey(target.ModelId, target.Revision), out var previous)
                            ? previous
                            : new ManifestModelValidationState(target.ModelId, target.Revision, ManifestModelValidationStatus.Unknown, null);
                    }

                    return new ManifestModelValidationState(
                        target.ModelId,
                        target.Revision,
                        exists ? ManifestModelValidationStatus.Valid : ManifestModelValidationStatus.Invalid,
                        exists ? null : $"Revision `{target.Revision}` was not found on Hugging Face.");
                })
                .ToArray();
            _seenValidationCacheRevision = RepositoryValidationCacheService.CacheRevision;
        }

        public void RefreshFromCacheIfNeeded(IEnumerable<ValidationTarget> targets)
        {
            var distinctTargets = NormalizeTargets(targets);
            var targetsKey = BuildTargetsKey(distinctTargets);
            if (_seenValidationCacheRevision == RepositoryValidationCacheService.CacheRevision &&
                string.Equals(_seenValidationTargetsKey, targetsKey, StringComparison.Ordinal))
            {
                return;
            }

            if (_states.Any(state => state.Status == ManifestModelValidationStatus.Checking))
            {
                return;
            }

            RefreshFromCache(distinctTargets);
        }

        public void QueueUnknownValidationIfNeeded()
        {
            var targets = _states
                .Where(state => state.Status == ManifestModelValidationStatus.Unknown)
                .Select(state => new ValidationTarget(state.ModelId, state.Revision))
                .Where(target => RepositoryValidationCacheService.TryMarkSessionValidationRequested(BuildTargetKey(target.ModelId, target.Revision)))
                .ToArray();
            if (targets.Length == 0)
            {
                return;
            }

            QueueValidation(targets);
        }

        public void QueueValidation(IEnumerable<ValidationTarget> targets)
        {
            if (!_canValidate())
            {
                return;
            }

            var distinctTargets = NormalizeTargets(targets);
            if (distinctTargets.Length == 0)
            {
                _requestRepaint?.Invoke();
                return;
            }

            // Merge: rows outside this batch keep their current state.
            var checking = distinctTargets
                .Select(target => new ManifestModelValidationState(target.ModelId, target.Revision, ManifestModelValidationStatus.Checking, null))
                .ToArray();
            var checkingKeys = new HashSet<string>(checking.Select(state => BuildTargetKey(state.ModelId, state.Revision)), StringComparer.Ordinal);
            _states = _states
                .Where(state => !checkingKeys.Contains(BuildTargetKey(state.ModelId, state.Revision)))
                .Concat(checking)
                .ToArray();
            _requestRepaint?.Invoke();
            _ = ValidateDependenciesAsync(distinctTargets);
        }

        public ManifestModelValidationState GetState(string modelId, string revision)
        {
            return _states.FirstOrDefault(state =>
                string.Equals(state.ModelId, modelId, StringComparison.Ordinal) &&
                string.Equals(state.Revision, ManifestRevision.NormalizeForResolution(revision), StringComparison.Ordinal));
        }

        private async Task ValidateDependenciesAsync(ValidationTarget[] targets)
        {
            var results = new ManifestModelValidationState[targets.Length];
            for (var i = 0; i < targets.Length; i++)
            {
                results[i] = await ValidateDependencyRevisionAsync(targets[i].ModelId, targets[i].Revision, _writePersistentCache);
            }

            EditorApplication.delayCall += () =>
            {
                ReplaceKnownStates(results);
                _seenValidationCacheRevision = RepositoryValidationCacheService.CacheRevision;
                _requestRepaint?.Invoke();
                InternalEditorUtility.RepaintAllViews();
            };
        }

        private void ReplaceKnownStates(ManifestModelValidationState[] results)
        {
            if (results == null || results.Length == 0)
            {
                return;
            }

            var replacementByModelId = results.ToDictionary(
                result => BuildTargetKey(result.ModelId, result.Revision),
                StringComparer.Ordinal);
            // Only resolve rows that are still waiting, so a newer edit is never overwritten by an older check.
            _states = _states
                .Select(state => state.Status == ManifestModelValidationStatus.Checking &&
                    replacementByModelId.TryGetValue(BuildTargetKey(state.ModelId, state.Revision), out var replacement)
                        ? replacement
                        : state)
                .ToArray();
        }

        private static ValidationTarget[] NormalizeTargets(IEnumerable<ValidationTarget> targets)
        {
            return (targets ?? Array.Empty<ValidationTarget>())
                .Where(target => !string.IsNullOrWhiteSpace(target.ModelId))
                .GroupBy(target => BuildTargetKey(target.ModelId, target.Revision), StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();
        }

        private static string BuildTargetsKey(IEnumerable<ValidationTarget> targets)
        {
            return string.Join(
                "\n\n",
                NormalizeTargets(targets)
                    .Select(target => BuildTargetKey(target.ModelId, target.Revision))
                    .OrderBy(target => target, StringComparer.Ordinal));
        }

        internal static async Task<ManifestModelValidationState> ValidateDependencyAsync(string modelId, bool writePersistentCache)
        {
            if (string.IsNullOrWhiteSpace(modelId))
            {
                return new ManifestModelValidationState(modelId, "main", ManifestModelValidationStatus.Unknown, null);
            }

            if (RepositoryValidationCacheService.TryGetCachedRepositoryExists(modelId, out var cachedExists))
            {
                return new ManifestModelValidationState(
                    modelId,
                    "main",
                    cachedExists ? ManifestModelValidationStatus.Valid : ManifestModelValidationStatus.Invalid,
                    cachedExists ? null : "Model was not found on Hugging Face.");
            }

            try
            {
                var success = await RepositoryValidationCacheService.GetOrStartRepositoryValidationAsync(
                    modelId,
                    async () =>
                    {
                        using var request = UnityWebRequest.Head($"https://huggingface.co/{modelId.Trim()}");
                        request.timeout = 10;
                        await SendRequestAsync(request, CancellationToken.None);
                        return request.responseCode is >= 200 and < 400;
                    });
                if (writePersistentCache)
                {
                    RepositoryValidationCacheService.WriteCachedRepositoryExists(modelId, success);
                }

                return new ManifestModelValidationState(
                    modelId,
                    "main",
                    success ? ManifestModelValidationStatus.Valid : ManifestModelValidationStatus.Invalid,
                    success ? null : "Model was not found on Hugging Face.");
            }
            catch (Exception exception)
            {
                return new ManifestModelValidationState(modelId, "main", ManifestModelValidationStatus.Invalid, exception.Message);
            }
        }

        internal static async Task<ManifestModelValidationState> ValidateDependencyRevisionAsync(string modelId, string revision, bool writePersistentCache)
        {
            var normalizedRevision = ManifestRevision.NormalizeForResolution(revision);
            if (string.IsNullOrWhiteSpace(modelId))
            {
                return new ManifestModelValidationState(modelId, normalizedRevision, ManifestModelValidationStatus.Unknown, null);
            }

            if (RepositoryValidationCacheService.TryGetCachedRepositoryRevisionExists(modelId, normalizedRevision, out var cachedExists))
            {
                return new ManifestModelValidationState(
                    modelId,
                    normalizedRevision,
                    cachedExists ? ManifestModelValidationStatus.Valid : ManifestModelValidationStatus.Invalid,
                    cachedExists ? null : $"Revision `{normalizedRevision}` was not found on Hugging Face.");
            }

            try
            {
                var escapedRevision = UnityWebRequest.EscapeURL(normalizedRevision).Replace("+", "%20", StringComparison.Ordinal);
                var success = await RepositoryValidationCacheService.GetOrStartRepositoryValidationAsync(
                    BuildTargetKey(modelId, normalizedRevision),
                    async () =>
                    {
                        using var request = UnityWebRequest.Get($"https://huggingface.co/api/models/{modelId.Trim()}/revision/{escapedRevision}?expand[]=sha");
                        request.timeout = 10;
                        await SendRequestAsync(request, CancellationToken.None);
                        return request.responseCode is >= 200 and < 400;
                    });
                if (writePersistentCache)
                {
                    RepositoryValidationCacheService.WriteCachedRepositoryRevisionExists(modelId, normalizedRevision, success);
                }

                return new ManifestModelValidationState(
                    modelId,
                    normalizedRevision,
                    success ? ManifestModelValidationStatus.Valid : ManifestModelValidationStatus.Invalid,
                    success ? null : $"Revision `{normalizedRevision}` was not found on Hugging Face.");
            }
            catch (Exception exception)
            {
                return new ManifestModelValidationState(modelId, normalizedRevision, ManifestModelValidationStatus.Invalid, exception.Message);
            }
        }

        private static string BuildTargetKey(string modelId, string revision)
        {
            return $"{modelId?.Trim()}\n{ManifestRevision.NormalizeForResolution(revision)}";
        }

        private static Task SendRequestAsync(UnityWebRequest request, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            var operation = request.SendWebRequest();
            cancellationToken.Register(() =>
            {
                if (!request.isDone)
                {
                    request.Abort();
                }
            });
            operation.completed += _ =>
            {
                if (request.result == UnityWebRequest.Result.ConnectionError)
                {
                    tcs.TrySetException(new InvalidOperationException(request.error));
                    return;
                }

                tcs.TrySetResult(true);
            };
            return tcs.Task;
        }
    }
}
