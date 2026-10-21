using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// File operations for the shared Hugging Face cache: snapshot entries that link to blobs, and sizes that
    /// follow those links. Mirrors <c>_create_symlink</c> in <c>huggingface_hub</c>: a relative symlink when the
    /// file system allows it, otherwise a moved or copied file.
    /// </summary>
    internal static class CacheFiles
    {
        private const uint SymbolicLinkFlagAllowUnprivilegedCreate = 0x2;
        private static readonly Dictionary<string, bool> SymlinkSupportByFolder = new(StringComparer.OrdinalIgnoreCase);
        private static bool _loggedCopyFallback;

        /// <summary>
        /// Test hook that forces the copy fallback, as on Windows without Developer Mode.
        /// </summary>
        internal static bool? ForceSymlinkSupport { get; set; }

        /// <summary>
        /// Makes <paramref name="snapshotPath"/> point at <paramref name="blobPath"/>. With symlinks it creates a
        /// relative link. Without symlinks a new blob is moved into the snapshot, and an existing blob is copied,
        /// so other snapshots that already use it keep working.
        /// </summary>
        public static void LinkSnapshotFile(string blobPath, string snapshotPath, bool isNewBlob)
        {
            var snapshotFolder = Path.GetDirectoryName(snapshotPath);
            if (!string.IsNullOrWhiteSpace(snapshotFolder))
            {
                Directory.CreateDirectory(snapshotFolder);
            }

            DeleteFileOrLink(snapshotPath);

            var cacheFolder = Path.GetDirectoryName(Path.GetDirectoryName(blobPath));
            if (AreSymlinksSupported(cacheFolder) && TryCreateSymlink(GetRelativePath(snapshotFolder, blobPath), snapshotPath))
            {
                return;
            }

            if (!_loggedCopyFallback)
            {
                _loggedCopyFallback = true;
                UnityEngine.Debug.Log(
                    "HF Hub stores model files as copies because this file system does not allow symbolic links. " +
                    "Files shared by several revisions then use extra disk space. On Windows, turn on Developer Mode to allow symbolic links.");
            }

            if (isNewBlob)
            {
                File.Move(blobPath, snapshotPath);
            }
            else
            {
                File.Copy(blobPath, snapshotPath, true);
            }
        }

        /// <summary>
        /// Returns the size of the file content, following symlinks. On Windows a link itself reports 0 bytes.
        /// </summary>
        public static long? GetLength(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return stream.Length;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>
        /// Whether a path is a symbolic link.
        /// </summary>
        public static bool IsSymlink(string path)
        {
            try
            {
                return File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
            }
            catch (IOException)
            {
                return false;
            }
        }

        /// <summary>
        /// Checks once per cache folder whether symlinks can be created there, like <c>are_symlinks_supported</c>.
        /// </summary>
        internal static bool AreSymlinksSupported(string folder)
        {
            if (ForceSymlinkSupport.HasValue)
            {
                return ForceSymlinkSupport.Value;
            }

            if (string.IsNullOrWhiteSpace(folder))
            {
                return false;
            }

            lock (SymlinkSupportByFolder)
            {
                if (SymlinkSupportByFolder.TryGetValue(folder, out var supported))
                {
                    return supported;
                }

                supported = ProbeSymlinkSupport(folder);
                SymlinkSupportByFolder[folder] = supported;
                return supported;
            }
        }

        private static bool ProbeSymlinkSupport(string folder)
        {
            Directory.CreateDirectory(folder);
            var probeFolder = Path.Combine(folder, ".hfhub-symlink-probe-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(probeFolder);
                var target = Path.Combine(probeFolder, "target");
                File.WriteAllText(target, string.Empty);
                return TryCreateSymlink("target", Path.Combine(probeFolder, "link"));
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                try
                {
                    Directory.Delete(probeFolder, true);
                }
                catch (Exception)
                {
                    // The probe folder is empty except for its own files; leaving it behind is harmless.
                }
            }
        }

        private static bool TryCreateSymlink(string relativeTarget, string linkPath)
        {
            try
            {
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    return CreateSymbolicLinkW(linkPath, relativeTarget.Replace('/', '\\'), SymbolicLinkFlagAllowUnprivilegedCreate) != 0;
                }

                return symlink(relativeTarget.Replace('\\', '/'), linkPath) == 0;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
        }

        private static void DeleteFileOrLink(string path)
        {
            if (File.Exists(path) || IsSymlink(path))
            {
                File.Delete(path);
            }
        }

        /// <summary>
        /// Relative path from a folder to a file, with platform separators.
        /// </summary>
        internal static string GetRelativePath(string fromFolder, string toFile)
        {
            var fromUri = new Uri(AppendSeparator(Path.GetFullPath(fromFolder)));
            var toUri = new Uri(Path.GetFullPath(toFile));
            var relative = Uri.UnescapeDataString(fromUri.MakeRelativeUri(toUri).ToString());
            return relative.Replace('/', Path.DirectorySeparatorChar);
        }

        private static string AppendSeparator(string path)
        {
            return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? path
                : path + Path.DirectorySeparatorChar;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern byte CreateSymbolicLinkW(string symlinkFileName, string targetFileName, uint flags);

        [DllImport("libc", SetLastError = true)]
        private static extern int symlink(string target, string linkPath);
    }
}
