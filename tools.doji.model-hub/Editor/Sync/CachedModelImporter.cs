using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Finishes the imports of models whose selected files are all in the cache. It never downloads. Without
    /// conversions allowed it only writes this project's records and reuses runtime models, which is cheap enough to run
    /// without asking; with conversions allowed it also converts ONNX files, for builds and other user actions.
    /// </summary>
    internal static class CachedModelImporter
    {
        internal sealed class Result
        {
            /// <summary>
            /// Dependencies whose import finished.
            /// </summary>
            public List<DiscoveredModelDependency> Imported { get; } = new();

            /// <summary>
            /// Dependencies left alone because they need a conversion that was not allowed.
            /// </summary>
            public List<DiscoveredModelDependency> NeedConversion { get; } = new();

            /// <summary>
            /// One line per dependency whose import failed: the model and the reason.
            /// </summary>
            public List<string> Failures { get; } = new();
        }

        /// <summary>
        /// Imports every dependency of <paramref name="report"/> whose files are cached and whose import is missing or
        /// out of date. <paramref name="onProgress"/> gets each dependency before its import, with its index and the count.
        /// </summary>
        public static Result Import(
            StatusReport report,
            bool allowConversion,
            IRawCacheService rawCacheService,
            IProjectImportService importService,
            Action<DiscoveredModelDependency, int, int> onProgress = null)
        {
            var result = new Result();
            var pending = (report?.DependencyStatuses ?? Array.Empty<DependencyStatus>())
                .Where(status => allowConversion ? status.IsImportPending : status.CanImportAutomatically)
                .Select(status => status.Dependency)
                .ToArray();

            for (var index = 0; index < pending.Length; index++)
            {
                var dependency = pending[index];
                onProgress?.Invoke(dependency, index, pending.Length);
                try
                {
                    var cachedSnapshot = rawCacheService.GetCachedOrThrow(dependency);
                    importService.ImportAsync(dependency, cachedSnapshot, CancellationToken.None, allowConversion).GetAwaiter().GetResult();
                    result.Imported.Add(dependency);
                }
                catch (ConversionRequiredException)
                {
                    result.NeedConversion.Add(dependency);
                }
                catch (Exception exception)
                {
                    result.Failures.Add($"{BackgroundSyncManager.GetDependencyDisplayLabel(dependency)}: {exception.Message}");
                }
            }

            return result;
        }
    }
}
