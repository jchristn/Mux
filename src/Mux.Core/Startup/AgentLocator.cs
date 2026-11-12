namespace Mux.Core.Startup
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    /// <summary>
    /// Finds the tray agent (Mux.Agent) executable so any surface (the terminal UI, the desktop app, the CLI's
    /// <c>mux agent start</c>, and through it the VS Code extension) can start it. The search order is:
    /// <list type="number">
    /// <item><c>MUX_AGENT</c>: a full path to the executable, or a directory containing it.</item>
    /// <item>Beside the running program.</item>
    /// <item>The installed agent that belongs to the same install as the running program (for example
    /// <c>/Applications/Mux Agent.app</c> when the desktop app runs from <c>/Applications/Mux.app</c>).</item>
    /// <item>The per-user install, then the system-wide install, at the paths the install scripts use: macOS
    /// <c>~/Applications</c> and <c>/Applications</c>; Linux and WSL <c>${XDG_DATA_HOME:-~/.local/share}/mux/apps/agent</c>
    /// and <c>/opt/mux/agent</c>; Windows <c>%LOCALAPPDATA%\Programs\Mux\Agent</c> and <c>%ProgramFiles%\Mux\Agent</c>.</item>
    /// <item>A source checkout above the working directory: the newest build under <c>src/Mux.Agent/bin</c>.</item>
    /// </list>
    /// </summary>
    public static class AgentLocator
    {
        #region Public-Members

        /// <summary>The agent's process name.</summary>
        public const string ProcessName = "Mux.Agent";

        /// <summary>The environment variable that overrides the search with a path or directory.</summary>
        public const string OverrideVariable = "MUX_AGENT";

        #endregion

        #region Public-Methods

        /// <summary>The agent executable's file name on this OS.</summary>
        /// <returns><c>Mux.Agent.exe</c> on Windows, otherwise <c>Mux.Agent</c>.</returns>
        public static string ExecutableName()
        {
            return OperatingSystem.IsWindows() ? "Mux.Agent.exe" : "Mux.Agent";
        }

        /// <summary>
        /// The installed-agent locations for this OS, per-user first, then system-wide.
        /// </summary>
        /// <returns>Full paths to where an installed agent executable would be.</returns>
        public static IReadOnlyList<string> InstalledLocations()
        {
            string platform = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return InstalledLocations(platform, home, Environment.GetEnvironmentVariable);
        }

        /// <summary>
        /// The installed-agent locations for a platform, per-user first, then system-wide. Exposed for tests.
        /// </summary>
        /// <param name="platform">"macos", "linux" (also WSL), or "windows".</param>
        /// <param name="home">The user's home directory.</param>
        /// <param name="getEnvironment">Reads an environment variable (null when unset).</param>
        /// <returns>Full paths to where an installed agent executable would be.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="getEnvironment"/> is null.</exception>
        public static IReadOnlyList<string> InstalledLocations(string platform, string home, Func<string, string?> getEnvironment)
        {
            ArgumentNullException.ThrowIfNull(getEnvironment);
            List<string> paths = new List<string>();
            switch (platform)
            {
                case "macos":
                    paths.Add(Path.Combine(home, "Applications", "Mux Agent.app", "Contents", "MacOS", "Mux.Agent"));
                    paths.Add(Path.Combine("/Applications", "Mux Agent.app", "Contents", "MacOS", "Mux.Agent"));
                    break;
                case "windows":
                    string? local = NonEmpty(getEnvironment("LOCALAPPDATA")) ?? Path.Combine(home, "AppData", "Local");
                    paths.Add(Path.Combine(local, "Programs", "Mux", "Agent", "Mux.Agent.exe"));
                    string? programFiles = NonEmpty(getEnvironment("ProgramFiles"));
                    if (programFiles != null)
                    {
                        paths.Add(Path.Combine(programFiles, "Mux", "Agent", "Mux.Agent.exe"));
                    }

                    break;
                default:
                    string data = NonEmpty(getEnvironment("XDG_DATA_HOME")) ?? Path.Combine(home, ".local", "share");
                    paths.Add(Path.Combine(data, "mux", "apps", "agent", "Mux.Agent"));
                    paths.Add(Path.Combine("/opt", "mux", "agent", "Mux.Agent"));
                    break;
            }

            return paths;
        }

        /// <summary>
        /// Orders installed locations so the one sharing an install root with <paramref name="baseDirectory"/> comes
        /// first (the desktop app in <c>/Applications</c> starts the agent in <c>/Applications</c>), keeping the
        /// per-user-then-system order otherwise. Exposed for tests.
        /// </summary>
        /// <param name="installed">Installed locations, per-user first.</param>
        /// <param name="baseDirectory">The running program's directory.</param>
        /// <returns>The reordered locations.</returns>
        public static IReadOnlyList<string> PreferSameInstall(IReadOnlyList<string> installed, string? baseDirectory)
        {
            if (installed == null) throw new ArgumentNullException(nameof(installed));
            if (string.IsNullOrWhiteSpace(baseDirectory)) return installed;
            string normalizedBase = Normalize(baseDirectory);
            return installed
                .OrderBy(path => normalizedBase.StartsWith(InstallRoot(path), StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ToList();
        }

        /// <summary>
        /// The mux version of an installed agent, read from the marker the install scripts write: <c>mux-version.txt</c>
        /// beside the executable (Linux, WSL, Windows) or <c>CFBundleShortVersionString</c> in the bundle's
        /// <c>Info.plist</c> (macOS). Null when there is no marker (for example a build from source).
        /// </summary>
        /// <param name="executable">The agent executable.</param>
        /// <returns>The version, or null.</returns>
        public static string? InstalledVersion(string? executable)
        {
            if (string.IsNullOrWhiteSpace(executable)) return null;
            try
            {
                string? directory = Path.GetDirectoryName(executable);
                if (directory == null) return null;
                string marker = Path.Combine(directory, "mux-version.txt");
                if (File.Exists(marker))
                {
                    string version = File.ReadAllText(marker).Trim();
                    return version.Length > 0 ? version : null;
                }

                string plist = Path.Combine(directory, "..", "Info.plist");
                if (File.Exists(plist))
                {
                    string text = File.ReadAllText(plist);
                    int key = text.IndexOf("<key>CFBundleShortVersionString</key>", StringComparison.Ordinal);
                    if (key >= 0)
                    {
                        int open = text.IndexOf("<string>", key, StringComparison.Ordinal);
                        int close = open < 0 ? -1 : text.IndexOf("</string>", open, StringComparison.Ordinal);
                        if (open >= 0 && close > open) return text.Substring(open + 8, close - open - 8).Trim();
                    }
                }
            }
            catch (Exception)
            {
            }

            return null;
        }

        /// <summary>
        /// Finds the agent executable, or null when there is none (see the class remarks for the order).
        /// </summary>
        /// <returns>The full path, or null.</returns>
        public static string? Locate()
        {
            string exeName = ExecutableName();

            string? overridePath = NonEmpty(Environment.GetEnvironmentVariable(OverrideVariable));
            if (overridePath != null)
            {
                if (File.Exists(overridePath)) return overridePath;
                string inDirectory = Path.Combine(overridePath, exeName);
                if (File.Exists(inDirectory)) return inDirectory;
            }

            string? baseDirectory = null;
            try
            {
                baseDirectory = AppContext.BaseDirectory;
                string beside = Path.Combine(baseDirectory, exeName);
                if (File.Exists(beside)) return beside;
            }
            catch (Exception)
            {
            }

            foreach (string installed in PreferSameInstall(InstalledLocations(), baseDirectory))
            {
                if (File.Exists(installed)) return installed;
            }

            return LocateInCheckout(Environment.CurrentDirectory, exeName);
        }

        #endregion

        #region Private-Methods

        // The newest agent build under src/Mux.Agent/bin in a checkout at or above the working directory.
        private static string? LocateInCheckout(string workingDirectory, string exeName)
        {
            try
            {
                DirectoryInfo? dir = new DirectoryInfo(workingDirectory);
                for (int depth = 0; dir != null && depth < 8; depth++, dir = dir.Parent)
                {
                    string agentDir = Path.Combine(dir.FullName, "src", "Mux.Agent");
                    if (!Directory.Exists(agentDir)) continue;
                    string binDir = Path.Combine(agentDir, "bin");
                    if (!Directory.Exists(binDir)) return null; // found the checkout, but nothing is built
                    return new DirectoryInfo(binDir)
                        .EnumerateFiles(exeName, SearchOption.AllDirectories)
                        .OrderByDescending(f => f.LastWriteTimeUtc)
                        .FirstOrDefault()?.FullName;
                }
            }
            catch (Exception)
            {
            }

            return null;
        }

        // The folder that holds every app of one install: the Applications folder on macOS (above the .app bundle),
        // and the folder above the per-app directory elsewhere (…/Mux on Windows, …/mux/apps or /opt/mux on Linux).
        private static string InstallRoot(string agentExecutable)
        {
            string path = Normalize(agentExecutable);
            int bundle = path.IndexOf("/Mux Agent.app/", StringComparison.Ordinal);
            if (bundle >= 0) return path.Substring(0, bundle + 1);
            string? agentDirectory = Path.GetDirectoryName(path.Replace('/', Path.DirectorySeparatorChar));
            string? root = agentDirectory == null ? null : Path.GetDirectoryName(agentDirectory);
            return root == null ? path : Normalize(root) + "/";
        }

        private static string Normalize(string path)
        {
            return path.Replace('\\', '/');
        }

        private static string? NonEmpty(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        #endregion
    }
}
