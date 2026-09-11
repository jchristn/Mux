namespace Mux.Desktop
{
    using System;
    using System.IO;

    /// <summary>
    /// Cross-process single-instance guard. Holds an exclusive (<see cref="FileShare.None"/>) lock on
    /// <c>desktop.lock</c> in the config directory for the life of the process; a second launch fails to
    /// acquire it and exits, so only one desktop instance runs per config directory at a time.
    /// </summary>
    public sealed class DesktopInstanceLock : IDisposable
    {
        private FileStream? _Stream;

        private DesktopInstanceLock(FileStream stream)
        {
            _Stream = stream;
        }

        /// <summary>
        /// Try to acquire the single-instance lock.
        /// </summary>
        /// <param name="directory">Directory to place the lock file in.</param>
        /// <returns>The held lock, or null when another instance already holds it or the lock cannot be taken.</returns>
        public static DesktopInstanceLock? TryAcquire(string directory)
        {
            if (string.IsNullOrEmpty(directory))
            {
                return null;
            }

            try
            {
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "desktop.lock");
                FileStream stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return new DesktopInstanceLock(stream);
            }
            catch (IOException)
            {
                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Release the lock.
        /// </summary>
        public void Dispose()
        {
            try
            {
                _Stream?.Dispose();
            }
            catch (Exception)
            {
                // Best-effort release.
            }

            _Stream = null;
        }
    }
}
