using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Doji.AI.ModelHub.Samples;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Doji.AI.ModelHub.Samples.Editor
{
    /// <summary>
    /// Builds a Windows player that loads models through <see cref="Hub"/>, runs it headless with an empty Hugging Face
    /// cache, and returns what it loaded. Bundled models come from the build; downloaded models come from the Hub.
    /// </summary>
    public static class BuildSmokeTestRunner
    {
        private const string SceneFolder = "Assets/HF Hub/Build Smoke Test/Generated";
        private const string ScenePath = SceneFolder + "/BuildSmokeTest.unity";

        /// <summary>
        /// Builds and runs the player. Each entry is <c>repoId:repoPath</c>. Returns the player's result lines, the
        /// build size of StreamingAssets/hf-hub, and the path of the player log.
        /// </summary>
        public static string BuildAndRun(string[] entries, int timeoutSeconds = 900)
        {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "HfHubSmoke"));
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }

            Directory.CreateDirectory(root);
            CreateScene(entries);

            var playerPath = Path.Combine(root, "Player", "Smoke.exe");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = playerPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
            if (report.summary.result != BuildResult.Succeeded)
            {
                return $"BUILD {report.summary.result}: {string.Join(" | ", report.steps.SelectMany(step => step.messages).Where(message => message.type is LogType.Error or LogType.Exception).Select(message => message.content))}";
            }

            var streamingAssets = Path.Combine(root, "Player", "Smoke_Data", "StreamingAssets", "hf-hub");
            var shipped = Directory.Exists(streamingAssets)
                ? Directory.GetFiles(streamingAssets, "*", SearchOption.AllDirectories)
                    .Select(file => $"{Path.GetRelativePath(streamingAssets, file)} ({new FileInfo(file).Length / 1024} KB)")
                    .ToArray()
                : Array.Empty<string>();

            var output = Path.Combine(root, "results.txt");
            var log = Path.Combine(root, "player.log");
            var start = new ProcessStartInfo(playerPath, $"-batchmode -nographics -logFile \"{log}\" -hfhub-smoke-output \"{output}\"")
            {
                UseShellExecute = false
            };
            start.EnvironmentVariables["HF_HUB_CACHE"] = Path.Combine(root, "PlayerCache");
            start.EnvironmentVariables["DOJI_HF_HUB_DATA_ROOT"] = Path.Combine(root, "PlayerData");
            using var process = Process.Start(start);
            var exited = process.WaitForExit(timeoutSeconds * 1000);
            if (!exited)
            {
                process.Kill();
            }

            var results = File.Exists(output) ? File.ReadAllLines(output) : new[] { "NO RESULTS" };
            return string.Join("\n", new[]
            {
                $"exit={(exited ? process.ExitCode.ToString() : "timeout")}",
                "shipped:",
            }.Concat(shipped.Select(line => "  " + line)).Append("results:").Concat(results.Select(line => "  " + line)).Append($"log: {log}"));
        }

        private static void CreateScene(IEnumerable<string> entries)
        {
            Directory.CreateDirectory(Path.Combine(Application.dataPath, "..", SceneFolder));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var test = new GameObject("Build Smoke Test").AddComponent<BuildSmokeTest>();
            foreach (var entry in entries)
            {
                var separator = entry.IndexOf(':');
                test.Entries.Add(new BuildSmokeTest.Entry { repoId = entry.Substring(0, separator), repoPath = entry.Substring(separator + 1) });
            }

            EditorSceneManager.SaveScene(scene, ScenePath);
        }
    }
}
