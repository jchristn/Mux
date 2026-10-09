namespace Mux.Cli.Commands
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.McpServer;
    using Mux.Core.Models;
    using Mux.Core.Processes;
    using Mux.Core.Sessions;
    using Mux.Core.Settings;
    using Mux.Core.Skills;
    using Voltaic.Mcp;

    /// <summary>
    /// Implements <c>mux mcp serve</c>: exposes mux to other agents and editors as an MCP server, over stdio (the
    /// default, for clients that launch it) or Streamable HTTP (<c>--http &lt;port&gt;</c>). Status lines go to stderr;
    /// stdout carries only the protocol.
    /// </summary>
    public sealed class McpServeCommand
    {
        #region Public-Members

        /// <summary>The usage text.</summary>
        public const string Usage = "Usage: mux mcp serve [--http <port>] [--host <name>] [--api-key <key>] [--allow-skills] [--approval-policy deny|auto-safe|auto] [--yolo] [--endpoint <name>] [--working-directory <dir>]";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parses the arguments after <c>mcp</c> into options.
        /// </summary>
        /// <param name="args">The arguments after the <c>mcp</c> verb, starting with <c>serve</c>.</param>
        /// <param name="parsed">The parsed options, or null on error.</param>
        /// <param name="error">Why parsing failed, or empty.</param>
        /// <returns>True when the arguments are valid.</returns>
        public static bool TryParse(string[] args, out McpServeArguments? parsed, out string error)
        {
            parsed = null;
            error = string.Empty;
            if (args == null || args.Length == 0 || !string.Equals(args[0], "serve", StringComparison.OrdinalIgnoreCase))
            {
                error = Usage;
                return false;
            }

            McpServeArguments result = new McpServeArguments();
            string? policy = null;
            for (int i = 1; i < args.Length; i++)
            {
                string arg = args[i];
                string? Next()
                {
                    if (i + 1 >= args.Length) return null;
                    i++;
                    return args[i];
                }

                switch (arg)
                {
                    case "--http":
                        string? port = Next();
                        if (!int.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value < 1 || value > 65535)
                        {
                            error = "--http needs a port from 1 to 65535.";
                            return false;
                        }

                        result.HttpPort = value;
                        break;
                    case "--host":
                        result.Host = Next() ?? string.Empty;
                        if (string.IsNullOrWhiteSpace(result.Host)) { error = "--host needs a host name."; return false; }
                        break;
                    case "--api-key":
                        result.ApiKey = Next();
                        if (string.IsNullOrWhiteSpace(result.ApiKey)) { error = "--api-key needs a value."; return false; }
                        break;
                    case "--allow-skills":
                        result.AllowSkills = true;
                        break;
                    case "--approval-policy":
                        policy = Next();
                        if (string.IsNullOrWhiteSpace(policy)) { error = "--approval-policy needs deny, auto-safe, or auto."; return false; }
                        break;
                    case "--yolo":
                        policy = "auto";
                        break;
                    case "-e":
                    case "--endpoint":
                        result.Endpoint = Next();
                        if (string.IsNullOrWhiteSpace(result.Endpoint)) { error = "--endpoint needs a name."; return false; }
                        break;
                    case "-w":
                    case "--working-directory":
                        string? directory = Next();
                        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) { error = "--working-directory needs an existing directory."; return false; }
                        result.WorkingDirectory = Path.GetFullPath(directory);
                        break;
                    case "--config-dir":
                        Next();
                        break;
                    default:
                        error = "Unknown option '" + arg + "'. " + Usage;
                        return false;
                }
            }

            try
            {
                result.MaxApprovalPolicy = MuxMcpTools.ResolvePolicy(policy, ApprovalPolicyEnum.AutoApprove);
                if (policy == null) result.MaxApprovalPolicy = ApprovalPolicyEnum.Deny;
            }
            catch (McpToolException ex)
            {
                error = "--approval-policy: " + ex.Message;
                return false;
            }

            parsed = result;
            return true;
        }

        /// <summary>
        /// Runs <c>mux mcp ...</c>.
        /// </summary>
        /// <param name="args">The arguments after <c>mcp</c>.</param>
        /// <param name="cancellationToken">Stops the server.</param>
        /// <returns>The exit code.</returns>
        public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
        {
            if (!TryParse(args, out McpServeArguments? parsed, out string error) || parsed == null)
            {
                Console.Error.WriteLine(error);
                return 2;
            }

            MuxSettings settings = SettingsLoader.LoadSettings();
            string configDirectory = SettingsLoader.GetConfigDirectory();
            string? apiKey = parsed.ApiKey ?? (string.IsNullOrWhiteSpace(settings.McpServeApiKey) ? null : settings.McpServeApiKey);
            MuxMcpServerOptions options = new MuxMcpServerOptions
            {
                ServerName = "mux",
                ServerVersion = Defaults.ProductVersion,
                AllowSkills = parsed.AllowSkills,
                MaxApprovalPolicy = parsed.MaxApprovalPolicy,
                DefaultEndpoint = parsed.Endpoint,
                DefaultWorkingDirectory = parsed.WorkingDirectory ?? Directory.GetCurrentDirectory(),
                SessionsDirectory = Path.Combine(configDirectory, "sessions")
            };

            SkillRuntime? skills = null;
            using CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            ConsoleCancelEventHandler onCancel = (object? sender, ConsoleCancelEventArgs e) => { e.Cancel = true; stop.Cancel(); };
            Console.CancelKeyPress += onCancel;
            try
            {
                if (settings.SkillsEnabled)
                {
                    skills = SkillRuntime.FromSettings(settings, () => { });
                    skills.Start();
                    await Task.WhenAny(skills.FirstRefreshCompleted, Task.Delay(TimeSpan.FromSeconds(10), stop.Token)).ConfigureAwait(false);
                }

                using BackgroundProcessRegistry processes = new BackgroundProcessRegistry(settings.BackgroundProcessMaxConcurrent, settings.BackgroundProcessOutputBytes);
                McpRunExecutor executor = new McpRunExecutor(configDirectory, skills, processes);
                MuxMcpTools tools = new MuxMcpTools(options, executor, () => SettingsLoader.LoadEndpoints(), new SessionStore(options.SessionsDirectory), skills);
                MuxMcpServerHost server = new MuxMcpServerHost(tools, options);

                Console.Error.WriteLine("mux MCP server: tools " + string.Join(", ", tools.ToolNames) + "; approval ceiling " + PolicyName(options.MaxApprovalPolicy) + ".");
                if (parsed.HttpPort.HasValue)
                {
                    string host = string.IsNullOrWhiteSpace(parsed.Host) ? "localhost" : parsed.Host;
                    if (!IsLoopback(host) && string.IsNullOrEmpty(apiKey))
                    {
                        Console.Error.WriteLine("Warning: serving on '" + host + "' without --api-key; anyone who can reach this port can run mux.");
                    }

                    Console.Error.WriteLine("Listening on http://" + host + ":" + parsed.HttpPort.Value + MuxMcpServerHost.HttpPath + (string.IsNullOrEmpty(apiKey) ? string.Empty : " (bearer key required)") + ". Press Ctrl+C to stop.");
                    await server.RunHttpAsync(host, parsed.HttpPort.Value, apiKey, stop.Token).ConfigureAwait(false);
                }
                else
                {
                    await server.RunStdioAsync(stop.Token).ConfigureAwait(false);
                }

                return 0;
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("mux mcp serve failed: " + ex.Message);
                return 1;
            }
            finally
            {
                Console.CancelKeyPress -= onCancel;
                skills?.Dispose();
            }
        }

        #endregion

        #region Private-Methods

        private static bool IsLoopback(string host)
        {
            return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
                || host.StartsWith("127.", StringComparison.Ordinal)
                || host == "::1"
                || host == "[::1]";
        }

        private static string PolicyName(ApprovalPolicyEnum policy)
        {
            return policy switch
            {
                ApprovalPolicyEnum.AutoApprove => "auto",
                ApprovalPolicyEnum.AutoSafe => "auto-safe",
                _ => "deny"
            };
        }

        #endregion
    }
}
