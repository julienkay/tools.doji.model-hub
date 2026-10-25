using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Remembers which files exist in the project's generated AI Model Hub folders before a test, and on dispose deletes
    /// only the files and folders the test added. Models a maintainer imported into the companion project survive
    /// test runs. Isolated files, such as the project's optional-file selections, are moved aside for the test and
    /// restored afterwards, so the test starts without them and real settings come back.
    /// </summary>
    public sealed class TestProjectStateScope : IDisposable
    {
        /// <summary>
        /// Generated runtime files and registries, and editor-only import metadata.
        /// </summary>
        public static readonly string[] DefaultRoots =
        {
            "Assets/StreamingAssets/model-hub",
            "Library/Doji/ModelHub/imports"
        };

        private readonly string[] _roots;
        private readonly HashSet<string> _existingEntries;
        private readonly List<(string Path, byte[] Content)> _savedFiles = new();
        private readonly string[] _isolatedFiles;

        public TestProjectStateScope(params string[] projectRelativeRoots)
            : this(projectRelativeRoots, Array.Empty<string>())
        {
        }

        public TestProjectStateScope(string[] projectRelativeRoots, string[] isolatedProjectFiles)
        {
            _isolatedFiles = (isolatedProjectFiles ?? Array.Empty<string>())
                .SelectMany(file => new[] { ToAbsolutePath(file), ToAbsolutePath(file) + ".meta" })
                .ToArray();
            foreach (var file in _isolatedFiles.Where(File.Exists))
            {
                _savedFiles.Add((file, File.ReadAllBytes(file)));
                File.Delete(file);
            }

            if (_savedFiles.Count > 0)
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }

            _roots = (projectRelativeRoots == null || projectRelativeRoots.Length == 0 ? DefaultRoots : projectRelativeRoots)
                .Select(ToAbsolutePath)
                .ToArray();
            _existingEntries = new HashSet<string>(_roots.SelectMany(EnumerateEntries), StringComparer.OrdinalIgnoreCase);
            foreach (var root in _roots)
            {
                // A root that did not exist yet is new too; remember its parents so they are not deleted.
                for (var folder = root; !string.IsNullOrEmpty(folder); folder = Path.GetDirectoryName(folder))
                {
                    if (Directory.Exists(folder))
                    {
                        _existingEntries.Add(Path.GetFullPath(folder));
                    }
                }
            }
        }

        public void Dispose()
        {
            foreach (var root in _roots)
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToArray())
                {
                    if (!_existingEntries.Contains(Path.GetFullPath(file)))
                    {
                        File.Delete(file);
                    }
                }

                foreach (var folder in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                             .OrderByDescending(path => path.Length)
                             .Append(root)
                             .ToArray())
                {
                    var fullPath = Path.GetFullPath(folder);
                    if (_existingEntries.Contains(fullPath) || Directory.EnumerateFileSystemEntries(fullPath).Any())
                    {
                        continue;
                    }

                    Directory.Delete(fullPath);
                    var meta = fullPath.TrimEnd(Path.DirectorySeparatorChar) + ".meta";
                    if (File.Exists(meta) && !_existingEntries.Contains(meta))
                    {
                        File.Delete(meta);
                    }
                }
            }

            foreach (var file in _isolatedFiles.Where(File.Exists))
            {
                File.Delete(file);
            }

            foreach (var (filePath, content) in _savedFiles)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                File.WriteAllBytes(filePath, content);
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private static IEnumerable<string> EnumerateEntries(string root)
        {
            if (!Directory.Exists(root))
            {
                yield break;
            }

            yield return Path.GetFullPath(root);
            foreach (var entry in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories))
            {
                yield return Path.GetFullPath(entry);
            }

            var rootMeta = root.TrimEnd(Path.DirectorySeparatorChar) + ".meta";
            if (File.Exists(rootMeta))
            {
                yield return Path.GetFullPath(rootMeta);
            }
        }

        private static string ToAbsolutePath(string projectRelativePath)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, projectRelativePath));
        }
    }
}
