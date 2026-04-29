namespace Mux.Cli.Commands
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using Mux.Cli.Rendering;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Llm;
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
    /// Interactive REPL command that supports queued prompts while the current run is still active.
    /// </summary>
    public class InteractiveCommand : AsyncCommand<InteractiveSettings>
    {
        #region Private-Members

        private const int PromptWidth = 5;
        private const int InputPollDelayMs = 25;
        private const int PromptTopPaddingLines = 1;
        private const int PromptSpacingBelowStatusLines = 1;
        private const string ThinkingText = "Thinking...";
        private const int TitleReviewIntervalTurns = 3;
        private const int MinimumTitleReviewMessages = 4;
        private const int MinimumTitleReviewChars = 240;
        private const int PreflightCompactionTargetPercent = 60;
        private const string SyntheticSummaryPrefix = "[mux summary generated automatically; older conversation condensed]";

        private readonly QueuedMessageManager _QueuedMessages = new QueuedMessageManager();
        private readonly object _ConsoleSync = new object();

        private CancellationTokenSource? _CurrentCts = null;
        private DateTime _LastCtrlCTime = DateTime.MinValue;
        private List<ConversationMessage> _ConversationHistory = new List<ConversationMessage>();
        private PromptHistory _PromptHistory = new PromptHistory();
        private McpToolManager? _McpToolManager = null;
        private List<EndpointConfig> _AllEndpoints = new List<EndpointConfig>();
        private List<McpServerConfig> _McpServers = new List<McpServerConfig>();
        private EndpointConfig _CurrentEndpoint = new EndpointConfig();
        private MuxSettings _MuxSettings = new MuxSettings();
        private ApprovalPolicyEnum _ApprovalPolicy = ApprovalPolicyEnum.Ask;
        private string _WorkingDirectory = string.Empty;
        private string _SystemPrompt = string.Empty;
        private bool _Verbose = false;
        private bool _ShouldExit = false;
        private bool _QueuePaused = false;
        private bool _AssistantTextOpen = false;
        private bool _RunHasVisibleOutput = false;
        private int _ChromeTop = 0;
        private int _PromptTop = 0;
        private int _RenderedPromptRowCount = 0;
        private int _OutputCursorLeft = 0;
        private int _OutputCursorTop = 0;
        private bool _OutputEndsWithPromptSpacer = false;
        private int _LastBufferWidth = -1;
        private string _TransientStatusNotice = string.Empty;
        private DateTime _TransientStatusNoticeExpiresUtc = DateTime.MinValue;
        private string? _RetryStatusMessage = null;
        private string _ConversationTitle = SessionTitleHelper.DefaultTitle;
        private bool _ConversationTitleSetByUser = false;
        private int _TurnsSinceLastTitleReview = 0;
        private int _CompactionCount = 0;
        private string _LastCompactionSummary = string.Empty;
        private DateTime _LastCompactionUtc = DateTime.MinValue;
        private LineBuffer _DraftBuffer = new LineBuffer();
        private ApprovalRequestState? _PendingApproval = null;
        private ActiveRunState? _ActiveRun = null;

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
            _MuxSettings = SettingsLoader.LoadSettings();
            CommandRuntimeResolver.ApplyMuxSettingsOverrides(settings, _MuxSettings);
            _McpServers = settings.NoMcp
                ? new List<McpServerConfig>()
                : SettingsLoader.LoadMcpServers();
            _Verbose = settings.Verbose;

            _CurrentEndpoint = SettingsLoader.ResolveEndpoint(
                _AllEndpoints,
                settings.Endpoint,
                settings.Model,
                settings.BaseUrl,
                settings.AdapterType,
                settings.Temperature,
                settings.MaxTokens);

            _WorkingDirectory = settings.WorkingDirectory ?? Directory.GetCurrentDirectory();

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

            _SystemPrompt = SettingsLoader.LoadSystemPrompt(settings.SystemPrompt, _MuxSettings);

            if (!toolsEnabled)
            {
                _SystemPrompt = "You are mux, an AI assistant. You help the user by reading, writing, and editing data including documents, code, and other types " +
                    "in their project.\n\n" +
                    "Your current working directory is: {WorkingDirectory}\n\n" +
                    "Guidelines:\n" +
                    "- Explain your reasoning when making non-trivial suggestions.\n" +
                    "- If a task is ambiguous, ask for clarification before proceeding.";
            }

            _SystemPrompt = _SystemPrompt
                .Replace("{WorkingDirectory}", _WorkingDirectory)
                .Replace("{ToolDescriptions}", toolDescBuilder.ToString().TrimEnd());

            _ApprovalPolicy = ResolveApprovalPolicy(settings, _MuxSettings);
            TryClearInteractiveScreen();
            RenderWelcomeScreen();

            if (_McpServers.Count > 0)
            {
                _McpToolManager = new McpToolManager(_McpServers);

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

            if (_ApprovalPolicy == ApprovalPolicyEnum.AutoApprove)
            {
                AnsiConsole.MarkupLine("[yellow]notice: all tool calls will be auto-approved (--yolo)[/]");
            }

            bool originalTreatControlCAsInput = Console.TreatControlCAsInput;
            bool originalCursorVisible = GetCursorVisibleSafe();
            ConsoleCancelEventHandler cancelHandler = HandleConsoleCancelKeyPress;
            Console.CancelKeyPress += cancelHandler;
            Console.TreatControlCAsInput = false;

            try
            {
                _ChromeTop = Console.CursorTop;
                _PromptTop = Console.CursorTop;
                _OutputCursorLeft = 0;
                _OutputCursorTop = _PromptTop;
                RenderInteractiveChrome();

                while (!_ShouldExit)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (_ActiveRun == null && !_QueuePaused && _QueuedMessages.TryDequeue(out QueuedMessageEntry queuedEntry))
                    {
                        StartRun(queuedEntry.Text);
                        continue;
                    }

                    if (_ActiveRun != null)
                    {
                        await ProcessActiveRunAsync(cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await ProcessIdleInputAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                Console.CancelKeyPress -= cancelHandler;
                Console.TreatControlCAsInput = originalTreatControlCAsInput;
                SetCursorVisibleSafe(originalCursorVisible);
                _CurrentCts?.Dispose();
                _CurrentCts = null;
            }

            return 0;
        }

        #endregion

        #region Private-Methods

        private async Task ProcessActiveRunAsync(CancellationToken cancellationToken)
        {
            if (_ActiveRun == null)
            {
                return;
            }

            DrainRunEvents();

            if (_ActiveRun.CompletionTask.IsCompleted && _ActiveRun.Events.Reader.Completion.IsCompleted)
            {
                await FinalizeActiveRunAsync().ConfigureAwait(false);
                return;
            }

            if (ClearExpiredStatusNotice())
            {
                RenderInteractiveChrome();
                return;
            }

            if (HasConsoleWidthChanged())
            {
                RenderInteractiveChrome();
                return;
            }

            if (Console.KeyAvailable)
            {
                ConsoleKeyInfo keyInfo = Console.ReadKey(intercept: true);
                HandleBusyKey(keyInfo);
            }
            else
            {
                await Task.Delay(InputPollDelayMs, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task ProcessIdleInputAsync(CancellationToken cancellationToken)
        {
            if (ClearExpiredStatusNotice())
            {
                RenderInteractiveChrome();
                return;
            }

            if (HasConsoleWidthChanged())
            {
                RenderInteractiveChrome();
                return;
            }

            if (Console.KeyAvailable)
            {
                ConsoleKeyInfo keyInfo;

                try
                {
                    keyInfo = Console.ReadKey(intercept: true);
                }
                catch (InvalidOperationException)
                {
                    _ShouldExit = true;
                    return;
                }

                HandleIdleKey(keyInfo);
                return;
            }

            await Task.Delay(InputPollDelayMs, cancellationToken).ConfigureAwait(false);
        }

        private void HandleIdleKey(ConsoleKeyInfo keyInfo)
        {
            if (HandleCommonKey(keyInfo, allowPromptHistory: true, busyMode: false))
            {
                return;
            }

            if (keyInfo.Key == ConsoleKey.Enter)
            {
                bool isShiftEnter = (keyInfo.Modifiers & ConsoleModifiers.Shift) != 0;
                bool isCtrlEnter = (keyInfo.Modifiers & ConsoleModifiers.Control) != 0;

                if (isShiftEnter || isCtrlEnter)
                {
                    _PromptHistory.ResetNavigation();
                    _DraftBuffer.InsertNewLine();
                    RenderInteractiveChrome();
                    return;
                }

                SubmitCurrentDraft();
                return;
            }

            if (keyInfo.Key == ConsoleKey.UpArrow)
            {
                if (_PromptHistory.TryMovePrevious(_DraftBuffer.GetText(), out string previousPrompt))
                {
                    _DraftBuffer.SetText(previousPrompt);
                    RenderInteractiveChrome();
                }
                return;
            }

            if (keyInfo.Key == ConsoleKey.DownArrow)
            {
                if (_PromptHistory.TryMoveNext(out string nextPrompt))
                {
                    _DraftBuffer.SetText(nextPrompt);
                    RenderInteractiveChrome();
                }
                return;
            }
        }

        private void HandleBusyKey(ConsoleKeyInfo keyInfo)
        {
            if (_PendingApproval != null)
            {
                if (keyInfo.Key == ConsoleKey.C && (keyInfo.Modifiers & ConsoleModifiers.Control) != 0)
                {
                    CancelActiveRun();
                    return;
                }

                if (keyInfo.Key == ConsoleKey.Enter)
                {
                    ResolveApproval("y");
                    return;
                }

                if (keyInfo.KeyChar == 'y' || keyInfo.KeyChar == 'Y')
                {
                    ResolveApproval("y");
                    return;
                }

                if (keyInfo.KeyChar == 'n' || keyInfo.KeyChar == 'N')
                {
                    ResolveApproval("n");
                    return;
                }

                if (keyInfo.KeyChar == 'a' || keyInfo.KeyChar == 'A')
                {
                    ResolveApproval("always");
                    return;
                }

                return;
            }

            if (keyInfo.Key == ConsoleKey.Escape)
            {
                CancelActiveRun();
                return;
            }

            if (keyInfo.Key == ConsoleKey.Tab)
            {
                QueueCurrentDraft();
                return;
            }

            if (HandleCommonKey(keyInfo, allowPromptHistory: false, busyMode: true))
            {
                return;
            }

            if (keyInfo.Key == ConsoleKey.Enter)
            {
                bool isShiftEnter = (keyInfo.Modifiers & ConsoleModifiers.Shift) != 0;
                bool isCtrlEnter = (keyInfo.Modifiers & ConsoleModifiers.Control) != 0;

                if (isShiftEnter || isCtrlEnter)
                {
                    _PromptHistory.ResetNavigation();
                    _DraftBuffer.InsertNewLine();
                    RenderInteractiveChrome();
                }
                else if (TryExecuteBusySlashCommand())
                {
                    return;
                }
                else
                {
                    QueueCurrentDraft();
                }

                return;
            }
        }

        private bool HandleCommonKey(ConsoleKeyInfo keyInfo, bool allowPromptHistory, bool busyMode)
        {
            if (keyInfo.Key == ConsoleKey.C
                && (keyInfo.Modifiers & ConsoleModifiers.Control) != 0)
            {
                HandleCtrlC(busyMode);
                return true;
            }

            if (keyInfo.Key == ConsoleKey.UpArrow
                && (keyInfo.Modifiers & ConsoleModifiers.Alt) != 0)
            {
                EditLastQueuedPrompt();
                return true;
            }

            if (keyInfo.Key == ConsoleKey.LeftArrow)
            {
                if (_DraftBuffer.MoveLeft())
                {
                    RenderInteractiveChrome();
                }
                return true;
            }

            if (keyInfo.Key == ConsoleKey.RightArrow)
            {
                if (_DraftBuffer.MoveRight())
                {
                    RenderInteractiveChrome();
                }
                return true;
            }

            if (keyInfo.Key == ConsoleKey.Home)
            {
                if (_DraftBuffer.MoveHome())
                {
                    RenderInteractiveChrome();
                }
                return true;
            }

            if (keyInfo.Key == ConsoleKey.End)
            {
                if (_DraftBuffer.MoveEnd())
                {
                    RenderInteractiveChrome();
                }
                return true;
            }

            if (keyInfo.Key == ConsoleKey.Delete)
            {
                if (_DraftBuffer.Delete())
                {
                    _PromptHistory.ResetNavigation();
                    RenderInteractiveChrome();
                }
                return true;
            }

            if (keyInfo.Key == ConsoleKey.Backspace)
            {
                if (_DraftBuffer.IsCursorAtStart && _DraftBuffer.CurrentLineIndex > 0)
                {
                    _PromptHistory.ResetNavigation();
                    _DraftBuffer.RemoveCurrentLineAndMergeUp();
                    RenderInteractiveChrome();
                }
                else if (_DraftBuffer.Backspace())
                {
                    _PromptHistory.ResetNavigation();
                    RenderInteractiveChrome();
                }
                return true;
            }

            if (keyInfo.Key == ConsoleKey.Escape)
            {
                return true;
            }

            if (!allowPromptHistory
                && (keyInfo.Key == ConsoleKey.UpArrow || keyInfo.Key == ConsoleKey.DownArrow))
            {
                return true;
            }

            if (keyInfo.KeyChar != '\0' && !char.IsControl(keyInfo.KeyChar))
            {
                _PromptHistory.ResetNavigation();
                _DraftBuffer.Insert(keyInfo.KeyChar);
                RenderInteractiveChrome();
                return true;
            }

            return false;
        }

        private void HandleCtrlC(bool busyMode)
        {
            HandleUserCancellationRequest(busyMode);
        }

        private void HandleConsoleCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            HandleUserCancellationRequest(_ActiveRun != null || _PendingApproval != null);
        }

        private void HandleUserCancellationRequest(bool busyMode)
        {
            if (_ShouldExit)
            {
                return;
            }

            if (busyMode || _ActiveRun != null)
            {
                if (_CurrentCts?.IsCancellationRequested == true)
                {
                    return;
                }

                CancelActiveRun("Cancellation requested via Ctrl+C");
                return;
            }

            DateTime now = DateTime.UtcNow;
            bool draftIsEmpty = string.IsNullOrWhiteSpace(_DraftBuffer.GetText());
            bool exitRequested = draftIsEmpty && (now - _LastCtrlCTime).TotalSeconds <= 2.0;

            if (exitRequested)
            {
                _ShouldExit = true;
                WriteOutputBlock(() => AnsiConsole.MarkupLine("[dim]Exiting due to user cancellation.[/]"), renderChromeAfterWrite: false);
                return;
            }

            _LastCtrlCTime = now;

            if (!draftIsEmpty)
            {
                _DraftBuffer.Clear();
                _PromptHistory.ResetNavigation();
                SetStatusNotice("draft cleared; press Ctrl+C again to exit");
                return;
            }

            SetStatusNotice("press Ctrl+C again to exit");
        }

        private void SubmitCurrentDraft()
        {
            string input = _DraftBuffer.GetText();
            string trimmed = input.Trim();

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return;
            }

            _DraftBuffer.Clear();
            _PromptHistory.ResetNavigation();

            if (trimmed.StartsWith("/", StringComparison.Ordinal))
            {
                WriteSubmittedPrompt(input);
            }

            if (HandleSlashCommand(trimmed))
            {
                RenderInteractiveChrome();
                return;
            }

            WriteSubmittedPrompt(input);
            StartRun(trimmed);
        }

        private void QueueCurrentDraft()
        {
            string draftText = _DraftBuffer.GetText();
            string trimmed = draftText.Trim();

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return;
            }

            if (trimmed.StartsWith("/", StringComparison.Ordinal))
            {
                SetStatusNotice("press Enter to run slash commands immediately while mux is busy");
                return;
            }

            QueuedMessageEntry entry = _QueuedMessages.Enqueue(trimmed);
            _DraftBuffer.Clear();
            _PromptHistory.ResetNavigation();
            SetStatusNotice($"queued #{entry.SequenceNumber}: {TruncateString(trimmed, 40)}");
        }

        private bool TryExecuteBusySlashCommand()
        {
            string input = _DraftBuffer.GetText();
            string trimmed = input.Trim();

            if (!trimmed.StartsWith("/", StringComparison.Ordinal))
            {
                return false;
            }

            _DraftBuffer.Clear();
            _PromptHistory.ResetNavigation();
            WriteSubmittedPrompt(input);

            if (HandleSlashCommand(trimmed, allowBusyCommandsOnly: true))
            {
                RenderInteractiveChrome();
            }

            return true;
        }

        private void EditLastQueuedPrompt()
        {
            if (_QueuedMessages.Count == 0)
            {
                SetStatusNotice("no queued prompts to edit");
                return;
            }

            string currentDraft = _DraftBuffer.GetText();
            if (string.IsNullOrWhiteSpace(currentDraft))
            {
                if (_QueuedMessages.TryTakeLast(out QueuedMessageEntry last))
                {
                    _DraftBuffer.SetText(last.Text);
                    RenderInteractiveChrome();
                }

                return;
            }

            if (_QueuedMessages.TryReplaceLast(currentDraft, out QueuedMessageEntry previous))
            {
                _DraftBuffer.SetText(previous.Text);
                RenderInteractiveChrome();
            }
        }

        private void CancelActiveRun(string? statusNotice = null)
        {
            if (_ActiveRun == null)
            {
                return;
            }

            if (_CurrentCts?.IsCancellationRequested == true)
            {
                return;
            }

            _QueuePaused = true;
            _RetryStatusMessage = "Cancelling current run...";

            if (!string.IsNullOrWhiteSpace(statusNotice))
            {
                _TransientStatusNotice = statusNotice;
                _TransientStatusNoticeExpiresUtc = DateTime.UtcNow.AddMilliseconds(2500);
            }

            if (_CurrentCts?.IsCancellationRequested != true)
            {
                _CurrentCts?.Cancel();
            }

            RenderInteractiveChrome();
        }

        private void StartRun(string prompt)
        {
            if (!EnsureConversationFitsForPrompt(prompt))
            {
                return;
            }

            EndpointConfig runEndpoint = CreateInteractiveRunEndpoint();
            _PromptHistory.Add(prompt);
            _CurrentCts?.Dispose();
            _CurrentCts = new CancellationTokenSource();
            _RetryStatusMessage = null;
            _PendingApproval = null;
            _RunHasVisibleOutput = false;
            ClearStatusNotice();

            AgentLoopOptions loopOptions = new AgentLoopOptions(runEndpoint)
            {
                ConversationHistory = _ConversationHistory,
                SystemPrompt = _SystemPrompt,
                ApprovalPolicy = _ApprovalPolicy,
                WorkingDirectory = _WorkingDirectory,
                MaxIterations = _MuxSettings.MaxAgentIterations,
                Verbose = _Verbose,
                TokenEstimationRatio = _MuxSettings.TokenEstimationRatio,
                ContextWindowSafetyMarginPercent = _MuxSettings.ContextWindowSafetyMarginPercent,
                AutoCompactEnabled = _MuxSettings.AutoCompactEnabled,
                ContextWarningThresholdPercent = _MuxSettings.ContextWarningThresholdPercent,
                CompactionStrategy = _MuxSettings.CompactionStrategy,
                CompactionPreserveTurns = _MuxSettings.CompactionPreserveTurns,
                PromptUserFunc = RequestApprovalAsync,
                AdditionalTools = _McpToolManager?.GetToolDefinitions(),
                ExternalToolExecutor = _McpToolManager != null
                    ? (Func<string, JsonElement, string, CancellationToken, Task<ToolResult>>)(async (string toolName, JsonElement arguments, string workDir, CancellationToken ct) =>
                    {
                        return await _McpToolManager.ExecuteAsync(toolName, toolName, arguments, ct).ConfigureAwait(false);
                    })
                    : null,
                OnRetry = (int attempt, int maxRetries, string message) =>
                {
                    _RetryStatusMessage = $"Connection failed, retrying ({attempt}/{maxRetries})...";
                }
            };

            Channel<AgentEvent> channel = Channel.CreateUnbounded<AgentEvent>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true
            });

            Task<RunExecutionResult> completionTask = Task.Run(async () =>
            {
                StringBuilder assistantResponse = new StringBuilder();

                try
                {
                    using AgentLoop agentLoop = new AgentLoop(loopOptions);
                    await foreach (AgentEvent evt in agentLoop.RunAsync(prompt, _CurrentCts.Token).ConfigureAwait(false))
                    {
                        if (evt is AssistantTextEvent textEvent)
                        {
                            assistantResponse.Append(textEvent.Text);
                        }

                        await channel.Writer.WriteAsync(evt).ConfigureAwait(false);
                    }

                    return new RunExecutionResult
                    {
                        Prompt = prompt,
                        AssistantResponse = assistantResponse.ToString()
                    };
                }
                catch (OperationCanceledException)
                {
                    return new RunExecutionResult
                    {
                        Prompt = prompt,
                        AssistantResponse = assistantResponse.ToString(),
                        Cancelled = true
                    };
                }
                catch (Exception ex)
                {
                    return new RunExecutionResult
                    {
                        Prompt = prompt,
                        AssistantResponse = assistantResponse.ToString(),
                        Error = ex
                    };
                }
                finally
                {
                    channel.Writer.TryComplete();
                }
            });

            _ActiveRun = new ActiveRunState
            {
                Prompt = prompt,
                Events = channel,
                CompletionTask = completionTask
            };

            _AssistantTextOpen = false;
            WriteThinkingLine();
            RenderInteractiveChrome();
        }

        private void DrainRunEvents()
        {
            if (_ActiveRun == null)
            {
                return;
            }

            ActiveRunState activeRun = _ActiveRun;
            while (activeRun.Events.Reader.TryRead(out AgentEvent? agentEvent))
            {
                if (agentEvent == null)
                {
                    continue;
                }

                _RetryStatusMessage = null;

                RenderAgentEvent(agentEvent);
            }
        }

        private async Task FinalizeActiveRunAsync()
        {
            if (_ActiveRun == null)
            {
                return;
            }

            RunExecutionResult result = await _ActiveRun.CompletionTask.ConfigureAwait(false);
            ActiveRunState activeRun = _ActiveRun;
            _ActiveRun = null;
            _PendingApproval = null;
            _RetryStatusMessage = null;
            ClearStatusNotice();
            bool cancellationWasRequested = _CurrentCts?.IsCancellationRequested == true;

            _CurrentCts?.Dispose();
            _CurrentCts = null;

            bool treatAsCancellation = result.Cancelled
                || (cancellationWasRequested && IsCancellationException(result.Error));

            if (result.Error != null && !treatAsCancellation)
            {
                WriteMarkupLine($"[red]Error: {Markup.Escape(result.Error.Message)}[/]");
                if (_QueuedMessages.Count > 0)
                {
                    _QueuePaused = true;
                    WriteFailureLine($"Current run failed. Queue paused with {_QueuedMessages.Count} queued messages remaining.");
                }
                else
                {
                    _QueuePaused = false;
                }
            }
            else if (treatAsCancellation)
            {
                if (_QueuedMessages.Count > 0)
                {
                    _QueuePaused = true;
                    WriteNotificationLine($"Current run cancelled by user. Queue paused with {_QueuedMessages.Count} queued messages remaining.");
                }
                else
                {
                    _QueuePaused = false;
                    WriteNotificationLine("Current run cancelled by user.");
                }
            }
            else
            {
                _ConversationHistory.Add(new ConversationMessage
                {
                    Role = RoleEnum.User,
                    Content = result.Prompt
                });

                if (!string.IsNullOrEmpty(result.AssistantResponse))
                {
                    _ConversationHistory.Add(new ConversationMessage
                    {
                        Role = RoleEnum.Assistant,
                        Content = result.AssistantResponse
                    });
                }
                else if (!_RunHasVisibleOutput)
                {
                    WriteNotificationLine("Run completed without visible output.");
                }

                _TurnsSinceLastTitleReview++;

                if (!_ConversationTitleSetByUser && _QueuedMessages.Count == 0)
                {
                    await MaybeRefreshConversationTitleAsync().ConfigureAwait(false);
                }

                MaybeWritePostTurnContextNotice();
            }

            if (_QueuedMessages.Count == 0 && _QueuePaused)
            {
                _QueuePaused = false;
            }

            RenderInteractiveChrome();
        }

        private void RenderAgentEvent(AgentEvent agentEvent)
        {
            switch (agentEvent)
            {
                case AssistantTextEvent textEvent:
                    _RunHasVisibleOutput = true;
                    WriteAssistantTextChunk(textEvent.Text);
                    break;

                case ToolCallProposedEvent proposedEvent:
                    _RunHasVisibleOutput = true;
                    string proposedSummary = ToolCallRenderer.FormatToolSummary(
                        proposedEvent.ToolCall.Name,
                        proposedEvent.ToolCall.Arguments);
                    WriteNotificationLine($"Tool call: {proposedSummary}");
                    break;

                case ToolCallApprovedEvent approvedEvent:
                    break;

                case ToolCallCompletedEvent completedEvent:
                    _RunHasVisibleOutput = true;
                    string summary = SummarizeToolResult(completedEvent.Result.Content);
                    string line = completedEvent.Result.Success
                        ? $"Tool {completedEvent.ToolName}: {summary} ok {completedEvent.ElapsedMs}ms"
                        : $"Tool {completedEvent.ToolName}: {summary} failed {completedEvent.ElapsedMs}ms";
                    if (completedEvent.Result.Success)
                    {
                        WriteSuccessLine(line);
                    }
                    else
                    {
                        WriteFailureLine(line);
                    }
                    break;

                case ErrorEvent errorEvent:
                    _RunHasVisibleOutput = true;
                    WriteFailureLine($"Error: {errorEvent.Code}: {errorEvent.Message}");
                    break;

                case ContextStatusEvent contextStatusEvent:
                    _RunHasVisibleOutput = true;
                    string contextLine =
                        $"Context usage: est. {FormatTokenEstimate(contextStatusEvent.EstimatedTokens)} / {FormatTokenEstimate(contextStatusEvent.UsableInputLimit)} used | {FormatTokenEstimate(contextStatusEvent.RemainingTokens)} remaining.";
                    if (string.Equals(contextStatusEvent.WarningLevel, "critical", StringComparison.Ordinal))
                    {
                        WriteFailureLine(contextLine);
                    }
                    else if (string.Equals(contextStatusEvent.WarningLevel, "approaching", StringComparison.Ordinal))
                    {
                        WriteNotificationLine(contextLine);
                    }
                    break;

                case ContextCompactedEvent compactedEvent:
                    _RunHasVisibleOutput = true;
                    WriteNotificationLine(
                        $"Auto-compacted context ({compactedEvent.Strategy}): est. {FormatTokenEstimate(compactedEvent.EstimatedTokensBefore)} -> {FormatTokenEstimate(compactedEvent.EstimatedTokensAfter)}.");
                    break;

                case HeartbeatEvent:
                    break;

                default:
                    break;
            }
        }

        private Task<string> RequestApprovalAsync(ToolCall toolCall)
        {
            string summary = ToolCallRenderer.FormatToolSummary(toolCall.Name, toolCall.Arguments);
            TaskCompletionSource<string> completionSource = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            _PendingApproval = new ApprovalRequestState
            {
                ToolCall = toolCall,
                Summary = summary,
                CompletionSource = completionSource
            };

            RenderInteractiveChrome();
            return completionSource.Task;
        }

        private void ResolveApproval(string response)
        {
            if (_PendingApproval == null)
            {
                return;
            }

            ApprovalRequestState pending = _PendingApproval;
            _PendingApproval = null;
            pending.CompletionSource.TrySetResult(response);
            SetStatusNotice(BuildApprovalNotice(response, pending.ToolCall.Name), 1500);
        }

        private void WriteAssistantTextChunk(string text)
        {
            lock (_ConsoleSync)
            {
                BeginOutputWrite(closeAssistantText: false);
                Console.Write(text);
                _AssistantTextOpen = !EndsWithLineBreak(text);
                EndOutputWrite();
            }
        }

        private void WriteMarkupLine(string markup)
        {
            WriteOutputBlock(() =>
            {
                AnsiConsole.MarkupLine(markup);
                Console.WriteLine();
            }, outputEndsWithPromptSpacer: true);
        }

        private void WritePlainLine(string line)
        {
            WriteOutputBlock(() => Console.WriteLine(line));
        }

        private void WriteNotificationLine(string line)
        {
            WriteMarkupLine($"[dim]{Markup.Escape(line)}[/]");
        }

        private void WriteSuccessLine(string line)
        {
            WriteMarkupLine($"[green]{Markup.Escape(line)}[/]");
        }

        private void WriteFailureLine(string line)
        {
            WriteMarkupLine($"[red]{Markup.Escape(line)}[/]");
        }

        private void WriteSubmittedPrompt(string input)
        {
            WriteOutputBlock(() =>
            {
                string normalized = input
                    .Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Replace("\r", "\n", StringComparison.Ordinal);

                string[] lines = normalized.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    WritePrompt(i);
                    Console.WriteLine(lines[i]);
                }
            }, renderChromeAfterWrite: false, startAtPrompt: true);
        }

        private void WriteThinkingLine()
        {
            WriteOutputBlock(() =>
            {
                AnsiConsole.MarkupLine($"[dim]{ThinkingText}[/]");
                Console.WriteLine();
            }, renderChromeAfterWrite: false, outputEndsWithPromptSpacer: true);
        }

        private void RenderWelcomeScreen()
        {
            string configDir = SettingsLoader.GetConfigDirectory();
            string endpointsPath = Path.Combine(configDir, "endpoints.json");
            const string logo = @" _____ _ _ _ _
|     | | |_'_|
|_|_|_|___|_,_|
";

            AnsiConsole.Markup($"[bold cyan]{logo}[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[bold cyan]{Defaults.ProductName}[/] [dim]v{Defaults.ProductVersion}[/] [dim]|[/] [dim]AI agent for local and remote LLMs[/]");
            AnsiConsole.MarkupLine($"[bold]Title:[/] {Markup.Escape(_ConversationTitle)}");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[dim]Using endpoints defined in: {Markup.Escape(endpointsPath)}[/]");
            AnsiConsole.MarkupLine($"[dim]Endpoint:[/] {Markup.Escape(_CurrentEndpoint.Name)} [dim]|[/] [dim]Model:[/] {Markup.Escape(_CurrentEndpoint.Model)}");
            AnsiConsole.MarkupLine("[dim]Type /help for commands, Tab queues while busy, Alt+Up edits the last queued prompt, Esc cancels active generation.[/]");
        }

        private void WriteOutputBlock(
            Action writer,
            bool renderChromeAfterWrite = true,
            bool startAtPrompt = false,
            bool outputEndsWithPromptSpacer = false)
        {
            lock (_ConsoleSync)
            {
                BeginOutputWrite(startAtPrompt: startAtPrompt);
                writer();
                _AssistantTextOpen = false;
                EndOutputWrite(renderChromeAfterWrite, outputEndsWithPromptSpacer);
            }
        }

        private void BeginOutputWrite(bool closeAssistantText = true, bool startAtPrompt = false)
        {
            ClearInteractiveChrome();

            if (startAtPrompt)
            {
                SetCursorPositionSafe(0, _PromptTop);
            }
            else
            {
                SetCursorPositionSafe(_OutputCursorLeft, _OutputCursorTop);
            }

            if (closeAssistantText && _AssistantTextOpen && !startAtPrompt)
            {
                Console.WriteLine();
                _AssistantTextOpen = false;
            }
        }

        private void EndOutputWrite(bool renderChromeAfterWrite = true, bool outputEndsWithPromptSpacer = false)
        {
            _OutputCursorLeft = Console.CursorLeft;
            _OutputCursorTop = Console.CursorTop;
            _OutputEndsWithPromptSpacer = outputEndsWithPromptSpacer;
            _RenderedPromptRowCount = 0;
            _ChromeTop = _PromptTop;

            if (renderChromeAfterWrite)
            {
                RenderInteractiveChrome();
            }
        }

        private void ClearInteractiveChrome()
        {
            if (_RenderedPromptRowCount > 0)
            {
                ClearRows(_ChromeTop, _RenderedPromptRowCount);
            }
        }

        private void RenderInteractiveChrome()
        {
            lock (_ConsoleSync)
            {
                bool shouldShowCursor = ShouldShowInteractiveCursor();
                if (!shouldShowCursor)
                {
                    SetCursorVisibleSafe(false);
                }

                try
                {
                    PromptLayout promptLayout = GetPromptLayout();
                    int nextOutputRow = GetNextAvailableOutputRow();
                    string statusText = TruncateToConsoleWidth(BuildStatusText(), Math.Max(1, GetBufferWidthSafe() - 1));
                    bool hasStatusLine = !string.IsNullOrWhiteSpace(statusText);
                    int promptTopPadding = (_ActiveRun != null || _OutputEndsWithPromptSpacer) ? 0 : PromptTopPaddingLines;
                    int chromeTop = hasStatusLine
                        ? nextOutputRow
                        : nextOutputRow + promptTopPadding;
                    int promptTop = hasStatusLine
                        ? chromeTop + 1 + PromptSpacingBelowStatusLines
                        : chromeTop;

                    if (_RenderedPromptRowCount > 0)
                    {
                        ClearRows(_ChromeTop, _RenderedPromptRowCount);
                    }

                    _ChromeTop = chromeTop;
                    _PromptTop = promptTop;

                    if (hasStatusLine)
                    {
                        ClearRows(_ChromeTop, 1);
                        SetCursorPositionSafe(0, _ChromeTop);
                        AnsiConsole.Markup($"[dim]{Markup.Escape(statusText)}[/]");
                    }

                    for (int lineIndex = 0; lineIndex < _DraftBuffer.LineCount; lineIndex++)
                    {
                        SetCursorPositionSafe(0, _PromptTop + promptLayout.LineRowOffsets[lineIndex]);
                        WritePrompt(lineIndex);
                        Console.Write(_DraftBuffer.GetLine(lineIndex));
                    }

                    SetCursorPositionSafe(promptLayout.CursorColumn, _PromptTop + promptLayout.CursorRowOffset);
                    _RenderedPromptRowCount = promptLayout.TotalRows + (hasStatusLine ? 1 + PromptSpacingBelowStatusLines : 0);
                    CaptureConsoleWidth();
                }
                finally
                {
                    SetCursorVisibleSafe(shouldShowCursor);
                }
            }
        }

        private string BuildStatusText()
        {
            string baseInfo = $"model={_CurrentEndpoint.Model} | endpoint={_CurrentEndpoint.Name}";

            if (_PendingApproval != null)
            {
                return $"Approval required | {baseInfo} | {TruncateString(_PendingApproval.Summary, 80)} | Y / n / always";
            }

            if (_ActiveRun != null)
            {
                if (!string.IsNullOrWhiteSpace(_RetryStatusMessage))
                {
                    return $"Busy | {baseInfo} | {_RetryStatusMessage}";
                }

                if (TryGetStatusNotice(out string busyNotice))
                {
                    return $"Busy | {baseInfo} | {busyNotice}";
                }

                return string.Empty;
            }

            if (_QueuePaused && _QueuedMessages.Count > 0)
            {
                if (TryGetStatusNotice(out string pausedNotice))
                {
                    return $"Queue paused | {baseInfo} | {pausedNotice}";
                }

                return $"Queue paused | {baseInfo} | {_QueuedMessages.Count} queued | /queue resume";
            }

            if (TryGetStatusNotice(out string readyNotice))
            {
                return readyNotice;
            }

            return string.Empty;
        }

        private PromptLayout GetPromptLayout()
        {
            return InteractiveChromeLayout.Calculate(_DraftBuffer, PromptWidth, GetBufferWidthSafe());
        }

        private ContextBudgetSnapshot GetContextBudgetSnapshot(
            List<ConversationMessage>? historyOverride = null,
            string? pendingPrompt = null)
        {
            List<ConversationMessage> messages = historyOverride != null
                ? new List<ConversationMessage>(historyOverride)
                : new List<ConversationMessage>(_ConversationHistory);

            if (!string.IsNullOrWhiteSpace(pendingPrompt))
            {
                messages.Add(new ConversationMessage
                {
                    Role = RoleEnum.User,
                    Content = pendingPrompt
                });
            }

            ContextWindowManager manager = new ContextWindowManager(
                _CurrentEndpoint.ContextWindow,
                _MuxSettings.TokenEstimationRatio,
                _MuxSettings.ContextWindowSafetyMarginPercent);

            return manager.GetBudgetSnapshot(
                _SystemPrompt,
                messages,
                GetAllToolDefinitions(),
                _CurrentEndpoint.MaxTokens,
                _MuxSettings.ContextWarningThresholdPercent);
        }

        private bool EnsureConversationFitsForPrompt(string prompt)
        {
            ContextBudgetSnapshot preflightSnapshot = GetContextBudgetSnapshot(pendingPrompt: prompt);

            if (!preflightSnapshot.IsApproachingLimit)
            {
                return true;
            }

            if (!preflightSnapshot.IsOverLimit)
            {
                WriteNotificationLine(
                    $"Context nearing the usable limit: est. {FormatTokenEstimate(preflightSnapshot.UsedTokens)} / {FormatTokenEstimate(preflightSnapshot.UsableInputLimit)} used. Older history will be compacted automatically if needed.");
                return true;
            }

            if (!_MuxSettings.AutoCompactEnabled)
            {
                WriteFailureLine(
                    $"Prompt exceeds the usable context budget: est. {FormatTokenEstimate(preflightSnapshot.UsedTokens)} / {FormatTokenEstimate(preflightSnapshot.UsableInputLimit)} used. Use /compact or /clear before retrying.");
                return false;
            }

            return TryAutoCompactForPrompt(prompt, preflightSnapshot);
        }

        private void MaybeWritePostTurnContextNotice()
        {
            ContextBudgetSnapshot snapshot = GetContextBudgetSnapshot();

            if (snapshot.IsOverLimit)
            {
                WriteFailureLine(
                    $"Context is over the usable limit: est. {FormatTokenEstimate(snapshot.UsedTokens)} / {FormatTokenEstimate(snapshot.UsableInputLimit)} used. mux will compact older history before the next run.");
                return;
            }

            if (snapshot.IsApproachingLimit)
            {
                WriteNotificationLine(
                    $"Context usage: est. {FormatTokenEstimate(snapshot.UsedTokens)} / {FormatTokenEstimate(snapshot.UsableInputLimit)} used | {FormatTokenEstimate(snapshot.RemainingTokens)} remaining.");
            }
        }

        private bool TryAutoCompactForPrompt(string prompt, ContextBudgetSnapshot beforeSnapshot)
        {
            string strategy = _MuxSettings.CompactionStrategy;
            List<ConversationMessage> workingHistory = new List<ConversationMessage>(_ConversationHistory);
            bool usedSummary = false;
            bool usedTrim = false;
            bool usedTrimFallback = false;

            try
            {
                if (string.Equals(strategy, "summary", StringComparison.Ordinal))
                {
                    SetStatusNotice("compacting conversation...", 30000);
                    if (TryCreateSummaryCompactedHistory(workingHistory, out List<ConversationMessage> summaryHistory, out string? summaryFailure))
                    {
                        workingHistory = summaryHistory;
                        usedSummary = true;
                    }
                    else if (!string.IsNullOrWhiteSpace(summaryFailure))
                    {
                        usedTrimFallback = true;
                    }
                    else
                    {
                        WriteFailureLine(
                            $"Prompt exceeds the usable context budget: est. {FormatTokenEstimate(beforeSnapshot.UsedTokens)} / {FormatTokenEstimate(beforeSnapshot.UsableInputLimit)} used. No older history is eligible for automatic compaction.");
                        return false;
                    }
                }

                ContextBudgetSnapshot afterSummarySnapshot = GetContextBudgetSnapshot(workingHistory, pendingPrompt: prompt);
                int targetUsedTokens = Math.Max(
                    1,
                    (int)(afterSummarySnapshot.UsableInputLimit * (PreflightCompactionTargetPercent / 100.0)));

                if (string.Equals(strategy, "trim", StringComparison.Ordinal) || afterSummarySnapshot.IsOverLimit)
                {
                    ConversationTrimResult trimResult = ConversationTrimCompactor.TrimToTarget(
                        workingHistory,
                        _MuxSettings.CompactionPreserveTurns,
                        targetUsedTokens,
                        candidateHistory => GetContextBudgetSnapshot(candidateHistory, pendingPrompt: prompt).UsedTokens);

                    if (trimResult.DidTrim)
                    {
                        workingHistory = trimResult.CompactedHistory;
                        usedTrim = true;
                    }
                    else if (!usedSummary)
                    {
                        WriteFailureLine(
                            $"Prompt exceeds the usable context budget: est. {FormatTokenEstimate(beforeSnapshot.UsedTokens)} / {FormatTokenEstimate(beforeSnapshot.UsableInputLimit)} used. No older history is eligible for automatic compaction.");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                WriteFailureLine($"Automatic compaction failed: {ex.Message}");
                return false;
            }
            finally
            {
                ClearStatusNotice();
            }

            ContextBudgetSnapshot afterSnapshot = GetContextBudgetSnapshot(workingHistory, pendingPrompt: prompt);

            if (afterSnapshot.IsOverLimit)
            {
                WriteFailureLine(
                    $"Prompt still exceeds the usable context budget after compaction: est. {FormatTokenEstimate(afterSnapshot.UsedTokens)} / {FormatTokenEstimate(afterSnapshot.UsableInputLimit)} used. Use /clear or shorten the next prompt.");
                return false;
            }

            string strategyLabel = usedSummary && usedTrim
                ? "summary + trim"
                : usedSummary
                    ? "summary"
                    : usedTrimFallback
                        ? "trim fallback"
                        : "trim";

            CommitCompactedHistory(
                workingHistory,
                $"auto {strategyLabel}: {FormatTokenEstimate(beforeSnapshot.UsedTokens)} -> {FormatTokenEstimate(afterSnapshot.UsedTokens)}");

            WriteSuccessLine(
                $"Auto-compacted older history ({strategyLabel}): est. {FormatTokenEstimate(beforeSnapshot.UsedTokens)} -> {FormatTokenEstimate(afterSnapshot.UsedTokens)} before the next run.");
            return true;
        }

        private bool TryCreateSummaryCompactedHistory(
            List<ConversationMessage> history,
            out List<ConversationMessage> compactedHistory,
            out string? failureMessage)
        {
            ConversationCompactionPlan plan = ConversationCompactionPlanner.CreatePlan(
                history,
                _MuxSettings.CompactionPreserveTurns,
                SyntheticSummaryPrefix);

            if (!plan.CanCompact)
            {
                compactedHistory = new List<ConversationMessage>(history);
                failureMessage = null;
                return false;
            }

            try
            {
                string summary = GenerateCompactionSummary(plan.MessagesToCompact);
                if (string.IsNullOrWhiteSpace(summary))
                {
                    compactedHistory = new List<ConversationMessage>(history);
                    failureMessage = "Compaction produced no summary.";
                    return false;
                }

                compactedHistory = new List<ConversationMessage>
                {
                    new ConversationMessage
                    {
                        Role = RoleEnum.System,
                        Content = $"{SyntheticSummaryPrefix}{Environment.NewLine}{Environment.NewLine}{summary.Trim()}"
                    }
                };
                compactedHistory.AddRange(plan.MessagesToPreserve);
                failureMessage = null;
                return true;
            }
            catch (Exception ex)
            {
                compactedHistory = new List<ConversationMessage>(history);
                failureMessage = ex.Message;
                return false;
            }
        }

        private void CommitCompactedHistory(List<ConversationMessage> compactedHistory, string summary)
        {
            _ConversationHistory = compactedHistory ?? new List<ConversationMessage>();
            _CompactionCount++;
            _LastCompactionUtc = DateTime.UtcNow;
            _LastCompactionSummary = summary ?? string.Empty;
        }

        private List<ToolDefinition> GetAllToolDefinitions()
        {
            if (!(_CurrentEndpoint.Quirks?.SupportsTools ?? true))
            {
                return new List<ToolDefinition>();
            }

            BuiltInToolRegistry toolRegistry = new BuiltInToolRegistry();
            List<ToolDefinition> tools = toolRegistry.GetToolDefinitions();

            if (_McpToolManager != null)
            {
                tools.AddRange(_McpToolManager.GetToolDefinitions());
            }

            return tools;
        }

        private void ResetConversationState()
        {
            _ConversationHistory.Clear();
            _CompactionCount = 0;
            _LastCompactionSummary = string.Empty;
            _LastCompactionUtc = DateTime.MinValue;
            _TurnsSinceLastTitleReview = 0;
            ClearStatusNotice();

            if (!_ConversationTitleSetByUser)
            {
                _ConversationTitle = SessionTitleHelper.DefaultTitle;
            }
        }

        private void ResetInteractiveScreenForCurrentTitle()
        {
            lock (_ConsoleSync)
            {
                _RenderedPromptRowCount = 0;
                _OutputCursorLeft = 0;
                _OutputCursorTop = 0;
                _OutputEndsWithPromptSpacer = false;
                _ChromeTop = 0;
                _PromptTop = 0;
                _LastBufferWidth = -1;

                TryClearInteractiveScreen();
                RenderSessionTitleHeader();
                _ChromeTop = Console.CursorTop;
                _PromptTop = _ChromeTop;
                _OutputCursorTop = _PromptTop;
            }
        }

        private void RenderSessionTitleHeader()
        {
            AnsiConsole.MarkupLine($"[bold]Title:[/] {Markup.Escape(_ConversationTitle)}");
            Console.WriteLine();
        }

        private bool TryUpdateConversationTitle(string title, bool setByUser, bool emitUpdateMessage)
        {
            string normalizedTitle = SessionTitleHelper.Normalize(title, _ConversationTitle);
            bool changed = !string.Equals(_ConversationTitle, normalizedTitle, StringComparison.Ordinal);

            if (changed)
            {
                _ConversationTitle = normalizedTitle;

                if (emitUpdateMessage)
                {
                    WriteNotificationLine($"Conversation title update: {normalizedTitle}");
                }
            }

            if (setByUser)
            {
                _ConversationTitleSetByUser = true;
            }
            else if (changed)
            {
                _ConversationTitleSetByUser = false;
            }

            return changed;
        }

        private async Task MaybeRefreshConversationTitleAsync()
        {
            if (_ConversationTitleSetByUser)
            {
                return;
            }

            if (!HasEnoughContextForAutomaticTitleReview())
            {
                return;
            }

            bool shouldReview = string.Equals(_ConversationTitle, SessionTitleHelper.DefaultTitle, StringComparison.Ordinal)
                || _TurnsSinceLastTitleReview >= TitleReviewIntervalTurns;

            if (!shouldReview)
            {
                return;
            }

            try
            {
                string? generatedTitle = await GenerateConversationTitleAsync().ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(generatedTitle))
                {
                    TryUpdateConversationTitle(generatedTitle, setByUser: false, emitUpdateMessage: true);
                }
            }
            catch
            {
                // Title refresh is best-effort only.
            }
            finally
            {
                _TurnsSinceLastTitleReview = 0;
            }
        }

        private bool HasEnoughContextForAutomaticTitleReview()
        {
            int conversationalMessageCount = 0;
            int conversationalCharCount = 0;
            bool hasUserMessage = false;
            bool hasAssistantMessage = false;

            foreach (ConversationMessage message in _ConversationHistory)
            {
                if ((message.Role != RoleEnum.User && message.Role != RoleEnum.Assistant)
                    || string.IsNullOrWhiteSpace(message.Content))
                {
                    continue;
                }

                string content = message.Content.Trim();
                conversationalMessageCount++;
                conversationalCharCount += content.Length;
                hasUserMessage |= message.Role == RoleEnum.User;
                hasAssistantMessage |= message.Role == RoleEnum.Assistant;
            }

            return hasUserMessage
                && hasAssistantMessage
                && conversationalMessageCount >= MinimumTitleReviewMessages
                && conversationalCharCount >= MinimumTitleReviewChars;
        }

        private async Task<string?> GenerateConversationTitleAsync()
        {
            if (_ConversationHistory.Count == 0)
            {
                return null;
            }

            string conversationDigest = BuildConversationDigest(_ConversationHistory, maxChars: 5000);
            string systemPrompt =
                "You generate a short session title for a CLI coding conversation. " +
                "Return only the title text, 3 to 8 words, plain text, no quotes, no markdown, and no trailing punctuation.";
            string userPrompt =
                $"Current title: {_ConversationTitle}{Environment.NewLine}{Environment.NewLine}" +
                $"Conversation:{Environment.NewLine}{conversationDigest}";

            string title = await RunSidecarPromptAsync(systemPrompt, userPrompt, CancellationToken.None).ConfigureAwait(false);
            return SessionTitleHelper.Normalize(title, _ConversationTitle);
        }

        private string GenerateCompactionSummary(List<ConversationMessage> messagesToCompact)
        {
            string compactableDigest = BuildConversationDigest(messagesToCompact, maxChars: 12000);
            string systemPrompt =
                "You compact older conversation history for a coding agent. " +
                "Preserve goals, constraints, important files, decisions, errors, and unresolved work. " +
                "Return plain text only, concise but information-dense, suitable to carry forward as session memory.";
            string userPrompt =
                $"Compact this older conversation history:{Environment.NewLine}{Environment.NewLine}{compactableDigest}";

            string summary = RunSidecarPromptAsync(systemPrompt, userPrompt, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            return summary.Trim();
        }

        private async Task<string> RunSidecarPromptAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken)
        {
            EndpointConfig sidecarEndpoint = CreateSidecarEndpoint();
            using LlmClient client = new LlmClient(sidecarEndpoint);

            ConversationMessage response = await client.SendAsync(
                new List<ConversationMessage>
                {
                    new ConversationMessage
                    {
                        Role = RoleEnum.System,
                        Content = systemPrompt
                    },
                    new ConversationMessage
                    {
                        Role = RoleEnum.User,
                        Content = userPrompt
                    }
                },
                new List<ToolDefinition>(),
                cancellationToken).ConfigureAwait(false);

            return response.Content?.Trim() ?? string.Empty;
        }

        private EndpointConfig CreateSidecarEndpoint()
        {
            EndpointConfig sidecarEndpoint = CloneEndpoint(_CurrentEndpoint);
            sidecarEndpoint.Quirks ??= Defaults.QuirksForAdapter(sidecarEndpoint.AdapterType);
            sidecarEndpoint.Quirks.SupportsTools = false;
            sidecarEndpoint.Quirks.EnableMalformedToolCallRecovery = false;
            sidecarEndpoint.Temperature = 0.0;
            sidecarEndpoint.MaxTokens = Math.Min(sidecarEndpoint.MaxTokens, 2048);
            return sidecarEndpoint;
        }

        private static string BuildConversationDigest(IEnumerable<ConversationMessage> messages, int maxChars)
        {
            StringBuilder sb = new StringBuilder();

            foreach (ConversationMessage message in messages)
            {
                if (string.IsNullOrWhiteSpace(message.Content) && (message.ToolCalls == null || message.ToolCalls.Count == 0))
                {
                    continue;
                }

                sb.Append('[');
                sb.Append(message.Role.ToString().ToLowerInvariant());
                sb.Append("] ");

                if (!string.IsNullOrWhiteSpace(message.Content))
                {
                    sb.Append(message.Content.Trim());
                }
                else if (message.ToolCalls != null && message.ToolCalls.Count > 0)
                {
                    sb.Append("tool calls: ");
                    sb.Append(string.Join(", ", message.ToolCalls.Select(static tc => tc.Name)));
                }

                sb.AppendLine();
                sb.AppendLine();
            }

            string digest = sb.ToString().Trim();

            if (digest.Length <= maxChars)
            {
                return digest;
            }

            int half = Math.Max(1, (maxChars - 24) / 2);
            return digest.Substring(0, half).TrimEnd()
                + Environment.NewLine
                + "...[conversation truncated]..."
                + Environment.NewLine
                + digest.Substring(Math.Max(0, digest.Length - half)).TrimStart();
        }

        private static string FormatTokenEstimate(int tokens)
        {
            if (tokens >= 1000000)
            {
                return $"{tokens / 1000000.0:0.0}m";
            }

            if (tokens >= 1000)
            {
                return $"{tokens / 1000.0:0.0}k";
            }

            return tokens.ToString();
        }

        private bool ShouldShowInteractiveCursor()
        {
            return true;
        }

        private void SetStatusNotice(string message, int durationMs = 2000)
        {
            _TransientStatusNotice = message ?? string.Empty;
            _TransientStatusNoticeExpiresUtc = string.IsNullOrWhiteSpace(_TransientStatusNotice)
                ? DateTime.MinValue
                : DateTime.UtcNow.AddMilliseconds(Math.Max(1, durationMs));
            RenderInteractiveChrome();
        }

        private void ClearStatusNotice()
        {
            _TransientStatusNotice = string.Empty;
            _TransientStatusNoticeExpiresUtc = DateTime.MinValue;
        }

        private bool TryGetStatusNotice(out string message)
        {
            ClearExpiredStatusNotice();

            if (!string.IsNullOrWhiteSpace(_TransientStatusNotice))
            {
                message = _TransientStatusNotice;
                return true;
            }

            message = string.Empty;
            return false;
        }

        private bool ClearExpiredStatusNotice()
        {
            if (string.IsNullOrWhiteSpace(_TransientStatusNotice))
            {
                return false;
            }

            if (DateTime.UtcNow < _TransientStatusNoticeExpiresUtc)
            {
                return false;
            }

            ClearStatusNotice();
            return true;
        }

        private static string BuildApprovalNotice(string response, string toolName)
        {
            return response.ToLowerInvariant() switch
            {
                "y" => $"approved {toolName}",
                "always" => $"auto-approving {toolName}",
                "n" => $"denied {toolName}",
                _ => $"approval response sent for {toolName}"
            };
        }

        private static string TruncateToConsoleWidth(string value, int maxWidth)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxWidth)
            {
                return value;
            }

            if (maxWidth <= 3)
            {
                return value.Substring(0, Math.Max(0, maxWidth));
            }

            return value.Substring(0, maxWidth - 3) + "...";
        }

        private static string TruncateString(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value;
            }

            return value.Substring(0, maxLength - 3) + "...";
        }

        private static bool EndsWithLineBreak(string value)
        {
            return value.EndsWith("\n", StringComparison.Ordinal)
                || value.EndsWith("\r", StringComparison.Ordinal);
        }

        private static bool IsCancellationException(Exception? exception)
        {
            while (exception != null)
            {
                if (exception is OperationCanceledException || exception is TaskCanceledException)
                {
                    return true;
                }

                exception = exception.InnerException;
            }

            return false;
        }

        private static string SummarizeToolResult(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return "(empty)";
            }

            try
            {
                JsonDocument doc = JsonDocument.Parse(content);
                JsonElement root = doc.RootElement;

                if (root.TryGetProperty("success", out JsonElement successEl) && successEl.GetBoolean())
                {
                    if (root.TryGetProperty("file_path", out JsonElement pathEl))
                    {
                        string path = pathEl.GetString() ?? string.Empty;
                        string fileName = Path.GetFileName(path);
                        if (root.TryGetProperty("line_count", out JsonElement lineCountEl))
                        {
                            return $"{fileName} ({lineCountEl.GetInt32()} lines)";
                        }

                        if (root.TryGetProperty("edits_applied", out JsonElement editsAppliedEl))
                        {
                            return $"{fileName} ({editsAppliedEl.GetInt32()} edits)";
                        }

                        return fileName;
                    }

                    if (root.TryGetProperty("path", out JsonElement dirPathEl))
                    {
                        return Path.GetFileName(dirPathEl.GetString() ?? string.Empty) + "/";
                    }
                }

                if (root.TryGetProperty("success", out JsonElement failEl) && !failEl.GetBoolean())
                {
                    if (root.TryGetProperty("error", out JsonElement errorEl))
                    {
                        return errorEl.GetString() ?? "error";
                    }

                    if (root.TryGetProperty("message", out JsonElement messageEl))
                    {
                        return TruncateString(messageEl.GetString() ?? string.Empty, 120);
                    }
                }
            }
            catch
            {
                // Not JSON.
            }

            string normalized = content.Replace("\r\n", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
            return TruncateString(normalized, 120);
        }

        /// <summary>
        /// Clears the requested rows used by the live prompt chrome.
        /// </summary>
        /// <param name="top">The zero-based buffer row to start clearing from.</param>
        /// <param name="lineCount">The number of rows to clear.</param>
        private static void ClearRows(int top, int lineCount)
        {
            if (lineCount <= 0)
            {
                return;
            }

            int clearWidth = Math.Max(1, GetBufferWidthSafe() - 1);
            int clearEnd = top + lineCount - 1;
            EnsureBufferHeightForRow(clearEnd);

            for (int row = top; row <= clearEnd; row++)
            {
                SetCursorPositionSafe(0, row);
                Console.Write(new string(' ', clearWidth));
            }
        }

        /// <summary>
        /// Returns the first buffer row available for prompt rendering after the current output.
        /// </summary>
        /// <returns>The next available output row.</returns>
        private int GetNextAvailableOutputRow()
        {
            return _AssistantTextOpen || _OutputCursorLeft > 0
                ? _OutputCursorTop + 1
                : _OutputCursorTop;
        }

        /// <summary>
        /// Captures the current console width used by the interactive renderer.
        /// </summary>
        private void CaptureConsoleWidth()
        {
            _LastBufferWidth = GetBufferWidthSafe();
        }

        /// <summary>
        /// Determines whether the console width changed since the last render.
        /// </summary>
        /// <returns>True when the width changed.</returns>
        private bool HasConsoleWidthChanged()
        {
            return _LastBufferWidth != GetBufferWidthSafe();
        }

        /// <summary>
        /// Returns the current console buffer width, or a safe fallback.
        /// </summary>
        /// <returns>The buffer width.</returns>
        private static int GetBufferWidthSafe()
        {
            try
            {
                return Math.Max(1, Console.BufferWidth);
            }
            catch
            {
                return 80;
            }
        }

        private static bool GetCursorVisibleSafe()
        {
            if (!OperatingSystem.IsWindows())
            {
                return true;
            }

            try
            {
                return Console.CursorVisible;
            }
            catch
            {
                return true;
            }
        }

        private static void SetCursorVisibleSafe(bool visible)
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            try
            {
                if (Console.CursorVisible != visible)
                {
                    Console.CursorVisible = visible;
                }
            }
            catch
            {
                // Best effort only. Some terminals do not expose cursor visibility.
            }
        }

        private static void TryClearInteractiveScreen()
        {
            try
            {
                Console.Clear();
            }
            catch
            {
                // Best effort only. Some hosts do not support clearing.
            }
        }

        /// <summary>
        /// Ensures the console buffer can accommodate the requested row.
        /// </summary>
        /// <param name="row">The zero-based row that must exist in the buffer.</param>
        private static void EnsureBufferHeightForRow(int row)
        {
            if (row < 0 || !OperatingSystem.IsWindows())
            {
                return;
            }

            try
            {
                if (row < Console.BufferHeight)
                {
                    return;
                }

                int requiredHeight = row + 1;
                int minimumVisibleHeight = Console.WindowTop + Console.WindowHeight;
                if (requiredHeight < minimumVisibleHeight)
                {
                    requiredHeight = minimumVisibleHeight;
                }

                Console.BufferHeight = requiredHeight;
            }
            catch
            {
                // Best effort only. Some terminals restrict buffer resizing.
            }
        }

        /// <summary>
        /// Moves the cursor without throwing when the requested row falls on the current buffer edge.
        /// </summary>
        /// <param name="left">The zero-based column.</param>
        /// <param name="top">The zero-based row.</param>
        private static void SetCursorPositionSafe(int left, int top)
        {
            int safeTop = Math.Max(0, top);
            EnsureBufferHeightForRow(safeTop);

            try
            {
                int bufferWidth = Math.Max(1, Console.BufferWidth);
                int bufferHeight = Math.Max(1, Console.BufferHeight);
                int safeLeft = Math.Clamp(left, 0, bufferWidth - 1);
                safeTop = Math.Clamp(safeTop, 0, bufferHeight - 1);
                Console.SetCursorPosition(safeLeft, safeTop);
            }
            catch
            {
                // If the terminal rejects cursor movement, leave the cursor where it is.
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

        private EndpointConfig CreateInteractiveRunEndpoint()
        {
            EndpointConfig runEndpoint = CloneEndpoint(_CurrentEndpoint);
            runEndpoint.Quirks ??= Defaults.QuirksForAdapter(runEndpoint.AdapterType);
            runEndpoint.Quirks.EnableMalformedToolCallRecovery = false;
            return runEndpoint;
        }

        private static EndpointConfig CloneEndpoint(EndpointConfig endpoint)
        {
            return new EndpointConfig
            {
                Name = endpoint.Name,
                AdapterType = endpoint.AdapterType,
                BaseUrl = endpoint.BaseUrl,
                Model = endpoint.Model,
                IsDefault = endpoint.IsDefault,
                MaxTokens = endpoint.MaxTokens,
                Temperature = endpoint.Temperature,
                ContextWindow = endpoint.ContextWindow,
                TimeoutMs = endpoint.TimeoutMs,
                Headers = new Dictionary<string, string>(endpoint.Headers),
                Quirks = CloneBackendQuirks(endpoint.Quirks)
            };
        }

        private static BackendQuirks? CloneBackendQuirks(BackendQuirks? quirks)
        {
            if (quirks == null)
            {
                return null;
            }

            return new BackendQuirks
            {
                AssembleToolCallDeltas = quirks.AssembleToolCallDeltas,
                SupportsParallelToolCalls = quirks.SupportsParallelToolCalls,
                SupportsTools = quirks.SupportsTools,
                EnableMalformedToolCallRecovery = quirks.EnableMalformedToolCallRecovery,
                RequiresToolResultContentAsString = quirks.RequiresToolResultContentAsString,
                DefaultFinishReason = quirks.DefaultFinishReason,
                StripRequestFields = new List<string>(quirks.StripRequestFields)
            };
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
        /// <param name="allowBusyCommandsOnly">True to allow only the subset of slash commands that are safe while a run is active.</param>
        /// <returns>True if the input was a slash command and was handled; false otherwise.</returns>
        private bool HandleSlashCommand(string input, bool allowBusyCommandsOnly = false)
        {
            if (!input.StartsWith("/", StringComparison.Ordinal))
            {
                return false;
            }

            string[] parts = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            string command = parts[0].ToLowerInvariant();
            string argument = parts.Length > 1 ? parts[1] : string.Empty;

            if (allowBusyCommandsOnly && !CanExecuteSlashCommandWhileBusy(command, argument))
            {
                WriteNotificationLine("That command can't run while mux is busy. Use /queue, /status, /context, /compact strategy, /help, /title, /tools, /system, /endpoint, or /mcp list.");
                return true;
            }

            switch (command)
            {
                case "/exit":
                case "/quit":
                    _ShouldExit = true;
                    return true;

                case "/endpoint":
                    HandleEndpointCommand(argument);
                    return true;

                case "/tools":
                    HandleToolsCommand();
                    return true;

                case "/clear":
                    HandleClearCommand();
                    return true;

                case "/status":
                case "/context":
                    HandleStatusCommand();
                    return true;

                case "/compact":
                    HandleCompactCommand(argument);
                    return true;

                case "/title":
                    HandleTitleCommand(argument);
                    return true;

                case "/queue":
                    HandleQueueCommand(argument);
                    return true;

                case "/system":
                    HandleSystemCommand(argument);
                    return true;

                case "/mcp":
                    HandleMcpCommand(argument);
                    return true;

                case "/help":
                case "/?":
                    HandleHelpCommand();
                    return true;

                default:
                    WriteMarkupLine($"[yellow]Unknown command: {Markup.Escape(command)}. Type /help for available commands.[/]");
                    return true;
            }
        }

        /// <summary>
        /// Handles the /endpoint command.
        /// </summary>
        /// <param name="argument">The optional endpoint name to switch to.</param>
        private void HandleEndpointCommand(string argument)
        {
            if (string.IsNullOrWhiteSpace(argument))
            {
                if (_AllEndpoints.Count == 0)
                {
                    WriteMarkupLine($"[dim]Current:[/] {Markup.Escape(_CurrentEndpoint.Name)} [dim]([/]{Markup.Escape(_CurrentEndpoint.Model)}[dim])[/]");
                    WriteMarkupLine("[dim]No other endpoints configured.[/]");
                    return;
                }

                Table table = new Table();
                table.Border = TableBorder.Rounded;
                table.AddColumn("[bold]Endpoint[/]");
                table.AddColumn("[bold]Model[/]");
                table.AddColumn("[bold]Adapter[/]");
                table.AddColumn("[bold]URL[/]");

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
                    string urlDisplay = isCurrent
                        ? $"[green]{Markup.Escape(ep.BaseUrl)}[/]"
                        : Markup.Escape(ep.BaseUrl);
                    table.AddRow(nameDisplay, modelDisplay, adapterDisplay, urlDisplay);
                }

                WriteOutputBlock(() =>
                {
                    AnsiConsole.Write(table);
                    Console.WriteLine();
                });
            }
            else
            {
                if (!EnsureQueueEmptyForStateChange("switch endpoints"))
                {
                    return;
                }

                string targetName = argument.Trim();
                EndpointConfig? found = _AllEndpoints
                    .FirstOrDefault((EndpointConfig e) => string.Equals(e.Name, targetName, StringComparison.OrdinalIgnoreCase));

                if (found != null)
                {
                    _CurrentEndpoint = found;
                    ResetConversationState();
                    WriteMarkupLine(
                        $"[green]Switched to endpoint:[/] {Markup.Escape(found.Name)} [dim]([/]{Markup.Escape(found.Model)}[dim])[/] [dim]|[/] [dim]{Markup.Escape(found.AdapterType.ToString())}[/] [dim]|[/] {Markup.Escape(found.BaseUrl)}");
                    WriteMarkupLine("[dim]Conversation history cleared.[/]");
                }
                else
                {
                    _CurrentEndpoint.Model = targetName;
                    ResetConversationState();
                    WriteMarkupLine($"[green]Model changed to:[/] {Markup.Escape(targetName)}");
                    WriteMarkupLine("[dim]Conversation history cleared.[/]");
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

            WriteOutputBlock(() =>
            {
                AnsiConsole.Write(table);
                Console.WriteLine();
            });
        }

        /// <summary>
        /// Handles the /clear command by resetting the conversation history.
        /// </summary>
        private void HandleClearCommand()
        {
            if (!EnsureQueueEmptyForStateChange("clear the conversation"))
            {
                return;
            }

            ResetConversationState();
            ResetInteractiveScreenForCurrentTitle();
            SetStatusNotice("conversation history cleared");
        }

        /// <summary>
        /// Handles the /status command by displaying session metadata and estimated context usage.
        /// </summary>
        private void HandleStatusCommand()
        {
            ContextBudgetSnapshot snapshot = GetContextBudgetSnapshot();
            string titleSource = _ConversationTitleSetByUser ? "user" : "model";
            List<ToolDefinition> allTools = GetAllToolDefinitions();

            Table table = new Table();
            table.Border = TableBorder.Rounded;
            table.AddColumn("[bold]Field[/]");
            table.AddColumn("[bold]Value[/]");

            table.AddRow("Title", Markup.Escape(_ConversationTitle));
            table.AddRow("Title source", titleSource);
            table.AddRow("Endpoint", Markup.Escape(_CurrentEndpoint.Name));
            table.AddRow("Model", Markup.Escape(_CurrentEndpoint.Model));
            table.AddRow("Adapter", Markup.Escape(_CurrentEndpoint.AdapterType.ToString()));
            table.AddRow("Base URL", Markup.Escape(_CurrentEndpoint.BaseUrl));
            table.AddRow("Working directory", Markup.Escape(_WorkingDirectory));
            table.AddRow("Approval policy", Markup.Escape(_ApprovalPolicy.ToString()));
            table.AddRow("Conversation messages", _ConversationHistory.Count.ToString());
            table.AddRow("Queued prompts", _QueuedMessages.Count.ToString());
            table.AddRow("Queue paused", _QueuePaused ? "yes" : "no");
            table.AddRow("Auto title updates", _ConversationTitleSetByUser ? "disabled (user title)" : $"enabled ({TitleReviewIntervalTurns} successful turns)");
            table.AddRow("Auto compaction", _MuxSettings.AutoCompactEnabled ? "enabled" : "disabled");
            table.AddRow("Compaction strategy", Markup.Escape(_MuxSettings.CompactionStrategy));
            table.AddRow("Preserve turns", _MuxSettings.CompactionPreserveTurns.ToString());
            table.AddRow("Available tools", allTools.Count.ToString());
            table.AddRow("System prompt", $"{_SystemPrompt.Length} chars | est. {FormatTokenEstimate(snapshot.SystemPromptTokens)}");
            table.AddRow("History usage", FormatTokenEstimate(snapshot.MessageTokens));
            table.AddRow("Tool surface usage", FormatTokenEstimate(snapshot.ToolTokens));
            table.AddRow("Context usage", $"{FormatTokenEstimate(snapshot.UsedTokens)} / {FormatTokenEstimate(snapshot.UsableInputLimit)} used");
            table.AddRow("Context remaining", FormatTokenEstimate(snapshot.RemainingTokens));
            table.AddRow("Warning threshold", $"{FormatTokenEstimate(snapshot.WarningThresholdTokens)} ({_MuxSettings.ContextWarningThresholdPercent}%)");
            table.AddRow("Hard window", FormatTokenEstimate(snapshot.ContextWindowSize));
            table.AddRow("Reserved output", FormatTokenEstimate(snapshot.ReservedOutputTokens));
            table.AddRow("Safety margin", FormatTokenEstimate(snapshot.SafetyMarginTokens));
            table.AddRow("Compactions", _CompactionCount.ToString());
            table.AddRow("Last compaction", string.IsNullOrWhiteSpace(_LastCompactionSummary) ? "none" : Markup.Escape(_LastCompactionSummary));
            table.AddRow("Estimate scope", "system prompt + persisted history + tools");
            table.AddRow("Preflight behavior", "new prompts are checked before each run and compacted automatically when needed");

            WriteOutputBlock(() =>
            {
                AnsiConsole.MarkupLine($"[bold]Session title:[/] {Markup.Escape(_ConversationTitle)} [dim]({Markup.Escape(titleSource)})[/]");
                AnsiConsole.Write(table);
                Console.WriteLine();
            });
        }

        /// <summary>
        /// Handles the /compact command by summarizing or trimming older preserved history.
        /// </summary>
        /// <param name="argument">Optional compact subcommand.</param>
        private void HandleCompactCommand(string argument)
        {
            string trimmedArgument = argument?.Trim() ?? string.Empty;

            if (TryHandleCompactStrategyCommand(trimmedArgument))
            {
                return;
            }

            if (!EnsureQueueEmptyForStateChange("compact the conversation"))
            {
                return;
            }

            string strategy = _MuxSettings.CompactionStrategy;

            if (!string.IsNullOrWhiteSpace(trimmedArgument))
            {
                if (!MuxSettings.TryNormalizeCompactionStrategy(trimmedArgument, out strategy))
                {
                    WriteMarkupLine("[yellow]Usage: /compact, /compact [[summary|trim]], or /compact strategy [[summary|trim]][/]");
                    return;
                }
            }

            ContextBudgetSnapshot beforeSnapshot = GetContextBudgetSnapshot();

            if (string.Equals(strategy, "trim", StringComparison.Ordinal))
            {
                ConversationTrimResult trimResult = ConversationTrimCompactor.TrimAllEligible(
                    _ConversationHistory,
                    _MuxSettings.CompactionPreserveTurns,
                    candidateHistory => GetContextBudgetSnapshot(candidateHistory).UsedTokens);

                if (!trimResult.DidTrim)
                {
                    WriteMarkupLine("[dim]Nothing old enough to compact yet.[/]");
                    return;
                }

                CommitCompactedHistory(
                    trimResult.CompactedHistory,
                    $"manual trim: {FormatTokenEstimate(beforeSnapshot.UsedTokens)} -> {FormatTokenEstimate(trimResult.UsedTokensAfter)}");
                WriteSuccessLine(
                    $"Manual trim compaction complete: est. {FormatTokenEstimate(beforeSnapshot.UsedTokens)} -> {FormatTokenEstimate(trimResult.UsedTokensAfter)}.");
                return;
            }

            SetStatusNotice("compacting conversation...", 30000);

            if (!TryCreateSummaryCompactedHistory(_ConversationHistory, out List<ConversationMessage> compactedHistory, out string? failureMessage))
            {
                ClearStatusNotice();

                if (string.IsNullOrWhiteSpace(failureMessage))
                {
                    WriteMarkupLine("[dim]Nothing old enough to compact yet.[/]");
                }
                else
                {
                    WriteFailureLine($"Compaction failed: {failureMessage}");
                }
                return;
            }

            ClearStatusNotice();
            CommitCompactedHistory(
                compactedHistory,
                string.Empty);
            ContextBudgetSnapshot afterSnapshot = GetContextBudgetSnapshot();
            _LastCompactionSummary = $"manual summary: {FormatTokenEstimate(beforeSnapshot.UsedTokens)} -> {FormatTokenEstimate(afterSnapshot.UsedTokens)}";
            WriteSuccessLine($"Manual summary compaction complete: est. {FormatTokenEstimate(beforeSnapshot.UsedTokens)} -> {FormatTokenEstimate(afterSnapshot.UsedTokens)}.");
        }

        private bool TryHandleCompactStrategyCommand(string trimmedArgument)
        {
            if (string.IsNullOrWhiteSpace(trimmedArgument))
            {
                return false;
            }

            string[] parts = trimmedArgument.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (!string.Equals(parts[0], "strategy", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (parts.Length == 1)
            {
                WriteNotificationLine($"Compaction strategy: {_MuxSettings.CompactionStrategy}");
                return true;
            }

            string requestedStrategy = parts[1].Trim();
            if (!MuxSettings.TryNormalizeCompactionStrategy(requestedStrategy, out string normalizedStrategy))
            {
                WriteMarkupLine("[yellow]Usage: /compact strategy [[summary|trim]][/]");
                return true;
            }

            if (string.Equals(_MuxSettings.CompactionStrategy, normalizedStrategy, StringComparison.Ordinal))
            {
                WriteNotificationLine($"Compaction strategy is already set to {normalizedStrategy}.");
                return true;
            }

            _MuxSettings.CompactionStrategy = normalizedStrategy;
            WriteSuccessLine($"Compaction strategy set to {normalizedStrategy} for this session.");
            return true;
        }

        /// <summary>
        /// Handles the /title command by showing or updating the session title.
        /// </summary>
        /// <param name="argument">Optional new title text.</param>
        private void HandleTitleCommand(string argument)
        {
            if (string.IsNullOrWhiteSpace(argument))
            {
                WriteNotificationLine($"Conversation title: {_ConversationTitle} ({(_ConversationTitleSetByUser ? "user" : "model")})");
                return;
            }

            bool wasUserSet = _ConversationTitleSetByUser;
            string normalizedTitle = SessionTitleHelper.Normalize(argument, _ConversationTitle);
            bool titleChanged = TryUpdateConversationTitle(normalizedTitle, setByUser: true, emitUpdateMessage: true);

            if (!titleChanged && !wasUserSet)
            {
                WriteNotificationLine("Conversation title locked to the current value.");
            }
            else if (!titleChanged)
            {
                WriteNotificationLine("Conversation title is already set to that value.");
            }
        }

        /// <summary>
        /// Handles queue management commands.
        /// </summary>
        /// <param name="argument">Optional queue subcommand.</param>
        private void HandleQueueCommand(string argument)
        {
            string normalized = string.IsNullOrWhiteSpace(argument)
                ? "list"
                : argument.Trim().ToLowerInvariant();

            switch (normalized)
            {
                case "list":
                    if (_QueuedMessages.Count == 0)
                    {
                        WriteMarkupLine(_QueuePaused
                            ? "[dim]No queued prompts. Queue is paused.[/]"
                            : "[dim]No queued prompts.[/]");
                        return;
                    }

                    Table table = new Table();
                    table.Border = TableBorder.Rounded;
                    table.AddColumn("[bold]#[/]");
                    table.AddColumn("[bold]Queued[/]");
                    table.AddColumn("[bold]Preview[/]");

                    foreach (QueuedMessageEntry entry in _QueuedMessages.Snapshot())
                    {
                        table.AddRow(
                            entry.SequenceNumber.ToString(),
                            entry.EnqueuedAtUtc.ToLocalTime().ToString("HH:mm:ss"),
                            Markup.Escape(TruncateString(entry.Text, 80)));
                    }

                    WriteOutputBlock(() =>
                    {
                        AnsiConsole.MarkupLine(_QueuePaused
                            ? "[yellow]Queue paused[/]"
                            : "[green]Queue active[/]");
                        AnsiConsole.Write(table);
                        Console.WriteLine();
                    });
                    return;

                case "clear":
                    _QueuedMessages.Clear();
                    _QueuePaused = false;
                    WriteSuccessLine("Cleared all queued prompts.");
                    return;

                case "drop-last":
                    if (_QueuedMessages.TryTakeLast(out QueuedMessageEntry removed))
                    {
                        if (_QueuedMessages.Count == 0)
                        {
                            _QueuePaused = false;
                        }

                        WriteSuccessLine($"Removed #{removed.SequenceNumber} \"{TruncateString(removed.Text, 80)}\"");
                    }
                    else
                    {
                        WriteNotificationLine("No queued prompts to remove.");
                    }
                    return;

                case "resume":
                    if (_QueuedMessages.Count == 0)
                    {
                        _QueuePaused = false;
                        WriteNotificationLine("No queued prompts to resume.");
                        return;
                    }

                    _QueuePaused = false;
                    WriteSuccessLine($"Resumed. {_QueuedMessages.Count} queued prompts remain.");
                    return;

                default:
                    WriteMarkupLine("[yellow]Usage: /queue, /queue clear, /queue drop-last, /queue resume[/]");
                    return;
            }
        }

        /// <summary>
        /// Handles the /system command.
        /// </summary>
        /// <param name="argument">The optional new system prompt text.</param>
        private void HandleSystemCommand(string argument)
        {
            if (!string.IsNullOrWhiteSpace(argument))
            {
                if (!EnsureQueueEmptyForStateChange("change the system prompt"))
                {
                    return;
                }

                _SystemPrompt = argument;
                WriteMarkupLine("[green]System prompt updated for this session.[/]");
            }
            else
            {
                WriteOutputBlock(() =>
                {
                    AnsiConsole.MarkupLine($"[dim]System prompt ({_SystemPrompt.Length} chars):[/]");
                    Console.WriteLine(_SystemPrompt);
                    Console.WriteLine();
                });
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
            table.AddRow("[cyan]/status[/]", "Show session metadata, context usage, title, and queue state");
            table.AddRow("[cyan]/context[/]", "Alias for /status");
            table.AddRow("[cyan]/compact[/]", "Compact older conversation history with the configured strategy");
            table.AddRow("[cyan]/compact summary[/]", "Compact older conversation history with a one-off summary pass");
            table.AddRow("[cyan]/compact trim[/]", "Trim older conversation history without asking the model to summarize it");
            table.AddRow("[cyan]/compact strategy[/] [dim][[summary|trim]][/]", "Show or set the session compaction strategy");
            table.AddRow("[cyan]/title[/]", "Show the current conversation title");
            table.AddRow("[cyan]/title[/] [dim]<text>[/]", "Set the conversation title and disable automatic retitling");
            table.AddRow("[cyan]/clear[/]", "Clear conversation history");
            table.AddRow("[cyan]/queue[/]", "List queued prompts and whether dispatch is paused");
            table.AddRow("[cyan]/queue clear[/]", "Clear all queued prompts");
            table.AddRow("[cyan]/queue drop-last[/]", "Remove the newest queued prompt");
            table.AddRow("[cyan]/queue resume[/]", "Resume automatic queue dispatch");
            table.AddRow("[cyan]/system[/]", "Show the full current system prompt");
            table.AddRow("[cyan]/system[/] [dim]<text>[/]", "Replace system prompt for this session");
            table.AddRow("[cyan]/mcp list[/]", "List MCP server connections and status");
            table.AddRow("[cyan]/mcp add[/] [dim]<name> <cmd>[/]", "Add an MCP server at runtime");
            table.AddRow("[cyan]/mcp remove[/] [dim]<name>[/]", "Remove an MCP server");
            table.AddRow("[cyan]/exit[/]", "Exit mux");

            WriteOutputBlock(() =>
            {
                Console.WriteLine();
                AnsiConsole.MarkupLine($"[bold]Session title:[/] {Markup.Escape(_ConversationTitle)} [dim]({(_ConversationTitleSetByUser ? "user" : "model")})[/]");
                AnsiConsole.Write(table);
                Console.WriteLine();
                AnsiConsole.MarkupLine("[bold]Input:[/]");
                AnsiConsole.MarkupLine("  [dim]Enter[/]           Submit input when idle");
                AnsiConsole.MarkupLine("  [dim]Up / Down[/]       Recall submitted prompts when idle");
                AnsiConsole.MarkupLine("  [dim]Shift+Enter[/]     Insert newline");
                AnsiConsole.MarkupLine("  [dim]Ctrl+Enter[/]      Insert newline");
                AnsiConsole.MarkupLine("  [dim]Tab[/]             Queue the current draft while mux is busy");
                AnsiConsole.MarkupLine("  [dim]Enter on /command[/] Run supported slash commands immediately while busy");
                AnsiConsole.MarkupLine("  [dim]Alt+Up[/]          Edit the newest queued prompt");
                AnsiConsole.MarkupLine("  [dim]Esc[/]             Cancel active generation");
                AnsiConsole.MarkupLine("  [dim]Ctrl+C[/]          Cancel active generation / clear idle draft");
                AnsiConsole.MarkupLine("  [dim]Ctrl+C x2[/]       Exit mux when idle with an empty draft");
                Console.WriteLine();
            });
        }

        private static bool IsCompactStrategyCommand(string argument)
        {
            if (string.IsNullOrWhiteSpace(argument))
            {
                return false;
            }

            string[] parts = argument.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0
                && string.Equals(parts[0], "strategy", StringComparison.OrdinalIgnoreCase);
        }

        private static bool CanExecuteSlashCommandWhileBusy(string command, string argument)
        {
            switch (command)
            {
                case "/help":
                case "/?":
                case "/status":
                case "/context":
                case "/tools":
                case "/queue":
                case "/title":
                    return true;

                case "/compact":
                    return IsCompactStrategyCommand(argument);

                case "/system":
                case "/endpoint":
                    return string.IsNullOrWhiteSpace(argument);

                case "/mcp":
                    string[] subParts = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    string subCommand = subParts.Length > 0 ? subParts[0].ToLowerInvariant() : "list";
                    return string.Equals(subCommand, "list", StringComparison.Ordinal);

                default:
                    return false;
            }
        }

        /// <summary>
        /// Handles /mcp subcommands: list, add, remove.
        /// </summary>
        /// <param name="argument">The argument string after /mcp.</param>
        private void HandleMcpCommand(string argument)
        {
            string[] subParts = argument.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string subCommand = subParts.Length > 0 ? subParts[0].ToLowerInvariant() : "list";

            switch (subCommand)
            {
                case "list":
                    if (_McpToolManager == null)
                    {
                        WriteMarkupLine("[dim]No MCP servers configured.[/]");
                        return;
                    }

                    List<(string Name, int ToolCount, bool Connected)> status = _McpToolManager.GetServerStatus();
                    if (status.Count == 0)
                    {
                        WriteMarkupLine("[dim]No MCP servers connected.[/]");
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

                    WriteOutputBlock(() =>
                    {
                        AnsiConsole.Write(mcpTable);
                        Console.WriteLine();
                    });
                    break;

                case "add":
                    if (!EnsureQueueEmptyForStateChange("modify MCP servers"))
                    {
                        return;
                    }

                    if (subParts.Length < 3)
                    {
                        WriteMarkupLine("[yellow]Usage: /mcp add <name> <command> [[args...]][/]");
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
                        WriteMarkupLine($"[green]Added MCP server:[/] {Markup.Escape(addName)}");

                        List<ToolDefinition> newTools = _McpToolManager.GetToolDefinitions();
                        int toolCount = 0;
                        foreach (ToolDefinition tool in newTools)
                        {
                            if (tool.Name.StartsWith(addName + ".", StringComparison.OrdinalIgnoreCase))
                            {
                                toolCount++;
                            }
                        }

                        WriteMarkupLine($"[dim]Discovered {toolCount} tools[/]");
                    }
                    catch (Exception ex)
                    {
                        WriteMarkupLine($"[red]Failed to add MCP server: {Markup.Escape(ex.Message)}[/]");
                    }
                    break;

                case "remove":
                    if (!EnsureQueueEmptyForStateChange("modify MCP servers"))
                    {
                        return;
                    }

                    if (subParts.Length < 2)
                    {
                        WriteMarkupLine("[yellow]Usage: /mcp remove <name>[/]");
                        return;
                    }

                    string removeName = subParts[1];

                    if (_McpToolManager == null)
                    {
                        WriteMarkupLine("[dim]No MCP servers configured.[/]");
                        return;
                    }

                    try
                    {
                        _McpToolManager.RemoveServerAsync(removeName).GetAwaiter().GetResult();
                        WriteMarkupLine($"[green]Removed MCP server:[/] {Markup.Escape(removeName)}");
                    }
                    catch (Exception ex)
                    {
                        WriteMarkupLine($"[red]Failed to remove MCP server: {Markup.Escape(ex.Message)}[/]");
                    }
                    break;

                default:
                    HandleMcpCommand("list");
                    break;
            }
        }

        private bool EnsureQueueEmptyForStateChange(string action)
        {
            if (_QueuedMessages.Count > 0)
            {
                WriteMarkupLine($"[yellow]Cannot {Markup.Escape(action)} while {_QueuedMessages.Count} queued prompts remain. Use /queue clear or /queue resume first.[/]");
                return false;
            }

            return true;
        }

        #endregion

        #region Private-Types

        private class ApprovalRequestState
        {
            public ToolCall ToolCall { get; set; } = new ToolCall();

            public string Summary { get; set; } = string.Empty;

            public TaskCompletionSource<string> CompletionSource { get; set; } =
                new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private class ActiveRunState
        {
            public string Prompt { get; set; } = string.Empty;

            public Channel<AgentEvent> Events { get; set; } = Channel.CreateUnbounded<AgentEvent>();

            public Task<RunExecutionResult> CompletionTask { get; set; } = Task.FromResult(new RunExecutionResult());
        }

        private class RunExecutionResult
        {
            public string Prompt { get; set; } = string.Empty;

            public string AssistantResponse { get; set; } = string.Empty;

            public bool Cancelled { get; set; } = false;

            public Exception? Error { get; set; } = null;
        }

        #endregion
    }
}
