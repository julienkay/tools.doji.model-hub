using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Finishes cheap imports without asking: when every selected file of a model is in the cache and no ONNX file needs
    /// a new conversion, AI Model Hub writes this project's records and the model is ready. Runs after editor start, after
    /// project changes, after a sync, and before Play mode. Downloads and conversions still wait for the user.
    /// </summary>
    [InitializeOnLoad]
    internal static class AutoImport
    {
        private static readonly HashSet<string> ReportedFailures = new(StringComparer.Ordinal);
        private static bool _isQueued;

        static AutoImport()
        {
            Queue();
            EditorApplication.projectChanged += Queue;
            BackgroundSyncManager.SyncStateChanged += Queue;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode)
                {
                    Run();
                }
            };
        }

        /// <summary>
        /// Stops automatic imports, so tests control the project's import records.
        /// </summary>
        internal static bool Suspended { get; set; }

        private static void Queue()
        {
            if (_isQueued)
            {
                return;
            }

            _isQueued = true;
            EditorApplication.delayCall += RunQueued;
        }

        private static void RunQueued()
        {
            _isQueued = false;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                Queue();
                return;
            }

            Run();
        }

        /// <summary>
        /// Imports every model that needs no download and no conversion. Returns how many imports finished.
        /// </summary>
        internal static int Run()
        {
            if (Suspended || Application.isBatchMode || EditorApplication.isPlaying || BackgroundSyncManager.IsRunning)
            {
                return 0;
            }

            var report = EditorOperations.CreateStatusReport();
            if (!report.DependencyStatuses.Any(status => status.CanImportAutomatically))
            {
                return 0;
            }

            var result = CachedModelImporter.Import(report, allowConversion: false, new RawCacheService(), new ProjectImportService());
            foreach (var failure in result.Failures.Where(ReportedFailures.Add))
            {
                Debug.LogWarning($"AI Model Hub could not set up a model from the cache. {failure}");
            }

            if (result.Imported.Count > 0)
            {
                EditorRuntimeCatalog.Invalidate();
                ProjectSettingsProvider.Refresh();
                BackgroundSyncManager.NotifyStateChanged();
            }

            return result.Imported.Count;
        }
    }
}
