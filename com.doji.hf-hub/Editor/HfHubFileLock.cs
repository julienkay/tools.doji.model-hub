using System;
using System.IO;
using System.Threading;

namespace Doji.AI.ModelHub.Editor
{
    /// <summary>
    /// Provides coarse file-level lock for cache download and metadata writes.
    /// </summary>
    internal sealed class HfHubFileLock : IDisposable
    {
        private readonly FileStream _lockStream;

        private HfHubFileLock(FileStream lockStream)
        {
            _lockStream = lockStream;
        }

        /// <summary>
        /// Acquires exclusive lock for path, waiting until timeout expires.
        /// </summary>
        public static HfHubFileLock Acquire(string lockPath, TimeSpan timeout)
        {
            var lockDirectory = Path.GetDirectoryName(lockPath);
            if (!string.IsNullOrWhiteSpace(lockDirectory))
            {
                Directory.CreateDirectory(lockDirectory);
            }

            var startTime = DateTime.UtcNow;
            while (true)
            {
                try
                {
                    var stream = new FileStream(
                        lockPath,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None);
                    return new HfHubFileLock(stream);
                }
                catch (IOException)
                {
                    if (DateTime.UtcNow - startTime >= timeout)
                    {
                        throw;
                    }

                    Thread.Sleep(100);
                }
            }
        }

        public void Dispose()
        {
            _lockStream.Dispose();
        }
    }
}
