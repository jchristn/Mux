namespace Mux.Agent
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using Mux.Core.Models;
    using Mux.Core.Sessions;
    using Mux.Core.Hosting;
    using Mux.Core.Settings;
    using Mux.Server;

    /// <summary>
    /// Owns the background <see cref="MuxServer"/> for the tray agent and the "Launch Mux" action. The tray
    /// agent is itself the opt-in: launching it starts the server (loopback, token-guarded).
    /// </summary>
    public sealed class AgentHost
    {
        private MuxServer? _Server;
        private Mux.Core.Telemetry.UsageTelemetry? _Telemetry;

        /// <summary>
        /// The base URL the server is listening on, or an empty string before start.
        /// </summary>
        public string BaseUrl { get; private set; } = string.Empty;

        /// <summary>
        /// Whether the background REST server is currently running.
        /// </summary>
        public bool IsRunning
        {
            get => _Server != null && !string.IsNullOrEmpty(BaseUrl);
        }

        /// <summary>
        /// Ensures the background REST server is running, starting it if it is not (for example when the
        /// initial start failed to bind). Idempotent and best-effort; never throws.
        /// </summary>
        public void EnsureStarted()
        {
            if (IsRunning)
            {
                return;
            }

            // Clear any half-started server before retrying so a new bind does not race a stale instance.
            Stop();
            try
            {
                Start();
            }
            catch (Exception)
            {
                // Best-effort; the tray stays up even if the server cannot bind.
            }
        }

        /// <summary>
        /// Ensures the server is serving (starting it if needed) and opens the web dashboard in the default
        /// browser. Best-effort; never throws.
        /// </summary>
        public void LaunchDashboard()
        {
            EnsureStarted();

            // Prefer the URL this agent bound. If binding failed because another process (a standalone
            // `mux serve`) already owns the port, the dashboard is still being served there — fall back to
            // the configured host/port so the browser opens the right place either way.
            string url = !string.IsNullOrEmpty(BaseUrl) ? BaseUrl : ConfiguredBaseUrl();
            if (!string.IsNullOrEmpty(url))
            {
                OpenUrl(url.TrimEnd('/') + "/dashboard");
            }
        }

        private static string ConfiguredBaseUrl()
        {
            try
            {
                RestServerSettings rest = SettingsLoader.LoadSettings().Rest;
                string scheme = rest.Ssl ? "https" : "http";
                return scheme + "://" + rest.Hostname + ":" + rest.Port;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Opens a URL in the operating system's default browser. Best-effort; never throws.
        /// </summary>
        /// <param name="url">The URL to open. Ignored when null or blank.</param>
        public static void OpenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            try
            {
                if (OperatingSystem.IsWindows())
                {
                    Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
                }
                else if (OperatingSystem.IsMacOS())
                {
                    Process.Start(new ProcessStartInfo { FileName = "open", Arguments = url, UseShellExecute = false });
                }
                else
                {
                    Process.Start(new ProcessStartInfo { FileName = "xdg-open", Arguments = url, UseShellExecute = false });
                }
            }
            catch (Exception)
            {
                // Best-effort; nothing to do if no browser handler is available.
            }
        }

        /// <summary>
        /// Start the background REST server.
        /// </summary>
        public void Start()
        {
            MuxSettings settings = SettingsLoader.LoadSettings();
            RestServerSettings rest = settings.Rest;

            string? key = rest.ApiKey;
            if (string.IsNullOrWhiteSpace(key))
            {
                key = "mux_" + Guid.NewGuid().ToString("N");
                rest.ApiKey = key;
                try { SettingsLoader.SaveSettings(settings); } catch (Exception) { }
            }
            else
            {
                key = SettingsLoader.ExpandEnvironmentVariables(key);
            }
            rest.ApiKey = key;

            string sessionsDir = Path.Combine(SettingsLoader.GetConfigDirectory(), "sessions");

            // Open the shared usage-telemetry store and hand the server a query service (so the dashboard's
            // charts and KPIs are populated) and a recorder (so the agent's own chat calls are captured).
            // Without this the agent served an empty dashboard even though `mux serve` did not. Best-effort:
            // a disabled/unopenable store yields empty data and a no-op recorder.
            _Telemetry?.Dispose();
            Mux.Hosting.MuxObservabilityHost? observability = Mux.Hosting.MuxObservabilityHost.Current;
            _Telemetry = Mux.Core.Telemetry.UsageTelemetry.Create(settings, SettingsLoader.GetConfigDirectory(), observability?.CreateLogSink("Mux.UsageTelemetry"));

            SessionStore agentSessionStore = new SessionStore(sessionsDir);

            _Server = new MuxServer(
                rest,
                Defaults.ProductVersion,
                agentSessionStore,
                () => SettingsLoader.LoadEndpoints(),
                logger: observability?.CreateLogSink("Mux.Server"),
                usageQuery: _Telemetry.CreateQueryService(
                    () => SettingsLoader.LoadPricing(),
                    new Mux.Core.Telemetry.SessionStoreMetadataIndex(agentSessionStore)),
                usageRecorder: _Telemetry.Recorder);

            _Server.Start();
            BaseUrl = _Server.BaseUrl;
        }

        /// <summary>
        /// Launch the interactive mux terminal UI (the TUI) in a new terminal window. Best-effort; never throws. The
        /// tray usually runs with a minimal PATH (macOS gives Dock and login-item apps only the system folders), so the
        /// CLI is located explicitly (MUX_CLI, PATH, ~/.dotnet/tools, beside the agent, or a checkout build) and the
        /// window starts a login shell. When no CLI is found, the window explains how to install it.
        /// </summary>
        public void LaunchTerminal()
        {
            string workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            MuxCliLocation? location = MuxCliLocator.Locate();
            try
            {
                if (OperatingSystem.IsMacOS())
                {
                    // Terminal runs a .command file in a new window; unlike AppleScript this needs no automation
                    // permission. The script deletes itself once it starts.
                    string script = Path.Combine(Path.GetTempPath(), "mux-terminal-" + Guid.NewGuid().ToString("N") + ".command");
                    File.WriteAllText(script, TerminalLaunchPlanner.BuildMacCommandFile(location, workingDirectory));
                    File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                    ProcessStartInfo open = new ProcessStartInfo { FileName = "/usr/bin/open", UseShellExecute = false };
                    open.ArgumentList.Add("-a");
                    open.ArgumentList.Add("Terminal");
                    open.ArgumentList.Add(script);
                    Process.Start(open);
                    return;
                }

                if (OperatingSystem.IsWindows())
                {
                    TerminalCommand windows = TerminalLaunchPlanner.BuildWindowsCommand(location, workingDirectory);
                    Process.Start(new ProcessStartInfo { FileName = windows.FileName, Arguments = windows.RawArguments ?? string.Empty, UseShellExecute = false, CreateNoWindow = true });
                    return;
                }

                foreach (TerminalCommand candidate in TerminalLaunchPlanner.BuildLinuxCommands(location, workingDirectory))
                {
                    try
                    {
                        ProcessStartInfo start = new ProcessStartInfo { FileName = candidate.FileName, UseShellExecute = false };
                        foreach (string argument in candidate.Arguments)
                        {
                            start.ArgumentList.Add(argument);
                        }

                        Process.Start(start);
                        return;
                    }
                    catch (Exception)
                    {
                        // Not installed; try the next emulator.
                    }
                }
            }
            catch (Exception)
            {
                // Best effort; the tray stays up either way.
            }
        }

        /// <summary>
        /// Launch the mux desktop application as an independent process. Best-effort; never throws. Locates
        /// the desktop executable via the <c>MUX_DESKTOP</c> environment variable (a full path to the
        /// executable or the directory containing it), then a copy alongside the running agent, then the
        /// newest built <c>Mux.Desktop</c> under a repository checkout's <c>src/Mux.Desktop/bin</c>.
        /// </summary>
        public void LaunchDesktop()
        {
            try
            {
                string? executable = LocateDesktopExecutable();
                if (executable == null)
                {
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = executable,
                    UseShellExecute = true, // detach as an independent GUI process, not a child console
                    WorkingDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory
                });
            }
            catch (Exception)
            {
                // Best-effort; the tray stays up either way.
            }
        }

        private static string? LocateDesktopExecutable()
        {
            string exeName = OperatingSystem.IsWindows() ? "Mux.Desktop.exe" : "Mux.Desktop";

            // 1) Explicit override: a full path to the executable, or a directory containing it.
            string? overridePath = Environment.GetEnvironmentVariable("MUX_DESKTOP");
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

            // 2) Alongside the running agent binary (covers a side-by-side install).
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
            //    src/Mux.Desktop) and take the newest built desktop executable under its bin output.
            try
            {
                DirectoryInfo? dir = new DirectoryInfo(Environment.CurrentDirectory);
                for (int depth = 0; dir != null && depth < 8; depth++, dir = dir.Parent)
                {
                    string desktopDir = Path.Combine(dir.FullName, "src", "Mux.Desktop");
                    if (!Directory.Exists(desktopDir))
                    {
                        continue;
                    }

                    string binDir = Path.Combine(desktopDir, "bin");
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

        /// <summary>
        /// Stop the background server.
        /// </summary>
        public void Stop()
        {
            try
            {
                _Server?.Stop();
                _Server?.Dispose();
            }
            catch (Exception)
            {
                // Best-effort shutdown.
            }
            _Server = null;

            try
            {
                _Telemetry?.Dispose(); // flush and close the usage store
            }
            catch (Exception)
            {
                // Best-effort shutdown.
            }
            _Telemetry = null;
        }
    }
}
