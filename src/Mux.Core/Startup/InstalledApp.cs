namespace Mux.Core.Startup
{
    using System;

    /// <summary>
    /// Describes one of mux's installed apps for <see cref="InstalledAppLocator"/>: its executable, the macOS bundle it
    /// ships in, the per-app folder the install scripts use on Linux, WSL, and Windows, its override variable, and its
    /// project folder in a source checkout.
    /// </summary>
    public sealed class InstalledApp
    {
        #region Public-Members

        /// <summary>The tray agent: <c>Mux Agent.app</c>, <c>…/agent</c>, <c>…\Agent</c>, <c>MUX_AGENT</c>.</summary>
        public static InstalledApp Agent { get; } = new InstalledApp("Mux.Agent", "Mux Agent.app", "Agent", "MUX_AGENT");

        /// <summary>The desktop app: <c>Mux.app</c>, <c>…/desktop</c>, <c>…\Desktop</c>, <c>MUX_DESKTOP</c>.</summary>
        public static InstalledApp Desktop { get; } = new InstalledApp("Mux.Desktop", "Mux.app", "Desktop", "MUX_DESKTOP");

        /// <summary>The executable's name without an extension, which is also the process name and the checkout project.</summary>
        public string ProcessName { get; }

        /// <summary>The macOS bundle folder name, for example <c>Mux.app</c>.</summary>
        public string BundleName { get; }

        /// <summary>The per-app folder name on Windows (<c>Desktop</c>); Linux and WSL use it in lower case.</summary>
        public string FolderName { get; }

        /// <summary>The environment variable that overrides the search with a path or directory.</summary>
        public string OverrideVariable { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>Initializes a new instance of the <see cref="InstalledApp"/> class.</summary>
        /// <param name="processName">The executable's name without an extension.</param>
        /// <param name="bundleName">The macOS bundle folder name.</param>
        /// <param name="folderName">The per-app folder name.</param>
        /// <param name="overrideVariable">The override environment variable.</param>
        /// <exception cref="ArgumentException">Thrown when a name is null or empty.</exception>
        public InstalledApp(string processName, string bundleName, string folderName, string overrideVariable)
        {
            if (string.IsNullOrWhiteSpace(processName)) throw new ArgumentException("A process name is required.", nameof(processName));
            if (string.IsNullOrWhiteSpace(bundleName)) throw new ArgumentException("A bundle name is required.", nameof(bundleName));
            if (string.IsNullOrWhiteSpace(folderName)) throw new ArgumentException("A folder name is required.", nameof(folderName));
            if (string.IsNullOrWhiteSpace(overrideVariable)) throw new ArgumentException("An override variable is required.", nameof(overrideVariable));
            ProcessName = processName;
            BundleName = bundleName;
            FolderName = folderName;
            OverrideVariable = overrideVariable;
        }

        #endregion
    }
}
