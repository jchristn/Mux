namespace Mux.Desktop
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net.Http;
    using System.Text.Json;

    /// <summary>
    /// Best-effort launcher that starts the mux tray agent — which hosts the background REST server, usage
    /// telemetry, and the web dashboard — when the desktop app starts and no agent is already running. It
    /// never throws and never blocks startup: if an agent is already up, or its executable cannot be located,
    /// it quietly does nothing. Mirrors the interactive TUI's launcher so both front ends bring the tray
    /// agent up the same way.
    ///
    /// Resolution order for the agent executable: the <c>MUX_AGENT</c> environment variable (a full path to
    /// the executable or the directory containing it), then a copy sitting next to the running binary, then
    /// the newest built <c>Mux.Agent</c> under a repository checkout's <c>src/Mux.Agent/bin</c> found by
    /// walking up from the working directory. Set <c>MUX_AGENT_AUTOSTART=0</c> to opt out entirely.
    /// </summary>
    public static class AgentLauncher
    {
        private const string ProcessName = "Mux.Agent";

        /// <summary>
        /// Starts the tray agent if it is not already running. Best-effort and safe to call unconditionally;
        /// swallows every failure so it can never disrupt desktop startup.
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
                    // A tray agent is already running. Keep it only when it is this build — an older agent has
                    // a stale WebSocket bridge and dashboard and silently breaks cross-surface sync. Replace it
                    // only on a definitive version mismatch; if we cannot determine the version (starting up, or
                    // an unrelated same-named process), leave it alone rather than risk killing a healthy one.
                    string? version = ProbeAgentVersion();
                    if (version == null || string.Equals(version, Mux.Core.Settings.Defaults.ProductVersion, StringComparison.Ordinal))
                    {
                        return;
                    }

                    KillAgents();
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
                // Best-effort: the desktop app is fully functional without the background agent.
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

        // Probes the configured hub's health endpoint for the running agent's product version. Returns null
        // when it cannot be determined (unreachable, non-2xx, or unparseable) — the caller treats null as
        // "leave the agent alone".
        private static string? ProbeAgentVersion()
        {
            try
            {
                Mux.Core.Models.RestServerSettings rest = Mux.Core.Settings.SettingsLoader.LoadSettings().Rest;
                string url = (rest.Ssl ? "https" : "http") + "://" + rest.Hostname + ":" + rest.Port + "/v1.0/api/health";
                using HttpClient http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                HttpResponseMessage response = http.GetAsync(url).GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                using JsonDocument doc = JsonDocument.Parse(body);
                return doc.RootElement.TryGetProperty("Version", out JsonElement v) ? v.GetString() : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Stops all running tray-agent processes (used to replace a stale one). Best-effort.
        private static void KillAgents()
        {
            try
            {
                foreach (Process process in Process.GetProcessesByName(ProcessName))
                {
                    try
                    {
                        process.Kill(true);
                        process.WaitForExit(3000);
                    }
                    catch (Exception)
                    {
                        // Best-effort per process.
                    }
                }
            }
            catch (Exception)
            {
                // Best-effort.
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

            // 2) Alongside the running binary (covers a future side-by-side install).
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
