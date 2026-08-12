namespace Mux.Cli.Commands
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.App;
    using Mux.Cli.Rendering;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Sessions;
    using Mux.Core.Settings;
    using Mux.Core.Utility;

    /// <summary>
    /// Settings specific to the print (single-shot) command.
    /// </summary>
    public class PrintSettings : CommonSettings
    {
        #region Public-Members

        /// <summary>
        /// The prompt to send to the agent. If omitted, input is read from stdin.
        /// </summary>
        [Description("The prompt to send to the agent.")]
        [CommandArgument(0, "[prompt]")]
        public string? Prompt { get; set; }

        /// <summary>
        /// Flag indicating single-shot print mode (enables piping via <c>--print</c>).
        /// </summary>
        [Description("Run in single-shot print mode.")]
        [CommandOption("-p|--print")]
        public bool Print { get; set; }

        /// <summary>
        /// Optional file path that receives only the final assistant-visible response text.
        /// </summary>
        [Description("Write only the final assistant response text to the specified file.")]
        [CommandOption("--output-last-message")]
        public string? OutputLastMessage { get; set; }

        /// <summary>
        /// Resume a persisted session by its id or (failing that) its title, continuing its history.
        /// </summary>
        [Description("Resume a persisted session by id or title.")]
        [CommandOption("--resume")]
        public string? Resume { get; set; }

        /// <summary>
        /// Continue the most recently updated persisted session in the active config directory.
        /// </summary>
        [Description("Continue the most recently updated persisted session.")]
        [CommandOption("--continue")]
        public bool Continue { get; set; }

        /// <summary>
        /// Run under a specific session id, creating it if it does not exist.
        /// </summary>
        [Description("Run under a specific session id, creating it if absent.")]
        [CommandOption("--session-id")]
        public string? SessionId { get; set; }

        /// <summary>
        /// When resuming or continuing, persist the updated history under a new session id instead of
        /// overwriting the source session.
        /// </summary>
        [Description("Persist the resumed run under a new session id.")]
        [CommandOption("--fork-session")]
        public bool ForkSession { get; set; }

        /// <summary>
        /// Do not persist the session to disk for this run.
        /// </summary>
        [Description("Do not persist the session to disk for this run.")]
        [CommandOption("--no-session-persistence")]
        public bool NoSessionPersistence { get; set; }

        /// <summary>
        /// Path to a JSON Schema file the final response must conform to.
        /// </summary>
        [Description("Path to a JSON Schema file the final response must conform to.")]
        [CommandOption("--output-schema")]
        public string? OutputSchema { get; set; }

        #endregion
    }

    /// <summary>
    /// Single-shot command that runs a prompt through the agent and prints the result to stdout.
    /// Designed for use in pipelines and non-interactive contexts.
    /// </summary>
    public class PrintCommand : AsyncCommand<PrintSettings>
    {
        #region Public-Methods

        /// <summary>
        /// Executes the single-shot print command.
        /// </summary>
        /// <param name="context">The command context.</param>
        /// <param name="settings">The resolved command settings.</param>
        /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
        /// <returns>Exit code: 0 success, 1 error, 2 tool denied.</returns>
        public override async Task<int> ExecuteAsync(CommandContext context, PrintSettings settings, CancellationToken cancellationToken)
        {
            OutputFormatEnum outputFormat;
            try
            {
                outputFormat = CommandRuntimeResolver.ParseOutputFormat(settings.OutputFormat, OutputFormatEnum.Text, OutputFormatEnum.Json, OutputFormatEnum.Jsonl);
            }
            catch (Exception ex)
            {
                EmitBootstrapError(settings, ex.Message);
                return 1;
            }

            // Input format: text (default, one prompt) or jsonl (multi-turn records on stdin).
            bool jsonlInput;
            string normalizedInputFormat = (settings.InputFormat ?? "text").Trim().ToLowerInvariant();
            switch (normalizedInputFormat)
            {
                case "text":
                    jsonlInput = false;
                    break;
                case "jsonl":
                    jsonlInput = true;
                    break;
                default:
                    EmitBootstrapError(settings, $"Unsupported input format '{settings.InputFormat}'. Supported values: text, jsonl.");
                    return 1;
            }

            // In jsonl input mode the prompt stream is read from stdin (one record per line) inside the run
            // loop; the positional prompt is ignored. In text mode the single prompt comes from the argument
            // or stdin as before.
            string prompt = string.Empty;
            if (!jsonlInput)
            {
                prompt = ResolvePrompt(settings);
                if (string.IsNullOrWhiteSpace(prompt))
                {
                    EmitBootstrapError(settings, "No prompt provided. Pass a prompt argument or pipe input via stdin.");
                    return 1;
                }
            }

            // MCP is opt-in for print: supplying --mcp-config enables it (unless --no-mcp overrides). This
            // keeps a plain `mux print` hermetic while allowing scripted MCP tool use when asked for.
            bool mcpConfigSupplied = !string.IsNullOrWhiteSpace(settings.McpConfig);
            bool mcpRequested = mcpConfigSupplied && !settings.NoMcp;

            ResolvedRuntime runtime;
            try
            {
                runtime = CommandRuntimeResolver.ResolveRuntime(settings, "print", supportsMcp: mcpConfigSupplied, allowAskApproval: false);
            }
            catch (Exception ex)
            {
                EmitBootstrapError(settings, ex.Message);
                return 1;
            }

            string? outputLastMessagePath;
            try
            {
                outputLastMessagePath = PrepareOutputLastMessagePath(settings.OutputLastMessage);
            }
            catch (Exception ex)
            {
                EmitBootstrapError(settings, $"Invalid --output-last-message path: {ex.Message}");
                return 1;
            }

            // Resolve any requested session up front so the run can continue its history and so its id is
            // surfaced on the run events. Persistence is opt-in: a plain `mux print` remains stateless and
            // only a session flag (--resume / --continue / --session-id / --fork-session) engages the store.
            SessionStore? sessionStore = null;
            SessionSnapshot? loadedSnapshot = null;
            string sessionId = string.Empty;
            bool persistSession = false;
            bool sessionRequested = !string.IsNullOrWhiteSpace(settings.Resume)
                || settings.Continue
                || !string.IsNullOrWhiteSpace(settings.SessionId)
                || settings.ForkSession;

            if (sessionRequested)
            {
                sessionStore = new SessionStore();
                try
                {
                    loadedSnapshot = await ResolveSessionSnapshotAsync(sessionStore, settings, cancellationToken).ConfigureAwait(false);
                }
                catch (InvalidOperationException ex)
                {
                    EmitBootstrapError(settings, ex.Message);
                    return 1;
                }

                persistSession = !settings.NoSessionPersistence;
                sessionId = ResolveEffectiveSessionId(settings, loadedSnapshot);
            }

            // Load and validate the optional output schema up front, and fold its directive into the system
            // prompt so the model is told to conform. The response is checked against it after the run.
            string? outputSchemaJson = null;
            if (!string.IsNullOrWhiteSpace(settings.OutputSchema))
            {
                try
                {
                    string schemaPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(settings.OutputSchema!.Trim()));
                    outputSchemaJson = File.ReadAllText(schemaPath);
                }
                catch (Exception ex)
                {
                    EmitBootstrapError(settings, $"Invalid --output-schema path: {ex.Message}");
                    return 1;
                }

                if (!OutputSchemaValidator.IsValidSchemaDocument(outputSchemaJson, out string schemaError))
                {
                    EmitBootstrapError(settings, $"--output-schema is not valid JSON: {schemaError}");
                    return 1;
                }
            }

            string effectiveSystemPrompt = outputSchemaJson == null
                ? runtime.SystemPrompt
                : runtime.SystemPrompt + Environment.NewLine + Environment.NewLine + BuildSchemaDirective(outputSchemaJson);

            // Load MCP servers for the run (from --mcp-config, plus the config directory unless strict).
            List<McpServerConfig> mcpServers = new List<McpServerConfig>();
            if (mcpRequested)
            {
                try
                {
                    mcpServers = LoadMcpServersForPrint(settings);
                }
                catch (Exception ex)
                {
                    EmitBootstrapError(settings, $"Invalid --mcp-config: {ex.Message}");
                    return 1;
                }
            }

            AgentLoopOptions loopOptions = new AgentLoopOptions(runtime.Endpoint)
            {
                MuxSettings = runtime.MuxSettings,
                IgnoreCertErrors = runtime.MuxSettings.IgnoreCertErrors,
                SystemPrompt = effectiveSystemPrompt,
                ApprovalPolicy = runtime.ApprovalPolicy,
                WorkingDirectory = runtime.WorkingDirectory,
                SessionId = sessionId,
                MaxTokenBudget = runtime.MuxSettings.MaxTokenBudget,
                SandboxPosture = runtime.SandboxPosture,
                AllowedTools = runtime.AllowedTools,
                DeniedTools = runtime.DeniedTools,
                AdditionalDirectories = runtime.AdditionalDirectories,
                ConversationHistory = loadedSnapshot?.ConversationHistory ?? new List<ConversationMessage>(),
                MaxIterations = runtime.MaxAgentIterations,
                TokenEstimationRatio = runtime.MuxSettings.TokenEstimationRatio,
                ContextWindowSafetyMarginPercent = runtime.MuxSettings.ContextWindowSafetyMarginPercent,
                AutoCompactEnabled = runtime.MuxSettings.AutoCompactEnabled,
                ContextWarningThresholdPercent = runtime.MuxSettings.ContextWarningThresholdPercent,
                CompactionStrategy = runtime.MuxSettings.CompactionStrategy,
                CompactionPreserveTurns = runtime.MuxSettings.CompactionPreserveTurns,
                CommandName = runtime.Metadata.CommandName,
                ConfigDirectory = runtime.Metadata.ConfigDirectory,
                EndpointSelectionSource = runtime.Metadata.EndpointSelectionSource,
                CliOverridesApplied = runtime.Metadata.CliOverridesApplied,
                McpSupported = runtime.Capabilities.McpSupported,
                McpConfigured = runtime.Capabilities.McpConfigured,
                McpServerCount = runtime.Capabilities.McpServerCount,
                BuiltInToolCount = runtime.Capabilities.BuiltInToolCount,
                EffectiveToolCount = runtime.Capabilities.EffectiveToolCount,
                Verbose = settings.Verbose,
                TaskPlan = runtime.MuxSettings.TaskPlanningEnabled ? new Mux.Core.Tasks.TaskPlan() : null,
                OnRetry = (int attempt, int maxRetries, string message) =>
                    Console.Error.WriteLine(ConsoleMessageStyler.Notification($"Retry {attempt}/{maxRetries}: {message}"))
            };

            if (mcpRequested)
            {
                loopOptions.McpSupported = true;
                loopOptions.McpConfigured = mcpServers.Count > 0;
                loopOptions.McpServerCount = mcpServers.Count;
            }

            int exitCode = 0;
            string lastAssistantText = string.Empty;
            List<ConversationMessage> latestConversation = loadedSnapshot?.ConversationHistory ?? new List<ConversationMessage>();

            if (runtime.MuxSettings.IgnoreCertErrors)
            {
                Console.Error.WriteLine(ConsoleMessageStyler.Notification(
                    "Warning: TLS certificate validation is disabled for mux-owned network requests."));
            }

            McpRuntime? mcpRuntime = null;

            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                Console.CancelKeyPress += (object? sender, ConsoleCancelEventArgs e) =>
                {
                    e.Cancel = true;
                    cts.Cancel();
                };

                try
                {
                    // Connect MCP servers once for the invocation (shared across turns in jsonl input mode):
                    // start, wait (bounded) for the first tool discovery, expose the tools to the loop, and
                    // dispose in the finally so the connections do not outlive the run.
                    if (mcpRequested && mcpServers.Count > 0)
                    {
                        mcpRuntime = new McpRuntime(() => mcpServers, () => { }, TimeSpan.FromSeconds(30));
                        mcpRuntime.Start();
                        await Task.WhenAny(
                            mcpRuntime.FirstRefreshCompleted,
                            Task.Delay(TimeSpan.FromSeconds(30), cts.Token)).ConfigureAwait(false);

                        loopOptions.AdditionalTools = mcpRuntime.CurrentTools;
                        loopOptions.ExternalToolExecutor = mcpRuntime.ExecuteToolAsync;
                        loopOptions.EffectiveToolCount = runtime.Capabilities.EffectiveToolCount + mcpRuntime.CurrentTools.Count;
                    }

                    List<ConversationMessage> history = new List<ConversationMessage>(latestConversation);

                    if (jsonlInput)
                    {
                        // Multi-turn: each stdin line is one user turn against the accumulating history.
                        int turnCount = 0;
                        string? line;
                        while ((line = await Console.In.ReadLineAsync().ConfigureAwait(false)) != null)
                        {
                            if (string.IsNullOrWhiteSpace(line))
                            {
                                continue;
                            }

                            string turnPrompt;
                            try
                            {
                                turnPrompt = ExtractTurnPrompt(line);
                            }
                            catch (Exception ex)
                            {
                                EmitRuntimeError(
                                    outputFormat,
                                    CreateRuntimeError("invalid_input_record", ex.Message, runtime),
                                    runtime.MuxSettings.IgnoreCertErrors);
                                exitCode = 1;
                                continue;
                            }

                            if (string.IsNullOrWhiteSpace(turnPrompt))
                            {
                                continue;
                            }

                            turnCount++;
                            loopOptions.ConversationHistory = history;
                            PrintTurnResult turn = await RunTurnAsync(
                                turnPrompt, loopOptions, runtime, outputFormat, outputSchemaJson, sessionId, settings.Verbose, cts.Token).ConfigureAwait(false);

                            if (turn.ExitCode != 0)
                            {
                                exitCode = turn.ExitCode;
                            }

                            history = StripSystemMessages(turn.FinalConversation);
                            latestConversation = turn.FinalConversation;
                            lastAssistantText = turn.AssistantText;
                        }

                        if (turnCount == 0)
                        {
                            EmitBootstrapError(settings, "No input records were provided on stdin for --input-format jsonl.");
                            exitCode = 1;
                        }
                    }
                    else
                    {
                        loopOptions.ConversationHistory = history;
                        PrintTurnResult turn = await RunTurnAsync(
                            prompt, loopOptions, runtime, outputFormat, outputSchemaJson, sessionId, settings.Verbose, cts.Token).ConfigureAwait(false);
                        exitCode = turn.ExitCode;
                        latestConversation = turn.FinalConversation;
                        lastAssistantText = turn.AssistantText;
                    }

                    if (exitCode == 0 && !string.IsNullOrWhiteSpace(outputLastMessagePath))
                    {
                        WriteLastMessageArtifact(outputLastMessagePath!, lastAssistantText);
                    }

                    // Persist the resumable session (opt-in). Saved after the run(s) so the stored history is
                    // exactly what the loop produced; the leading system message is dropped because the loop
                    // re-adds it from the system prompt on the next resume.
                    if (persistSession && sessionStore != null && latestConversation.Count > 0)
                    {
                        await PersistSessionAsync(
                            sessionStore,
                            sessionId,
                            loadedSnapshot,
                            runtime,
                            jsonlInput ? "(multi-turn session)" : prompt,
                            latestConversation,
                            cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    EmitRuntimeError(
                        outputFormat,
                        CreateRuntimeError("cancelled", "Operation was cancelled.", runtime),
                        runtime.MuxSettings.IgnoreCertErrors);
                    exitCode = 1;
                }
                catch (Exception ex)
                {
                    string errorCode = ex is IOException || ex is UnauthorizedAccessException
                        ? "artifact_write_error"
                        : "print_error";
                    EmitRuntimeError(
                        outputFormat,
                        CreateRuntimeError(errorCode, ex.Message, runtime),
                        runtime.MuxSettings.IgnoreCertErrors);
                    exitCode = 1;
                }
                finally
                {
                    mcpRuntime?.Dispose();
                }
            }

            return exitCode;
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Resolves the prompt from the command argument or stdin.
        /// </summary>
        /// <param name="settings">The print command settings.</param>
        /// <returns>The resolved prompt string.</returns>
        private static List<McpServerConfig> LoadMcpServersForPrint(PrintSettings settings)
        {
            string raw = settings.McpConfig!.Trim();
            string json = raw.StartsWith("{", StringComparison.Ordinal)
                ? raw
                : File.ReadAllText(Path.GetFullPath(Environment.ExpandEnvironmentVariables(raw)));

            List<McpServerConfig> servers = new List<McpServerConfig>(SettingsLoader.ParseMcpServers(json));

            // Unless strict, merge in the config directory's servers so --mcp-config adds to, rather than
            // replaces, the standing configuration.
            if (!settings.StrictMcpConfig)
            {
                servers.AddRange(SettingsLoader.LoadMcpServers());
            }

            return servers;
        }

        private async Task<PrintTurnResult> RunTurnAsync(
            string prompt,
            AgentLoopOptions loopOptions,
            ResolvedRuntime runtime,
            OutputFormatEnum outputFormat,
            string? outputSchemaJson,
            string sessionId,
            bool verbose,
            CancellationToken cancellationToken)
        {
            int exitCode = 0;
            int stepCount = 0;
            StringBuilder finalAssistantResponse = new StringBuilder();
            RunCompletedEvent? completedForSummary = null;
            List<ConversationMessage> finalConversation = new List<ConversationMessage>();

            using (AgentLoop agentLoop = new AgentLoop(loopOptions))
            {
                await foreach (AgentEvent agentEvent in agentLoop.RunAsync(prompt, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    if (agentEvent is ErrorEvent structuredErrorEvent)
                    {
                        EnrichErrorEvent(structuredErrorEvent, runtime);
                    }

                    if (agentEvent is RunCompletedEvent runCompletedForSummary)
                    {
                        completedForSummary = runCompletedForSummary;
                    }

                    if (outputFormat == OutputFormatEnum.Jsonl)
                    {
                        Console.WriteLine(StructuredOutputFormatter.FormatEvent(agentEvent));
                    }

                    switch (agentEvent)
                    {
                        case AssistantTextEvent textEvent:
                            finalAssistantResponse.Append(textEvent.Text);
                            if (outputFormat == OutputFormatEnum.Text)
                            {
                                Console.Write(textEvent.Text);
                            }
                            break;

                        case AssistantThinkingEvent thinkingEvent:
                            // Thinking is display-only: never part of the final response, and in text mode it
                            // goes to stderr so stdout stays the answer (and --output-last-message is clean).
                            if (outputFormat == OutputFormatEnum.Text)
                            {
                                Console.Error.Write(thinkingEvent.Text);
                            }
                            break;

                        case HeartbeatEvent heartbeatEvent:
                            stepCount = heartbeatEvent.StepNumber;
                            if (outputFormat == OutputFormatEnum.Text)
                            {
                                Console.Error.WriteLine(ConsoleMessageStyler.Notification($"Working... (step {heartbeatEvent.StepNumber})"));
                            }
                            break;

                        case ContextStatusEvent contextStatusEvent:
                            if (outputFormat == OutputFormatEnum.Text)
                            {
                                string contextLine =
                                    $"Context usage: est. {contextStatusEvent.EstimatedTokens} / {contextStatusEvent.UsableInputLimit} tokens | {contextStatusEvent.RemainingTokens} remaining.";
                                if (string.Equals(contextStatusEvent.WarningLevel, "critical", StringComparison.Ordinal))
                                {
                                    Console.Error.WriteLine(ConsoleMessageStyler.Failure(contextLine));
                                }
                                else if (string.Equals(contextStatusEvent.WarningLevel, "approaching", StringComparison.Ordinal))
                                {
                                    Console.Error.WriteLine(ConsoleMessageStyler.Notification(contextLine));
                                }
                            }
                            break;

                        case ContextCompactedEvent contextCompactedEvent:
                            if (outputFormat == OutputFormatEnum.Text)
                            {
                                Console.Error.WriteLine(
                                    ConsoleMessageStyler.Notification(
                                        $"Auto-compacted context ({contextCompactedEvent.Strategy}): est. {contextCompactedEvent.EstimatedTokensBefore} -> {contextCompactedEvent.EstimatedTokensAfter} tokens."));
                            }
                            break;

                        case ErrorEvent errorEvent:
                            if (outputFormat == OutputFormatEnum.Text)
                            {
                                Console.Error.WriteLine(ConsoleMessageStyler.Failure($"Error: {errorEvent.Code}: {errorEvent.Message}"));
                                CertificateWarningHint.WriteIfNeeded(
                                    errorEvent.Code,
                                    errorEvent.Message,
                                    runtime.MuxSettings.IgnoreCertErrors);
                            }
                            if (exitCode == 0 || errorEvent.Code != "tool_call_denied")
                            {
                                exitCode = errorEvent.Code == "tool_call_denied" ? 2 : 1;
                            }
                            break;

                        case ToolCallProposedEvent proposedEvent:
                            stepCount++;
                            if (outputFormat == OutputFormatEnum.Text)
                            {
                                Console.Error.WriteLine(ConsoleMessageStyler.Notification($"Working... (step {stepCount})"));
                                if (verbose)
                                {
                                    string summary = ToolCallRenderer.FormatToolSummary(
                                        proposedEvent.ToolCall.Name,
                                        proposedEvent.ToolCall.Arguments);
                                    Console.Error.WriteLine(
                                        ConsoleMessageStyler.Notification(ToolCallRenderer.FormatToolCallLine(summary)));
                                }
                            }
                            if (runtime.ApprovalPolicy == ApprovalPolicyEnum.Deny)
                            {
                                if (outputFormat == OutputFormatEnum.Text)
                                {
                                    Console.Error.WriteLine(
                                        ConsoleMessageStyler.Failure(
                                            ToolCallRenderer.FormatToolLogLine(
                                                $"Tool call denied in non-interactive mode: {proposedEvent.ToolCall.Name}",
                                                isTerminal: true)));
                                }
                                if (exitCode == 0) exitCode = 2;
                            }
                            break;

                        case ToolCallCompletedEvent completedEvent:
                            if (outputFormat == OutputFormatEnum.Text)
                            {
                                if (verbose)
                                {
                                    string resultPreview = completedEvent.Result.Content ?? "(no content)";
                                    if (resultPreview.Length > 200)
                                    {
                                        resultPreview = resultPreview.Substring(0, 200) + "...";
                                    }
                                    string status = completedEvent.Result.Success ? "ok" : "failed";
                                    string line = ToolCallRenderer.FormatToolExecutionLine(
                                        completedEvent.ToolName,
                                        resultPreview,
                                        status,
                                        completedEvent.ElapsedMs);
                                    string styledLine = completedEvent.Result.Success
                                        ? ConsoleMessageStyler.Success(line)
                                        : ConsoleMessageStyler.Failure(line);
                                    Console.Error.WriteLine(styledLine);
                                    if (!completedEvent.Result.Success)
                                    {
                                        CertificateWarningHint.WriteIfNeeded(
                                            null,
                                            completedEvent.Result.Content,
                                            runtime.MuxSettings.IgnoreCertErrors);
                                    }
                                    Console.Error.WriteLine();
                                }
                                else if (!completedEvent.Result.Success)
                                {
                                    CertificateWarningHint.WriteIfNeeded(
                                        null,
                                        completedEvent.Result.Content,
                                        runtime.MuxSettings.IgnoreCertErrors);
                                }
                            }
                            break;

                        default:
                            break;
                    }
                }

                bool schemaFailed = false;
                if (outputSchemaJson != null && exitCode == 0)
                {
                    string? schemaViolation = OutputSchemaValidator.Validate(outputSchemaJson, finalAssistantResponse.ToString());
                    if (schemaViolation != null)
                    {
                        EmitRuntimeError(
                            outputFormat,
                            CreateRuntimeError("schema_validation_failed", $"Output did not conform to --output-schema: {schemaViolation}", runtime),
                            runtime.MuxSettings.IgnoreCertErrors);
                        exitCode = 1;
                        schemaFailed = true;
                    }
                }

                if (outputFormat == OutputFormatEnum.Text)
                {
                    Console.WriteLine();
                }

                if (outputFormat == OutputFormatEnum.Json && !schemaFailed)
                {
                    Console.WriteLine(StructuredOutputFormatter.FormatRunSummary(
                        completedForSummary, finalAssistantResponse.ToString(), sessionId));
                }

                finalConversation = new List<ConversationMessage>(agentLoop.FinalConversation);
            }

            return new PrintTurnResult
            {
                ExitCode = exitCode,
                AssistantText = finalAssistantResponse.ToString(),
                FinalConversation = finalConversation
            };
        }

        internal static string ExtractTurnPrompt(string line)
        {
            using JsonDocument doc = JsonDocument.Parse(line);
            JsonElement root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.String)
            {
                return root.GetString() ?? string.Empty;
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (string key in new string[] { "prompt", "text", "content" })
                {
                    if (root.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.String)
                    {
                        return value.GetString() ?? string.Empty;
                    }
                }

                throw new InvalidOperationException("input record must contain a 'prompt', 'text', or 'content' string field.");
            }

            throw new InvalidOperationException("input record must be a JSON object or a JSON string.");
        }

        internal static List<ConversationMessage> StripSystemMessages(IReadOnlyList<ConversationMessage> conversation)
        {
            List<ConversationMessage> result = new List<ConversationMessage>();
            foreach (ConversationMessage message in conversation)
            {
                if (message.Role != RoleEnum.System)
                {
                    result.Add(message);
                }
            }

            return result;
        }

        private static string BuildSchemaDirective(string schemaJson)
        {
            return "You must respond with a single JSON value that strictly conforms to the following JSON "
                + "Schema. Output only the JSON value — no prose, no explanation, and no Markdown code fences."
                + Environment.NewLine + Environment.NewLine
                + "JSON Schema:" + Environment.NewLine + schemaJson;
        }

        private static string ResolvePrompt(PrintSettings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.Prompt))
            {
                return settings.Prompt!;
            }

            if (Console.IsInputRedirected)
            {
                return Console.In.ReadToEnd();
            }

            return string.Empty;
        }

        /// <summary>
        /// Resolves the source session snapshot for a resume/continue/session-id/fork request. Returns null
        /// when a specific <c>--session-id</c> does not yet exist (a fresh session is created under it).
        /// </summary>
        /// <param name="store">The session store. Must not be null.</param>
        /// <param name="settings">The parsed print settings. Must not be null.</param>
        /// <param name="cancellationToken">A token to observe for cancellation.</param>
        /// <returns>The resolved snapshot, or null when starting a fresh session under an explicit id.</returns>
        /// <exception cref="InvalidOperationException">Thrown when a requested session cannot be found.</exception>
        private static async Task<SessionSnapshot?> ResolveSessionSnapshotAsync(
            SessionStore store,
            PrintSettings settings,
            CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(settings.Resume))
            {
                string key = settings.Resume!.Trim();
                SessionSnapshot? byId = await TryLoadByIdAsync(store, key, cancellationToken).ConfigureAwait(false);
                if (byId != null)
                {
                    return byId;
                }

                SessionSnapshot? byTitle = await FindByTitleAsync(store, key, cancellationToken).ConfigureAwait(false);
                if (byTitle != null)
                {
                    return byTitle;
                }

                throw new InvalidOperationException($"No session found matching id or title '{key}'.");
            }

            if (settings.Continue)
            {
                SessionSnapshot? mostRecent = await FindMostRecentAsync(store, cancellationToken).ConfigureAwait(false);
                if (mostRecent == null)
                {
                    throw new InvalidOperationException("No sessions to continue in the active config directory.");
                }

                return mostRecent;
            }

            if (!string.IsNullOrWhiteSpace(settings.SessionId))
            {
                return await TryLoadByIdAsync(store, settings.SessionId!.Trim(), cancellationToken).ConfigureAwait(false);
            }

            throw new InvalidOperationException(
                "--fork-session requires --resume, --continue, or --session-id to identify the source session.");
        }

        /// <summary>
        /// Computes the session id the run persists under, honoring forking and explicit ids.
        /// </summary>
        /// <param name="settings">The parsed print settings. Must not be null.</param>
        /// <param name="loaded">The resolved source snapshot, or null.</param>
        /// <returns>The effective session id.</returns>
        private static string ResolveEffectiveSessionId(PrintSettings settings, SessionSnapshot? loaded)
        {
            if (settings.ForkSession)
            {
                return Guid.NewGuid().ToString("N");
            }

            if (loaded != null)
            {
                return loaded.Id;
            }

            if (!string.IsNullOrWhiteSpace(settings.SessionId))
            {
                return settings.SessionId!.Trim();
            }

            return Guid.NewGuid().ToString("N");
        }

        private static async Task<SessionSnapshot?> TryLoadByIdAsync(SessionStore store, string id, CancellationToken cancellationToken)
        {
            try
            {
                return await store.LoadAsync(id, cancellationToken).ConfigureAwait(false);
            }
            catch (ArgumentException)
            {
                // The key is not a valid on-disk id (e.g. a title with spaces); fall back to a title search.
                return null;
            }
        }

        private static async Task<SessionSnapshot?> FindByTitleAsync(SessionStore store, string title, CancellationToken cancellationToken)
        {
            IReadOnlyList<SessionSnapshot> all = await store.ListAsync(cancellationToken).ConfigureAwait(false);
            return all
                .Where((SessionSnapshot s) => string.Equals(s.Title, title, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending((SessionSnapshot s) => s.UpdatedUtc)
                .FirstOrDefault();
        }

        private static async Task<SessionSnapshot?> FindMostRecentAsync(SessionStore store, CancellationToken cancellationToken)
        {
            IReadOnlyList<SessionSnapshot> all = await store.ListAsync(cancellationToken).ConfigureAwait(false);
            return all
                .OrderByDescending((SessionSnapshot s) => s.UpdatedUtc)
                .FirstOrDefault();
        }

        private static async Task PersistSessionAsync(
            SessionStore store,
            string sessionId,
            SessionSnapshot? source,
            ResolvedRuntime runtime,
            string prompt,
            IReadOnlyList<ConversationMessage> finalConversation,
            CancellationToken cancellationToken)
        {
            DateTime nowUtc = DateTime.UtcNow;

            List<ConversationMessage> history = finalConversation
                .Where((ConversationMessage m) => m.Role != RoleEnum.System)
                .ToList();

            List<string> promptHistory = source?.PromptHistory != null
                ? new List<string>(source.PromptHistory)
                : new List<string>();
            promptHistory.Add(prompt);

            SessionSnapshot snapshot = new SessionSnapshot
            {
                Id = sessionId,
                Title = !string.IsNullOrWhiteSpace(source?.Title)
                    ? source!.Title
                    : SessionTitleHelper.Normalize(prompt),
                EndpointName = runtime.Endpoint.Name,
                Model = runtime.Endpoint.Model,
                ConversationHistory = history,
                PromptHistory = promptHistory,
                CreatedUtc = source?.CreatedUtc ?? nowUtc,
                UpdatedUtc = nowUtc
            };

            await store.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);
        }

        private static void EmitBootstrapError(PrintSettings settings, string message)
        {
            OutputFormatEnum format = string.Equals(settings.OutputFormat, "jsonl", StringComparison.OrdinalIgnoreCase)
                ? OutputFormatEnum.Jsonl
                : OutputFormatEnum.Text;

            ErrorEvent errorEvent = StructuredOutputFormatter.CreateErrorEvent(ClassifyBootstrapErrorCode(message), message);
            errorEvent.CommandName = "print";
            errorEvent.FailureCategory = "configuration";

            TryPopulateBootstrapMetadata(errorEvent, settings);

            EmitRuntimeError(format, errorEvent, settings.IgnoreCertErrors);
        }

        private static void EmitRuntimeError(OutputFormatEnum outputFormat, ErrorEvent errorEvent, bool ignoreCertErrors)
        {
            if (outputFormat == OutputFormatEnum.Jsonl)
            {
                Console.WriteLine(StructuredOutputFormatter.FormatEvent(errorEvent));
            }
            else
            {
                Console.Error.WriteLine(ConsoleMessageStyler.Failure($"Error: {errorEvent.Message}"));
                CertificateWarningHint.WriteIfNeeded(errorEvent.Code, errorEvent.Message, ignoreCertErrors);
            }
        }

        private static ErrorEvent CreateRuntimeError(string code, string message, ResolvedRuntime runtime)
        {
            ErrorEvent errorEvent = StructuredOutputFormatter.CreateErrorEvent(code, message);
            EnrichErrorEvent(errorEvent, runtime);
            return errorEvent;
        }

        private static void EnrichErrorEvent(ErrorEvent errorEvent, ResolvedRuntime runtime)
        {
            errorEvent.FailureCategory = string.IsNullOrWhiteSpace(errorEvent.FailureCategory)
                ? ClassifyFailureCategory(errorEvent.Code)
                : errorEvent.FailureCategory;
            errorEvent.EndpointName = string.IsNullOrWhiteSpace(errorEvent.EndpointName) ? runtime.Endpoint.Name : errorEvent.EndpointName;
            errorEvent.AdapterType = string.IsNullOrWhiteSpace(errorEvent.AdapterType) ? runtime.Endpoint.AdapterType.ToString() : errorEvent.AdapterType;
            errorEvent.BaseUrl = string.IsNullOrWhiteSpace(errorEvent.BaseUrl) ? runtime.Endpoint.BaseUrl : errorEvent.BaseUrl;
            errorEvent.Model = string.IsNullOrWhiteSpace(errorEvent.Model) ? runtime.Endpoint.Model : errorEvent.Model;
            errorEvent.CommandName = string.IsNullOrWhiteSpace(errorEvent.CommandName) ? runtime.Metadata.CommandName : errorEvent.CommandName;
            errorEvent.ConfigDirectory = string.IsNullOrWhiteSpace(errorEvent.ConfigDirectory) ? runtime.Metadata.ConfigDirectory : errorEvent.ConfigDirectory;
            errorEvent.EndpointSelectionSource = string.IsNullOrWhiteSpace(errorEvent.EndpointSelectionSource)
                ? runtime.Metadata.EndpointSelectionSource
                : errorEvent.EndpointSelectionSource;
            if (errorEvent.CliOverridesApplied.Count == 0)
            {
                errorEvent.CliOverridesApplied = new List<string>(runtime.Metadata.CliOverridesApplied);
            }
        }

        private static void TryPopulateBootstrapMetadata(ErrorEvent errorEvent, PrintSettings settings)
        {
            try
            {
                SettingsLoader.EnsureConfigDirectory();
                errorEvent.ConfigDirectory = SettingsLoader.GetConfigDirectory();
                errorEvent.CliOverridesApplied = GetCliOverrides(settings);
                if (!string.IsNullOrWhiteSpace(settings.Endpoint))
                {
                    errorEvent.EndpointSelectionSource = "named_endpoint";
                    errorEvent.EndpointName = settings.Endpoint!;
                }

                if (!string.IsNullOrWhiteSpace(settings.Model))
                {
                    errorEvent.Model = settings.Model!;
                }

                if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
                {
                    errorEvent.BaseUrl = settings.BaseUrl!;
                }

                if (!string.IsNullOrWhiteSpace(settings.AdapterType))
                {
                    errorEvent.AdapterType = settings.AdapterType!;
                }
            }
            catch
            {
                // Best-effort only. Bootstrap error emission should not fail while collecting metadata.
            }
        }

        private static List<string> GetCliOverrides(CommonSettings settings)
        {
            List<string> overrides = new List<string>();

            if (!string.IsNullOrWhiteSpace(settings.Endpoint)) overrides.Add("endpoint");
            if (!string.IsNullOrWhiteSpace(settings.ConfigDir)) overrides.Add("configDir");
            if (!string.IsNullOrWhiteSpace(settings.Model)) overrides.Add("model");
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl)) overrides.Add("baseUrl");
            if (!string.IsNullOrWhiteSpace(settings.AdapterType)) overrides.Add("adapterType");
            if (settings.Temperature.HasValue) overrides.Add("temperature");
            if (settings.MaxTokens.HasValue) overrides.Add("maxTokens");
            if (!string.IsNullOrWhiteSpace(settings.WorkingDirectory)) overrides.Add("workingDirectory");
            if (!string.IsNullOrWhiteSpace(settings.SystemPrompt)) overrides.Add("systemPrompt");
            if (!string.IsNullOrWhiteSpace(settings.AppendSystemPrompt)) overrides.Add("appendSystemPrompt");
            if (settings.MaxTurns.HasValue) overrides.Add("maxTurns");
            if (settings.MaxTokenBudget.HasValue) overrides.Add("maxTokenBudget");
            if (settings.Yolo) overrides.Add("yolo");
            if (!string.IsNullOrWhiteSpace(settings.ApprovalPolicy)) overrides.Add("approvalPolicy");
            if (!string.IsNullOrWhiteSpace(settings.CompactionStrategy)) overrides.Add("compactionStrategy");
            if (settings.IgnoreCertErrors) overrides.Add("ignoreCertErrors");

            return overrides;
        }

        private static string ClassifyBootstrapErrorCode(string message)
        {
            if (message.Contains("No endpoint named", StringComparison.OrdinalIgnoreCase))
            {
                return "endpoint_not_found";
            }

            if (message.Contains("--no-mcp", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Approval policy 'ask' is not supported", StringComparison.OrdinalIgnoreCase))
            {
                return "unsupported_option";
            }

            if (message.Contains("Unsupported output format", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Unsupported approval policy", StringComparison.OrdinalIgnoreCase)
                || message.Contains("No prompt provided", StringComparison.OrdinalIgnoreCase))
            {
                return "invalid_argument";
            }

            return "print_error";
        }

        private static string ClassifyFailureCategory(string code)
        {
            return code switch
            {
                "endpoint_not_found" => "configuration",
                "unsupported_option" => "configuration",
                "invalid_argument" => "configuration",
                "config_error" => "configuration",
                "cancelled" => "cancellation",
                "tool_call_denied" => "approval",
                "approval_error" => "approval",
                "tool_execution_error" => "tool",
                "artifact_write_error" => "filesystem",
                "llm_connection_error" => "network",
                "llm_error" => "backend",
                "llm_stream_error" => "backend",
                "context_limit_exceeded" => "runtime",
                "max_iterations_reached" => "runtime",
                "budget_exceeded" => "runtime",
                "schema_validation_failed" => "validation",
                "print_error" => "unknown",
                _ => "unknown"
            };
        }

        private static string? PrepareOutputLastMessagePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            string fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim()));
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }

            return fullPath;
        }

        private static void WriteLastMessageArtifact(string path, string content)
        {
            string fullPath = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
            Directory.CreateDirectory(directory);

            string tempFile = Path.Combine(directory, Path.GetRandomFileName());
            try
            {
                File.WriteAllText(tempFile, content ?? string.Empty);
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                }

                File.Move(tempFile, fullPath);
            }
            catch
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }

                throw;
            }
        }

        #endregion
    }
}
