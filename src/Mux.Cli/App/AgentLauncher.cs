namespace Mux.Cli.App
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;

    /// <summary>
    /// Best-effort launcher that starts the mux tray agent — which hosts the background REST server, usage
    /// telemetry, and the web dashboard — when the interactive TUI starts and no agent is already running.
    /// It never throws and never blocks startup: if an agent is already up, or its executable cannot be
    /// located, it quietly does nothing.
    ///
    /// Resolution order for the agent executable: the <c>MUX_AGENT</c> environment variable (a full path to
    /// the executable or the directory containing it), then a copy sitting next to the running mux binary,
    /// then the newest built <c>Mux.Agent</c> under a repository checkout's <c>src/Mux.Agent/bin</c> found by
    /// walking up from the working directory (the common developer layout, since mux installs as a global
    /// tool while the agent does not). Set <c>MUX_AGENT_AUTOSTART=0</c> to opt out entirely.
    /// </summary>
    public static class AgentLauncher
    {
        private const string ProcessName = "Mux.Agent";

        /// <summary>
        /// Starts the tray agent if it is not already running. Best-effort and safe to call unconditionally;
        /// swallows every failure so it can never disrupt TUI startup.
        /// </summary>
        public static void EnsureRunning()
        {
            try
            {
                if (!AutostartEnabled())
                {
                    return;
                }

                if (IsRunning())
                {
                    return;
                }

                string? executable = LocateAgentExecutable();
                if (executable == null)
                {
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = executable,
                    UseShellExecute = true, // detach as an independent tray/GUI process, not a child console
                    WorkingDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory
                });
            }
            catch (Exception)
            {
                // Best-effort: the TUI is fully functional without the background agent.
            }
        }

        private static bool AutostartEnabled()
        {
            string? value = Environment.GetEnvironmentVariable("MUX_AGENT_AUTOSTART");
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }

            value = value.Trim();
            return !(value == "0"
                || value.Equals("false", StringComparison.OrdinalIgnoreCase)
                || value.Equals("no", StringComparison.OrdinalIgnoreCase)
                || value.Equals("off", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsRunning()
        {
            try
            {
                return Process.GetProcessesByName(ProcessName).Length > 0;
            }
            catch (Exception)
            {
                // If we cannot enumerate processes, assume it is running so we never spawn a duplicate.
                return true;
            }
        }

        private static string? LocateAgentExecutable()
        {
            string exeName = OperatingSystem.IsWindows() ? "Mux.Agent.exe" : "Mux.Agent";

            // 1) Explicit override: a full path to the executable, or a directory containing it.
            string? overridePath = Environment.GetEnvironmentVariable("MUX_AGENT");
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                if (File.Exists(overridePath))
                {
                    return overridePath;
                }

                string inDir = Path.Combine(overridePath, exeName);
                if (File.Exists(inDir))
                {
                    return inDir;
                }
            }

            // 2) Alongside the running mux binary (covers a future side-by-side install).
            try
            {
                string beside = Path.Combine(AppContext.BaseDirectory, exeName);
                if (File.Exists(beside))
                {
                    return beside;
                }
            }
            catch (Exception)
            {
            }

            // 3) Developer layout: walk up from the working directory to a checkout (a directory containing
            //    src/Mux.Agent) and take the newest built agent under its bin output.
            try
            {
                DirectoryInfo? dir = new DirectoryInfo(Environment.CurrentDirectory);
                for (int depth = 0; dir != null && depth < 8; depth++, dir = dir.Parent)
                {
                    string agentDir = Path.Combine(dir.FullName, "src", "Mux.Agent");
                    if (!Directory.Exists(agentDir))
                    {
                        continue;
                    }

                    string binDir = Path.Combine(agentDir, "bin");
                    if (!Directory.Exists(binDir))
                    {
                        break; // found the checkout, but nothing is built
                    }

                    FileInfo? newest = new DirectoryInfo(binDir)
                        .EnumerateFiles(exeName, SearchOption.AllDirectories)
                        .OrderByDescending(f => f.LastWriteTimeUtc)
                        .FirstOrDefault();
                    return newest?.FullName;
                }
            }
            catch (Exception)
            {
            }

            return null;
        }
    }
}
