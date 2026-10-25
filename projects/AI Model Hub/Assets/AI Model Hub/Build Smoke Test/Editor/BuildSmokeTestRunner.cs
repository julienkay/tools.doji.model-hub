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
    /// Builds players that load models through <see cref="Hub"/> and report what they loaded. <see cref="BuildAndRun"/>
    /// builds and runs a Windows player with an empty Hugging Face cache; <see cref="BuildPlayer"/> builds Android and
    /// Web players to run on a device or in a browser. Bundled models come from the build; downloaded models come
    /// from the Hub.
    /// </summary>
    public static class BuildSmokeTestRunner
    {
        private const string SceneFolder = "Assets/AI Model Hub/Build Smoke Test/Generated";
        private const string ScenePath = SceneFolder + "/BuildSmokeTest.unity";

        /// <summary>
        /// Builds and runs the player. Each entry is <c>repoId:repoPath</c>. Returns the player's result lines, the
        /// build size of StreamingAssets/model-hub, and the path of the player log.
        /// </summary>
        public static string BuildAndRun(string[] entries, int timeoutSeconds = 900)
        {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "ModelHubSmoke"));
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

            var streamingAssets = Path.Combine(root, "Player", "Smoke_Data", "StreamingAssets", "model-hub");
            var shipped = Directory.Exists(streamingAssets)
                ? Directory.GetFiles(streamingAssets, "*", SearchOption.AllDirectories)
                    .Select(file => $"{Path.GetRelativePath(streamingAssets, file)} ({new FileInfo(file).Length / 1024} KB)")
                    .ToArray()
                : Array.Empty<string>();

            var output = Path.Combine(root, "results.txt");
            var log = Path.Combine(root, "player.log");
            var start = new ProcessStartInfo(playerPath, $"-batchmode -nographics -logFile \"{log}\" -modelhub-smoke-output \"{output}\"")
            {
                UseShellExecute = false
            };
            start.EnvironmentVariables["HF_HUB_CACHE"] = Path.Combine(root, "PlayerCache");
            start.EnvironmentVariables["DOJI_MODEL_HUB_DATA_ROOT"] = Path.Combine(root, "PlayerData");
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

        /// <summary>
        /// Builds an Android APK (IL2CPP, ARM64) or an uncompressed Web player for the same scene, and returns its path.
        /// The player logs its results with the prefix <c>MODELHUB-SMOKE</c>: install the APK with an empty app and read
        /// <c>adb logcat -s Unity</c>, or serve the Web folder locally and read the browser console. Run both twice: the
        /// second run must load downloaded models from the device without downloading them again.
        /// </summary>
        public static string BuildPlayer(string platform, string[] entries)
        {
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "ModelHubSmoke-" + platform));
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }

            Directory.CreateDirectory(root);
            CreateScene(entries);

            // Release-like Android players, and a Web build that a plain local server can host.
            var android = string.Equals(platform, "android", StringComparison.OrdinalIgnoreCase);
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;

            var location = android ? Path.Combine(root, "Smoke.apk") : Path.Combine(root, "Web");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = location,
                target = android ? BuildTarget.Android : BuildTarget.WebGL,
                options = BuildOptions.Development
            });
            return report.summary.result == BuildResult.Succeeded
                ? $"BUILT {location}"
                : $"BUILD {report.summary.result}: {string.Join(" | ", report.steps.SelectMany(step => step.messages).Where(message => message.type is LogType.Error or LogType.Exception).Select(message => message.content))}";
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
