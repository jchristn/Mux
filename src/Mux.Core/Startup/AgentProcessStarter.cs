namespace Mux.Core.Startup
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using Mux.Core.Settings;

    /// <summary>
    /// Starts the mux tray agent as a fully detached background process. On Windows the shell launches it as
    /// an independent GUI process. On macOS and Linux (including WSL), <see cref="ProcessStartInfo.UseShellExecute"/>
    /// does not detach: the child inherits the caller's terminal, so anything the agent or its UI toolkit
    /// writes (for example Avalonia's X11 backend failing to load <c>libICE.so.6</c>/<c>libSM.so.6</c> under
    /// WSLg) lands on top of the TUI. There the agent is started through <c>/bin/sh</c> in its own session
    /// (via <c>setsid</c> when available), with stdin from <c>/dev/null</c> and stdout/stderr appended to
    /// <see cref="LogFileName"/> in the mux config directory.
    /// </summary>
    public static class AgentProcessStarter
    {
        /// <summary>The file (under the config directory) that receives the agent's stdout/stderr on Unix.</summary>
        public const string LogFileName = "agent.log";

        // $0 = agent executable, $1 = log file. Run in a new session when setsid exists so the agent never
        // shares the terminal's foreground process group (and is not signalled when the terminal closes).
        private const string UnixLaunchScript =
            "if command -v setsid >/dev/null 2>&1; then exec setsid \"$0\" </dev/null >>\"$1\" 2>&1; "
            + "else exec \"$0\" </dev/null >>\"$1\" 2>&1; fi";

        /// <summary>
        /// Starts the agent executable detached from the calling process's console. Throws on failure; callers
        /// are expected to treat the launch as best-effort.
        /// </summary>
        /// <param name="executable">Absolute path to the agent executable.</param>
        public static void Start(string executable)
        {
            if (string.IsNullOrWhiteSpace(executable)) throw new ArgumentNullException(nameof(executable));

            string workingDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory;

            if (OperatingSystem.IsWindows())
            {
                using Process? started = Process.Start(new ProcessStartInfo
                {
                    FileName = executable,
                    UseShellExecute = true, // independent GUI process, not a child of the console
                    WorkingDirectory = workingDirectory
                });
                return;
            }

            ProcessStartInfo info = new ProcessStartInfo("/bin/sh")
            {
                UseShellExecute = false,
                WorkingDirectory = workingDirectory
            };
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add(UnixLaunchScript);
            info.ArgumentList.Add(executable);
            info.ArgumentList.Add(ResolveLogPath());

            using Process? process = Process.Start(info);
        }

        private static string ResolveLogPath()
        {
            try
            {
                string directory = SettingsLoader.GetConfigDirectory();
                Directory.CreateDirectory(directory);
                return Path.Combine(directory, LogFileName);
            }
            catch (Exception)
            {
                return "/dev/null";
            }
        }
    }
}
