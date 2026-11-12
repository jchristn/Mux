namespace Mux.Core.Startup
{
    using System;
    using System.Collections.Generic;

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
    /// The search itself is <see cref="InstalledAppLocator"/>, shared with <see cref="DesktopLocator"/>.
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
            return InstalledAppLocator.ExecutableName(InstalledApp.Agent);
        }

        /// <summary>
        /// The installed-agent locations for this OS, per-user first, then system-wide.
        /// </summary>
        /// <returns>Full paths to where an installed agent executable would be.</returns>
        public static IReadOnlyList<string> InstalledLocations()
        {
            return InstalledAppLocator.InstalledLocations(InstalledApp.Agent);
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
            return InstalledAppLocator.InstalledLocations(InstalledApp.Agent, platform, home, getEnvironment);
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
            return InstalledAppLocator.PreferSameInstall(installed, baseDirectory);
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
            return InstalledAppLocator.InstalledVersion(executable);
        }

        /// <summary>
        /// Finds the agent executable, or null when there is none (see the class remarks for the order).
        /// </summary>
        /// <returns>The full path, or null.</returns>
        public static string? Locate()
        {
            return InstalledAppLocator.Locate(InstalledApp.Agent);
        }

        #endregion
    }
}
