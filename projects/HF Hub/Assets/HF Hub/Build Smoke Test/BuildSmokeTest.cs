using System;
using System.Collections.Generic;
using System.IO;
using Doji.AI.ModelHub;
using UnityEngine;

namespace Doji.AI.ModelHub.Samples
{
    /// <summary>
    /// Loads a list of models through the public <see cref="Hub"/> API in a built player and writes one line per model
    /// to the file given by <c>-hfhub-smoke-output</c>, then quits. Every line is also logged with the prefix
    /// <c>HFHUB-SMOKE</c>, followed by <c>HFHUB-SMOKE-DONE</c>, for players without a command line (Android, Web).
    /// <c>BuildSmokeTestRunner</c> builds and runs it.
    /// </summary>
    public sealed class BuildSmokeTest : MonoBehaviour
    {
        [Serializable]
        public struct Entry
        {
            public string repoId;
            public string repoPath;
        }

        [SerializeField]
        private List<Entry> _entries = new();

        public List<Entry> Entries => _entries;

        private async void Start()
        {
            var lines = new List<string>();
            foreach (var entry in _entries)
            {
                try
                {
                    var started = DateTime.UtcNow;
                    var model = await Hub.LoadModelAsync(entry.repoId, entry.repoPath, null, destroyCancellationToken);
                    lines.Add($"OK {entry.repoId}:{entry.repoPath} inputs={model.inputs.Count} seconds={(DateTime.UtcNow - started).TotalSeconds:0.0}");
                }
                catch (Exception exception)
                {
                    lines.Add($"FAIL {entry.repoId}:{entry.repoPath} {exception.GetType().Name}: {exception.Message.Replace('\n', ' ')}");
                }
            }

            var output = GetArgument("-hfhub-smoke-output");
            if (!string.IsNullOrWhiteSpace(output))
            {
                File.WriteAllLines(output, lines);
            }

            var passed = lines.TrueForAll(line => line.StartsWith("OK", StringComparison.Ordinal));
            foreach (var line in lines)
            {
                Debug.Log("HFHUB-SMOKE " + line);
            }

            Debug.Log("HFHUB-SMOKE-DONE " + (passed ? "PASS" : "FAIL"));
            Application.Quit(passed ? 0 : 1);
        }

        private static string GetArgument(string name)
        {
            var arguments = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(arguments, name);
            return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
        }
    }
}
