namespace Mux.Cli.Commands
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.Rendering;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Mux.Core.Tools;
    using RoleEnum = Mux.Core.Enums.RoleEnum;
    using Spectre.Console;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings specific to the interactive REPL command.
    /// </summary>
    public class InteractiveSettings : CommonSettings
    {
    }

    /// <summary>
    /// Interactive REPL command that provides a multi-line input prompt and streams agent responses.
    /// </summary>
    public class InteractiveCommand : AsyncCommand<InteractiveSettings>
    {
        #region Private-Members

        private CancellationTokenSource? _CurrentCts = null;
        private DateTime _LastCtrlCTime = DateTime.MinValue;
        private List<ConversationMessage> _ConversationHistory = new List<ConversationMessage>();
        private McpToolManager? _McpToolManager = null;
        private List<EndpointConfig> _AllEndpoints = new List<EndpointConfig>();
        private EndpointConfig _CurrentEndpoint = new EndpointConfig();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Executes the interactive REPL loop.
        /// </summary>
        /// <param name="context">The command context provided by Spectre.Console.Cli.</param>
        /// <param name="settings">The resolved command settings.</param>
        /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
        /// <returns>The process exit code.</returns>
        public override async Task<int> ExecuteAsync(CommandContext context, InteractiveSettings settings, CancellationToken cancellationToken)
        {
            SettingsLoader.EnsureConfigDirectory();
            _AllEndpoints = SettingsLoader.LoadEndpoints();
            MuxSettings muxSettings = SettingsLoader.LoadSettings();
            List<McpServerConfig> mcpServers = settings.NoMcp
                ? new List<McpServerConfig>()
                : SettingsLoader.LoadMcpServers();

            _CurrentEndpoint = SettingsLoader.ResolveEndpoint(
                _AllEndpoints,
                settings.Endpoint,
                settings.Model,
                settings.BaseUrl,
                settings.AdapterType,
                settings.Temperature,
                settings.MaxTokens);

            string workingDirectory = settings.WorkingDirectory ?? Directory.GetCurrentDirectory();

            // Build tool descriptions from registry
            BuiltInToolRegistry toolRegistry = new BuiltInToolRegistry();
            List<ToolDefinition> builtInTools = toolRegistry.GetToolDefinitions();
            bool toolsEnabled = _CurrentEndpoint.Quirks?.SupportsTools ?? true;

            StringBuilder toolDescBuilder = new StringBuilder();
            if (toolsEnabled)
            {
                foreach (ToolDefinition tool in builtInTools)
                {
                    toolDescBuilder.AppendLine($"- {tool.Name}: {tool.Description}");
                }
            }

            string systemPrompt = SettingsLoader.LoadSystemPrompt(settings.SystemPrompt, muxSettings);

            if (!toolsEnabled)
            {
                // Strip tool references for models that don't support tools
                systemPrompt = "You are mux, an AI assistant. You help the user by reading, writing, and editing data including documents, code, and other types " +
                    "in their project.\n\n" +
                    "Your current working directory is: {WorkingDirectory}\n\n" +
                    "Guidelines:\n" +
                    "- Explain your reasoning when making non-trivial suggestions.\n" +
                    "- If a task is ambiguous, ask for clarification before proceeding.";
            }

            systemPrompt = systemPrompt
                .Replace("{WorkingDirectory}", workingDirectory)
                .Replace("{ToolDescriptions}", toolDescBuilder.ToString().TrimEnd());

            ApprovalPolicyEnum approvalPolicy = ResolveApprovalPolicy(settings, muxSettings);

            // Initialize MCP servers
            if (mcpServers.Count > 0)
            {
                _McpToolManager = new McpToolManager(mcpServers);

                try
                {
                    AnsiConsole.MarkupLine("[dim]Connecting to MCP servers...[/]");
                    await _McpToolManager.InitializeAsync(cancellationToken).ConfigureAwait(false);

                    List<(string Name, int ToolCount, bool Connected)> serverStatus = _McpToolManager.GetServerStatus();
                    foreach ((string Name, int ToolCount, bool Connected) server in serverStatus)
                    {
                        if (server.Connected)
                        {
                            AnsiConsole.MarkupLine($"  [green]Connected:[/] {Markup.Escape(server.Name)} ({server.ToolCount} tools)");
                        }
                        else
                        {
                            AnsiConsole.MarkupLine($"  [yellow]Failed:[/] {Markup.Escape(server.Name)}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[yellow]MCP initialization error: {Markup.Escape(ex.Message)}[/]");
                }
            }

            if (approvalPolicy == ApprovalPolicyEnum.AutoApprove)
            {
                AnsiConsole.MarkupLine("[yellow]notice: all tool calls will be auto-approved (--yolo)[/]");
            }

            AnsiConsole.MarkupLine($"[dim]Endpoint:[/] {Markup.Escape(_CurrentEndpoint.Name)} [dim]|[/] [dim]Model:[/] {Markup.Escape(_CurrentEndpoint.Model)}");
            AnsiConsole.MarkupLine("[dim]Type /help for commands, Shift+Enter for newline, Ctrl+C to cancel, Ctrl+C twice to exit.[/]");
            AnsiConsole.WriteLine();

            while (true)
            {
                string? input = ReadMultiLineInput();

                if (input == null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(input))
                {
                    continue;
                }

                string trimmed = input.Trim();

                if (HandleSlashCommand(trimmed, _CurrentEndpoint, ref systemPrompt, mcpServers))
                {
                    continue;
                }

                AgentLoopOptions loopOptions = new AgentLoopOptions(_CurrentEndpoint)
                {
                    ConversationHistory = _ConversationHistory,
                    SystemPrompt = systemPrompt,
                    ApprovalPolicy = approvalPolicy,
                    WorkingDirectory = workingDirectory,
                    MaxIterations = muxSettings.MaxAgentIterations,
                    Verbose = settings.Verbose,
                    PromptUserFunc = ToolCallRenderer.PromptApprovalAsync,
                    AdditionalTools = _McpToolManager?.GetToolDefinitions(),
                    ExternalToolExecutor = _McpToolManager != null
                        ? (Func<string, JsonElement, string, CancellationToken, Task<ToolResult>>)(async (string toolName, JsonElement arguments, string workDir, CancellationToken ct) =>
                        {
                            return await _McpToolManager.ExecuteAsync(toolName, toolName, arguments, ct).ConfigureAwait(false);
                        })
                        : null,
                    OnRetry = (int attempt, int maxRetries, string message) =>
                    {
                        EventRenderer.UpdateStatusLine($"Connection failed, retrying ({attempt}/{maxRetries})...");
                    }
                };

                _CurrentCts = new CancellationTokenSource();

                Console.CancelKeyPress += OnCancelKeyPress;

                try
                {
                    AgentLoop agentLoop = new AgentLoop(loopOptions);
                    IAsyncEnumerable<AgentEvent> events = agentLoop.RunAsync(trimmed, _CurrentCts.Token);

                    // Collect assistant text while rendering for conversation history
                    StringBuilder assistantResponse = new StringBuilder();
                    async IAsyncEnumerable<AgentEvent> WrapEvents(IAsyncEnumerable<AgentEvent> source)
                    {
                        await foreach (AgentEvent evt in source)
                        {
                            if (evt is AssistantTextEvent textEvt)
                            {
                                assistantResponse.Append(textEvt.Text);
                            }
                            yield return evt;
                        }
                    }

                    await EventRenderer.RenderAsync(WrapEvents(events), settings.Verbose);

                    // Add user message and assistant response to conversation history
                    _ConversationHistory.Add(new ConversationMessage
                    {
                        Role = RoleEnum.User,
                        Content = trimmed
                    });

                    string responseText = assistantResponse.ToString();
                    if (!string.IsNullOrEmpty(responseText))
                    {
                        _ConversationHistory.Add(new ConversationMessage
                        {
                            Role = RoleEnum.Assistant,
                            Content = responseText
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    AnsiConsole.MarkupLine("[yellow]Generation cancelled.[/]");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(ex.Message)}[/]");
                }
                finally
                {
                    Console.CancelKeyPress -= OnCancelKeyPress;
                    _CurrentCts?.Dispose();
                    _CurrentCts = null;
                }

                AnsiConsole.WriteLine();
            }

            return 0;
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Reads multi-line input from the console with support for Shift+Enter and Ctrl+Enter
        /// to insert newlines, Enter to submit, and left/right/Home/End for cursor navigation.
        /// </summary>
        /// <returns>The full input string, or null if the user signals exit.</returns>
        private string? ReadMultiLineInput()
        {
            LineBuffer lineBuffer = new LineBuffer();
            int promptWidth = 5; // "mux> " or "...> "

            WritePrompt(0);

            while (true)
            {
                ConsoleKeyInfo keyInfo;

                try
                {
                    keyInfo = Console.ReadKey(intercept: true);
                }
                catch (InvalidOperationException)
                {
                    return null;
                }

                // Ctrl+C: clear or exit
                if (keyInfo.Key == ConsoleKey.C
                    && (keyInfo.Modifiers & ConsoleModifiers.Control) != 0)
                {
                    DateTime now = DateTime.UtcNow;

                    if ((now - _LastCtrlCTime).TotalSeconds <= 2.0)
                    {
                        AnsiConsole.WriteLine();
                        return null;
                    }

                    _LastCtrlCTime = now;
                    AnsiConsole.WriteLine();
                    lineBuffer.Clear();
                    WritePrompt(0);
                    continue;
                }

                // Enter: submit or newline
                if (keyInfo.Key == ConsoleKey.Enter)
                {
                    bool isShiftEnter = (keyInfo.Modifiers & ConsoleModifiers.Shift) != 0;
                    bool isCtrlEnter = (keyInfo.Modifiers & ConsoleModifiers.Control) != 0;

                    if (isShiftEnter || isCtrlEnter)
                    {
                        lineBuffer.InsertNewLine();
                        Console.WriteLine();
                        WritePrompt(lineBuffer.CurrentLineIndex);
                        continue;
                    }

                    Console.WriteLine();
                    return lineBuffer.GetText();
                }

                // Left arrow
                if (keyInfo.Key == ConsoleKey.LeftArrow)
                {
                    if (lineBuffer.MoveLeft())
                    {
                        Console.SetCursorPosition(promptWidth + lineBuffer.CursorColumn, Console.CursorTop);
                    }
                    continue;
                }

                // Right arrow
                if (keyInfo.Key == ConsoleKey.RightArrow)
                {
                    if (lineBuffer.MoveRight())
                    {
                        Console.SetCursorPosition(promptWidth + lineBuffer.CursorColumn, Console.CursorTop);
                    }
                    continue;
                }

                // Home
                if (keyInfo.Key == ConsoleKey.Home)
                {
                    lineBuffer.MoveHome();
                    Console.SetCursorPosition(promptWidth, Console.CursorTop);
                    continue;
                }

                // End
                if (keyInfo.Key == ConsoleKey.End)
                {
                    lineBuffer.MoveEnd();
                    Console.SetCursorPosition(promptWidth + lineBuffer.CursorColumn, Console.CursorTop);
                    continue;
                }

                // Delete key (forward delete)
                if (keyInfo.Key == ConsoleKey.Delete)
                {
                    if (lineBuffer.Delete())
                    {
                        RedrawFromCursor(lineBuffer, promptWidth);
                    }
                    continue;
                }

                // Backspace
                if (keyInfo.Key == ConsoleKey.Backspace)
                {
                    if (lineBuffer.IsCursorAtStart && lineBuffer.CurrentLineIndex > 0)
                    {
                        // At start of continuation line — merge up
                        lineBuffer.RemoveCurrentLineAndMergeUp();
                        RedrawCurrentLine(lineBuffer, promptWidth);
                    }
                    else if (lineBuffer.Backspace())
                    {
                        if (lineBuffer.IsCursorAtEnd)
                        {
                            // Simple case: cursor at end, just erase last char
                            Console.Write("\b \b");
                        }
                        else
                        {
                            // Mid-line: redraw from cursor position
                            RedrawFromCursor(lineBuffer, promptWidth);
                        }
                    }
                    continue;
                }

                // Escape: ignore
                if (keyInfo.Key == ConsoleKey.Escape)
                {
                    continue;
                }

                // Up/Down arrows: ignore for now
                if (keyInfo.Key == ConsoleKey.UpArrow || keyInfo.Key == ConsoleKey.DownArrow)
                {
                    continue;
                }

                // Regular character
                if (keyInfo.KeyChar != '\0' && !char.IsControl(keyInfo.KeyChar))
                {
                    lineBuffer.Insert(keyInfo.KeyChar);

                    if (lineBuffer.IsCursorAtEnd)
                    {
                        // Appending at end: just write the char
                        Console.Write(keyInfo.KeyChar);
                    }
                    else
                    {
                        // Inserting mid-line: redraw from cursor
                        RedrawFromCursor(lineBuffer, promptWidth);
                    }
                }
            }
        }

        /// <summary>
        /// Writes the appropriate prompt prefix for the given line number.
        /// </summary>
        /// <param name="lineNumber">The zero-based line number in the multi-line input.</param>
        private static void WritePrompt(int lineNumber)
        {
            if (lineNumber == 0)
            {
                AnsiConsole.Markup("[bold green]mux>[/] ");
            }
            else
            {
                AnsiConsole.Markup("[dim]...>[/] ");
            }
        }

        /// <summary>
        /// Redraws the text from the cursor position to the end of the current line,
        /// clearing any leftover characters and repositioning the cursor.
        /// </summary>
        /// <param name="lineBuffer">The line buffer.</param>
        /// <param name="promptWidth">The width of the prompt prefix in characters.</param>
        private static void RedrawFromCursor(LineBuffer lineBuffer, int promptWidth)
        {
            int cursorX = promptWidth + lineBuffer.CursorColumn;
            string textAfter = lineBuffer.TextAfterCursor;

            Console.SetCursorPosition(cursorX, Console.CursorTop);
            Console.Write(textAfter);
            // Clear any trailing characters from the previous longer text
            Console.Write("  ");
            Console.SetCursorPosition(cursorX, Console.CursorTop);
        }

        /// <summary>
        /// Redraws the entire current line including the prompt.
        /// Used when merging lines (backspace at start of continuation line).
        /// </summary>
        /// <param name="lineBuffer">The line buffer.</param>
        /// <param name="promptWidth">The width of the prompt prefix in characters.</param>
        private static void RedrawCurrentLine(LineBuffer lineBuffer, int promptWidth)
        {
            Console.SetCursorPosition(0, Console.CursorTop);
            Console.Write(new string(' ', promptWidth + lineBuffer.CurrentLine.Length + 10));
            Console.SetCursorPosition(0, Console.CursorTop);

            WritePrompt(lineBuffer.CurrentLineIndex);
            Console.Write(lineBuffer.CurrentLine);
            Console.SetCursorPosition(promptWidth + lineBuffer.CursorColumn, Console.CursorTop);
        }

        /// <summary>
        /// Handles Ctrl+C during generation to cancel the current operation.
        /// </summary>
        private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            _CurrentCts?.Cancel();
        }

        /// <summary>
        /// Resolves the effective approval policy from CLI flags and settings.
        /// </summary>
        /// <param name="settings">The interactive command settings.</param>
        /// <param name="muxSettings">The global mux settings.</param>
        /// <returns>The resolved approval policy.</returns>
        private static ApprovalPolicyEnum ResolveApprovalPolicy(
            InteractiveSettings settings,
            MuxSettings muxSettings)
        {
            if (settings.Yolo)
            {
                return ApprovalPolicyEnum.AutoApprove;
            }

            if (!string.IsNullOrWhiteSpace(settings.ApprovalPolicy))
            {
                if (Enum.TryParse<ApprovalPolicyEnum>(settings.ApprovalPolicy, true, out ApprovalPolicyEnum parsed))
                {
                    return parsed;
                }
            }

            if (string.Equals(muxSettings.DefaultApprovalPolicy, "auto_approve", StringComparison.OrdinalIgnoreCase)
                || string.Equals(muxSettings.DefaultApprovalPolicy, "auto", StringComparison.OrdinalIgnoreCase))
            {
                return ApprovalPolicyEnum.AutoApprove;
            }

            if (string.Equals(muxSettings.DefaultApprovalPolicy, "deny", StringComparison.OrdinalIgnoreCase))
            {
                return ApprovalPolicyEnum.Deny;
            }

            return ApprovalPolicyEnum.Ask;
        }

        /// <summary>
        /// Processes slash commands typed at the REPL prompt.
        /// </summary>
        /// <param name="input">The trimmed user input.</param>
        /// <param name="endpoint">The current endpoint configuration (kept for signature compatibility).</param>
        /// <param name="systemPrompt">The current system prompt (may be modified by /system).</param>
        /// <param name="mcpServers">The loaded MCP server configurations.</param>
        /// <returns>True if the input was a slash command and was handled; false otherwise.</returns>
        private bool HandleSlashCommand(
            string input,
            EndpointConfig endpoint,
            ref string systemPrompt,
            List<McpServerConfig> mcpServers)
        {
            if (!input.StartsWith("/"))
            {
                return false;
            }

            string[] parts = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            string command = parts[0].ToLowerInvariant();
            string argument = parts.Length > 1 ? parts[1] : string.Empty;

            switch (command)
            {
                case "/exit":
                case "/quit":
                    Environment.Exit(0);
                    return true;

                case "/endpoint":
                case "/model":
                    HandleEndpointCommand(argument);
                    return true;

                case "/tools":
                    HandleToolsCommand();
                    return true;

                case "/clear":
                    HandleClearCommand();
                    return true;

                case "/system":
                    HandleSystemCommand(argument, ref systemPrompt);
                    return true;

                case "/mcp":
                    HandleMcpCommand(argument, mcpServers);
                    return true;

                case "/help":
                case "/?":
                    HandleHelpCommand();
                    return true;

                default:
                    AnsiConsole.MarkupLine($"[yellow]Unknown command: {Markup.Escape(command)}. Type /help for available commands.[/]");
                    return true;
            }
        }

        /// <summary>
        /// Handles the /endpoint command. With no arguments, lists all configured endpoints
        /// with the current one highlighted in green with an asterisk marker.
        /// With an argument, switches to the named endpoint, clears the conversation, and prints confirmation.
        /// </summary>
        /// <param name="argument">The optional endpoint name to switch to.</param>
        private void HandleEndpointCommand(string argument)
        {
            if (string.IsNullOrWhiteSpace(argument))
            {
                if (_AllEndpoints.Count == 0)
                {
                    AnsiConsole.MarkupLine($"[dim]Current:[/] {Markup.Escape(_CurrentEndpoint.Name)} [dim]([/]{Markup.Escape(_CurrentEndpoint.Model)}[dim])[/]");
                    AnsiConsole.MarkupLine("[dim]No other endpoints configured.[/]");
                    return;
                }

                Table table = new Table();
                table.Border = TableBorder.Rounded;
                table.AddColumn("[bold]Endpoint[/]");
                table.AddColumn("[bold]Model[/]");
                table.AddColumn("[bold]Adapter[/]");

                foreach (EndpointConfig ep in _AllEndpoints)
                {
                    bool isCurrent = string.Equals(ep.Name, _CurrentEndpoint.Name, StringComparison.OrdinalIgnoreCase);
                    string nameDisplay = isCurrent
                        ? $"[green]* {Markup.Escape(ep.Name)}[/]"
                        : $"  {Markup.Escape(ep.Name)}";
                    string modelDisplay = isCurrent
                        ? $"[green]{Markup.Escape(ep.Model)}[/]"
                        : Markup.Escape(ep.Model);
                    string adapterDisplay = isCurrent
                        ? $"[green]{Markup.Escape(ep.AdapterType.ToString())}[/]"
                        : Markup.Escape(ep.AdapterType.ToString());
                    table.AddRow(nameDisplay, modelDisplay, adapterDisplay);
                }

                AnsiConsole.Write(table);
            }
            else
            {
                string targetName = argument.Trim();
                EndpointConfig? found = _AllEndpoints
                    .FirstOrDefault((EndpointConfig e) => string.Equals(e.Name, targetName, StringComparison.OrdinalIgnoreCase));

                if (found != null)
                {
                    _CurrentEndpoint = found;
                    _ConversationHistory.Clear();
                    AnsiConsole.MarkupLine($"[green]Switched to endpoint:[/] {Markup.Escape(found.Name)} [dim]([/]{Markup.Escape(found.Model)}[dim])[/]");
                    AnsiConsole.MarkupLine("[dim]Conversation history cleared.[/]");
                }
                else
                {
                    _CurrentEndpoint.Model = targetName;
                    _ConversationHistory.Clear();
                    AnsiConsole.MarkupLine($"[green]Model changed to:[/] {Markup.Escape(targetName)}");
                    AnsiConsole.MarkupLine("[dim]Conversation history cleared.[/]");
                }
            }
        }

        /// <summary>
        /// Handles the /tools command by listing all built-in tools and their descriptions in a table.
        /// Also shows MCP tools if any are connected.
        /// </summary>
        private void HandleToolsCommand()
        {
            Table table = new Table();
            table.Border = TableBorder.Rounded;
            table.AddColumn("[bold]Tool[/]");
            table.AddColumn("[bold]Description[/]");
            table.AddColumn("[bold]Source[/]");

            table.AddRow("read_file", "Read file contents from disk", "[green]built-in[/]");
            table.AddRow("write_file", "Write content to a file", "[green]built-in[/]");
            table.AddRow("edit_file", "Apply a search-and-replace edit to a file", "[green]built-in[/]");
            table.AddRow("multi_edit", "Apply multiple edits to a file atomically", "[green]built-in[/]");
            table.AddRow("delete_file", "Delete a file from disk", "[green]built-in[/]");
            table.AddRow("file_metadata", "Read file/directory metadata (size, timestamps)", "[green]built-in[/]");
            table.AddRow("list_directory", "List files and directories at a path", "[green]built-in[/]");
            table.AddRow("manage_directory", "Create, delete, or rename directories", "[green]built-in[/]");
            table.AddRow("glob", "Find files matching a glob pattern", "[green]built-in[/]");
            table.AddRow("grep", "Search file contents using regex patterns", "[green]built-in[/]");
            table.AddRow("run_process", "Execute a shell command", "[green]built-in[/]");

            if (_McpToolManager != null)
            {
                List<ToolDefinition> mcpTools = _McpToolManager.GetToolDefinitions();
                foreach (ToolDefinition tool in mcpTools)
                {
                    table.AddRow(
                        $"[dim]{Markup.Escape(tool.Name)}[/]",
                        $"[dim]{Markup.Escape(tool.Description)}[/]",
                        "[cyan]mcp[/]");
                }
            }

            AnsiConsole.Write(table);
        }

        /// <summary>
        /// Handles the /clear command by resetting the conversation history.
        /// </summary>
        private void HandleClearCommand()
        {
            _ConversationHistory.Clear();
            AnsiConsole.MarkupLine("[green]Conversation history cleared.[/]");
        }

        /// <summary>
        /// Handles the /system command. With no arguments, displays the current system prompt
        /// (truncated to 500 characters). With an argument, replaces the system prompt for this session.
        /// </summary>
        /// <param name="argument">The optional new system prompt text.</param>
        /// <param name="systemPrompt">Reference to the current system prompt.</param>
        private void HandleSystemCommand(string argument, ref string systemPrompt)
        {
            if (!string.IsNullOrWhiteSpace(argument))
            {
                systemPrompt = argument;
                AnsiConsole.MarkupLine("[green]System prompt updated for this session.[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"[dim]System prompt ({systemPrompt.Length} chars):[/]");
                string preview = systemPrompt.Length > 500
                    ? systemPrompt.Substring(0, 500) + "..."
                    : systemPrompt;
                AnsiConsole.WriteLine(preview);
            }
        }

        /// <summary>
        /// Handles the /help command by displaying all available commands in a formatted table.
        /// </summary>
        private void HandleHelpCommand()
        {
            Table table = new Table();
            table.Border = TableBorder.Rounded;
            table.AddColumn("[bold]Command[/]");
            table.AddColumn("[bold]Description[/]");

            table.AddRow("[cyan]/help[/], [cyan]/?[/]", "Show this help message");
            table.AddRow("[cyan]/endpoint[/]", "List all configured endpoints with current one highlighted");
            table.AddRow("[cyan]/endpoint[/] [dim]<name>[/]", "Switch to a named endpoint (clears conversation)");
            table.AddRow("[cyan]/tools[/]", "List all available tools with descriptions");
            table.AddRow("[cyan]/clear[/]", "Clear conversation history");
            table.AddRow("[cyan]/system[/]", "Show current system prompt (truncated to 500 chars)");
            table.AddRow("[cyan]/system[/] [dim]<text>[/]", "Replace system prompt for this session");
            table.AddRow("[cyan]/mcp list[/]", "List MCP server connections and status");
            table.AddRow("[cyan]/mcp add[/] [dim]<name> <cmd>[/]", "Add an MCP server at runtime");
            table.AddRow("[cyan]/mcp remove[/] [dim]<name>[/]", "Remove an MCP server");
            table.AddRow("[cyan]/exit[/]", "Exit mux");

            AnsiConsole.Write(table);

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]Input:[/]");
            AnsiConsole.MarkupLine("  [dim]Enter[/]           Submit input");
            AnsiConsole.MarkupLine("  [dim]Shift+Enter[/]     Insert newline");
            AnsiConsole.MarkupLine("  [dim]Ctrl+Enter[/]      Insert newline");
            AnsiConsole.MarkupLine("  [dim]Ctrl+C[/]          Cancel generation / clear input");
            AnsiConsole.MarkupLine("  [dim]Ctrl+C x2[/]       Exit mux");
        }

        /// <summary>
        /// Handles /mcp subcommands: list, add, remove.
        /// </summary>
        /// <param name="argument">The argument string after /mcp.</param>
        /// <param name="mcpServers">The loaded MCP server configurations.</param>
        private void HandleMcpCommand(string argument, List<McpServerConfig> mcpServers)
        {
            string[] subParts = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string subCommand = subParts.Length > 0 ? subParts[0].ToLowerInvariant() : "list";

            switch (subCommand)
            {
                case "list":
                    if (_McpToolManager == null)
                    {
                        AnsiConsole.MarkupLine("[dim]No MCP servers configured.[/]");
                        return;
                    }

                    List<(string Name, int ToolCount, bool Connected)> status = _McpToolManager.GetServerStatus();
                    if (status.Count == 0)
                    {
                        AnsiConsole.MarkupLine("[dim]No MCP servers connected.[/]");
                        return;
                    }

                    Table mcpTable = new Table();
                    mcpTable.AddColumn("Server");
                    mcpTable.AddColumn("Tools");
                    mcpTable.AddColumn("Status");

                    foreach ((string Name, int ToolCount, bool Connected) server in status)
                    {
                        string statusText = server.Connected
                            ? "[green]Connected[/]"
                            : "[red]Disconnected[/]";
                        mcpTable.AddRow(
                            Markup.Escape(server.Name),
                            server.ToolCount.ToString(),
                            statusText);
                    }

                    AnsiConsole.Write(mcpTable);
                    break;

                case "add":
                    if (subParts.Length < 3)
                    {
                        AnsiConsole.MarkupLine("[yellow]Usage: /mcp add <name> <command> [args...][/]");
                        return;
                    }

                    string addName = subParts[1];
                    string addCommand = subParts[2];
                    List<string> addArgs = new List<string>();
                    for (int i = 3; i < subParts.Length; i++)
                    {
                        addArgs.Add(subParts[i]);
                    }

                    if (_McpToolManager == null)
                    {
                        _McpToolManager = new McpToolManager(new List<McpServerConfig>());
                    }

                    try
                    {
                        _McpToolManager.AddServerAsync(addName, addCommand, addArgs, CancellationToken.None)
                            .GetAwaiter().GetResult();
                        AnsiConsole.MarkupLine($"[green]Added MCP server:[/] {Markup.Escape(addName)}");

                        List<ToolDefinition> newTools = _McpToolManager.GetToolDefinitions();
                        int toolCount = 0;
                        foreach (ToolDefinition tool in newTools)
                        {
                            if (tool.Name.StartsWith(addName + ".", StringComparison.OrdinalIgnoreCase))
                            {
                                toolCount++;
                            }
                        }

                        AnsiConsole.MarkupLine($"  [dim]Discovered {toolCount} tools[/]");
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]Failed to add MCP server: {Markup.Escape(ex.Message)}[/]");
                    }
                    break;

                case "remove":
                    if (subParts.Length < 2)
                    {
                        AnsiConsole.MarkupLine("[yellow]Usage: /mcp remove <name>[/]");
                        return;
                    }

                    string removeName = subParts[1];

                    if (_McpToolManager == null)
                    {
                        AnsiConsole.MarkupLine("[dim]No MCP servers configured.[/]");
                        return;
                    }

                    try
                    {
                        _McpToolManager.RemoveServerAsync(removeName).GetAwaiter().GetResult();
                        AnsiConsole.MarkupLine($"[green]Removed MCP server:[/] {Markup.Escape(removeName)}");
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]Failed to remove MCP server: {Markup.Escape(ex.Message)}[/]");
                    }
                    break;

                default:
                    // Fallback: treat bare /mcp as /mcp list
                    HandleMcpCommand("list", mcpServers);
                    break;
            }
        }

        #endregion
    }
}
