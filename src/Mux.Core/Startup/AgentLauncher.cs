namespace Mux.Core.Startup
{
    using System;
    using System.Diagnostics;
    using System.Net.Http;
    using System.Text.Json;
    using Mux.Core.Settings;

    /// <summary>
    /// Starts the mux tray agent, which hosts the background REST server, usage telemetry, and the web dashboard.
    /// Every user surface calls it: the terminal UI and <c>mux mirror</c> through <see cref="EnsureRunning"/>, the
    /// desktop app the same way, and the VS Code extension through <c>mux agent start</c> (<see cref="Start"/>).
    /// A running agent from this build is left alone; one from a different version is replaced, because an older
    /// agent serves a stale WebSocket bridge and dashboard. The executable is found by <see cref="AgentLocator"/>.
    /// Set <c>MUX_AGENT_AUTOSTART=0</c> to stop surfaces from starting it automatically.
    /// </summary>
    public static class AgentLauncher
    {
        #region Public-Members

        /// <summary>The environment variable that turns automatic starts off (0, false, no, or off).</summary>
        public const string AutostartVariable = "MUX_AGENT_AUTOSTART";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Starts the agent unless it is already running or automatic starts are turned off. Best-effort: never
        /// throws and never blocks for long, so it is safe to call at the start of any surface.
        /// </summary>
        public static void EnsureRunning()
        {
            try
            {
                if (!AutostartEnabled(Environment.GetEnvironmentVariable(AutostartVariable)))
                {
                    return;
                }

                Start(out _);
            }
            catch (Exception)
            {
                // Best-effort: every surface works without the background agent.
            }
        }

        /// <summary>
        /// Starts the agent now, regardless of <c>MUX_AGENT_AUTOSTART</c> (the explicit <c>mux agent start</c>).
        /// </summary>
        /// <param name="executable">The agent executable that was started or found, or null when none was found.</param>
        /// <returns>What happened.</returns>
        public static AgentStartResult Start(out string? executable)
        {
            executable = null;
            if (IsRunning())
            {
                // Keep a running agent unless it is definitely another version; if the version cannot be read
                // (it is starting, or an unrelated process has the same name), leave it alone.
                string? version = ProbeAgentVersion();
                if (version == null || string.Equals(version, Defaults.ProductVersion, StringComparison.Ordinal))
                {
                    return AgentStartResult.AlreadyRunning;
                }

                // Replace it only with a copy of this version: restarting the same stale installed copy would just
                // stop and start the old agent. A copy without a version marker (a build from source) is trusted.
                executable = AgentLocator.Locate();
                if (!ShouldReplace(version, AgentLocator.InstalledVersion(executable)))
                {
                    return AgentStartResult.AlreadyRunning;
                }

                StopAgents();
            }

            executable ??= AgentLocator.Locate();
            if (executable == null)
            {
                return AgentStartResult.NotFound;
            }

            try
            {
                // Detached, with the agent's console output kept off the caller's terminal.
                AgentProcessStarter.Start(executable);
                return AgentStartResult.Started;
            }
            catch (Exception)
            {
                return AgentStartResult.Failed;
            }
        }

        /// <summary>True when a tray agent process is running (or processes cannot be listed).</summary>
        /// <returns>Whether an agent is running.</returns>
        public static bool IsRunning()
        {
            try
            {
                return Process.GetProcessesByName(AgentLocator.ProcessName).Length > 0;
            }
            catch (Exception)
            {
                // If processes cannot be listed, assume it is running so a duplicate is never started.
                return true;
            }
        }

        /// <summary>
        /// Whether to replace a running agent of <paramref name="runningVersion"/> with the located copy of
        /// <paramref name="locatedVersion"/>: only when the running one is another version than this build and the
        /// located copy is this build's version, or has no version marker (a build from source).
        /// </summary>
        /// <param name="runningVersion">The running agent's version, or null when unknown.</param>
        /// <param name="locatedVersion">The located copy's version marker, or null when it has none.</param>
        /// <returns>True to stop the running agent and start the located copy.</returns>
        public static bool ShouldReplace(string? runningVersion, string? locatedVersion)
        {
            if (runningVersion == null || string.Equals(runningVersion, Defaults.ProductVersion, StringComparison.Ordinal))
            {
                return false;
            }

            return locatedVersion == null || string.Equals(locatedVersion, Defaults.ProductVersion, StringComparison.Ordinal);
        }

        /// <summary>
        /// Whether a <c>MUX_AGENT_AUTOSTART</c> value allows automatic starts: anything except 0, false, no, or off
        /// (case-insensitive); unset allows them.
        /// </summary>
        /// <param name="value">The variable's value, or null.</param>
        /// <returns>True when automatic starts are allowed.</returns>
        public static bool AutostartEnabled(string? value)
        {
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

        #endregion

        #region Private-Methods

        // The running agent's product version from the configured server's health endpoint, or null when it
        // cannot be determined (unreachable, non-2xx, or unparseable).
        private static string? ProbeAgentVersion()
        {
            try
            {
                Mux.Core.Models.RestServerSettings rest = SettingsLoader.LoadSettings().Rest;
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

        // Stops every running agent process (to replace one from another version). Best-effort.
        private static void StopAgents()
        {
            try
            {
                foreach (Process process in Process.GetProcessesByName(AgentLocator.ProcessName))
                {
                    try
                    {
                        process.Kill(true);
                        process.WaitForExit(3000);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        #endregion
    }
}
