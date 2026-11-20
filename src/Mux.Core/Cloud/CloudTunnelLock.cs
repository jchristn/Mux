namespace Mux.Core.Cloud
{
    using System;
    using System.IO;

    /// <summary>
    /// An exclusive lock file in the mux config directory (<c>cloud-tunnel.lock</c>) that guarantees only one process
    /// on the machine runs the Mux Cloud tunnel. The operating system releases it if the process dies.
    /// <para>Thread safety: not thread safe; own it from one host.</para>
    /// </summary>
    public sealed class CloudTunnelLock : IDisposable
    {
        #region Private-Members

        private FileStream? _Stream;

        #endregion

        #region Public-Members

        /// <summary>Lock file name inside the config directory.</summary>
        public const string FileName = "cloud-tunnel.lock";

        /// <summary>Full path of the lock file.</summary>
        public string Path { get; }

        /// <summary>Whether this instance holds the lock.</summary>
        public bool IsHeld => _Stream != null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate (does not acquire).</summary>
        /// <param name="configDirectory">The mux config directory.</param>
        /// <exception cref="ArgumentException">configDirectory is blank.</exception>
        public CloudTunnelLock(string configDirectory)
        {
            if (string.IsNullOrWhiteSpace(configDirectory)) throw new ArgumentException("A config directory is required for the Mux Cloud tunnel lock.", nameof(configDirectory));
            Path = System.IO.Path.Combine(configDirectory, FileName);
        }

        #endregion

        #region Public-Methods

        /// <summary>Try to take the lock. Idempotent while held.</summary>
        /// <returns>True when this instance holds the lock.</returns>
        public bool TryAcquire()
        {
            if (_Stream != null) return true;
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                _Stream = new FileStream(Path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.None);
                _Stream.SetLength(0);
                byte[] pid = System.Text.Encoding.ASCII.GetBytes(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                _Stream.Write(pid, 0, pid.Length);
                _Stream.Flush();
                return true;
            }
            catch (IOException)
            {
                _Stream = null;
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                _Stream = null;
                return false;
            }
        }

        /// <summary>Release the lock if held.</summary>
        public void Release()
        {
            FileStream? stream = _Stream;
            _Stream = null;
            if (stream == null) return;
            try { stream.Dispose(); } catch (IOException) { }
            try { File.Delete(Path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        /// <summary>Release the lock.</summary>
        public void Dispose()
        {
            Release();
        }

        #endregion
    }
}
