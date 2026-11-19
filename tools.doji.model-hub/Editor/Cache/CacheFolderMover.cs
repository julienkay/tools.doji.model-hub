using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// How a move takes the content to its new folder.
    /// </summary>
    internal enum CacheMoveMethod
    {
        /// <summary>
        /// Same drive: folders are renamed, nothing is copied. The source is gone afterwards.
        /// </summary>
        Rename,

        /// <summary>
        /// Other drive: files are copied and checked. The source stays until the user confirms its deletion.
        /// </summary>
        Copy
    }

    /// <summary>
    /// What a move would do, measured before it starts.
    /// </summary>
    internal sealed class CacheMovePlan
    {
        public CacheMovePlan(string source, string destination, CacheMoveMethod method, long fileBytes, long linkedBytes, int fileCount, int linkCount)
        {
            Source = source;
            Destination = destination;
            Method = method;
            FileBytes = fileBytes;
            LinkedBytes = linkedBytes;
            FileCount = fileCount;
            LinkCount = linkCount;
        }

        public string Source { get; }
        public string Destination { get; }
        public CacheMoveMethod Method { get; }

        /// <summary>
        /// Bytes of the regular files. Symlinks are not followed.
        /// </summary>
        public long FileBytes { get; }

        /// <summary>
        /// Bytes the symlinks point at. A copy needs them only where the new drive does not allow symlinks.
        /// </summary>
        public long LinkedBytes { get; }

        public int FileCount { get; }
        public int LinkCount { get; }

        /// <summary>
        /// Whether the source has anything to move.
        /// </summary>
        public bool IsEmpty => FileCount == 0 && LinkCount == 0;
    }

    /// <summary>
    /// What a move did.
    /// </summary>
    internal sealed class CacheMoveResult
    {
        public CacheMoveResult(CacheMovePlan plan, long copiedBytes, int movedEntries, int linksCreated, int linksCopiedAsFiles, IReadOnlyList<string> keptInDestination)
        {
            Plan = plan;
            CopiedBytes = copiedBytes;
            MovedEntries = movedEntries;
            LinksCreated = linksCreated;
            LinksCopiedAsFiles = linksCopiedAsFiles;
            KeptInDestination = keptInDestination;
        }

        public CacheMovePlan Plan { get; }
        public long CopiedBytes { get; }

        /// <summary>
        /// Files and folders renamed into the destination. Zero for a copy.
        /// </summary>
        public int MovedEntries { get; }

        /// <summary>
        /// Relative symlinks recreated in the destination. Zero for a rename, which keeps the links as they are.
        /// </summary>
        public int LinksCreated { get; }

        /// <summary>
        /// Symlinks stored as file copies because the destination does not allow symlinks.
        /// </summary>
        public int LinksCopiedAsFiles { get; }

        /// <summary>
        /// Source paths, relative to the source folder, that the destination already had. The destination's file is
        /// kept. Blobs are named by their content, so for them both files are the same.
        /// </summary>
        public IReadOnlyList<string> KeptInDestination { get; }
    }

    /// <summary>
    /// Moves the content of a cache or data folder to a new folder. On the same drive it renames, so a move of any
    /// size takes a moment. Across drives it copies, checks every file's size, and leaves the source for
    /// <see cref="DeleteSource"/>, which runs only after the user confirms. The Hugging Face layout stays intact:
    /// <c>refs/</c> and <c>blobs/</c> keep their files, and the relative symlinks in <c>snapshots/</c> point at the
    /// moved blobs. When the destination already holds a file, the destination's file is kept.
    /// </summary>
    internal static class CacheFolderMover
    {
        private static readonly string[] SkippedTopLevelFolders = { ".locks" };
        private const string SymlinkProbePrefix = ".modelhub-symlink-probe-";
        private static int _moving;

        /// <summary>
        /// Whether a move runs. Syncs do not start meanwhile.
        /// </summary>
        public static bool IsMoving => Volatile.Read(ref _moving) != 0;

        /// <summary>
        /// Measures the source and picks the method. Throws <see cref="InvalidOperationException"/> when the folders
        /// are the same or one contains the other.
        /// </summary>
        public static CacheMovePlan Plan(string source, string destination, IDiskSpace diskSpace = null)
        {
            var sourceFull = Path.GetFullPath(source);
            var destinationFull = Path.GetFullPath(destination);
            if (PathsEqual(sourceFull, destinationFull))
            {
                throw new InvalidOperationException("The new folder is the folder in use.");
            }

            if (IsInside(destinationFull, sourceFull) || IsInside(sourceFull, destinationFull))
            {
                throw new InvalidOperationException($"'{destinationFull}' and '{sourceFull}' contain each other. Choose a folder outside the current one.");
            }

            long fileBytes = 0;
            long linkedBytes = 0;
            var fileCount = 0;
            var linkCount = 0;
            if (Directory.Exists(sourceFull))
            {
                foreach (var file in EnumerateMovableFiles(sourceFull))
                {
                    if (CacheFiles.IsSymlink(file))
                    {
                        linkCount++;
                        linkedBytes += CacheFiles.GetLength(file) ?? 0;
                    }
                    else if (File.Exists(file))
                    {
                        fileCount++;
                        fileBytes += new FileInfo(file).Length;
                    }
                }
            }

            var method = IsSameDrive(sourceFull, destinationFull, diskSpace ?? DiskSpaceCheck.DiskSpace) ? CacheMoveMethod.Rename : CacheMoveMethod.Copy;
            return new CacheMovePlan(sourceFull, destinationFull, method, fileBytes, linkedBytes, fileCount, linkCount);
        }

        /// <summary>
        /// Moves the content as planned. A copy checks the free space first and checks each file afterwards; it
        /// never deletes the source. A rename that the file system refuses across drives falls back to a copy.
        /// </summary>
        public static CacheMoveResult Move(CacheMovePlan plan, IProgress<float> progress = null, CancellationToken cancellationToken = default, IDiskSpace diskSpace = null)
        {
            if (Interlocked.Exchange(ref _moving, 1) != 0)
            {
                throw new InvalidOperationException("Another folder move is running.");
            }

            try
            {
                if (!Directory.Exists(plan.Source))
                {
                    Directory.CreateDirectory(plan.Destination);
                    return new CacheMoveResult(plan, 0, 0, 0, 0, Array.Empty<string>());
                }

                if (plan.Method == CacheMoveMethod.Rename)
                {
                    try
                    {
                        return Rename(plan);
                    }
                    catch (IOException) when (!IsSameDrive(plan.Source, plan.Destination, new SystemDiskSpace()))
                    {
                        // The drive lookup was wrong, for example for a mount point it could not read. Copy instead;
                        // a rename moves nothing when it fails.
                    }
                }

                var symlinksAllowed = CacheFiles.AreSymlinksSupported(plan.Destination);
                DiskSpaceCheck.EnsureFits(
                    "move the folder",
                    new[] { new DiskSpaceRequirement(plan.Destination, plan.FileBytes + (symlinksAllowed ? 0 : plan.LinkedBytes), "the moved files") },
                    diskSpace);
                var result = Copy(plan, symlinksAllowed, progress, cancellationToken);
                Verify(result);
                return result;
            }
            finally
            {
                Volatile.Write(ref _moving, 0);
            }
        }

        /// <summary>
        /// Deletes the source of a finished, checked copy. Call it only after the user confirmed.
        /// </summary>
        public static void DeleteSource(CacheMoveResult result)
        {
            if (result.Plan.Method != CacheMoveMethod.Copy || !Directory.Exists(result.Plan.Source))
            {
                return;
            }

            DeleteTree(result.Plan.Source);
        }

        private static CacheMoveResult Rename(CacheMovePlan plan)
        {
            var kept = new List<string>();
            var moved = 0;
            if (!Directory.Exists(plan.Destination))
            {
                var parent = Path.GetDirectoryName(plan.Destination);
                if (!string.IsNullOrEmpty(parent))
                {
                    Directory.CreateDirectory(parent);
                }

                Directory.Move(plan.Source, plan.Destination);
                return new CacheMoveResult(plan, 0, 1, 0, 0, kept);
            }

            MergeByRename(plan.Source, plan.Destination, plan.Source, kept, ref moved);
            return new CacheMoveResult(plan, 0, moved, 0, 0, kept);
        }

        private static void MergeByRename(string sourceFolder, string destinationFolder, string sourceRoot, List<string> kept, ref int moved)
        {
            if (!Directory.Exists(destinationFolder) && !CacheFiles.IsSymlink(destinationFolder))
            {
                Directory.Move(sourceFolder, destinationFolder);
                moved++;
                return;
            }

            foreach (var file in Directory.GetFiles(sourceFolder))
            {
                var target = Path.Combine(destinationFolder, Path.GetFileName(file));
                if (File.Exists(target) || CacheFiles.IsSymlink(target))
                {
                    kept.Add(GetRelative(sourceRoot, file));
                    continue;
                }

                File.Move(file, target);
                moved++;
            }

            foreach (var folder in Directory.GetDirectories(sourceFolder))
            {
                MergeByRename(folder, Path.Combine(destinationFolder, Path.GetFileName(folder)), sourceRoot, kept, ref moved);
            }

            if (kept.Count == 0 || !Directory.EnumerateFileSystemEntries(sourceFolder).Any())
            {
                TryDeleteEmptyFolder(sourceFolder);
            }
        }

        private static CacheMoveResult Copy(CacheMovePlan plan, bool symlinksAllowed, IProgress<float> progress, CancellationToken cancellationToken)
        {
            var kept = new List<string>();
            long copiedBytes = 0;
            var linksCreated = 0;
            var linksCopied = 0;
            var totalBytes = Math.Max(1L, plan.FileBytes + (symlinksAllowed ? 0 : plan.LinkedBytes));
            var sourcePrefix = AppendSeparator(plan.Source);
            Directory.CreateDirectory(plan.Destination);

            foreach (var file in EnumerateMovableFiles(plan.Source))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = GetRelative(plan.Source, file);
                var target = Path.Combine(plan.Destination, relative);
                if (File.Exists(target) || CacheFiles.IsSymlink(target))
                {
                    kept.Add(relative);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target) ?? plan.Destination);
                if (CacheFiles.IsSymlink(file))
                {
                    var linkTarget = CacheFiles.TryGetLinkTarget(file);
                    if (linkTarget == null)
                    {
                        // A broken link has nothing to copy; huggingface_hub downloads the file again.
                        kept.Add(relative);
                        continue;
                    }

                    var insideSource = linkTarget.StartsWith(sourcePrefix, PathComparison);
                    if (insideSource && symlinksAllowed)
                    {
                        var movedTarget = Path.Combine(plan.Destination, linkTarget.Substring(sourcePrefix.Length));
                        if (CacheFiles.TryCreateSymlink(CacheFiles.GetRelativePath(Path.GetDirectoryName(target), movedTarget), target))
                        {
                            linksCreated++;
                            continue;
                        }
                    }

                    File.Copy(linkTarget, target);
                    copiedBytes += new FileInfo(target).Length;
                    linksCopied++;
                }
                else if (File.Exists(file))
                {
                    File.Copy(file, target);
                    copiedBytes += new FileInfo(target).Length;
                }
                else
                {
                    // A link whose target is gone reads as neither a file nor a link.
                    kept.Add(relative);
                    continue;
                }

                progress?.Report(Math.Min(1f, copiedBytes / (float)totalBytes));
            }

            return new CacheMoveResult(plan, copiedBytes, 0, linksCreated, linksCopied, kept);
        }

        /// <summary>
        /// Checks that every copied file has its source's size and every link resolves to content of the same size.
        /// </summary>
        private static void Verify(CacheMoveResult result)
        {
            var plan = result.Plan;
            var kept = new HashSet<string>(result.KeptInDestination, StringComparer.Ordinal);
            var problems = new List<string>();
            long sourceBytes = 0;
            long destinationBytes = 0;
            foreach (var file in EnumerateMovableFiles(plan.Source))
            {
                var relative = GetRelative(plan.Source, file);
                if (kept.Contains(relative))
                {
                    continue;
                }

                var target = Path.Combine(plan.Destination, relative);
                var sourceLength = CacheFiles.GetLength(file);
                var targetLength = CacheFiles.GetLength(target);
                if (!targetLength.HasValue || targetLength != sourceLength)
                {
                    problems.Add(relative);
                    continue;
                }

                sourceBytes += sourceLength ?? 0;
                destinationBytes += targetLength.Value;
            }

            if (problems.Count > 0 || sourceBytes != destinationBytes)
            {
                throw new IOException(
                    $"The copy in '{plan.Destination}' does not match '{plan.Source}' ({problems.Count} files differ, for example {string.Join(", ", problems.Take(3))}). " +
                    "The old folder was not deleted.");
            }
        }

        private static IEnumerable<string> EnumerateMovableFiles(string root)
        {
            foreach (var file in Directory.GetFiles(root))
            {
                yield return file;
            }

            foreach (var folder in Directory.GetDirectories(root))
            {
                var name = Path.GetFileName(folder);
                if (SkippedTopLevelFolders.Contains(name, StringComparer.Ordinal) || name.StartsWith(SymlinkProbePrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var file in EnumerateFilesRecursive(folder))
                {
                    yield return file;
                }
            }
        }

        private static IEnumerable<string> EnumerateFilesRecursive(string folder)
        {
            var pending = new Stack<string>();
            pending.Push(folder);
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                foreach (var file in Directory.GetFiles(current))
                {
                    yield return file;
                }

                foreach (var child in Directory.GetDirectories(current))
                {
                    pending.Push(child);
                }
            }
        }

        private static void DeleteTree(string folder)
        {
            // Delete links and files one by one so a symlink never makes the delete follow it.
            foreach (var file in Directory.GetFiles(folder))
            {
                var attributes = File.GetAttributes(file);
                if ((attributes & FileAttributes.ReadOnly) != 0 && (attributes & FileAttributes.ReparsePoint) == 0)
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                File.Delete(file);
            }

            foreach (var child in Directory.GetDirectories(folder))
            {
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                {
                    Directory.Delete(child);
                    continue;
                }

                DeleteTree(child);
            }

            Directory.Delete(folder);
        }

        private static void TryDeleteEmptyFolder(string folder)
        {
            try
            {
                if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                {
                    Directory.Delete(folder);
                }
            }
            catch (IOException)
            {
                // Another program opened the folder; an empty folder is harmless.
            }
        }

        internal static bool IsSameDrive(string left, string right, IDiskSpace diskSpace)
        {
            var leftDrive = diskSpace.GetDrive(left);
            var rightDrive = diskSpace.GetDrive(right);
            return leftDrive != null && rightDrive != null && string.Equals(leftDrive.RootPath, rightDrive.RootPath, PathComparison);
        }

        private static StringComparison PathComparison => Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        private static bool PathsEqual(string left, string right)
        {
            return string.Equals(left.TrimEnd(Path.DirectorySeparatorChar), right.TrimEnd(Path.DirectorySeparatorChar), PathComparison);
        }

        private static bool IsInside(string path, string folder)
        {
            return path.StartsWith(AppendSeparator(folder), PathComparison);
        }

        private static string GetRelative(string root, string path)
        {
            return path.Substring(AppendSeparator(root).Length);
        }

        private static string AppendSeparator(string path)
        {
            return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ? path : path + Path.DirectorySeparatorChar;
        }
    }
}
