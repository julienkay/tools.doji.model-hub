using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Lock on one blob in the shared Hugging Face cache, compatible with the <c>filelock</c> package that
    /// <c>huggingface_hub</c> uses: a one-byte <c>msvcrt.locking</c> lock on Windows and <c>flock</c> on macOS and
    /// Linux, on <c>.locks/models--owner--name/&lt;etag&gt;.lock</c>. Unity and Python never write the same blob
    /// at the same time.
    /// </summary>
    internal sealed class BlobLock : IDisposable
    {
        private const int LockExclusive = 2;
        private const int LockNonBlocking = 4;
        private const int LockUnlock = 8;
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

        private FileStream _stream;

        private BlobLock(FileStream stream)
        {
            _stream = stream;
        }

        /// <summary>
        /// Waits until the lock is free, then takes it. Cancel the token to stop waiting.
        /// </summary>
        public static async Task<BlobLock> AcquireAsync(string lockPath, Action onWaiting, CancellationToken cancellationToken)
        {
            var notifiedWaiting = false;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (TryAcquire(lockPath, out var blobLock))
                {
                    return blobLock;
                }

                if (!notifiedWaiting)
                {
                    notifiedWaiting = true;
                    onWaiting?.Invoke();
                }

                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Takes the lock if it is free and returns false otherwise.
        /// </summary>
        public static bool TryAcquire(string lockPath, out BlobLock blobLock)
        {
            blobLock = null;
            var folder = Path.GetDirectoryName(lockPath);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                Directory.CreateDirectory(folder);
            }

            FileStream stream;
            try
            {
                // Share read, write, and delete like Python's os.open, so filelock in another process can open the
                // file and then fail on the lock itself, and can delete the file after its own release.
                stream = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }

            if (!TryLockStream(stream))
            {
                stream.Dispose();
                return false;
            }

            blobLock = new BlobLock(stream);
            return true;
        }

        public void Dispose()
        {
            if (_stream == null)
            {
                return;
            }

            try
            {
                UnlockStream(_stream);
            }
            catch (IOException)
            {
                // Closing the file releases the lock anyway.
            }

            _stream.Dispose();
            _stream = null;
        }

        private static bool TryLockStream(FileStream stream)
        {
            if (IsWindows)
            {
                try
                {
                    // LockFile on byte 0, length 1: the same region msvcrt.locking(fd, LK_NBLCK, 1) locks.
                    stream.Lock(0, 1);
                    return true;
                }
                catch (IOException)
                {
                    return false;
                }
            }

            return flock(GetDescriptor(stream), LockExclusive | LockNonBlocking) == 0;
        }

        private static void UnlockStream(FileStream stream)
        {
            if (IsWindows)
            {
                stream.Unlock(0, 1);
                return;
            }

            flock(GetDescriptor(stream), LockUnlock);
        }

        private static bool IsWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;

        private static int GetDescriptor(FileStream stream)
        {
            return (int)stream.SafeFileHandle.DangerousGetHandle().ToInt64();
        }

        [DllImport("libc", SetLastError = true)]
        private static extern int flock(int fd, int operation);
    }
}
