using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Free and total space of the drive that holds one folder.
    /// </summary>
    internal sealed class DriveSpace
    {
        public DriveSpace(string rootPath, long availableBytes, long totalBytes)
        {
            RootPath = rootPath;
            AvailableBytes = availableBytes;
            TotalBytes = totalBytes;
        }

        /// <summary>
        /// The drive's root or mount point, for example <c>G:\</c> or <c>/Volumes/Data</c>.
        /// </summary>
        public string RootPath { get; }

        /// <summary>
        /// Bytes this user can still write on the drive.
        /// </summary>
        public long AvailableBytes { get; }

        public long TotalBytes { get; }
    }

    /// <summary>
    /// Looks up the drive of a folder. Tests replace it with a fake.
    /// </summary>
    internal interface IDiskSpace
    {
        /// <summary>
        /// Returns the drive that holds <paramref name="path"/>, which need not exist yet, or null when unknown.
        /// </summary>
        DriveSpace GetDrive(string path);
    }

    /// <summary>
    /// Reads drives with <see cref="DriveInfo"/>. The drive of a path is the ready drive with the longest root that
    /// contains it, so mount points on macOS and Linux resolve to their own volume.
    /// </summary>
    internal sealed class SystemDiskSpace : IDiskSpace
    {
        public DriveSpace GetDrive(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                var fullPath = AppendSeparator(Path.GetFullPath(path));
                var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                var drive = DriveInfo.GetDrives()
                    .Where(candidate => IsReady(candidate) && fullPath.StartsWith(AppendSeparator(candidate.RootDirectory.FullName), comparison))
                    .OrderByDescending(candidate => candidate.RootDirectory.FullName.Length)
                    .FirstOrDefault();
                return drive == null ? null : new DriveSpace(drive.RootDirectory.FullName, drive.AvailableFreeSpace, drive.TotalSize);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return null;
            }
        }

        private static bool IsReady(DriveInfo drive)
        {
            try
            {
                return drive.IsReady;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static string AppendSeparator(string path)
        {
            return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
                   path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? path
                : path + Path.DirectorySeparatorChar;
        }
    }

    /// <summary>
    /// Bytes one sync or move writes into one folder.
    /// </summary>
    internal sealed class DiskSpaceRequirement
    {
        public DiskSpaceRequirement(string folder, long bytes, string purpose)
        {
            Folder = folder;
            Bytes = bytes;
            Purpose = purpose;
        }

        public string Folder { get; }
        public long Bytes { get; }

        /// <summary>
        /// What the bytes are for, for the message, for example <c>downloads</c> or <c>converted models</c>.
        /// </summary>
        public string Purpose { get; }
    }

    /// <summary>
    /// One drive that has less free space than a sync or move needs.
    /// </summary>
    internal sealed class DiskSpaceShortfall
    {
        public DiskSpaceShortfall(string driveRoot, long neededBytes, long availableBytes, IReadOnlyList<string> purposes)
        {
            DriveRoot = driveRoot;
            NeededBytes = neededBytes;
            AvailableBytes = availableBytes;
            Purposes = purposes;
        }

        public string DriveRoot { get; }
        public long NeededBytes { get; }
        public long AvailableBytes { get; }
        public IReadOnlyList<string> Purposes { get; }
    }

    /// <summary>
    /// Thrown before a sync or move writes anything when a drive does not have the space for it.
    /// </summary>
    internal sealed class InsufficientDiskSpaceException : IOException
    {
        public InsufficientDiskSpaceException(string message, IReadOnlyList<DiskSpaceShortfall> shortfalls)
            : base(message)
        {
            Shortfalls = shortfalls;
        }

        public IReadOnlyList<DiskSpaceShortfall> Shortfalls { get; }
    }

    /// <summary>
    /// Compares what a sync or move writes with the free space of each target drive. Requirements on the same drive
    /// add up, so a cache and a data folder on one drive must fit together.
    /// </summary>
    internal static class DiskSpaceCheck
    {
        private static IDiskSpace _diskSpace = new SystemDiskSpace();

        /// <summary>
        /// The drive lookup in use. Tests set a fake and restore it.
        /// </summary>
        internal static IDiskSpace DiskSpace
        {
            get => _diskSpace;
            set => _diskSpace = value ?? new SystemDiskSpace();
        }

        /// <summary>
        /// Returns the drives that cannot hold their requirements. A drive that cannot be read is not reported:
        /// the write itself then fails with the system's message.
        /// </summary>
        public static DiskSpaceShortfall[] FindShortfalls(IEnumerable<DiskSpaceRequirement> requirements, IDiskSpace diskSpace = null)
        {
            var lookup = diskSpace ?? DiskSpace;
            var byDrive = new Dictionary<string, (DriveSpace Drive, long Bytes, List<string> Purposes)>(StringComparer.OrdinalIgnoreCase);
            foreach (var requirement in requirements ?? Array.Empty<DiskSpaceRequirement>())
            {
                if (requirement == null || requirement.Bytes <= 0)
                {
                    continue;
                }

                var drive = lookup.GetDrive(requirement.Folder);
                if (drive == null)
                {
                    continue;
                }

                if (!byDrive.TryGetValue(drive.RootPath, out var entry))
                {
                    entry = (drive, 0, new List<string>());
                }

                entry.Bytes += requirement.Bytes;
                if (!string.IsNullOrWhiteSpace(requirement.Purpose) && !entry.Purposes.Contains(requirement.Purpose))
                {
                    entry.Purposes.Add(requirement.Purpose);
                }

                byDrive[drive.RootPath] = entry;
            }

            return byDrive.Values
                .Where(entry => entry.Bytes > entry.Drive.AvailableBytes)
                .OrderBy(entry => entry.Drive.RootPath, StringComparer.OrdinalIgnoreCase)
                .Select(entry => new DiskSpaceShortfall(entry.Drive.RootPath, entry.Bytes, entry.Drive.AvailableBytes, entry.Purposes))
                .ToArray();
        }

        /// <summary>
        /// Throws <see cref="InsufficientDiskSpaceException"/> when a drive cannot hold its requirements.
        /// </summary>
        public static void EnsureFits(string action, IEnumerable<DiskSpaceRequirement> requirements, IDiskSpace diskSpace = null)
        {
            var shortfalls = FindShortfalls(requirements, diskSpace);
            if (shortfalls.Length > 0)
            {
                throw new InsufficientDiskSpaceException(BuildMessage(action, shortfalls), shortfalls);
            }
        }

        /// <summary>
        /// For example: <c>Not enough disk space to download owner/model. C:\ needs 12.4 GB for downloads and
        /// converted models, 3.1 GB free.</c>
        /// </summary>
        public static string BuildMessage(string action, IReadOnlyList<DiskSpaceShortfall> shortfalls)
        {
            var drives = shortfalls.Select(shortfall =>
            {
                var purposes = shortfall.Purposes.Count == 0 ? string.Empty : " for " + string.Join(" and ", shortfall.Purposes);
                return $"{shortfall.DriveRoot} needs {UiKit.FormatBytes(shortfall.NeededBytes)}{purposes}, {UiKit.FormatBytes(shortfall.AvailableBytes)} free.";
            });
            return $"Not enough disk space to {action}. {string.Join(" ", drives)} Free up space or choose other folders in Preferences > AI Model Hub.";
        }
    }
}
