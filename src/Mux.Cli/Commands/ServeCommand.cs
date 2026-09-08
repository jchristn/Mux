namespace Mux.Cli.Commands
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Mux.Core.Sessions;
    using Mux.Server;

    /// <summary>
    /// Implements <c>mux serve</c>: starts the opt-in local REST + WebSocket server over mux's in-process
    /// services and blocks until interrupted. The server binds to loopback and requires a local API key
    /// unless <c>--no-auth</c> is passed.
    /// </summary>
    public sealed class ServeCommand
    {
        /// <summary>
        /// Run the server until Ctrl+C / SIGTERM.
        /// </summary>
        /// <param name="args">Arguments after the <c>serve</c> verb.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Process exit code.</returns>
        public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
        {
            MuxSettings settings = SettingsLoader.LoadSettings();
            RestServerSettings rest = settings.Rest;

            // Environment overrides, then CLI overrides (CLI wins).
            ApplyEnvOverrides(rest);
            bool noAuth = false;
            string? cliApiKey = null;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                switch (a)
                {
                    case "--host":
                        if (i + 1 < args.Length) rest.Hostname = args[++i];
                        break;
                    case "--port":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out int port)) rest.Port = port;
                        break;
                    case "--api-key":
                        if (i + 1 < args.Length) cliApiKey = args[++i];
                        break;
                    case "--no-auth":
                        noAuth = true;
                        break;
                    default:
                        break;
                }
            }

            // Resolve the effective API key: --no-auth disables it; an explicit key wins; a stored key is
            // expanded; a blank key is generated and persisted so the next run reuses it.
            string? apiKey;
            if (noAuth)
            {
                apiKey = null;
            }
            else if (!string.IsNullOrWhiteSpace(cliApiKey))
            {
                apiKey = cliApiKey;
            }
            else
            {
                string? stored = rest.ApiKey;
                if (!string.IsNullOrWhiteSpace(stored))
                {
                    apiKey = SettingsLoader.ExpandEnvironmentVariables(stored);
                }
                else
                {
                    apiKey = "mux_" + Guid.NewGuid().ToString("N");
                    rest.ApiKey = apiKey;
                    try { SettingsLoader.SaveSettings(settings); } catch (Exception) { }
                }
            }

            // Apply the resolved key back so MuxServer sees the effective value.
            rest.ApiKey = noAuth ? null : apiKey;

            string sessionsDir = Path.Combine(SettingsLoader.GetConfigDirectory(), "sessions");
            SessionStore sessionStore = new SessionStore(sessionsDir);

            using MuxServer server = new MuxServer(
                rest,
                Defaults.ProductVersion,
                sessionStore,
                () => SettingsLoader.LoadEndpoints(),
                logger: null);

            try
            {
                server.Start();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("mux serve: failed to start server: " + ex.Message);
                return 1;
            }

            Console.WriteLine();
            Console.WriteLine("mux server listening on " + server.BaseUrl);
            Console.WriteLine("  health:    " + server.BaseUrl + "/v1.0/api/health");
            Console.WriteLine("  websocket: " + server.BaseUrl.Replace("http", "ws") + "/v1.0/ws");
            if (noAuth)
            {
                Console.WriteLine("  auth:      disabled (--no-auth)");
            }
            else
            {
                Console.WriteLine("  api key:   " + apiKey);
                Console.WriteLine("             send as 'Authorization: Bearer <key>' or 'X-Api-Key: <key>'");
            }
            Console.WriteLine();
            Console.WriteLine("Press Ctrl+C to stop.");
            Console.WriteLine();

            using ManualResetEventSlim stopped = new ManualResetEventSlim(false);
            ConsoleCancelEventHandler handler = (sender, e) =>
            {
                e.Cancel = true;
                stopped.Set();
            };
            Console.CancelKeyPress += handler;

            try
            {
                await Task.Run(() => stopped.Wait(cancellationToken), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on cancellation.
            }
            finally
            {
                Console.CancelKeyPress -= handler;
                server.Stop();
            }

            Console.WriteLine("mux server stopped.");
            return 0;
        }

        private static void ApplyEnvOverrides(RestServerSettings rest)
        {
            string? host = Environment.GetEnvironmentVariable("MUX_REST_HOST");
            if (!string.IsNullOrWhiteSpace(host)) rest.Hostname = host;

            string? portValue = Environment.GetEnvironmentVariable("MUX_REST_PORT");
            if (!string.IsNullOrWhiteSpace(portValue) && int.TryParse(portValue, out int port)) rest.Port = port;

            string? apiKey = Environment.GetEnvironmentVariable("MUX_REST_APIKEY");
            if (!string.IsNullOrWhiteSpace(apiKey)) rest.ApiKey = apiKey;
        }
    }
}
