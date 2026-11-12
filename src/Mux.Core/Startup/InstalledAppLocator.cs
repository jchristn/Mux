namespace Mux.Core.Startup
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    /// <summary>
    /// Finds an installed mux app (the tray agent or the desktop app, see <see cref="InstalledApp"/>). The search order
    /// is:
    /// <list type="number">
    /// <item>The app's override variable (<c>MUX_AGENT</c>, <c>MUX_DESKTOP</c>): a full path to the executable, or a
    /// directory containing it.</item>
    /// <item>Beside the running program.</item>
    /// <item>The installed copy that belongs to the same install as the running program (for example
    /// <c>/Applications/Mux.app</c> when the agent runs from <c>/Applications/Mux Agent.app</c>).</item>
    /// <item>The per-user install, then the system-wide install, at the paths the install scripts use: macOS
    /// <c>~/Applications/&lt;bundle&gt;</c> and <c>/Applications/&lt;bundle&gt;</c>; Linux and WSL
    /// <c>${XDG_DATA_HOME:-~/.local/share}/mux/apps/&lt;app&gt;</c> and <c>/opt/mux/&lt;app&gt;</c>; Windows
    /// <c>%LOCALAPPDATA%\Programs\Mux\&lt;App&gt;</c> and <c>%ProgramFiles%\Mux\&lt;App&gt;</c>.</item>
    /// <item>A source checkout above the working directory: the newest build under <c>src/&lt;project&gt;/bin</c>.</item>
    /// </list>
    /// </summary>
    public static class InstalledAppLocator
    {
        #region Public-Methods

        /// <summary>The current OS as the locator names it.</summary>
        /// <returns>"windows", "macos", or "linux" (also WSL).</returns>
        public static string CurrentPlatform()
        {
            return OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
        }

        /// <summary>The app's executable file name on this OS.</summary>
        /// <param name="app">The app.</param>
        /// <returns>For example <c>Mux.Desktop.exe</c> on Windows, otherwise <c>Mux.Desktop</c>.</returns>
        public static string ExecutableName(InstalledApp app)
        {
            return ExecutableName(app, CurrentPlatform());
        }

        /// <summary>The app's executable file name on a platform.</summary>
        /// <param name="app">The app.</param>
        /// <param name="platform">"macos", "linux", or "windows".</param>
        /// <returns>The file name.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="app"/> is null.</exception>
        public static string ExecutableName(InstalledApp app, string platform)
        {
            ArgumentNullException.ThrowIfNull(app);
            return platform == "windows" ? app.ProcessName + ".exe" : app.ProcessName;
        }

        /// <summary>The app's installed locations on this OS, per-user first, then system-wide.</summary>
        /// <param name="app">The app.</param>
        /// <returns>Full paths to where an installed executable would be.</returns>
        public static IReadOnlyList<string> InstalledLocations(InstalledApp app)
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return InstalledLocations(app, CurrentPlatform(), home, Environment.GetEnvironmentVariable);
        }

        /// <summary>The app's installed locations on a platform, per-user first, then system-wide. Exposed for tests.</summary>
        /// <param name="app">The app.</param>
        /// <param name="platform">"macos", "linux" (also WSL), or "windows".</param>
        /// <param name="home">The user's home directory.</param>
        /// <param name="getEnvironment">Reads an environment variable (null when unset).</param>
        /// <returns>Full paths to where an installed executable would be.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="app"/> or <paramref name="getEnvironment"/> is null.</exception>
        public static IReadOnlyList<string> InstalledLocations(InstalledApp app, string platform, string home, Func<string, string?> getEnvironment)
        {
            ArgumentNullException.ThrowIfNull(app);
            ArgumentNullException.ThrowIfNull(getEnvironment);
            string exeName = ExecutableName(app, platform);
            List<string> paths = new List<string>();
            switch (platform)
            {
                case "macos":
                    paths.Add(Path.Combine(home, "Applications", app.BundleName, "Contents", "MacOS", exeName));
                    paths.Add(Path.Combine("/Applications", app.BundleName, "Contents", "MacOS", exeName));
                    break;
                case "windows":
                    string local = NonEmpty(getEnvironment("LOCALAPPDATA")) ?? Path.Combine(home, "AppData", "Local");
                    paths.Add(Path.Combine(local, "Programs", "Mux", app.FolderName, exeName));
                    string? programFiles = NonEmpty(getEnvironment("ProgramFiles"));
                    if (programFiles != null)
                    {
                        paths.Add(Path.Combine(programFiles, "Mux", app.FolderName, exeName));
                    }

                    break;
                default:
                    string data = NonEmpty(getEnvironment("XDG_DATA_HOME")) ?? Path.Combine(home, ".local", "share");
                    string folder = app.FolderName.ToLowerInvariant();
                    paths.Add(Path.Combine(data, "mux", "apps", folder, exeName));
                    paths.Add(Path.Combine("/opt", "mux", folder, exeName));
                    break;
            }

            return paths;
        }

        /// <summary>
        /// Orders installed locations so the one sharing an install root with <paramref name="baseDirectory"/> comes
        /// first, keeping the per-user-then-system order otherwise. Exposed for tests.
        /// </summary>
        /// <param name="installed">Installed locations, per-user first.</param>
        /// <param name="baseDirectory">The running program's directory.</param>
        /// <returns>The reordered locations.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="installed"/> is null.</exception>
        public static IReadOnlyList<string> PreferSameInstall(IReadOnlyList<string> installed, string? baseDirectory)
        {
            ArgumentNullException.ThrowIfNull(installed);
            if (string.IsNullOrWhiteSpace(baseDirectory)) return installed;
            string normalizedBase = Normalize(baseDirectory);
            return installed
                .OrderBy(path => normalizedBase.StartsWith(InstallRoot(path), StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ToList();
        }

        /// <summary>
        /// Every path the search tries before a source checkout, in order: the override (as a file, then as a folder),
        /// beside the running program, then the installed copies with the same install first. Exposed for tests.
        /// </summary>
        /// <param name="app">The app.</param>
        /// <param name="platform">"macos", "linux" (also WSL), or "windows".</param>
        /// <param name="home">The user's home directory.</param>
        /// <param name="getEnvironment">Reads an environment variable (null when unset).</param>
        /// <param name="baseDirectory">The running program's directory, or null.</param>
        /// <returns>Candidate executable paths, first match wins.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="app"/> or <paramref name="getEnvironment"/> is null.</exception>
        public static IReadOnlyList<string> Candidates(InstalledApp app, string platform, string home, Func<string, string?> getEnvironment, string? baseDirectory)
        {
            ArgumentNullException.ThrowIfNull(app);
            ArgumentNullException.ThrowIfNull(getEnvironment);
            string exeName = ExecutableName(app, platform);
            List<string> candidates = new List<string>();
            string? overridePath = NonEmpty(getEnvironment(app.OverrideVariable));
            if (overridePath != null)
            {
                candidates.Add(overridePath);
                candidates.Add(Path.Combine(overridePath, exeName));
            }

            if (!string.IsNullOrWhiteSpace(baseDirectory)) candidates.Add(Path.Combine(baseDirectory, exeName));
            candidates.AddRange(PreferSameInstall(InstalledLocations(app, platform, home, getEnvironment), baseDirectory));
            return candidates;
        }

        /// <summary>
        /// The mux version of an installed app, read from the marker the install scripts write: <c>mux-version.txt</c>
        /// beside the executable (Linux, WSL, Windows) or <c>CFBundleShortVersionString</c> in the bundle's
        /// <c>Info.plist</c> (macOS). Null when there is no marker (for example a build from source).
        /// </summary>
        /// <param name="executable">The executable.</param>
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
        /// Finds the app's executable, or null when there is none (see the class remarks for the order).
        /// </summary>
        /// <param name="app">The app.</param>
        /// <returns>The full path, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="app"/> is null.</exception>
        public static string? Locate(InstalledApp app)
        {
            ArgumentNullException.ThrowIfNull(app);
            string? baseDirectory = null;
            try
            {
                baseDirectory = AppContext.BaseDirectory;
            }
            catch (Exception)
            {
            }

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (string candidate in Candidates(app, CurrentPlatform(), home, Environment.GetEnvironmentVariable, baseDirectory))
            {
                try
                {
                    if (File.Exists(candidate)) return candidate;
                }
                catch (Exception)
                {
                }
            }

            return LocateInCheckout(Environment.CurrentDirectory, app.ProcessName, ExecutableName(app));
        }

        #endregion

        #region Private-Methods

        // The newest build under src/<project>/bin in a checkout at or above the working directory.
        private static string? LocateInCheckout(string workingDirectory, string project, string exeName)
        {
            try
            {
                DirectoryInfo? dir = new DirectoryInfo(workingDirectory);
                for (int depth = 0; dir != null && depth < 8; depth++, dir = dir.Parent)
                {
                    string projectDir = Path.Combine(dir.FullName, "src", project);
                    if (!Directory.Exists(projectDir)) continue;
                    string binDir = Path.Combine(projectDir, "bin");
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
        private static string InstallRoot(string executable)
        {
            string path = Normalize(executable);
            int bundle = path.IndexOf(".app/", StringComparison.Ordinal);
            if (bundle >= 0)
            {
                int parent = path.LastIndexOf('/', bundle);
                if (parent >= 0) return path.Substring(0, parent + 1);
            }

            string? appDirectory = Path.GetDirectoryName(path.Replace('/', Path.DirectorySeparatorChar));
            string? root = appDirectory == null ? null : Path.GetDirectoryName(appDirectory);
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
