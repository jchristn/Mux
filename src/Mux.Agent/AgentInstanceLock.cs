namespace Mux.Agent
{
    using System;
    using System.IO;

    /// <summary>
    /// Cross-process single-instance guard. Holds an exclusive (<see cref="FileShare.None"/>) lock on
    /// <c>agent.lock</c> in the config directory for the life of the process; a second launch fails to
    /// acquire it and exits, so only one tray agent runs at a time.
    /// </summary>
    public sealed class AgentInstanceLock : IDisposable
    {
        private FileStream? _Stream;

        private AgentInstanceLock(FileStream stream)
        {
            _Stream = stream;
        }

        /// <summary>
        /// Try to acquire the single-instance lock.
        /// </summary>
        /// <param name="directory">Directory to place the lock file in.</param>
        /// <returns>The held lock, or null when another instance already holds it.</returns>
        public static AgentInstanceLock? TryAcquire(string directory)
        {
            try
            {
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "agent.lock");
                FileStream stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return new AgentInstanceLock(stream);
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
            try { _Stream?.Dispose(); } catch (Exception) { }
            _Stream = null;
        }
    }
}
