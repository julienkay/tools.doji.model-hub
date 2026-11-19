using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Doji.AI.ModelHub.Editor.Tests
{
    /// <summary>
    /// Drives for tests: each registered folder is the root of a drive with a chosen free space. A path belongs to
    /// the drive with the longest registered folder that contains it.
    /// </summary>
    internal sealed class FakeDiskSpace : IDiskSpace
    {
        private readonly List<(string Folder, DriveSpace Drive)> _drives = new();

        public FakeDiskSpace Add(string folder, string driveName, long availableBytes, long totalBytes = 1L << 40)
        {
            _drives.Add((AppendSeparator(Path.GetFullPath(folder)), new DriveSpace(driveName, availableBytes, totalBytes)));
            return this;
        }

        public DriveSpace GetDrive(string path)
        {
            var fullPath = AppendSeparator(Path.GetFullPath(path));
            return _drives
                .Where(entry => fullPath.StartsWith(entry.Folder, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(entry => entry.Folder.Length)
                .Select(entry => entry.Drive)
                .FirstOrDefault();
        }

        private static string AppendSeparator(string path)
        {
            return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ? path : path + Path.DirectorySeparatorChar;
        }
    }

    /// <summary>
    /// Environment variables for tests: values in the Editor process and values new programs get.
    /// </summary>
    internal sealed class FakeEnvironmentVariables : IEnvironmentVariables
    {
        public Dictionary<string, string> InProcess { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> ForNewPrograms { get; } = new(StringComparer.Ordinal);
        public bool CanReadForNewPrograms { get; set; } = true;

        public string GetInProcess(string name)
        {
            return InProcess.TryGetValue(name, out var value) ? value : null;
        }

        public string GetForNewPrograms(string name)
        {
            return ForNewPrograms.TryGetValue(name, out var value) ? value : null;
        }

        public void SetForUser(string name, string value)
        {
            InProcess[name] = value;
            ForNewPrograms[name] = value;
        }
    }
}
