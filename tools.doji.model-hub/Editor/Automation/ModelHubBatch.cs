using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Entry points for <c>-executeMethod</c>, for CI and scripts without <c>com.unity.pipeline</c>. Each method runs
    /// the same call as the matching Unity CLI command, logs the JSON result, and exits the Editor with a code:
    /// 0 on success, 1 when a model is not ready or the sync failed, 2 for a wrong argument or an error.
    /// Run without <c>-quit</c>: the method exits the Editor itself once the work is done.
    /// </summary>
    /// <example>
    /// <code>
    /// Unity -batchmode -projectPath . -executeMethod Doji.AI.ModelHub.Editor.ModelHubBatch.Sync -modelHubRepo owner/model -modelHubOutput sync.json
    /// </code>
    /// </example>
    public static class ModelHubBatch
    {
        internal const int Success = 0;
        internal const int NotReady = 1;
        internal const int Error = 2;

        private const string LogPrefix = "[AI Model Hub] ";

        /// <summary>
        /// Downloads and imports the models that need it, waits until the sync ends, and exits. Arguments:
        /// <c>-modelHubRepo &lt;id&gt;</c> (one model; all when omitted), <c>-modelHubMode full|download</c>, and
        /// <c>-modelHubOutput &lt;path&gt;</c> (also writes the JSON result to this file).
        /// </summary>
        public static async void Sync()
        {
            var args = Environment.GetCommandLineArgs();
            var problem = GetSyncArgumentProblem(args);
            if (problem != null)
            {
                Debug.LogError(LogPrefix + problem);
                EditorApplication.Exit(Error);
                return;
            }

            await RunAsync(args, async () =>
            {
                var result = await ModelHubAutomation.StartSyncAndWaitAsync(
                    GetArgument(args, "-modelHubRepo"),
                    GetArgument(args, "-modelHubMode"));
                return (result, GetExitCode(result));
            });
        }

        /// <summary>
        /// Writes the status of every model, or of <c>-modelHubRepo &lt;id&gt;</c>, and exits with 0 when every
        /// enabled model is ready. <c>-modelHubOutput &lt;path&gt;</c> also writes the JSON result to this file.
        /// </summary>
        public static async void Status()
        {
            var args = Environment.GetCommandLineArgs();
            await RunAsync(args, () =>
            {
                var result = ModelHubAutomation.GetStatus(GetArgument(args, "-modelHubRepo"));
                return Task.FromResult<(object, int)>((result, GetExitCode(result)));
            });
        }

        /// <summary>
        /// 0 when the sync succeeded, or started nothing because every model is ready or not enabled; 1 otherwise.
        /// </summary>
        internal static int GetExitCode(AutomationSyncStartResult result)
        {
            if (result.Run != null)
            {
                return result.Run.State == SyncRunStates.Succeeded ? Success : NotReady;
            }

            return result.Dependencies.All(item => item.Reason == "ready" || item.Reason == "not_enabled")
                ? Success
                : NotReady;
        }

        /// <summary>
        /// 0 when every enabled model is ready and every manifest was read; 1 otherwise.
        /// </summary>
        internal static int GetExitCode(AutomationStatusResult result)
        {
            var ready = result.DiscoveryIssues.Length == 0
                && result.Dependencies
                    .Where(dependency => dependency.Enabled)
                    .All(dependency => dependency.State == nameof(DependencyState.Ready));
            return ready ? Success : NotReady;
        }

        /// <summary>
        /// Why <see cref="Sync"/> cannot run with these arguments, or null. With <c>-quit</c>, Unity waits for the
        /// method without running Editor updates, which the sync needs to finish, so it would hang until Unity's
        /// timeout.
        /// </summary>
        internal static string GetSyncArgumentProblem(string[] args)
        {
            return Array.Exists(args, arg => string.Equals(arg, "-quit", StringComparison.OrdinalIgnoreCase))
                ? "Run ModelHubBatch.Sync without -quit. With -quit, Unity stops updating the Editor while it waits, so the sync never ends. The method exits the Editor itself when the sync is done."
                : null;
        }

        /// <summary>
        /// The value after <paramref name="name"/> in <paramref name="args"/>, or null.
        /// </summary>
        internal static string GetArgument(string[] args, string name)
        {
            var index = Array.FindIndex(args, arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        private static async Task RunAsync(string[] args, Func<Task<(object Result, int ExitCode)>> run)
        {
            int exitCode;
            try
            {
                var (result, code) = await run();
                var json = JsonConvert.SerializeObject(result, Formatting.Indented);
                var output = GetArgument(args, "-modelHubOutput");
                if (!string.IsNullOrEmpty(output))
                {
                    File.WriteAllText(output, json);
                }

                Debug.Log(LogPrefix + json);
                exitCode = code;
            }
            catch (Exception exception)
            {
                Debug.LogError(LogPrefix + exception.Message);
                exitCode = Error;
            }

            EditorApplication.Exit(exitCode);
        }
    }
}
