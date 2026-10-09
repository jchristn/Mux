namespace Mux.Core.Skills
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    /// <summary>
    /// Answers whether an executable is on <c>PATH</c>, for the <c>requiresTools</c> listing gate. It scans the
    /// <c>PATH</c> directories (and, on Windows, the <c>PATHEXT</c> extensions) without running anything, and caches
    /// each answer for <see cref="TimeToLive"/>. A requirement may name alternatives separated by <c>|</c>
    /// (<c>terraform|tofu</c>), which is satisfied when any one is present. Thread-safe.
    /// </summary>
    public sealed class ToolPresenceCache
    {
        #region Private-Members

        private readonly object _Sync = new object();
        private readonly Dictionary<string, bool> _Results = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Func<string?> _PathProvider;
        private DateTime _ResetUtc = DateTime.UtcNow;
        private TimeSpan _TimeToLive = TimeSpan.FromSeconds(30);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="ToolPresenceCache"/> class that reads the process
        /// <c>PATH</c>.
        /// </summary>
        public ToolPresenceCache()
            : this(() => Environment.GetEnvironmentVariable("PATH"))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ToolPresenceCache"/> class with a custom <c>PATH</c>
        /// source, for tests.
        /// </summary>
        /// <param name="pathProvider">Returns the PATH value to scan. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="pathProvider"/> is null.</exception>
        public ToolPresenceCache(Func<string?> pathProvider)
        {
            _PathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// How long an answer is cached before PATH is scanned again. Default 30 seconds, minimum zero (no
        /// caching), maximum one day.
        /// </summary>
        public TimeSpan TimeToLive
        {
            get { lock (_Sync) { return _TimeToLive; } }
            set { lock (_Sync) { _TimeToLive = value < TimeSpan.Zero ? TimeSpan.Zero : (value > TimeSpan.FromDays(1) ? TimeSpan.FromDays(1) : value); } }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Returns whether every requirement is satisfied. An empty or null list is satisfied.
        /// </summary>
        /// <param name="requirements">The requirements; each may name alternatives separated by <c>|</c>.</param>
        /// <returns><c>true</c> when all requirements are met.</returns>
        public bool AllAvailable(IReadOnlyList<string>? requirements)
        {
            if (requirements == null)
            {
                return true;
            }

            foreach (string requirement in requirements)
            {
                if (!IsAvailable(requirement))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Returns whether one requirement is met: any of its <c>|</c>-separated alternatives is on PATH. A blank
        /// requirement is met.
        /// </summary>
        /// <param name="requirement">The requirement.</param>
        /// <returns><c>true</c> when the requirement is met.</returns>
        public bool IsAvailable(string? requirement)
        {
            if (string.IsNullOrWhiteSpace(requirement))
            {
                return true;
            }

            foreach (string alternative in requirement.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (IsOnPath(alternative))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Discards every cached answer.
        /// </summary>
        public void Clear()
        {
            lock (_Sync)
            {
                _Results.Clear();
                _ResetUtc = DateTime.UtcNow;
            }
        }

        #endregion

        #region Private-Methods

        private bool IsOnPath(string executable)
        {
            lock (_Sync)
            {
                if (DateTime.UtcNow - _ResetUtc > _TimeToLive)
                {
                    _Results.Clear();
                    _ResetUtc = DateTime.UtcNow;
                }

                if (_Results.TryGetValue(executable, out bool cached))
                {
                    return cached;
                }
            }

            bool found = Scan(executable);
            lock (_Sync)
            {
                _Results[executable] = found;
            }

            return found;
        }

        private bool Scan(string executable)
        {
            if (executable.IndexOfAny(new[] { '/', '\\' }) >= 0)
            {
                return false;
            }

            List<string> names = new List<string> { executable };
            if (OperatingSystem.IsWindows())
            {
                string extensions = Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD";
                foreach (string extension in extensions.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    names.Add(executable + extension.ToLowerInvariant());
                }
            }

            foreach (string directory in (_PathProvider() ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (string name in names)
                {
                    try
                    {
                        if (File.Exists(Path.Combine(directory.Trim().Trim('"'), name)))
                        {
                            return true;
                        }
                    }
                    catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException)
                    {
                        // An unusable PATH entry is skipped.
                    }
                }
            }

            return false;
        }

        #endregion
    }
}
