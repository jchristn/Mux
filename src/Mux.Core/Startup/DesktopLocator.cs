namespace Mux.Core.Startup
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Finds the mux desktop app (Mux.Desktop) so the tray agent's "Launch Desktop" can start it. The search order is
    /// <see cref="InstalledAppLocator"/>'s: <c>MUX_DESKTOP</c> (the executable or its folder), beside the running
    /// program, the installed copy from the same install as the running program, then per-user and system-wide
    /// installs (macOS <c>~/Applications/Mux.app</c> and <c>/Applications/Mux.app</c>; Linux and WSL
    /// <c>${XDG_DATA_HOME:-~/.local/share}/mux/apps/desktop</c> and <c>/opt/mux/desktop</c>; Windows
    /// <c>%LOCALAPPDATA%\Programs\Mux\Desktop</c> and <c>%ProgramFiles%\Mux\Desktop</c>), then a source checkout.
    /// </summary>
    public static class DesktopLocator
    {
        #region Public-Members

        /// <summary>The desktop app's process name.</summary>
        public const string ProcessName = "Mux.Desktop";

        /// <summary>The environment variable that overrides the search with a path or directory.</summary>
        public const string OverrideVariable = "MUX_DESKTOP";

        #endregion

        #region Public-Methods

        /// <summary>The desktop executable's file name on this OS.</summary>
        /// <returns><c>Mux.Desktop.exe</c> on Windows, otherwise <c>Mux.Desktop</c>.</returns>
        public static string ExecutableName()
        {
            return InstalledAppLocator.ExecutableName(InstalledApp.Desktop);
        }

        /// <summary>The installed desktop locations for a platform, per-user first, then system-wide. Exposed for tests.</summary>
        /// <param name="platform">"macos", "linux" (also WSL), or "windows".</param>
        /// <param name="home">The user's home directory.</param>
        /// <param name="getEnvironment">Reads an environment variable (null when unset).</param>
        /// <returns>Full paths to where an installed desktop executable would be.</returns>
        public static IReadOnlyList<string> InstalledLocations(string platform, string home, Func<string, string?> getEnvironment)
        {
            return InstalledAppLocator.InstalledLocations(InstalledApp.Desktop, platform, home, getEnvironment);
        }

        /// <summary>Every path the search tries before a source checkout, in order. Exposed for tests.</summary>
        /// <param name="platform">"macos", "linux" (also WSL), or "windows".</param>
        /// <param name="home">The user's home directory.</param>
        /// <param name="getEnvironment">Reads an environment variable (null when unset).</param>
        /// <param name="baseDirectory">The running program's directory, or null.</param>
        /// <returns>Candidate executable paths, first match wins.</returns>
        public static IReadOnlyList<string> Candidates(string platform, string home, Func<string, string?> getEnvironment, string? baseDirectory)
        {
            return InstalledAppLocator.Candidates(InstalledApp.Desktop, platform, home, getEnvironment, baseDirectory);
        }

        /// <summary>Finds the desktop executable, or null when there is none.</summary>
        /// <returns>The full path, or null.</returns>
        public static string? Locate()
        {
            return InstalledAppLocator.Locate(InstalledApp.Desktop);
        }

        /// <summary>
        /// The macOS <c>.app</c> bundle an executable runs from (<c>/Applications/Mux.app</c> for
        /// <c>/Applications/Mux.app/Contents/MacOS/Mux.Desktop</c>), so it can be started with <c>open -a</c> as a
        /// proper app; null when the executable is not inside a bundle.
        /// </summary>
        /// <param name="executable">The executable path.</param>
        /// <returns>The bundle path, or null.</returns>
        public static string? AppBundle(string? executable)
        {
            if (string.IsNullOrWhiteSpace(executable)) return null;
            string path = executable.Replace('\\', '/');
            int marker = path.LastIndexOf(".app/Contents/MacOS/", StringComparison.Ordinal);
            if (marker < 0) return null;
            return executable.Substring(0, marker + 4);
        }

        #endregion
    }
}
