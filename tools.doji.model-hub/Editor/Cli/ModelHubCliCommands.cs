using System.Threading.Tasks;
using Unity.Pipeline.Commands;
using UnityEngine;

namespace Doji.AI.ModelHub.Editor.Cli
{
    /// <summary>
    /// Unity CLI commands for AI Model Hub, through the <c>com.unity.pipeline</c> server: <c>unity command
    /// model_hub_status</c> in a connected Editor, or <c>unity run &lt;project&gt; --command model_hub_sync</c> in
    /// one batch run. Each command calls <see cref="ModelHubAutomation"/>, returns in well under a second, and never
    /// opens a dialog. Long work runs in the background; poll <c>model_hub_sync_status</c>.
    /// </summary>
    internal static class ModelHubCliCommands
    {
        private const string Tag = "model_hub";

        [CliCommand("model_hub_status",
            "AI Model Hub: every model dependency of the project (owner, repository, revision, resolved commit, state, reason) and its files (optional, enabled, cached, imported, size). Reads local state only.",
            Tags = new[] { Tag })]
        public static object Status(
            [CliArg("repo", "Only this model's repository id, for example org/model. Omit for every model.")] string repo = null)
        {
            return ModelHubAutomation.GetStatus(repo);
        }

        [CliCommand("model_hub_sync",
            "AI Model Hub: start a background download and import of the models that need it, and return at once. When nothing starts, the result says why for each model. Poll model_hub_sync_status. In batch mode it waits until the sync ends.",
            Tags = new[] { Tag })]
        public static object Sync(
            [CliArg("repo", "Only this model's repository id. Omit for every model.")] string repo = null,
            [CliArg("mode", "full (download and import, the default) or download (download only).")] string mode = "full",
            [CliArg("wait", "Wait until the sync ends before returning. Always on in batch mode, where the Editor exits after the command.")] bool wait = false)
        {
            if (wait || Application.isBatchMode)
            {
                return WaitAsync(repo, mode);
            }

            return ModelHubAutomation.StartSync(repo, mode);
        }

        [CliCommand("model_hub_sync_status",
            "AI Model Hub: whether a sync runs, its progress (phase, current file, bytes), and the last run's report with each model's result.",
            Tags = new[] { Tag })]
        public static object SyncStatus()
        {
            return ModelHubAutomation.GetSyncStatus();
        }

        [CliCommand("model_hub_set_file",
            "AI Model Hub: turn an optional file on or off, or set an ONNX file's quantization, like the Files tab. Does not start a sync: run model_hub_sync afterwards. Turn on optional files only with the user's consent.",
            Tags = new[] { Tag })]
        public static object SetFile(
            [CliArg("repo", "The model's repository id.", Required = true)] string repo,
            [CliArg("path", "The file's path in the repository, as model_hub_status lists it.", Required = true)] string path,
            [CliArg("enabled", "true or false: turn the optional file on or off. Omit to keep it.")] string enabled = null,
            [CliArg("quantization", "none, float16, or uint8, for ONNX files. Omit to keep it.")] string quantization = null)
        {
            return ModelHubAutomation.SetFile(repo, path, ParseOptionalBool(enabled, "enabled"), quantization);
        }

        [CliCommand("model_hub_cancel",
            "AI Model Hub: cancel the running sync. Finished files stay in the cache.",
            Tags = new[] { Tag })]
        public static object Cancel()
        {
            return ModelHubAutomation.Cancel();
        }

        [CliCommand("model_hub_remove_cache",
            "AI Model Hub: delete one model's downloaded files from the shared cache on this machine. Other projects that use the model must download it again. Needs --confirm true, given only with the user's consent.",
            Tags = new[] { Tag })]
        public static object RemoveCache(
            [CliArg("repo", "The model's repository id.", Required = true)] string repo,
            [CliArg("confirm", "true to delete. Without it the command refuses and changes nothing.")] bool confirm = false)
        {
            return ModelHubAutomation.RemoveCache(repo, confirm);
        }

        private static async Task<object> WaitAsync(string repo, string mode)
        {
            return await ModelHubAutomation.StartSyncAndWaitAsync(repo, mode);
        }

        private static bool? ParseOptionalBool(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value.Trim().ToLowerInvariant() switch
            {
                "true" or "1" or "yes" or "on" => true,
                "false" or "0" or "no" or "off" => false,
                _ => throw new ModelHubAutomationException($"--{name} takes true or false, not '{value}'.")
            };
        }
    }
}
