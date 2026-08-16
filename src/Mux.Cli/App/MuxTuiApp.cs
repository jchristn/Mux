namespace Mux.Cli.App
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Jobs;
    using Mux.Core.Llm;
    using Mux.Core.Models;
    using Mux.Core.Sessions;
    using Mux.Core.Settings;
    using Mux.Core.Skills;
    using Mux.Core.Utility;
    using TUIKit;
    using TUIKit.Content;
    using TUIKit.Hosting;
    using TUIKit.Input;
    using TUIKit.Layout;
    using TUIKit.Modals;
    using TUIKit.Terminal;
    using TUIKit.Testing;
    using TUIKit.Theming;
    using TUIKit.Widgets;

    /// <summary>
    /// The TUIKit-hosted interactive shell for mux. Presents a single continuous conversation: the user
    /// types a prompt and it runs, one turn at a time. Prompts entered while a turn is running are queued
    /// and serviced in order; the pending queue is shown in a strip above the composer (not echoed into the
    /// transcript until each prompt starts) and can be edited, reordered, or trimmed in a modal (Ctrl+G)
    /// that pauses processing while open. One transcript <see cref="Pane"/> holds the whole conversation; a
    /// sidebar shows the active endpoint and per-turn / session telemetry; the bottom row is a green
    /// <c>mux&gt;</c> prompt and the composer. The terminal backend is injected so the shell can be driven
    /// headlessly in tests.
    /// </summary>
    public sealed class MuxTuiApp : IDisposable
    {
        #region Private-Members

        private const string TranscriptRegion = "transcript";
        private const string SidebarRegion = "sidebar";
        private const string ComposerRegion = "composer";
        private const string PromptLabelRegion = "promptlabel";
        private const string FooterRegion = "footer";
        private const string QueueRegion = "queue";
        private const int MaxQueueStripRows = 7;
        private const int MaxComposerRows = 8;
        private const string PromptText = "mux> ";
        private const int SidebarWidth = 24;

        // A one-column gutter kept clear between the transcript (and queue strip) on the left and the
        // right-anchored sidebar, so the two panes never butt directly against each other. The sidebar
        // stays anchored at SidebarWidth; the left panes simply reserve this extra column on their right.
        private const int SidebarGap = 1;
        private const int CollapseThreshold = 100;

        private readonly ITerminalBackend _Backend;
        private readonly TuiApplication _App;
        private readonly JobManager _JobManager;
        private readonly ApprovalPolicyEnum _ApprovalPolicy;
        private readonly SessionStore? _Store;
        private readonly Action<EndpointConfig>? _OnEndpointSelected;
        private readonly Func<EndpointConfig, CancellationToken, Task<ModelLoadResult>>? _OnValidateModel;
        private readonly Action<PromptProfile>? _OnPromptProfileSelected;
        private readonly McpRuntime? _McpRuntime;
        private readonly SkillRuntime? _SkillRuntime;
        private string _EndpointName;
        private string _Model;
        private string _EffortLabel = string.Empty;
        private string _ThinkingLabel = string.Empty;
        private readonly string _Title;
        private readonly Pane _Conversation;
        private readonly Pane _SidebarPane;
        private readonly Pane _PromptLabel;
        private readonly Pane _Footer;
        private readonly Pane _QueuePane;
        private readonly TextEditor _Composer;
        private readonly SubmitKeyResolver _SubmitResolver = new SubmitKeyResolver();
        private readonly SidebarView _Sidebar;
        private readonly MuxCommandCatalog _Catalog;
        private Layout _ExpandedLayout = null!;
        private Layout _CollapsedLayout = null!;
        private readonly List<ConversationMessage> _ConversationHistory = new List<ConversationMessage>();
        private readonly List<string> _PendingPrompts = new List<string>();
        private readonly List<string> _TurnJobIds = new List<string>();
        private readonly List<Task> _ProjectorTasks = new List<Task>();
        private readonly ConversationStats _Stats = new ConversationStats();
        private readonly object _Sync = new object();
        private readonly SemaphoreSlim _SaveGate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _Cts = new CancellationTokenSource();
        private readonly PromptHistory _PromptHistory = new PromptHistory();
        private readonly MenuBar _MenuBar;
        private Job? _ActiveJob;
        private bool _TurnInFlight;
        private readonly object _ThinkingSync = new object();
        private PaneLineHandle? _ThinkingHandle;
        private CancellationTokenSource? _ThinkingCts;
        private ActivityIndicator? _ThinkingIndicator;
        private bool _ThinkingActive;
        private Func<string, bool>? _SlashHandler;
        private bool _ManualCollapsed;
        private bool _CollapsedApplied;
        private bool _ShowBoundaries;
        private int _ThemeIndex;
        private bool _Disposed;

        // An optional prompt submitted as the first turn once the run loop starts, then cleared. Set from
        // the CLI --prompt option (or a bare positional prompt), which also suppresses the startup splash.
        private string? _InitialPrompt;

        // Queue processing is paused while the queue-editor modal is open, so a turn finishing mid-edit
        // does not start the next prompt out from under the user. _QueueHeight and _ComposerHeight are the
        // current row counts of the queue strip and the (multi-line) composer, tracked so the layout is
        // only rebuilt when they actually change.
        private bool _QueuePaused;
        private int _QueueHeight;
        private int _ComposerHeight = 1;

        private static readonly Theme MuxTheme = CreateTheme();
        private static readonly Theme[] _Themes = { MuxTheme, Theme.Dark, Theme.Light, Theme.HighContrast };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="MuxTuiApp"/> class.
        /// </summary>
        /// <param name="backend">The terminal backend (console for production, headless for tests). Must not be null.</param>
        /// <param name="jobManager">The job manager that runs submitted prompts. Must not be null.</param>
        /// <param name="title">A short session title shown in the sidebar.</param>
        /// <param name="approvalPolicy">The approval policy applied to runs.</param>
        /// <param name="sessionStore">Optional session store; when supplied, the session autosaves at turn boundaries and can be saved/browsed. Null disables persistence.</param>
        /// <param name="endpointName">The effective endpoint name (shown in the sidebar, recorded in sessions).</param>
        /// <param name="model">The effective model (recorded in sessions).</param>
        /// <param name="onEndpointSelected">Optional callback invoked when the user switches endpoints, so the caller can apply it to future runs. Null disables live switching.</param>
        /// <param name="onValidateModel">Optional callback invoked (in the background) after an endpoint switch to load/validate the newly selected model. Returns the load outcome so the shell can surface a notice. Null disables the model-load probe.</param>
        /// <param name="onPromptProfileSelected">Optional callback invoked when the user applies a prompt profile, so the caller can substitute placeholders and apply it to future runs. Null disables live prompt switching.</param>
        /// <param name="showSplash">When true, opens the startup splash modal.</param>
        /// <param name="showBoundaries">When true, the shell starts with the dark-grey boundary lines drawn (toggle with <c>/borders</c>).</param>
        /// <param name="mcpRuntime">Optional MCP runtime used to show per-server connectivity in the MCP manager and to trigger a reconnect after edits. Null disables live MCP status.</param>
        /// <param name="skillRuntime">Optional skills runtime used to show skill status in the skills manager and to trigger a re-scan after edits. Null disables the skills manager.</param>
        /// <param name="initialPrompt">Optional prompt submitted as the first turn once the run loop starts. Null or whitespace submits nothing.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="backend"/> or <paramref name="jobManager"/> is null.</exception>
        public MuxTuiApp(
            ITerminalBackend backend,
            JobManager jobManager,
            string title,
            ApprovalPolicyEnum approvalPolicy,
            SessionStore? sessionStore = null,
            string endpointName = "",
            string model = "",
            Action<EndpointConfig>? onEndpointSelected = null,
            Func<EndpointConfig, CancellationToken, Task<ModelLoadResult>>? onValidateModel = null,
            Action<PromptProfile>? onPromptProfileSelected = null,
            bool showSplash = false,
            bool showBoundaries = false,
            McpRuntime? mcpRuntime = null,
            SkillRuntime? skillRuntime = null,
            string? initialPrompt = null)
        {
            _Backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _JobManager = jobManager ?? throw new ArgumentNullException(nameof(jobManager));
            _ApprovalPolicy = approvalPolicy;
            _Store = sessionStore;
            _OnEndpointSelected = onEndpointSelected;
            _OnValidateModel = onValidateModel;
            _OnPromptProfileSelected = onPromptProfileSelected;
            _McpRuntime = mcpRuntime;
            _SkillRuntime = skillRuntime;
            _InitialPrompt = string.IsNullOrWhiteSpace(initialPrompt) ? null : initialPrompt;
            _EndpointName = endpointName ?? string.Empty;
            _Model = model ?? string.Empty;
            _EffortLabel = InitialEffortLabel(_EndpointName);
            _ThinkingLabel = InitialThinkingLabel(_EndpointName);
            _ShowBoundaries = showBoundaries;
            _Title = string.IsNullOrWhiteSpace(title) ? "mux" : title;

            _App = new TuiApplication(backend);
            _App.CtrlCPolicy = CtrlCPolicy.DoubleTapToExit;
            _App.MouseCaptureEnabled = true; // always on by default; F12 hands the mouse back for native text selection
            _App.Theme = MuxTheme; // no background color; the terminal's own background shows through
            _App.RenderOverlay = DrawBoundaries; // optional dark-grey rules between the main regions

            BuildLayouts(1, 0);

            _Conversation = new Pane(TranscriptRegion);
            _SidebarPane = new Pane(SidebarRegion);
            _PromptLabel = new Pane(PromptLabelRegion);
            _Footer = new Pane(FooterRegion);
            _QueuePane = new Pane(QueueRegion);
            _Composer = new TextEditor { IsFocused = true };
            _Sidebar = new SidebarView(_SidebarPane);

            _PromptLabel.WriteLine(Text.From(PromptText).Green().Bold());

            _App.BindPane(TranscriptRegion, _Conversation);
            _App.BindPane(SidebarRegion, _SidebarPane);
            _App.BindPane(PromptLabelRegion, _PromptLabel);
            _App.BindPane(FooterRegion, _Footer);
            _App.BindPane(QueueRegion, _QueuePane);
            _App.Bind(ComposerRegion, _Composer);

            // Fill every pane's blank cells with the theme background so the rectangles conform to the
            // active theme (the mux default is transparent, so this is a no-op until a theme is selected).
            ApplyPaneBackgrounds(MuxTheme);

            _Catalog = new MuxCommandCatalog();
            _Catalog.Add(new CommandDescriptor("mux.quit", "Exit", "ctrl+q", RequestQuit, "Session", new[] { "quit", "exit", "q" }));
            _Catalog.Add(new CommandDescriptor("mux.endpoint", "Endpoints / models", "ctrl+e", OpenEndpointModal, "Model", new[] { "endpoint", "endpoints", "model", "models" }));
            _Catalog.Add(new CommandDescriptor("mux.clear", "Clear transcript", "ctrl+l", ClearTranscript, "View", new[] { "clear" }));
            _Catalog.Add(new CommandDescriptor("mux.sidebar.toggle", "Toggle sidebar", "ctrl+b", ToggleSidebar, "View", new[] { "sidebar" }));
            _Catalog.Add(new CommandDescriptor("mux.save", "Save session", "ctrl+s", SaveSession, "Session", new[] { "save" }));
            _Catalog.Add(new CommandDescriptor("mux.queue", "Edit queue", "ctrl+g", OpenQueueEditor, "Session", new[] { "queue", "edit queue", "pending" }));
            _Catalog.Add(new CommandDescriptor("mux.prompts", "Prompts", "ctrl+p", OpenPromptEditor, "Model", new[] { "prompts", "prompt", "system prompt" }));
            _Catalog.Add(new CommandDescriptor("mux.mcp", "MCP servers", null, OpenMcpModal, "Model", new[] { "mcp", "mcp-servers", "mcpservers", "servers" }));
            _Catalog.Add(new CommandDescriptor("mux.skills", "Skills", null, OpenSkillsModal, "Model", new[] { "skills", "skill" }));
            _Catalog.Add(new CommandDescriptor("mux.sessions", "Sessions", null, OpenSessionBrowser, "Session", new[] { "sessions" }));
            _Catalog.Add(new CommandDescriptor("mux.tasks", "Tasks", null, OpenTasksModal, "View", new[] { "tasks", "task", "plan", "todo" }));
            _Catalog.Add(new CommandDescriptor("mux.effort", "Reasoning effort", null, OpenEffortSelector, "Model", new[] { "effort", "reasoning", "reasoning-effort" }));
            _Catalog.Add(new CommandDescriptor("mux.theme", "Theme", null, OpenThemeSelector, "View", new[] { "theme" }));
            _Catalog.Add(new CommandDescriptor("mux.mouse", "Toggle mouse capture", "f12", ToggleMouseCapture, "View", new[] { "mouse" }));
            _Catalog.Add(new CommandDescriptor("mux.borders", "Toggle boundary lines", null, ToggleBoundaries, "View", new[] { "borders", "boundaries", "boundary", "lines" }));
            _Catalog.Add(new CommandDescriptor("mux.thinking", "Toggle thinking display", null, ToggleThinking, "View", new[] { "thinking", "think", "reasoning-display" }));
            _Catalog.Add(new CommandDescriptor("mux.menu", "Command menu", "f1", OpenCommandMenu, "Help", new[] { "menu" }));
            _Catalog.Add(new CommandDescriptor("mux.help", "Help", null, OpenCommandMenu, "Help", new[] { "help", "?" }));
            _Catalog.ApplyTo(_App);
            _MenuBar = MenuBarBuilder.Build(_Catalog);
            _SlashHandler = new SlashCommandParser(_Catalog).TryHandle;

            // Own input via the pre-widget KeyFilter. The composer is a focusable TextEditor that would
            // otherwise consume Enter (as a newline) and every Ctrl+key (swallowing command chords) before
            // they could reach us, so we intercept here — dispatching submits, chords, and edits ourselves —
            // and forward the remaining keys to the composer. Modals and the Ctrl+C policy still run first.
            _App.KeyFilter = OnKeyFilter;

            // Bracketed paste delivers pasted content as one event rather than a stream of key presses, so
            // a multi-line paste (its embedded newlines and all) lands in the composer as a single block and
            // is sent as one prompt. InsertText normalizes CRLF/CR to LF. Without this, the paste event is
            // dropped and the text never appears.
            _App.PasteReceived += OnPaste;

            WriteHeader();
            RefreshSidebar();
            RefreshFooter();
            ApplyResponsiveLayout();

            if (showSplash)
            {
                _App.Modals.Push(new MuxBoxModal("mux", MuxBanner.SplashLines(Defaults.ProductVersion), "press any key to start", centered: true));
            }
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The job manager backing this shell.
        /// </summary>
        public JobManager JobManager
        {
            get => _JobManager;
        }

        /// <summary>
        /// The current composer text.
        /// </summary>
        public string ComposerText
        {
            get => _Composer.Text;
        }

        /// <summary>
        /// The command catalog wired to this shell.
        /// </summary>
        public MuxCommandCatalog Catalog
        {
            get => _Catalog;
        }

        /// <summary>
        /// The ids of the turn jobs run so far, in order. Each conversation turn is executed as one job.
        /// </summary>
        public IReadOnlyList<string> JobIds
        {
            get
            {
                lock (_Sync)
                {
                    return new List<string>(_TurnJobIds);
                }
            }
        }

        /// <summary>
        /// The id of the currently running turn's job, or the most recently run turn's job when idle;
        /// null before the first turn.
        /// </summary>
        public string? FocusedJobId
        {
            get
            {
                lock (_Sync)
                {
                    if (_ActiveJob != null)
                    {
                        return _ActiveJob.Id;
                    }

                    return _TurnJobIds.Count > 0 ? _TurnJobIds[_TurnJobIds.Count - 1] : null;
                }
            }
        }

        /// <summary>
        /// Whether a turn is currently running.
        /// </summary>
        public bool IsBusy
        {
            get
            {
                lock (_Sync)
                {
                    return _TurnInFlight;
                }
            }
        }

        /// <summary>
        /// The number of prompts queued behind the running turn.
        /// </summary>
        public int QueuedCount
        {
            get
            {
                lock (_Sync)
                {
                    return _PendingPrompts.Count;
                }
            }
        }

        /// <summary>
        /// The number of turns started this session.
        /// </summary>
        public int TurnCount
        {
            get
            {
                lock (_Sync)
                {
                    return _TurnJobIds.Count;
                }
            }
        }

        /// <summary>
        /// Whether the sidebar is currently collapsed (manually or by the responsive width rule).
        /// </summary>
        public bool IsSidebarCollapsed
        {
            get
            {
                lock (_Sync)
                {
                    return _CollapsedApplied;
                }
            }
        }

        /// <summary>
        /// The active theme's name (e.g. "mux", "Dark", "Light", "HighContrast").
        /// </summary>
        public string ThemeName
        {
            get => _App.Theme.Name;
        }

        /// <summary>
        /// Whether mouse capture is currently enabled.
        /// </summary>
        public bool IsMouseCaptureEnabled
        {
            get => _App.MouseCaptureEnabled;
        }

        /// <summary>
        /// Handler invoked for a composer submission that begins with <c>/</c>. Returns true when the
        /// input was handled as a command (so it is not submitted as a prompt).
        /// </summary>
        public Func<string, bool>? SlashHandler
        {
            get => _SlashHandler;
            set => _SlashHandler = value;
        }

        /// <summary>
        /// The catalog-derived menu bar (menus grouped by command category, items wired to command
        /// handlers).
        /// </summary>
        public MenuBar MenuBar
        {
            get => _MenuBar;
        }

        /// <summary>
        /// The name of the currently active endpoint (updated when the user switches).
        /// </summary>
        public string ActiveEndpointName
        {
            get
            {
                lock (_Sync)
                {
                    return _EndpointName;
                }
            }
        }

        /// <summary>
        /// Whether a modal (approval, sessions, message, …) is currently active and trapping input.
        /// </summary>
        public bool IsModalActive
        {
            get => _App.Modals.IsActive;
        }

        /// <summary>
        /// The number of active modals on the stack.
        /// </summary>
        public int ModalCount
        {
            get => _App.Modals.Count;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Starts the terminal session without entering the run loop. Used by tests that pump input and
        /// render frames manually; production callers use <see cref="RunAsync"/>.
        /// </summary>
        public void Start()
        {
            _App.Start();
        }

        /// <summary>
        /// Reads and dispatches one batch of pending input.
        /// </summary>
        public void PumpInputOnce()
        {
            _App.PumpInputOnce();
        }

        /// <summary>
        /// Composes and emits one frame.
        /// </summary>
        public void RenderOnce()
        {
            _App.RenderOnce();
        }

        /// <summary>
        /// Requests that the run loop exit at the next opportunity.
        /// </summary>
        public void RequestStop()
        {
            _App.RequestStop();
        }

        /// <summary>
        /// Runs the interactive input and render loop until the user quits or the token is cancelled,
        /// while a background monitor keeps the layout responsive to terminal-size changes.
        /// </summary>
        /// <param name="cancellationToken">A token used to stop the loop.</param>
        /// <returns>A task that completes when the loop exits.</returns>
        public async Task RunAsync(CancellationToken cancellationToken)
        {
            using (CancellationTokenSource loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _Cts.Token))
            {
                Task monitor = MonitorResponsiveAsync(loopCts.Token);
                try
                {
                    // Submit the startup prompt (if any) before entering the loop. Runs on this thread, so it
                    // is ordered ahead of any input; the echo and thinking indicator buffer into the panes and
                    // flush on the first rendered frame, exactly as a typed prompt would.
                    SubmitInitialPrompt();

                    await _App.RunAsync(loopCts.Token).ConfigureAwait(false);
                }
                finally
                {
                    loopCts.Cancel();
                    try
                    {
                        await monitor.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }
            }
        }

        /// <summary>
        /// Opens a modal reasoning-effort selector for the active endpoint. The chosen level is persisted to
        /// endpoints.json and applied to the running session so the next turn uses it.
        /// </summary>
        public void OpenEffortSelector()
        {
            EndpointConfig? active = LoadActiveEndpoint();
            ReasoningLevelEnum? current = active?.ReasoningEffort?.Level;
            int selected = EffortIndexFor(current);

            string[] names = { "Off", "Minimal", "Low", "Medium", "High" };
            List<string> labels = new List<string>();
            for (int i = 0; i < names.Length; i++)
            {
                string marker = i == selected ? "● " : "  ";
                labels.Add(marker + names[i]);
            }

            EffortSelectModal modal = new EffortSelectModal("Reasoning effort — ↑↓ then Enter to apply", labels, selected);
            _App.Modals.Push(modal);
            _ = ResolveEffortSelectorAsync(modal, active);
        }

        private async Task ResolveEffortSelectorAsync(EffortSelectModal modal, EndpointConfig? active)
        {
            object? result = await modal.Completion.ConfigureAwait(false);
            if (result is int index && index >= 0 && index <= 4)
            {
                ApplyEffort(active, index);
            }
        }

        private void ApplyEffort(EndpointConfig? active, int index)
        {
            if (active == null)
            {
                WriteNotice("No active endpoint to set reasoning effort on");
                return;
            }

            ReasoningLevelEnum? level = LevelForEffortIndex(index);
            if (level.HasValue)
            {
                ReasoningEffortConfig config = active.ReasoningEffort?.Clone() ?? new ReasoningEffortConfig();
                config.Level = level;
                active.ReasoningEffort = config;
            }
            else
            {
                active.ReasoningEffort = null;
            }

            try
            {
                List<EndpointConfig> endpoints = LoadEndpointsSafe();
                endpoints.RemoveAll(e => string.Equals(e.Name, active.Name, StringComparison.OrdinalIgnoreCase));
                endpoints.Add(active);
                SettingsLoader.SaveEndpoints(endpoints);
            }
            catch (Exception ex)
            {
                WriteNotice("Save failed: " + ex.Message);
                return;
            }

            // Rebuild the running client so the next turn uses the new effort.
            _OnEndpointSelected?.Invoke(active);
            lock (_Sync)
            {
                _EffortLabel = EffortLabelFor(active);
            }

            WriteNotice($"Reasoning effort set to {EffortDisplayName(level)}");
            RefreshSidebar();
        }

        private EndpointConfig? LoadActiveEndpoint()
        {
            string name;
            lock (_Sync)
            {
                name = _EndpointName;
            }

            foreach (EndpointConfig endpoint in LoadEndpointsSafe())
            {
                if (string.Equals(endpoint.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return endpoint;
                }
            }

            return null;
        }

        /// <summary>
        /// Toggles whether the model's reasoning ("thinking") is displayed for the active endpoint,
        /// persists the choice to endpoints.json, and applies it to the running session.
        /// </summary>
        public void ToggleThinking()
        {
            EndpointConfig? active = LoadActiveEndpoint();
            if (active == null)
            {
                WriteNotice("No active endpoint to toggle thinking on");
                return;
            }

            active.ShowThinking = !active.ShowThinking;

            try
            {
                List<EndpointConfig> endpoints = LoadEndpointsSafe();
                endpoints.RemoveAll(e => string.Equals(e.Name, active.Name, StringComparison.OrdinalIgnoreCase));
                endpoints.Add(active);
                SettingsLoader.SaveEndpoints(endpoints);
            }
            catch (Exception ex)
            {
                WriteNotice("Save failed: " + ex.Message);
                return;
            }

            _OnEndpointSelected?.Invoke(active);
            lock (_Sync)
            {
                _ThinkingLabel = active.ShowThinking ? "on" : "off";
            }

            WriteNotice($"Thinking display {(active.ShowThinking ? "on" : "off")} for endpoint {active.Name}");
            RefreshSidebar();
        }

        private string InitialThinkingLabel(string endpointName)
        {
            try
            {
                foreach (EndpointConfig endpoint in LoadEndpointsSafe())
                {
                    if (string.Equals(endpoint.Name, endpointName, StringComparison.OrdinalIgnoreCase))
                    {
                        return endpoint.ShowThinking ? "on" : "off";
                    }
                }
            }
            catch (Exception)
            {
                // The sidebar label is cosmetic; never fail construction over it.
            }

            return string.Empty;
        }

        private string InitialEffortLabel(string endpointName)
        {
            try
            {
                foreach (EndpointConfig endpoint in LoadEndpointsSafe())
                {
                    if (string.Equals(endpoint.Name, endpointName, StringComparison.OrdinalIgnoreCase))
                    {
                        return EffortLabelFor(endpoint);
                    }
                }
            }
            catch (Exception)
            {
                // The sidebar label is cosmetic; never fail construction over it.
            }

            return string.Empty;
        }

        private static string EffortLabelFor(EndpointConfig endpoint)
        {
            ReasoningLevelEnum? level = endpoint.ReasoningEffort?.Level;
            return level.HasValue ? ReasoningLevelEnumConverter.ToWire(level.Value) : "off";
        }

        private static int EffortIndexFor(ReasoningLevelEnum? level)
        {
            switch (level)
            {
                case ReasoningLevelEnum.Minimal: return 1;
                case ReasoningLevelEnum.Low: return 2;
                case ReasoningLevelEnum.Medium: return 3;
                case ReasoningLevelEnum.High: return 4;
                default: return 0;
            }
        }

        private static ReasoningLevelEnum? LevelForEffortIndex(int index)
        {
            switch (index)
            {
                case 1: return ReasoningLevelEnum.Minimal;
                case 2: return ReasoningLevelEnum.Low;
                case 3: return ReasoningLevelEnum.Medium;
                case 4: return ReasoningLevelEnum.High;
                default: return null;
            }
        }

        private static string EffortDisplayName(ReasoningLevelEnum? level)
        {
            switch (level)
            {
                case ReasoningLevelEnum.Minimal: return "Minimal";
                case ReasoningLevelEnum.Low: return "Low";
                case ReasoningLevelEnum.Medium: return "Medium";
                case ReasoningLevelEnum.High: return "High";
                default: return "Off";
            }
        }

        /// <summary>
        /// Opens a modal theme selector. The chosen theme is applied to the whole UI — text and the
        /// rectangles behind it — via <see cref="ApplyTheme"/>.
        /// </summary>
        public void OpenThemeSelector()
        {
            List<string> labels = new List<string>();
            for (int i = 0; i < _Themes.Length; i++)
            {
                string marker = i == _ThemeIndex ? "● " : "  ";
                labels.Add(marker + ThemeLabel(_Themes[i]));
            }

            SelectModal modal = new SelectModal("Theme — ↑↓ then Enter to apply", labels);
            _App.Modals.Push(modal);
            _ = ResolveThemeSelectorAsync(modal);
        }

        private async Task ResolveThemeSelectorAsync(SelectModal modal)
        {
            object? result = await modal.Completion.ConfigureAwait(false);
            if (result is int index && index >= 0 && index < _Themes.Length)
            {
                ApplyTheme(index);
            }
        }

        /// <summary>
        /// Applies the theme at the given index to the application and to every bound pane, so the whole
        /// UI conforms to the selected theme (the panes' blank cells inherit the theme background and any
        /// foreground-only text composes over it). The index wraps.
        /// </summary>
        /// <param name="index">The index into the theme preset list; wraps if out of range.</param>
        public void ApplyTheme(int index)
        {
            Theme theme;
            lock (_Sync)
            {
                _ThemeIndex = ((index % _Themes.Length) + _Themes.Length) % _Themes.Length;
                theme = _Themes[_ThemeIndex];
                _App.Theme = theme;
                ApplyPaneBackgrounds(theme);
            }

            // Rewrite the panes so the new background repaints immediately rather than on the next content
            // change.
            RepaintPromptLabel();
            RefreshSidebar();
            RefreshFooter();
        }

        /// <summary>
        /// Advances to the next theme preset. Retained for programmatic and test use; the interactive
        /// surface is the modal selector (<see cref="OpenThemeSelector"/>).
        /// </summary>
        public void CycleTheme()
        {
            int next;
            lock (_Sync)
            {
                next = _ThemeIndex + 1;
            }

            ApplyTheme(next);
        }

        private void ApplyPaneBackgrounds(Theme theme)
        {
            CellStyle background = theme.Text;
            _Conversation.Background = background;
            _SidebarPane.Background = background;
            _PromptLabel.Background = background;
            _Footer.Background = background;
        }

        private void RepaintPromptLabel()
        {
            _PromptLabel.Clear();
            _PromptLabel.WriteLine(Text.From(PromptText).Green().Bold());
        }

        private static string ThemeLabel(Theme theme)
        {
            string name = theme.Name ?? string.Empty;
            if (name.Length == 0)
            {
                return "theme";
            }

            return char.ToUpperInvariant(name[0]) + name.Substring(1);
        }

        /// <summary>
        /// Toggles terminal mouse capture.
        /// </summary>
        public void ToggleMouseCapture()
        {
            _App.ToggleMouseCapture();
        }

        /// <summary>
        /// Toggles the manual sidebar collapse and re-applies the responsive layout.
        /// </summary>
        public void ToggleSidebar()
        {
            lock (_Sync)
            {
                _ManualCollapsed = !_ManualCollapsed;
            }

            ApplyResponsiveLayout();
        }

        /// <summary>
        /// Toggles the dark-grey boundary lines on or off, rebuilds the layout so the rules get (or give
        /// back) their reserved rows, and persists the choice to <c>settings.json</c>.
        /// </summary>
        public void ToggleBoundaries()
        {
            bool enabled;
            lock (_Sync)
            {
                _ShowBoundaries = !_ShowBoundaries;
                enabled = _ShowBoundaries;
                RebuildLayoutNoLock();
            }

            PersistBoundaries(enabled);
            WriteNotice(enabled ? "Boundary lines on." : "Boundary lines off.");
            RefreshFooter();
        }

        private static void PersistBoundaries(bool enabled)
        {
            // Best-effort persistence: a save failure must never take down the interactive session, so a
            // failed write just leaves the choice in effect for this run.
            try
            {
                MuxSettings settings = SettingsLoader.LoadSettings();
                settings.ShowBoundaryLines = enabled;
                SettingsLoader.SaveSettings(settings);
            }
            catch (Exception)
            {
            }
        }

        // Paints the optional boundary rules onto the root surface after the panes render. The row/column
        // positions mirror the reserved gaps computed in BuildLayouts: a rule above the composer input, a
        // rule above the queue strip (when shown), and a vertical rule in the gutter left of the sidebar.
        private void DrawBoundaries(ISurface surface)
        {
            if (!_ShowBoundaries || surface == null)
            {
                return;
            }

            int width = surface.Size.Width;
            int height = surface.Size.Height;
            if (width < 2 || height < 4)
            {
                return;
            }

            int composerHeight;
            int queueHeight;
            bool collapsed;
            lock (_Sync)
            {
                composerHeight = _ComposerHeight;
                queueHeight = _QueueHeight;
                collapsed = _CollapsedApplied;
            }

            const int footerHeight = 2;
            const int inputBoundary = 1;
            int queueBoundary = queueHeight > 0 ? 1 : 0;
            int reserve = composerHeight + inputBoundary + footerHeight + queueHeight + queueBoundary;

            CellStyle style = CellStyle.Default.WithForeground(Color.FromPalette(8)); // dim / dark grey

            // 1) Horizontal rule directly above the prompt input (composer).
            DrawHorizontalRule(surface, height - composerHeight - 1, width, style);

            // 2) Horizontal rule directly above the queued-messages strip, when it is shown.
            if (queueHeight > 0)
            {
                int queueOffset = composerHeight + inputBoundary + footerHeight;
                DrawHorizontalRule(surface, height - queueOffset - queueHeight - 1, width, style);
            }

            // 3) Vertical rule in the gutter to the left of the sidebar, only when the sidebar is shown.
            if (!collapsed)
            {
                int gutterX = width - SidebarWidth - 1;
                int transcriptBottom = height - reserve - 1;
                DrawVerticalRule(surface, gutterX, transcriptBottom, style);
            }
        }

        private static void DrawHorizontalRule(ISurface surface, int row, int width, CellStyle style)
        {
            if (row < 0 || row >= surface.Size.Height)
            {
                return;
            }

            surface.DrawText(0, row, new string('─', Math.Max(0, Math.Min(width, surface.Size.Width))), style);
        }

        private static void DrawVerticalRule(ISurface surface, int col, int bottomRow, CellStyle style)
        {
            if (col < 0 || col >= surface.Size.Width)
            {
                return;
            }

            int bottom = Math.Min(bottomRow, surface.Size.Height - 1);
            for (int y = 0; y <= bottom; y++)
            {
                surface.DrawText(col, y, "│", style);
            }
        }

        /// <summary>
        /// Applies the collapsed or expanded layout based on the manual toggle and the current terminal
        /// width. Idempotent; safe to call from the render loop or from tests after a resize.
        /// </summary>
        public void ApplyResponsiveLayout()
        {
            lock (_Sync)
            {
                bool shouldCollapse = _ManualCollapsed || _Backend.Size.Width < CollapseThreshold;
                if (shouldCollapse == _CollapsedApplied && _App.Layout != null)
                {
                    return;
                }

                _App.Layout = shouldCollapse ? _CollapsedLayout : _ExpandedLayout;
                _CollapsedApplied = shouldCollapse;
            }
        }

        /// <summary>
        /// Presents a tool-approval modal and resolves to a response string understood by the agent
        /// loop's approval mapping: <c>"y"</c> (approve once), <c>"n"</c> (deny), or <c>"always"</c>
        /// (approve for the session). Intended to be used as the engine's <c>PromptUserFunc</c>; it runs
        /// on the engine's worker thread and blocks that tool call until the user answers on the UI
        /// thread. Escaping the modal, or app shutdown, denies.
        /// </summary>
        /// <param name="toolCall">The tool call awaiting approval.</param>
        /// <returns>The approval response string.</returns>
        public async Task<string> RequestApprovalAsync(ToolCall toolCall)
        {
            string name = toolCall != null && !string.IsNullOrWhiteSpace(toolCall.Name) ? toolCall.Name : "tool";
            SelectModal modal = new SelectModal(
                $"Approve {name}?",
                new List<string> { "Approve once", "Deny", "Always allow this session" });

            _App.Modals.Push(modal);

            using (CancellationTokenRegistration registration = _Cts.Token.Register(() => modal.RequestClose(-1)))
            {
                object? result = await modal.Completion.ConfigureAwait(false);
                int index = result is int value ? value : -1;
                return index == 0 ? "y" : index == 2 ? "always" : "n";
            }
        }

        /// <summary>
        /// Builds a snapshot of the current session (turn jobs, histories, prompt history, metadata).
        /// </summary>
        /// <returns>The session snapshot.</returns>
        public SessionSnapshot BuildSnapshot()
        {
            return SessionSnapshotBuilder.Build(
                _JobManager,
                _JobManager.SessionId,
                _Title,
                _EndpointName,
                _Model,
                _PromptHistory.Snapshot(),
                DateTime.UtcNow);
        }

        /// <summary>
        /// Saves the current session to the injected store. No-op when no store was supplied.
        /// </summary>
        /// <returns>A task that completes when the save finishes.</returns>
        public async Task SaveSessionAsync()
        {
            if (_Store == null)
            {
                return;
            }

            SessionSnapshot snapshot = BuildSnapshot();

            // Serialize writes so a background autosave and an explicit/manual save never race on the
            // same session file (each SaveAsync is an atomic temp+move; two at once can collide).
            await _SaveGate.WaitAsync(_Cts.Token).ConfigureAwait(false);
            try
            {
                await _Store.SaveAsync(snapshot, _Cts.Token).ConfigureAwait(false);
            }
            finally
            {
                _SaveGate.Release();
            }
        }

        /// <summary>
        /// Replays a resumed session into the single conversation transcript and rebuilds the prompt and
        /// conversation history. Nothing is auto-run.
        /// </summary>
        /// <param name="resume">The resumed session state. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="resume"/> is null.</exception>
        public void RestoreSession(SessionResumeResult resume)
        {
            if (resume is null) throw new ArgumentNullException(nameof(resume));

            lock (_Sync)
            {
                _Conversation.Clear();
                _ConversationHistory.Clear();
                _TurnJobIds.Clear();
                _PendingPrompts.Clear();
                _QueuePaused = false;
            }

            UpdateQueueStrip();
            _PromptHistory.Restore(resume.PromptHistory);

            foreach (PersistedJobSnapshot job in resume.CompletedJobs)
            {
                ReplayJob(job, interrupted: false);
            }

            foreach (PersistedJobSnapshot job in resume.InterruptedJobs)
            {
                ReplayJob(job, interrupted: true);
            }

            RefreshSidebar();
            RefreshFooter();
        }

        /// <summary>
        /// Awaits all in-flight turn projections, including turns dequeued while draining. Test helper
        /// that makes projected transcript content deterministic before asserting.
        /// </summary>
        /// <returns>A task that completes when the conversation is idle and no projections remain.</returns>
        public async Task DrainProjectorsAsync()
        {
            while (true)
            {
                Task[] snapshot;
                lock (_Sync)
                {
                    snapshot = _ProjectorTasks.ToArray();
                }

                await Task.WhenAll(snapshot).ConfigureAwait(false);

                lock (_Sync)
                {
                    if (!_TurnInFlight && _ProjectorTasks.Count == snapshot.Length)
                    {
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Returns a plain-text snapshot of the conversation transcript. Test helper.
        /// </summary>
        /// <returns>The committed transcript lines without styling.</returns>
        public IReadOnlyList<string> TranscriptSnapshot()
        {
            lock (_Sync)
            {
                return _Conversation.SnapshotPlainLines();
            }
        }

        /// <summary>
        /// Returns a plain-text snapshot of the sidebar lines. Test helper.
        /// </summary>
        /// <returns>The committed sidebar lines without styling.</returns>
        public IReadOnlyList<string> SidebarSnapshot()
        {
            return _SidebarPane.SnapshotPlainLines();
        }

        /// <summary>
        /// Returns a plain-text snapshot of the footer lines. Test helper.
        /// </summary>
        /// <returns>The committed footer lines without styling.</returns>
        public IReadOnlyList<string> FooterSnapshot()
        {
            return _Footer.SnapshotPlainLines();
        }

        /// <summary>
        /// Returns a plain-text snapshot of the queue strip above the composer. Test helper.
        /// </summary>
        /// <returns>The committed queue-strip lines without styling.</returns>
        public IReadOnlyList<string> QueueStripSnapshot()
        {
            return _QueuePane.SnapshotPlainLines();
        }

        /// <summary>
        /// The current height of the composer region in rows (grows with multi-line input). Test helper.
        /// </summary>
        public int ComposerRowCount
        {
            get
            {
                lock (_Sync)
                {
                    return _ComposerHeight;
                }
            }
        }

        /// <summary>
        /// Whether queue processing is currently paused (the queue editor is open). Test helper.
        /// </summary>
        public bool IsQueuePaused
        {
            get
            {
                lock (_Sync)
                {
                    return _QueuePaused;
                }
            }
        }

        /// <summary>
        /// Whether the thinking indicator is currently shown (a turn is running and has not yet produced
        /// output). Test helper.
        /// </summary>
        public bool IsThinking
        {
            get
            {
                lock (_ThinkingSync)
                {
                    return _ThinkingActive;
                }
            }
        }

        /// <summary>
        /// The thinking phrase currently displayed, or null when the indicator is hidden. Test helper.
        /// </summary>
        public string? CurrentThinkingMessage
        {
            get
            {
                lock (_ThinkingSync)
                {
                    return _ThinkingIndicator?.CurrentPhrase;
                }
            }
        }

        /// <summary>
        /// Renders a single region's widget into a fresh cell buffer of the given size and returns its
        /// text grid (via TUIKit's own render path). Deterministic golden-snapshot helper for tests.
        /// Unknown region ids or non-positive dimensions return an empty string.
        /// </summary>
        /// <param name="regionId">One of the shell region ids (transcript/sidebar/composer/footer).</param>
        /// <param name="width">The capture width in cells.</param>
        /// <param name="height">The capture height in cells.</param>
        /// <returns>The rendered grid as newline-separated rows.</returns>
        public string RenderRegion(string regionId, int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                return string.Empty;
            }

            IWidget? widget = regionId switch
            {
                TranscriptRegion => _Conversation,
                SidebarRegion => _SidebarPane,
                FooterRegion => _Footer,
                ComposerRegion => _Composer,
                _ => (IWidget?)null
            };

            return widget == null ? string.Empty : Snapshot.RenderWidget(widget, width, height);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;

            StopThinking();

            try
            {
                _Cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            _App.KeyFilter = null;
            _App.Stop();
            _App.Dispose();
            _Cts.Dispose();
            _SaveGate.Dispose();
        }

        #endregion

        #region Private-Methods

        private void BuildLayouts(int composerHeight, int queueHeight)
        {
            int promptWidth = PromptText.Length;

            // Bottom rows, from the bottom up: composer (composerHeight) · footer (2 rows: a blank spacer
            // above the hint so the transcript never butts into the prompt area) · queue strip (queueHeight,
            // zero when empty). The transcript fills whatever remains above them. When boundary lines are on,
            // one empty row is reserved above the composer (the input rule) and one above the queue strip
            // (the queue rule); those rows carry no pane content and are painted by DrawBoundaries.
            const int footerHeight = 2;
            int inputBoundary = _ShowBoundaries ? 1 : 0;
            int queueBoundary = (_ShowBoundaries && queueHeight > 0) ? 1 : 0;
            int reserve = composerHeight + inputBoundary + footerHeight + queueHeight + queueBoundary;
            int footerOffset = composerHeight + inputBoundary;
            int queueOffset = footerOffset + footerHeight;

            LayoutBuilder expanded = Layout.Create()
                .Add(TranscriptRegion, r => r.FillWidth(0, SidebarWidth + SidebarGap).FillHeight(0, reserve).WithPadding(0))
                .Add(SidebarRegion, r => r.RightAnchored(0, SidebarWidth).FillHeight(0, reserve).WithPadding(0))
                .Add(FooterRegion, r => r.FillWidth().BottomAnchored(footerOffset, footerHeight).WithPadding(0))
                .Add(PromptLabelRegion, r => r.LeftAnchored(0, promptWidth).BottomAnchored(0, composerHeight).WithPadding(0))
                .Add(ComposerRegion, r => r.FillWidth(promptWidth, 0).BottomAnchored(0, composerHeight).WithPadding(0));
            if (queueHeight > 0)
            {
                expanded.Add(QueueRegion, r => r.FillWidth(0, SidebarWidth + SidebarGap).BottomAnchored(queueOffset, queueHeight).WithPadding(0));
            }

            _ExpandedLayout = expanded.Build();

            LayoutBuilder collapsed = Layout.Create()
                .Add(TranscriptRegion, r => r.FillWidth().FillHeight(0, reserve).WithPadding(0))
                .Add(FooterRegion, r => r.FillWidth().BottomAnchored(footerOffset, footerHeight).WithPadding(0))
                .Add(PromptLabelRegion, r => r.LeftAnchored(0, promptWidth).BottomAnchored(0, composerHeight).WithPadding(0))
                .Add(ComposerRegion, r => r.FillWidth(promptWidth, 0).BottomAnchored(0, composerHeight).WithPadding(0));
            if (queueHeight > 0)
            {
                collapsed.Add(QueueRegion, r => r.FillWidth().BottomAnchored(queueOffset, queueHeight).WithPadding(0));
            }

            _CollapsedLayout = collapsed.Build();
        }

        private static Theme CreateTheme()
        {
            CellStyle text = CellStyle.Default.WithForeground(Color.FromPalette(7));   // light grey, default background
            CellStyle accent = CellStyle.Default.WithForeground(Color.FromPalette(2)); // mux green
            CellStyle border = CellStyle.Default.WithForeground(Color.FromPalette(8)); // dim
            CellStyle muted = CellStyle.Default.WithForeground(Color.FromPalette(8));
            return new Theme("mux", text, accent, border, muted, false);
        }

        private async Task MonitorResponsiveAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    ApplyResponsiveLayout();
                    await Task.Delay(150, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private bool OnKeyFilter(KeyEvent key)
        {
            // Submit vs. insert-newline is resolved by TUIKit's SubmitKeyResolver: bare Enter submits,
            // Shift+Enter and Ctrl+J (the terminal-independent line-feed chord) insert a newline. Ctrl+J is
            // handled here rather than in the Ctrl switch below.
            SubmitDecision submit = _SubmitResolver.Resolve(key);
            if (submit == SubmitDecision.Submit)
            {
                OnEnter();
                return true;
            }

            if (submit == SubmitDecision.InsertNewline)
            {
                _Composer.InsertNewline();
                RefreshComposerLayout();
                return true;
            }

            // Fallback for terminals that deliver a modified Enter as a raw carriage-return or line-feed
            // character (rune 13/10) rather than a decoded key, which the resolver classifies as Ignore.
            // Alt+Enter is intercepted by Windows Terminal for the full-screen toggle and never reaches us.
            if (IsCarriageReturn(key))
            {
                _Composer.InsertNewline();
                RefreshComposerLayout();
                return true;
            }

            // Function-key and Ctrl chords. The focused composer swallows every Ctrl+key, so command
            // chords must be dispatched here rather than through the app's command router.
            if (key.Code == KeyCode.F1)
            {
                OpenCommandMenu();
                return true;
            }

            if (key.Code == KeyCode.F12)
            {
                ToggleMouseCapture();
                return true;
            }

            // Ctrl+Backspace deletes the previous word. In the enhanced keyboard protocol it arrives as a
            // dedicated Backspace key carrying the Ctrl modifier; the Ctrl'd control-character form
            // (0x08/0x7F) is handled in the Character branch below for terminals without that protocol.
            if (key.Code == KeyCode.Backspace && (key.Modifiers & KeyModifiers.Ctrl) != 0)
            {
                DeletePreviousWord();
                return true;
            }

            if (key.Code == KeyCode.Character && (key.Modifiers & KeyModifiers.Ctrl) != 0)
            {
                // Ctrl+Backspace deletes the previous word (0x7F/0x08 carrying the Ctrl modifier).
                if (key.Rune == 127 || key.Rune == 8)
                {
                    DeletePreviousWord();
                    return true;
                }

                switch (char.ToLowerInvariant((char)key.Rune))
                {
                    // Ctrl+J (insert newline) is resolved above by SubmitKeyResolver before reaching here.
                    case 'q': RequestQuit(); return true;
                    case 'l': ClearTranscript(); return true;
                    case 'b': ToggleSidebar(); return true;
                    case 's': SaveSession(); return true;
                    case 'e': OpenEndpointModal(); return true;
                    case 'g': OpenQueueEditor(); return true;
                    case 'p': OpenPromptEditor(); return true;
                }

                // Swallow any other Ctrl+key so control codes never land in the composer as text.
                return true;
            }

            if (key.Code == KeyCode.Escape)
            {
                CancelActiveTurn();
                return true;
            }

            if (key.Code == KeyCode.Up && _Composer.CaretRow == 0 && RecallPrevious())
            {
                return true;
            }

            if (key.Code == KeyCode.Down && _Composer.CaretRow == ComposerLineCount() - 1 && RecallNext())
            {
                return true;
            }

            // Everything else is ordinary editing: forward to the composer and consume it so the app's
            // focus routing does not handle it a second time.
            _Composer.HandleKey(key);
            RefreshComposerLayout();
            return true;
        }

        private void OnPaste(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            // Insert verbatim at the caret; newlines are preserved so a pasted block stays one unit. Enter
            // still submits the whole composer, so a multi-line paste is sent to the model as a single prompt.
            _Composer.InsertText(text);
            RefreshComposerLayout();
        }

        private void OnEnter()
        {
            string prompt = _Composer.Text;
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return;
            }

            _Composer.Text = string.Empty;
            RefreshComposerLayout();
            _PromptHistory.Add(prompt);

            if (prompt.TrimStart().StartsWith("/", StringComparison.Ordinal))
            {
                RouteSlash(prompt.TrimStart());
                return;
            }

            EnqueueOrRun(prompt);
        }

        /// <summary>
        /// Submits the startup <see cref="_InitialPrompt"/> as the first turn, then clears it so it runs
        /// only once. Routes through the same slash-vs-turn logic as a typed prompt. No-op when unset.
        /// </summary>
        private void SubmitInitialPrompt()
        {
            string? prompt = _InitialPrompt;
            _InitialPrompt = null;
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return;
            }

            _PromptHistory.Add(prompt);

            if (prompt.TrimStart().StartsWith("/", StringComparison.Ordinal))
            {
                RouteSlash(prompt.TrimStart());
                return;
            }

            EnqueueOrRun(prompt);
        }

        private void EnqueueOrRun(string prompt)
        {
            bool startNow;
            lock (_Sync)
            {
                // Run immediately only when idle and the queue isn't paused for editing; otherwise the
                // prompt joins the queue and is serviced in order once the model is free again.
                if (!_TurnInFlight && !_QueuePaused)
                {
                    _TurnInFlight = true;
                    startNow = true;
                }
                else
                {
                    _PendingPrompts.Add(prompt);
                    startNow = false;
                }
            }

            if (startNow)
            {
                RunTurn(prompt);
            }
            else
            {
                // A queued prompt is shown in the strip above the composer, not echoed into the transcript;
                // it is echoed there only when it actually starts.
                UpdateQueueStrip();
                RefreshSidebar();
                RefreshFooter();
            }
        }

        private void RunTurn(string prompt)
        {
            // Echo the prompt into the transcript when the turn actually starts, so the transcript shows
            // real turns in order rather than prompts that are still waiting in the queue.
            EchoPrompt(prompt);

            List<ConversationMessage> seed;
            lock (_Sync)
            {
                seed = new List<ConversationMessage>(_ConversationHistory);
            }

            Job job = _JobManager
                .SubmitAsync(prompt, _ApprovalPolicy, seed, _Cts.Token)
                .GetAwaiter()
                .GetResult();

            AgentEventProjector projector = new AgentEventProjector(_Conversation);
            Stopwatch stopwatch = Stopwatch.StartNew();
            long[] ttft = { -1 };
            projector.FirstTokenReceived += () => ttft[0] = stopwatch.ElapsedMilliseconds;

            // Dismiss the thinking indicator the instant the model produces output.
            projector.ModelResponded += StopThinking;

            lock (_Sync)
            {
                _ActiveJob = job;
                _TurnJobIds.Add(job.Id);
            }

            RefreshSidebar();
            RefreshFooter();

            // Show a thinking indicator beneath the echoed prompt until results begin streaming.
            StartThinking();

            Task projection = Task.Run(async () =>
            {
                await projector.ProjectAsync(job.ReadEventsAsync(_Cts.Token), _Cts.Token).ConfigureAwait(false);
                long total = stopwatch.ElapsedMilliseconds;
                OnTurnComplete(prompt, projector, total, ttft[0]);
            });

            lock (_Sync)
            {
                _ProjectorTasks.Add(projection);
            }
        }

        private void OnTurnComplete(string prompt, AgentEventProjector projector, long totalMs, long ttftMs)
        {
            // Safety net: dismiss the indicator even if the turn produced no observable output.
            StopThinking();

            string? next;
            lock (_Sync)
            {
                _ConversationHistory.Add(new ConversationMessage { Role = RoleEnum.User, Content = prompt });
                string answer = projector.CapturedAssistantText;
                if (!string.IsNullOrEmpty(answer))
                {
                    _ConversationHistory.Add(new ConversationMessage { Role = RoleEnum.Assistant, Content = answer });
                }

                _Stats.Turns++;
                _Stats.LastTtftMs = ttftMs;
                long stream = ttftMs >= 0 ? Math.Max(0, totalMs - ttftMs) : 0;
                _Stats.LastStreamMs = stream;
                _Stats.SessionStreamMs += stream;
                if (ttftMs >= 0)
                {
                    _Stats.SessionTtftMs += ttftMs;
                    _Stats.TtftSamples++;
                }

                if (projector.LastRunCompleted != null)
                {
                    _Stats.LastContextTokens = projector.LastRunCompleted.FinalEstimatedTokens;
                    _Stats.InputTokens += projector.LastRunCompleted.InputTokens;
                    _Stats.OutputTokens += projector.LastRunCompleted.OutputTokens;
                }

                _ActiveJob = null;

                // Start the next queued prompt in order — unless the queue is paused for editing, in which
                // case the shell goes idle and resumes when the editor closes.
                if (!_QueuePaused && _PendingPrompts.Count > 0)
                {
                    next = _PendingPrompts[0];
                    _PendingPrompts.RemoveAt(0);
                }
                else
                {
                    next = null;
                    _TurnInFlight = false;
                }
            }

            UpdateQueueStrip();
            RefreshSidebar();
            RefreshFooter();
            AutoSave();

            if (next != null)
            {
                RunTurn(next);
            }
        }

        // Renders the pending-prompt strip above the composer and resizes it to fit, so an empty queue
        // takes no space and a growing queue pushes the transcript up. Called whenever the queue changes.
        private void UpdateQueueStrip()
        {
            List<string> queued;
            bool paused;
            lock (_Sync)
            {
                queued = new List<string>(_PendingPrompts);
                paused = _QueuePaused;

                // Rows: a blank spacer, the header, then one row per queued prompt (capped).
                int desired = queued.Count == 0 ? 0 : Math.Min(queued.Count + 2, MaxQueueStripRows);
                if (desired != _QueueHeight)
                {
                    _QueueHeight = desired;
                    RebuildLayoutNoLock();
                }
            }

            RenderQueueStrip(queued, paused);
        }

        // Grows or shrinks the composer to fit its current line count (up to a cap), so Ctrl+J newlines are
        // visible as they are typed. Called after any composer edit; rebuilds the layout only on a change.
        private void RefreshComposerLayout()
        {
            int desired = Math.Clamp(ComposerLineCount(), 1, MaxComposerRows);
            lock (_Sync)
            {
                if (desired == _ComposerHeight)
                {
                    return;
                }

                _ComposerHeight = desired;
                RebuildLayoutNoLock();
            }
        }

        // Rebuilds both layout variants from the current composer/queue heights and re-applies the active
        // one. Caller must hold _Sync.
        private void RebuildLayoutNoLock()
        {
            BuildLayouts(_ComposerHeight, _QueueHeight);
            bool collapse = _ManualCollapsed || _Backend.Size.Width < CollapseThreshold;
            _App.Layout = collapse ? _CollapsedLayout : _ExpandedLayout;
            _CollapsedApplied = collapse;
        }

        private void RenderQueueStrip(List<string> queued, bool paused)
        {
            _QueuePane.Clear();
            if (queued.Count == 0)
            {
                return;
            }

            // A blank spacer above the header so the transcript never butts into the queue strip.
            _QueuePane.WriteLine(Text.From(string.Empty));

            string hint = paused ? "paused — editing" : "CTRL-G/edit";
            _QueuePane.WriteLine(Text.From($"QUEUED ({queued.Count}) · {hint}").Yellow().Bold());

            // A spacer row and a header row, then up to MaxQueueStripRows-2 prompt rows; the last row
            // summarizes any excess.
            int promptRows = MaxQueueStripRows - 2;
            if (queued.Count <= promptRows)
            {
                for (int i = 0; i < queued.Count; i++)
                {
                    _QueuePane.WriteLine(Text.From($"  {i + 1}. {PromptPreview(queued[i])}").Dim());
                }

                return;
            }

            for (int i = 0; i < promptRows - 1; i++)
            {
                _QueuePane.WriteLine(Text.From($"  {i + 1}. {PromptPreview(queued[i])}").Dim());
            }

            _QueuePane.WriteLine(Text.From($"  …and {queued.Count - (promptRows - 1)} more").Dim());
        }

        private static string PromptPreview(string prompt)
        {
            string flattened = (prompt ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            const int max = 60;
            return flattened.Length <= max ? flattened : flattened.Substring(0, max - 1) + "…";
        }

        // Opens the queue editor. Processing pauses while it is open so a turn finishing mid-edit does not
        // start the next prompt; on close the edited list becomes the queue and processing resumes.
        private void OpenQueueEditor()
        {
            List<string> snapshot;
            lock (_Sync)
            {
                _QueuePaused = true;
                snapshot = new List<string>(_PendingPrompts);
            }

            UpdateQueueStrip();
            RefreshSidebar();

            QueueEditorModal modal = new QueueEditorModal(snapshot);
            _App.Modals.Push(modal);
            _ = ResolveQueueEditorAsync(modal);
        }

        // Opens the tasks viewer for the focused job's plan, letting a human inspect and annotate task
        // status and notes. Changes apply to the live plan, so they persist and show in the sidebar.
        private void OpenTasksModal()
        {
            Job? planJob;
            lock (_Sync)
            {
                planJob = _ActiveJob ?? _JobManager.FocusedJob;
            }

            Mux.Core.Tasks.TaskPlan plan = planJob?.TaskPlan ?? new Mux.Core.Tasks.TaskPlan();
            TasksModal modal = new TasksModal(plan);
            _App.Modals.Push(modal);
            _ = ResolveTasksModalAsync(modal);
        }

        private async Task ResolveTasksModalAsync(TasksModal modal)
        {
            await modal.Completion.ConfigureAwait(false);
            RefreshSidebar();
            AutoSave();
        }

        private async Task ResolveQueueEditorAsync(QueueEditorModal modal)
        {
            object? result = await modal.Completion.ConfigureAwait(false);

            string? next = null;
            lock (_Sync)
            {
                if (result is List<string> edited)
                {
                    _PendingPrompts.Clear();
                    _PendingPrompts.AddRange(edited);
                }

                _QueuePaused = false;

                // Resume: if the model is idle and prompts remain, start the next one in order.
                if (!_TurnInFlight && _PendingPrompts.Count > 0)
                {
                    _TurnInFlight = true;
                    next = _PendingPrompts[0];
                    _PendingPrompts.RemoveAt(0);
                }
            }

            UpdateQueueStrip();
            RefreshSidebar();
            RefreshFooter();

            if (next != null)
            {
                RunTurn(next);
            }
        }

        // Opens the prompt-profile editor. Empty fields are pre-filled with their built-in defaults so the
        // user sees the effective prompt; on save, a field still equal to its default is stored empty
        // (inherit), and the active profile is persisted and applied live.
        private void OpenPromptEditor()
        {
            List<PromptProfile> stored = SettingsLoader.LoadPrompts();
            if (stored.Count == 0)
            {
                stored = new List<PromptProfile> { new PromptProfile { Name = "Default", IsActive = true } };
            }

            List<PromptProfile> display = new List<PromptProfile>();
            foreach (PromptProfile p in stored)
            {
                display.Add(new PromptProfile
                {
                    Name = p.Name,
                    IsActive = p.IsActive,
                    SystemPrompt = string.IsNullOrWhiteSpace(p.SystemPrompt) ? Defaults.SystemPrompt : p.SystemPrompt,
                    ToolsDisabledPrompt = string.IsNullOrWhiteSpace(p.ToolsDisabledPrompt) ? Defaults.ToolsDisabledSystemPrompt : p.ToolsDisabledPrompt,
                    CompactionPrompt = string.IsNullOrWhiteSpace(p.CompactionPrompt) ? Defaults.CompactionSystemPrompt : p.CompactionPrompt
                });
            }

            PromptEditorModal modal = new PromptEditorModal(display);
            _App.Modals.Push(modal);
            _ = ResolvePromptEditorAsync(modal);
        }

        private async Task ResolvePromptEditorAsync(PromptEditorModal modal)
        {
            object? result = await modal.Completion.ConfigureAwait(false);
            if (result is not List<PromptProfile> edited || edited.Count == 0)
            {
                return;
            }

            // Store a field that still matches its built-in default as empty, so it keeps inheriting.
            PromptProfile? active = null;
            foreach (PromptProfile p in edited)
            {
                if (p.SystemPrompt == Defaults.SystemPrompt) p.SystemPrompt = string.Empty;
                if (p.ToolsDisabledPrompt == Defaults.ToolsDisabledSystemPrompt) p.ToolsDisabledPrompt = string.Empty;
                if (p.CompactionPrompt == Defaults.CompactionSystemPrompt) p.CompactionPrompt = string.Empty;

                if (p.IsActive && active == null)
                {
                    active = p;
                }
            }

            if (active == null)
            {
                active = edited[0];
                active.IsActive = true;
            }

            SettingsLoader.SavePrompts(edited);
            _OnPromptProfileSelected?.Invoke(active);
            WriteNotice($"Prompt profile: {active.Name}");
        }

        private void CancelActiveTurn()
        {
            Job? active;
            lock (_Sync)
            {
                active = _ActiveJob;
            }

            if (active != null)
            {
                _ = _JobManager.CancelAsync(active.Id, _Cts.Token);
            }
        }

        private void StartThinking()
        {
            // TUIKit's ActivityIndicator owns the spinner-and-rotating-phrase state machine (its default
            // braille frames match mux's). Phrases are shuffled so the sequential rotation still feels
            // varied, the way mux's per-swap random pick did.
            ActivityIndicator indicator = new ActivityIndicator
            {
                Phrases = ShuffledThinkingPhrases()
            };
            CancellationTokenSource cts = new CancellationTokenSource();

            lock (_ThinkingSync)
            {
                _ThinkingIndicator = indicator;
                _ThinkingActive = true;
                _ThinkingCts = cts;
                _ThinkingHandle = _Conversation.WriteLine(Text.From(indicator.CurrentLine).Dim());
            }

            _ = AnimateThinkingAsync(cts.Token);
        }

        private void StopThinking()
        {
            CancellationTokenSource? cts;
            lock (_ThinkingSync)
            {
                if (!_ThinkingActive)
                {
                    return;
                }

                _ThinkingActive = false;
                _ThinkingHandle?.Update(StyledText.Empty);
                _ThinkingHandle = null;
                _ThinkingIndicator = null;
                cts = _ThinkingCts;
                _ThinkingCts = null;
            }

            try
            {
                cts?.Cancel();
                cts?.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private async Task AnimateThinkingAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(130, cancellationToken).ConfigureAwait(false);

                    lock (_ThinkingSync)
                    {
                        if (!_ThinkingActive || _ThinkingHandle == null || _ThinkingIndicator == null)
                        {
                            return;
                        }

                        // The indicator advances its spinner every tick and rotates the phrase on its own
                        // schedule (default every 30 ticks ≈ four seconds).
                        _ThinkingIndicator.Tick();
                        _ThinkingHandle.Update(Text.From(_ThinkingIndicator.CurrentLine).Dim());
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private static string[] ShuffledThinkingPhrases()
        {
            List<string> phrases = new List<string>(ThinkingMessages.All);
            for (int i = phrases.Count - 1; i > 0; i--)
            {
                int j = Random.Shared.Next(i + 1);
                string swap = phrases[i];
                phrases[i] = phrases[j];
                phrases[j] = swap;
            }

            return phrases.ToArray();
        }

        private void RouteSlash(string input)
        {
            bool handled = _SlashHandler != null && _SlashHandler(input);
            if (!handled)
            {
                _Conversation.WriteLine(Text.From($"Unknown command: {input}").Yellow());
            }
        }

        private bool RecallPrevious()
        {
            if (_PromptHistory.TryPrevious(out string entry))
            {
                _Composer.Text = entry;
                RefreshComposerLayout();
                return true;
            }

            return false;
        }

        private bool RecallNext()
        {
            if (_PromptHistory.TryNext(out string entry))
            {
                _Composer.Text = entry;
                RefreshComposerLayout();
                return true;
            }

            return false;
        }

        private void DeletePreviousWord()
        {
            string text = _Composer.Text.Replace("\r\n", "\n").Replace("\r", "\n");
            int row = _Composer.CaretRow;
            int col = _Composer.CaretColumn;
            string[] lines = text.Split('\n');
            if (row < 0 || row >= lines.Length || col <= 0)
            {
                _Composer.Backspace();
                RefreshComposerLayout();
                return;
            }

            string line = lines[row];
            int i = Math.Min(col, line.Length);
            while (i > 0 && char.IsWhiteSpace(line[i - 1]))
            {
                i--;
            }

            while (i > 0 && !char.IsWhiteSpace(line[i - 1]))
            {
                i--;
            }

            int count = Math.Max(1, col - i);
            for (int k = 0; k < count; k++)
            {
                _Composer.Backspace();
            }

            RefreshComposerLayout();
        }

        private void EchoPrompt(string prompt)
        {
            // "mux> <prompt>" with a leading blank line; "mux>" in green, the prompt in grey. A multi-line
            // prompt keeps its newlines: the first line follows the "mux>" marker and each subsequent line
            // is indented to align under it. The thinking indicator is shown directly beneath the prompt.
            _Conversation.WriteLine(Text.From(string.Empty));

            string[] lines = (prompt ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            _Conversation.WriteLine(Text.From(PromptText).Green().Bold().Append(Text.From(lines[0])));

            string indent = new string(' ', PromptText.Length);
            for (int i = 1; i < lines.Length; i++)
            {
                _Conversation.WriteLine(Text.From(indent + lines[i]));
            }
        }

        private void AutoSave()
        {
            if (_Store == null)
            {
                return;
            }

            _ = SaveQuietlyAsync();
        }

        private async Task SaveQuietlyAsync()
        {
            try
            {
                await SaveSessionAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Autosave is best-effort; a failed background save must never disrupt the UI.
            }
        }

        private void SaveSession()
        {
            if (_Store == null)
            {
                WriteNotice("Session persistence is disabled.");
                return;
            }

            _ = SaveWithNoticeAsync();
        }

        private async Task SaveWithNoticeAsync()
        {
            try
            {
                await SaveSessionAsync().ConfigureAwait(false);
                WriteNotice("✓ Session saved.");
            }
            catch (Exception ex)
            {
                WriteNotice("Save failed: " + ex.Message);
            }
        }

        private void OpenSessionBrowser()
        {
            if (_Store == null)
            {
                _App.Modals.Push(new MessageModal("Sessions", "Session persistence is disabled.", new List<string> { "OK" }));
                return;
            }

            IReadOnlyList<SessionSnapshot> sessions = _Store.ListAsync(_Cts.Token).GetAwaiter().GetResult();
            if (sessions.Count == 0)
            {
                _App.Modals.Push(new MessageModal("Sessions", "No saved sessions.", new List<string> { "OK" }));
                return;
            }

            List<string> labels = new List<string>();
            foreach (SessionSnapshot session in sessions)
            {
                string title = string.IsNullOrWhiteSpace(session.Title) ? session.Id : session.Title;
                labels.Add($"{title}  ({session.Id})");
            }

            SelectModal modal = new SelectModal("Sessions — select to resume", labels);
            _App.Modals.Push(modal);
            _ = ResolveSessionBrowserAsync(modal, sessions);
        }

        private async Task ResolveSessionBrowserAsync(SelectModal modal, IReadOnlyList<SessionSnapshot> sessions)
        {
            object? result = await modal.Completion.ConfigureAwait(false);
            int index = result is int value ? value : -1;
            if (index >= 0 && index < sessions.Count)
            {
                RestoreSession(SessionResumeService.Resume(sessions[index]));
            }
        }

        private void ReplayJob(PersistedJobSnapshot job, bool interrupted)
        {
            string label = string.IsNullOrWhiteSpace(job.Title) ? job.Prompt : job.Title;
            _Conversation.WriteLine(Text.From($"« resumed: {label} »").Dim());

            foreach (ConversationMessage message in job.ConversationHistory)
            {
                if (message.Role == RoleEnum.User && !string.IsNullOrEmpty(message.Content))
                {
                    EchoPrompt(message.Content);
                    lock (_Sync)
                    {
                        _ConversationHistory.Add(new ConversationMessage { Role = RoleEnum.User, Content = message.Content });
                    }
                }
                else if (message.Role == RoleEnum.Assistant && !string.IsNullOrEmpty(message.Content))
                {
                    _Conversation.WriteLine(Text.From(message.Content));
                    lock (_Sync)
                    {
                        _ConversationHistory.Add(new ConversationMessage { Role = RoleEnum.Assistant, Content = message.Content });
                    }
                }
            }

            if (interrupted)
            {
                _Conversation.WriteLine(Text.From("⚠ interrupted — re-run required").Yellow());
            }
        }

        private void WriteNotice(string text)
        {
            _Conversation.WriteLine(Text.From(text).Dim());
        }

        /// <summary>
        /// Writes a dim background notice — for example an MCP server connection result — into the transcript.
        /// Safe to call from a background thread; the conversation view serializes writes with the render loop,
        /// the same path the streaming agent projector uses.
        /// </summary>
        /// <param name="text">The notice text. Ignored when null or empty.</param>
        public void PostNotice(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            WriteNotice(text);
        }

        private void OpenCommandMenu()
        {
            List<CommandDescriptor> commands = new List<CommandDescriptor>();
            List<IReadOnlyList<string>> rows = new List<IReadOnlyList<string>>();
            foreach (CommandDescriptor descriptor in _Catalog.Commands)
            {
                // The menu-openers (F1 / help) would just reopen the menu from within itself, so omit them.
                if (string.Equals(descriptor.Id, "mux.menu", StringComparison.Ordinal)
                    || string.Equals(descriptor.Id, "mux.help", StringComparison.Ordinal))
                {
                    continue;
                }

                commands.Add(descriptor);
                string chord = string.IsNullOrEmpty(descriptor.Chord) ? string.Empty : descriptor.Chord;
                string alias = descriptor.SlashAliases.Count > 0
                    ? "/" + string.Join(" /", descriptor.SlashAliases)
                    : string.Empty;
                rows.Add(new[] { descriptor.Title, chord, alias });
            }

            if (commands.Count == 0)
            {
                return;
            }

            // Align the title / chord / slash columns each on a single vertical axis across every row.
            IReadOnlyList<string> labels = ColumnFormatter.Format(rows, 2);

            // Widen the modal by ~50% over the default select width (46) so the three aligned columns fit.
            const int commandMenuWidth = 69;
            WideSelectModal modal = new WideSelectModal("Commands — ↑↓ then Enter to run", labels, commandMenuWidth);
            _App.Modals.Push(modal);
            _ = ResolveCommandMenuAsync(modal, commands);
        }

        private async Task ResolveCommandMenuAsync(WideSelectModal modal, List<CommandDescriptor> commands)
        {
            object? result = await modal.Completion.ConfigureAwait(false);
            int index = result is int value ? value : -1;
            if (index >= 0 && index < commands.Count)
            {
                commands[index].Handler();
            }
        }

        private void RequestQuit()
        {
            MessageModal modal = new MessageModal(
                string.Empty,
                CenterMessage("Exit mux?"),
                new List<string> { "Exit", "Cancel" });
            _App.Modals.Push(modal);
            _ = ResolveQuitAsync(modal);
        }

        // MessageModal draws its message left-aligned in a box that is at least 28 columns wide, so a short
        // prompt looks left-shifted; pad it so it renders centered.
        private static string CenterMessage(string text)
        {
            const int minBoxContentWidth = 28;
            int pad = Math.Max(0, (minBoxContentWidth - text.Length) / 2);
            return new string(' ', pad) + text;
        }

        private async Task ResolveQuitAsync(MessageModal modal)
        {
            object? result = await modal.Completion.ConfigureAwait(false);
            if (result is int index && index == 0)
            {
                _App.RequestStop();
            }
        }

        // The kinds of rows shown in the Endpoints / models list, kept in a list parallel to the option
        // strings so a blank separator or a trailing action never has to be inferred from index math.
        private enum EndpointMenuAction
        {
            Switch,
            None,
            Add,
            Edit,
            Remove,
            Import
        }

        private void OpenEndpointModal()
        {
            List<EndpointConfig> endpoints = LoadEndpointsSafe();
            List<string> options = new List<string>();
            List<EndpointMenuAction> actions = new List<EndpointMenuAction>();

            foreach (EndpointConfig endpoint in endpoints)
            {
                string marker = string.Equals(endpoint.Name, _EndpointName, StringComparison.OrdinalIgnoreCase) ? "● " : "  ";
                options.Add($"{marker}{endpoint.Name}  ({endpoint.AdapterType} · {endpoint.Model})");
                actions.Add(EndpointMenuAction.Switch);
            }

            // A blank separator row sets the configured models apart from the management actions below.
            if (endpoints.Count > 0)
            {
                options.Add(string.Empty);
                actions.Add(EndpointMenuAction.None);
            }

            options.Add("+ Add endpoint…");
            actions.Add(EndpointMenuAction.Add);
            if (endpoints.Count > 0)
            {
                options.Add("✎ Edit endpoint…");
                actions.Add(EndpointMenuAction.Edit);
                options.Add("- Remove endpoint…");
                actions.Add(EndpointMenuAction.Remove);
            }

            options.Add("⬇ Import from Ollama…");
            actions.Add(EndpointMenuAction.Import);

            EndpointSelectModal modal = new EndpointSelectModal("Endpoints / models", options, endpoints.Count);
            _App.Modals.Push(modal);
            _ = ResolveEndpointModalAsync(modal, endpoints, actions);
        }

        private async Task ResolveEndpointModalAsync(EndpointSelectModal modal, List<EndpointConfig> endpoints, List<EndpointMenuAction> actions)
        {
            object? result = await modal.Completion.ConfigureAwait(false);
            if (!(result is EndpointModalResult choice))
            {
                return;
            }

            int index = choice.Index;
            if (index < 0 || index >= actions.Count)
            {
                return;
            }

            // The e/d/Delete shortcuts act on the highlighted endpoint directly, skipping the "which
            // endpoint?" picker the management rows use.
            if (choice.Activation == EndpointModalActivationEnum.Edit)
            {
                if (index < endpoints.Count)
                {
                    await EditEndpointAsync(endpoints[index]).ConfigureAwait(false);
                }

                return;
            }

            if (choice.Activation == EndpointModalActivationEnum.Remove)
            {
                if (index < endpoints.Count)
                {
                    await RemoveEndpointConfirmAsync(endpoints[index]).ConfigureAwait(false);
                }

                return;
            }

            switch (actions[index])
            {
                case EndpointMenuAction.Switch:
                    SwitchEndpoint(endpoints[index]);
                    break;
                case EndpointMenuAction.Add:
                    await AddEndpointFormAsync().ConfigureAwait(false);
                    break;
                case EndpointMenuAction.Edit:
                    await EditEndpointFormAsync(endpoints).ConfigureAwait(false);
                    break;
                case EndpointMenuAction.Remove:
                    await RemoveEndpointAsync(endpoints).ConfigureAwait(false);
                    break;
                case EndpointMenuAction.Import:
                    await ImportFromOllamaAsync().ConfigureAwait(false);
                    break;
                case EndpointMenuAction.None:
                default:
                    break;
            }
        }

        private void SwitchEndpoint(EndpointConfig endpoint)
        {
            _OnEndpointSelected?.Invoke(endpoint);
            lock (_Sync)
            {
                _EndpointName = endpoint.Name;
                _Model = endpoint.Model;
                _EffortLabel = EffortLabelFor(endpoint);
                _ThinkingLabel = endpoint.ShowThinking ? "on" : "off";
            }

            WriteNotice($"Switched to endpoint {endpoint.Name} ({endpoint.Model})");
            RefreshFooter();
            RefreshSidebar();

            ValidateSwitchedModel(endpoint);
        }

        // After an endpoint switch, load/validate the newly selected model in the background — for lazily
        // loading backends (Ollama) this warms the model into memory; for hosted providers it confirms the
        // model, URL, and credentials are usable before the next turn. The outcome is surfaced as a notice.
        private void ValidateSwitchedModel(EndpointConfig endpoint)
        {
            if (_OnValidateModel == null)
            {
                return;
            }

            string label = string.IsNullOrWhiteSpace(endpoint.Model) ? endpoint.Name : endpoint.Model;
            WriteNotice($"Loading model {label} on endpoint {endpoint.Name}");

            _ = Task.Run(async () =>
            {
                try
                {
                    ModelLoadResult result = await _OnValidateModel(endpoint, _Cts.Token).ConfigureAwait(false);
                    if (result.Success)
                    {
                        PostNotice($"Model {label} loaded successfully on endpoint {endpoint.Name}");
                    }
                    else
                    {
                        PostNotice($"Unable to load model {label} on endpoint {endpoint.Name}: {result.Error}");
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    PostNotice($"Unable to load model {label} on endpoint {endpoint.Name}: {ex.Message}");
                }
            });
        }

        private async Task AddEndpointFormAsync()
        {
            EndpointFormModal modal = new EndpointFormModal("Add endpoint");
            _App.Modals.Push(modal);
            object? result = await modal.Completion.ConfigureAwait(false);
            if (result is EndpointConfig endpoint)
            {
                SaveEndpoint(endpoint, isNew: true, previousName: null);

                // Make the newly added endpoint active so the very next turn uses its adapter, URL, and
                // model — otherwise a freshly added endpoint looks ignored until it is switched to.
                SwitchEndpoint(endpoint);
            }
        }

        private async Task EditEndpointFormAsync(List<EndpointConfig> endpoints)
        {
            List<string> names = new List<string>();
            foreach (EndpointConfig endpoint in endpoints)
            {
                names.Add(endpoint.Name);
            }

            SelectModal pick = new SelectModal("Edit which endpoint?", names);
            _App.Modals.Push(pick);
            object? pickResult = await pick.Completion.ConfigureAwait(false);
            int pickIndex = pickResult is int p ? p : -1;
            if (pickIndex < 0 || pickIndex >= endpoints.Count)
            {
                return;
            }

            await EditEndpointAsync(endpoints[pickIndex]).ConfigureAwait(false);
        }

        private async Task EditEndpointAsync(EndpointConfig original)
        {
            EndpointFormModal modal = new EndpointFormModal($"Edit {original.Name}", original);
            _App.Modals.Push(modal);
            object? result = await modal.Completion.ConfigureAwait(false);
            if (result is EndpointConfig edited)
            {
                SaveEndpoint(edited, isNew: false, previousName: original.Name);

                // Keep the running session consistent if the active endpoint was edited.
                bool editingActive;
                lock (_Sync)
                {
                    editingActive = string.Equals(original.Name, _EndpointName, StringComparison.OrdinalIgnoreCase);
                }

                if (editingActive)
                {
                    SwitchEndpoint(edited);
                }
            }
        }

        private void SaveEndpoint(EndpointConfig endpoint, bool isNew, string? previousName)
        {
            List<EndpointConfig> endpoints = LoadEndpointsSafe();
            endpoints.RemoveAll(e => string.Equals(e.Name, endpoint.Name, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(previousName) && !string.Equals(previousName, endpoint.Name, StringComparison.OrdinalIgnoreCase))
            {
                endpoints.RemoveAll(e => string.Equals(e.Name, previousName, StringComparison.OrdinalIgnoreCase));
            }

            // Enforce a single default: when this endpoint is marked default, demote every other one so the
            // saved list has exactly one, regardless of position (LoadEndpoints keeps the first default it
            // finds and would otherwise demote this appended entry on the next load).
            if (endpoint.IsDefault)
            {
                foreach (EndpointConfig other in endpoints)
                {
                    other.IsDefault = false;
                }
            }

            endpoints.Add(endpoint);
            try
            {
                SettingsLoader.SaveEndpoints(endpoints);
                WriteNotice(isNew
                    ? $"Saved endpoint {endpoint.Name}."
                    : $"Updated endpoint {endpoint.Name}.");
            }
            catch (Exception ex)
            {
                WriteNotice("Save failed: " + ex.Message);
            }
        }

        private async Task RemoveEndpointAsync(List<EndpointConfig> endpoints)
        {
            List<string> names = new List<string>();
            foreach (EndpointConfig endpoint in endpoints)
            {
                names.Add(endpoint.Name);
            }

            SelectModal pick = new SelectModal("Remove which endpoint?", names);
            _App.Modals.Push(pick);
            object? pickResult = await pick.Completion.ConfigureAwait(false);
            int pickIndex = pickResult is int p ? p : -1;
            if (pickIndex < 0 || pickIndex >= endpoints.Count)
            {
                return;
            }

            await RemoveEndpointConfirmAsync(endpoints[pickIndex]).ConfigureAwait(false);
        }

        private async Task RemoveEndpointConfirmAsync(EndpointConfig endpoint)
        {
            string name = endpoint.Name;
            MessageModal confirm = new MessageModal("Remove endpoint", $"Remove '{name}'?", new List<string> { "Remove", "Cancel" });
            _App.Modals.Push(confirm);
            object? confirmResult = await confirm.Completion.ConfigureAwait(false);
            if (!(confirmResult is int c) || c != 0)
            {
                return;
            }

            List<EndpointConfig> current = LoadEndpointsSafe();
            current.RemoveAll(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
            try
            {
                SettingsLoader.SaveEndpoints(current);
                WriteNotice($"Removed endpoint {name}.");
            }
            catch (Exception ex)
            {
                WriteNotice("Remove failed: " + ex.Message);
            }
        }

        private async Task ImportFromOllamaAsync()
        {
            // Ask for the Ollama base URL to import completion models from.
            PromptModal urlPrompt = new PromptModal("Import from Ollama — base URL", "http://localhost:11434");
            _App.Modals.Push(urlPrompt);
            object? urlResult = await urlPrompt.Completion.ConfigureAwait(false);
            string entered = (urlResult as string ?? string.Empty).Trim();
            if (entered.Length == 0)
            {
                return;
            }

            string normalized = OllamaModelLister.NormalizeBaseUrl(entered);

            // Pull the installed models from the endpoint.
            WriteNotice($"Querying Ollama at {normalized}…");
            List<string> models;
            try
            {
                models = await OllamaModelLister
                    .ListModelsAsync(normalized, LoadIgnoreCertSafe(), _Cts.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                WriteNotice($"Ollama import failed: {ex.Message}");
                return;
            }

            if (models.Count == 0)
            {
                WriteNotice($"No models found at {normalized}.");
                return;
            }

            // Only offer model+endpoint combinations that are not already configured.
            List<EndpointConfig> existing = LoadEndpointsSafe();
            HashSet<string> alreadyImported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (EndpointConfig endpoint in existing)
            {
                if (endpoint.AdapterType == AdapterTypeEnum.Ollama
                    && string.Equals(OllamaModelLister.NormalizeBaseUrl(endpoint.BaseUrl), normalized, StringComparison.OrdinalIgnoreCase))
                {
                    alreadyImported.Add(endpoint.Model);
                }
            }

            List<string> candidates = new List<string>();
            foreach (string model in models)
            {
                if (!alreadyImported.Contains(model))
                {
                    candidates.Add(model);
                }
            }

            int skipped = models.Count - candidates.Count;
            if (candidates.Count == 0)
            {
                WriteNotice($"All {models.Count} model(s) at {normalized} are already imported.");
                return;
            }

            string title = skipped > 0
                ? $"Import from {normalized} ({skipped} already imported)"
                : $"Import from {normalized}";
            MultiSelectModal<string> picker = new MultiSelectModal<string>(title, candidates)
            {
                FooterHint = "⚠ COMPLETION models only · Space toggle · a all · Enter import · Esc cancel"
            };
            _App.Modals.Push(picker);
            object? pickResult = await picker.Completion.ConfigureAwait(false);
            if (!(pickResult is IReadOnlyList<int> chosen) || chosen.Count == 0)
            {
                return;
            }

            // Add an endpoint per selected model, skipping any combination that already exists and giving
            // each a name unique among the configured endpoints.
            List<EndpointConfig> current = LoadEndpointsSafe();
            HashSet<string> takenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (EndpointConfig endpoint in current)
            {
                takenNames.Add(endpoint.Name);
            }

            int added = 0;
            foreach (int index in chosen)
            {
                if (index < 0 || index >= candidates.Count)
                {
                    continue;
                }

                string model = candidates[index];
                bool exists = current.Exists(e =>
                    e.AdapterType == AdapterTypeEnum.Ollama
                    && string.Equals(OllamaModelLister.NormalizeBaseUrl(e.BaseUrl), normalized, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(e.Model, model, StringComparison.OrdinalIgnoreCase));
                if (exists)
                {
                    continue;
                }

                string name = UniqueEndpointName(model, takenNames);
                takenNames.Add(name);
                current.Add(new EndpointConfig
                {
                    Name = name,
                    AdapterType = AdapterTypeEnum.Ollama,
                    BaseUrl = normalized,
                    Model = model
                });
                added++;
            }

            if (added == 0)
            {
                WriteNotice("Nothing to import — the selected models are already configured.");
                return;
            }

            try
            {
                SettingsLoader.SaveEndpoints(current);
                WriteNotice($"Imported {added} model(s) from {normalized}.");
            }
            catch (Exception ex)
            {
                WriteNotice("Import save failed: " + ex.Message);
            }
        }

        private static string UniqueEndpointName(string model, HashSet<string> taken)
        {
            string baseName = string.IsNullOrWhiteSpace(model) ? "ollama-model" : model.Trim();
            if (!taken.Contains(baseName))
            {
                return baseName;
            }

            for (int suffix = 2; ; suffix++)
            {
                string candidate = $"{baseName} ({suffix})";
                if (!taken.Contains(candidate))
                {
                    return candidate;
                }
            }
        }

        private static bool LoadIgnoreCertSafe()
        {
            try
            {
                return SettingsLoader.LoadSettings().IgnoreCertErrors;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static List<EndpointConfig> LoadEndpointsSafe()
        {
            try
            {
                return SettingsLoader.LoadEndpoints();
            }
            catch (Exception)
            {
                return new List<EndpointConfig>();
            }
        }

        // The kinds of rows shown in the MCP servers list, kept in a list parallel to the option strings
        // so a blank separator or a trailing action never has to be inferred from index math. Selecting a
        // server row edits that server directly.
        private enum McpMenuAction
        {
            Edit,
            None,
            Add,
            Remove
        }

        private void OpenMcpModal()
        {
            List<McpServerConfig> servers = LoadMcpServersSafe();
            Dictionary<string, McpServerStatus> statusByName = new Dictionary<string, McpServerStatus>(StringComparer.OrdinalIgnoreCase);
            if (_McpRuntime != null)
            {
                foreach (McpServerStatus status in _McpRuntime.GetStatus())
                {
                    statusByName[status.Name] = status;
                }
            }

            List<string> options = new List<string>();
            List<McpMenuAction> actions = new List<McpMenuAction>();

            foreach (McpServerConfig server in servers)
            {
                string detail = server.Transport == McpTransportTypeEnum.Http
                    ? server.Url
                    : server.Command;

                // Prefix each server with a connectivity glyph and append its live status when known.
                string glyph = "·";
                string state = string.Empty;
                if (_McpRuntime != null)
                {
                    if (statusByName.TryGetValue(server.Name, out McpServerStatus? status) && status.Connected)
                    {
                        glyph = "●";
                        state = $" · online, {status.ToolCount} tool{(status.ToolCount == 1 ? string.Empty : "s")}";
                    }
                    else
                    {
                        glyph = "○";
                        state = " · offline";
                    }
                }

                options.Add($"{glyph} {server.Name}  ({server.Transport} · {detail}{state})");
                actions.Add(McpMenuAction.Edit);
            }

            // A blank separator row sets the configured servers apart from the management actions below.
            if (servers.Count > 0)
            {
                options.Add(string.Empty);
                actions.Add(McpMenuAction.None);
            }

            options.Add("+ Add MCP server…");
            actions.Add(McpMenuAction.Add);
            if (servers.Count > 0)
            {
                options.Add("- Remove MCP server…");
                actions.Add(McpMenuAction.Remove);
            }

            // Widen the modal by 25% over TUIKit's default select width (46) so server rows render with room.
            const int mcpModalWidth = 58;
            WideSelectModal modal = new WideSelectModal("MCP servers — Enter to edit", options, mcpModalWidth);
            _App.Modals.Push(modal);
            _ = ResolveMcpModalAsync(modal, servers, actions);
        }

        private async Task ResolveMcpModalAsync(WideSelectModal modal, List<McpServerConfig> servers, List<McpMenuAction> actions)
        {
            object? result = await modal.Completion.ConfigureAwait(false);
            int index = result is int value ? value : -1;
            if (index < 0 || index >= actions.Count)
            {
                return;
            }

            switch (actions[index])
            {
                case McpMenuAction.Edit:
                    await EditMcpFormAsync(servers[index]).ConfigureAwait(false);
                    break;
                case McpMenuAction.Add:
                    await AddMcpFormAsync().ConfigureAwait(false);
                    break;
                case McpMenuAction.Remove:
                    await RemoveMcpAsync(servers).ConfigureAwait(false);
                    break;
                case McpMenuAction.None:
                default:
                    break;
            }
        }

        private async Task AddMcpFormAsync()
        {
            McpServerFormModal modal = new McpServerFormModal("Add MCP server");
            _App.Modals.Push(modal);
            object? result = await modal.Completion.ConfigureAwait(false);
            if (result is McpServerConfig server)
            {
                SaveMcpServer(server, previousName: null);
            }
        }

        private async Task EditMcpFormAsync(McpServerConfig original)
        {
            McpServerFormModal modal = new McpServerFormModal($"Edit {original.Name}", original);
            _App.Modals.Push(modal);
            object? result = await modal.Completion.ConfigureAwait(false);
            if (result is McpServerConfig edited)
            {
                SaveMcpServer(edited, previousName: original.Name);
            }
        }

        private void SaveMcpServer(McpServerConfig server, string? previousName)
        {
            List<McpServerConfig> servers = LoadMcpServersSafe();
            servers.RemoveAll(s => string.Equals(s.Name, server.Name, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(previousName) && !string.Equals(previousName, server.Name, StringComparison.OrdinalIgnoreCase))
            {
                servers.RemoveAll(s => string.Equals(s.Name, previousName, StringComparison.OrdinalIgnoreCase));
            }

            servers.Add(server);
            try
            {
                SettingsLoader.SaveMcpServers(servers);
                RefreshMcpRuntime();
                WriteNotice(string.IsNullOrEmpty(previousName)
                    ? $"Saved MCP server {server.Name}. Connecting…"
                    : $"Updated MCP server {server.Name}. Reconnecting…");
            }
            catch (Exception ex)
            {
                WriteNotice("Save failed: " + ex.Message);
            }
        }

        private void RefreshMcpRuntime()
        {
            // Apply the new configuration to the live session: reconnect servers and rediscover tools so the
            // change takes effect on the next turn without a restart. Null when MCP is not wired (e.g. tests).
            _McpRuntime?.RequestRefresh();
        }

        private async Task RemoveMcpAsync(List<McpServerConfig> servers)
        {
            List<string> names = new List<string>();
            foreach (McpServerConfig server in servers)
            {
                names.Add(server.Name);
            }

            SelectModal pick = new SelectModal("Remove which MCP server?", names);
            _App.Modals.Push(pick);
            object? pickResult = await pick.Completion.ConfigureAwait(false);
            int pickIndex = pickResult is int p ? p : -1;
            if (pickIndex < 0 || pickIndex >= servers.Count)
            {
                return;
            }

            string name = servers[pickIndex].Name;
            MessageModal confirm = new MessageModal("Remove MCP server", $"Remove '{name}'?", new List<string> { "Remove", "Cancel" });
            _App.Modals.Push(confirm);
            object? confirmResult = await confirm.Completion.ConfigureAwait(false);
            if (!(confirmResult is int c) || c != 0)
            {
                return;
            }

            List<McpServerConfig> current = LoadMcpServersSafe();
            current.RemoveAll(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
            try
            {
                SettingsLoader.SaveMcpServers(current);
                RefreshMcpRuntime();
                WriteNotice($"Removed MCP server {name}. Reconnecting…");
            }
            catch (Exception ex)
            {
                WriteNotice("Remove failed: " + ex.Message);
            }
        }

        private static List<McpServerConfig> LoadMcpServersSafe()
        {
            try
            {
                return SettingsLoader.LoadMcpServers();
            }
            catch (Exception)
            {
                return new List<McpServerConfig>();
            }
        }

        private void OpenSkillsModal()
        {
            if (_SkillRuntime == null)
            {
                WriteNotice("Skills are disabled (settings.json: skillsEnabled).");
                return;
            }

            List<SkillStatus> statuses = _SkillRuntime.GetStatus();
            List<string> options = new List<string>();
            List<SkillMenuAction> actions = new List<SkillMenuAction>();

            int enabledCount = 0;
            int invalidCount = 0;
            foreach (SkillStatus status in statuses)
            {
                if (status.Valid && status.Enabled) enabledCount++;
                if (!status.Valid) invalidCount++;

                string glyph = !status.Valid ? "⚠" : (status.Enabled ? "●" : "○");
                string tags = status.Tags.Count > 0 ? "  [" + string.Join(", ", status.Tags) + "]" : string.Empty;
                string plural = status.CommandCount == 1 ? string.Empty : "s";
                options.Add($"{glyph} {status.Title}  ({status.CommandCount} command{plural}){tags}");
                actions.Add(SkillMenuAction.Manage);
            }

            if (statuses.Count > 0)
            {
                options.Add(string.Empty);
                actions.Add(SkillMenuAction.None);
            }

            options.Add("+ New skill…");
            actions.Add(SkillMenuAction.New);
            options.Add("⬇ Import skill…");
            actions.Add(SkillMenuAction.Import);
            options.Add("↻ Reload skills");
            actions.Add(SkillMenuAction.Reload);

            string title = statuses.Count == 0
                ? "Skills — none yet"
                : $"Skills — {statuses.Count} ({enabledCount} on, {invalidCount} invalid)";
            SelectModal modal = new SelectModal(title, options);
            _App.Modals.Push(modal);
            _ = ResolveSkillsModalAsync(modal, statuses, actions);
        }

        private async Task ResolveSkillsModalAsync(SelectModal modal, List<SkillStatus> statuses, List<SkillMenuAction> actions)
        {
            object? result = await modal.Completion.ConfigureAwait(false);
            int index = result is int value ? value : -1;
            if (index < 0 || index >= actions.Count)
            {
                return;
            }

            switch (actions[index])
            {
                case SkillMenuAction.Manage:
                    await ManageSkillAsync(statuses[index]).ConfigureAwait(false);
                    break;
                case SkillMenuAction.New:
                    await NewSkillWizardAsync().ConfigureAwait(false);
                    break;
                case SkillMenuAction.Import:
                    await ImportSkillAsync().ConfigureAwait(false);
                    break;
                case SkillMenuAction.Reload:
                    _SkillRuntime?.RequestRefresh();
                    WriteNotice("Re-scanning skills…");
                    break;
                case SkillMenuAction.None:
                default:
                    break;
            }
        }

        private async Task ManageSkillAsync(SkillStatus status)
        {
            List<string> actions = new List<string>
            {
                "View",
                "Edit",
                status.Enabled ? "Disable" : "Enable",
                "Duplicate",
                "Remove"
            };

            SelectModal pick = new SelectModal($"{status.Title} — action", actions);
            _App.Modals.Push(pick);
            object? result = await pick.Completion.ConfigureAwait(false);
            int index = result is int value ? value : -1;
            if (index < 0)
            {
                return;
            }

            switch (index)
            {
                case 0:
                    ViewSkill(status);
                    break;
                case 1:
                    await EditSkillAsync(status).ConfigureAwait(false);
                    break;
                case 2:
                    ToggleSkill(status);
                    break;
                case 3:
                    await DuplicateSkillAsync(status).ConfigureAwait(false);
                    break;
                case 4:
                    await RemoveSkillAsync(status).ConfigureAwait(false);
                    break;
                default:
                    break;
            }
        }

        private async Task EditSkillAsync(SkillStatus status)
        {
            string path = Path.Combine(_SkillRuntime!.SkillsDirectory, status.Name, "SKILL.md");
            string original;
            try
            {
                original = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                WriteNotice("Could not open skill for editing: " + ex.Message);
                return;
            }

            SkillEditorModal editor = new SkillEditorModal(status.Name + " — SKILL.md", original);
            _App.Modals.Push(editor);
            object? result = await editor.Completion.ConfigureAwait(false);
            if (!(result is string edited) || string.Equals(edited, original, StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                File.WriteAllText(path, edited);
                _SkillRuntime.RequestRefresh();

                Skill reloaded = new SkillLoader(_SkillRuntime.SkillsDirectory).Load(Path.Combine(_SkillRuntime.SkillsDirectory, status.Name));
                if (reloaded.IsValid)
                {
                    WriteNotice($"Saved {status.Name}.");
                }
                else
                {
                    string reason = reloaded.Validation.Errors.Count > 0 ? reloaded.Validation.Errors[0] : "unknown error";
                    WriteNotice($"Saved {status.Name}, but it is now invalid: {reason}");
                }
            }
            catch (Exception ex)
            {
                WriteNotice("Save failed: " + ex.Message);
            }
        }

        private void ViewSkill(SkillStatus status)
        {
            string path = Path.Combine(_SkillRuntime!.SkillsDirectory, status.Name, "SKILL.md");
            List<string> lines = new List<string>();
            try
            {
                foreach (string line in File.ReadAllLines(path))
                {
                    lines.Add(line);
                }
            }
            catch (Exception ex)
            {
                lines.Add("Could not read SKILL.md: " + ex.Message);
            }

            if (!status.Valid && status.Error != null)
            {
                lines.Insert(0, "⚠ " + status.Error);
                lines.Insert(1, string.Empty);
            }

            _App.Modals.Push(new MuxBoxModal(status.Name, lines));
        }

        private void ToggleSkill(SkillStatus status)
        {
            try
            {
                new SkillManager(_SkillRuntime!.SkillsDirectory).SetEnabled(status.Name, !status.Enabled);
                _SkillRuntime.RequestRefresh();
                WriteNotice($"{(status.Enabled ? "Disabled" : "Enabled")} skill {status.Name}.");
            }
            catch (Exception ex)
            {
                WriteNotice("Skill update failed: " + ex.Message);
            }
        }

        private async Task DuplicateSkillAsync(SkillStatus status)
        {
            PromptModal prompt = new PromptModal("Duplicate as (new id)", status.Name + "-copy");
            _App.Modals.Push(prompt);
            object? result = await prompt.Completion.ConfigureAwait(false);
            string newId = (result as string ?? string.Empty).Trim();
            if (newId.Length == 0)
            {
                return;
            }

            try
            {
                string sourceDir = Path.Combine(_SkillRuntime!.SkillsDirectory, status.Name);
                new SkillManager(_SkillRuntime.SkillsDirectory).Import(sourceDir, newId);
                _SkillRuntime.RequestRefresh();
                WriteNotice($"Duplicated {status.Name} to {newId}.");
            }
            catch (Exception ex)
            {
                WriteNotice("Duplicate failed: " + ex.Message);
            }
        }

        private async Task RemoveSkillAsync(SkillStatus status)
        {
            MessageModal confirm = new MessageModal("Remove skill", $"Remove '{status.Name}' and its files?", new List<string> { "Remove", "Cancel" });
            _App.Modals.Push(confirm);
            object? result = await confirm.Completion.ConfigureAwait(false);
            if (!(result is int c) || c != 0)
            {
                return;
            }

            try
            {
                new SkillManager(_SkillRuntime!.SkillsDirectory).Remove(status.Name);
                _SkillRuntime.RequestRefresh();
                WriteNotice($"Removed skill {status.Name}.");
            }
            catch (Exception ex)
            {
                WriteNotice("Remove failed: " + ex.Message);
            }
        }

        private async Task NewSkillWizardAsync()
        {
            SkillManager manager = new SkillManager(_SkillRuntime!.SkillsDirectory);

            PromptModal idPrompt = new PromptModal("New skill — id (lowercase-hyphenated)", string.Empty);
            _App.Modals.Push(idPrompt);
            string id = ((await idPrompt.Completion.ConfigureAwait(false)) as string ?? string.Empty).Trim();
            if (id.Length == 0)
            {
                return;
            }

            if (!SkillManager.IsValidId(id))
            {
                WriteNotice($"'{id}' is not a valid skill id (use lowercase and hyphens).");
                return;
            }

            if (manager.Exists(id))
            {
                WriteNotice($"A skill named '{id}' already exists.");
                return;
            }

            PromptModal titlePrompt = new PromptModal("New skill — title", id);
            _App.Modals.Push(titlePrompt);
            string skillTitle = ((await titlePrompt.Completion.ConfigureAwait(false)) as string ?? string.Empty).Trim();

            PromptModal descPrompt = new PromptModal("New skill — description", string.Empty);
            _App.Modals.Push(descPrompt);
            string description = ((await descPrompt.Completion.ConfigureAwait(false)) as string ?? string.Empty).Trim();

            SelectModal mutatingPick = new SelectModal("New skill — behavior", new List<string> { "Read-only (no writes)", "Mutating (writes; needs approval)" });
            _App.Modals.Push(mutatingPick);
            object? mutatingResult = await mutatingPick.Completion.ConfigureAwait(false);
            int mutatingIndex = mutatingResult is int m ? m : 0;
            if (mutatingIndex < 0)
            {
                return;
            }

            List<string> interpreters = new List<string> { "pwsh", "bash", "python", "node", "sh", "dotnet-script" };
            SelectModal interpreterPick = new SelectModal("New skill — interpreter", interpreters);
            _App.Modals.Push(interpreterPick);
            object? interpreterResult = await interpreterPick.Completion.ConfigureAwait(false);
            int interpreterIndex = interpreterResult is int i ? i : -1;
            if (interpreterIndex < 0 || interpreterIndex >= interpreters.Count)
            {
                return;
            }

            SkillScaffold scaffold = new SkillScaffold
            {
                Id = id,
                Title = skillTitle,
                Description = description,
                Mutating = mutatingIndex == 1,
                Interpreter = interpreters[interpreterIndex]
            };

            try
            {
                string dir = manager.Create(scaffold);
                _SkillRuntime.RequestRefresh();
                WriteNotice($"Created skill {id}. Edit {Path.Combine(dir, "SKILL.md")} to build it out.");
            }
            catch (Exception ex)
            {
                WriteNotice("Create failed: " + ex.Message);
            }
        }

        private async Task ImportSkillAsync()
        {
            PromptModal prompt = new PromptModal("Import skill — source directory path", string.Empty);
            _App.Modals.Push(prompt);
            object? result = await prompt.Completion.ConfigureAwait(false);
            string path = (result as string ?? string.Empty).Trim();
            if (path.Length == 0)
            {
                return;
            }

            try
            {
                string id = new SkillManager(_SkillRuntime!.SkillsDirectory).Import(path, null);
                _SkillRuntime.RequestRefresh();
                WriteNotice($"Imported skill {id}.");
            }
            catch (Exception ex)
            {
                WriteNotice("Import failed: " + ex.Message);
            }
        }

        private static bool IsCarriageReturn(KeyEvent key)
        {
            return key.Code == KeyCode.Character && (key.Rune == 13 || key.Rune == 10);
        }

        private int ComposerLineCount()
        {
            string text = _Composer.Text;
            int lines = 1;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    lines++;
                }
            }

            return lines;
        }

        private void ClearTranscript()
        {
            _Conversation.Clear();
        }

        private void SetFooterHint(string hint)
        {
            // A blank spacer line sits above the hint (the footer region is two rows) so the transcript
            // never butts directly into the prompt area.
            _Footer.Clear();
            _Footer.WriteLine(Text.From(string.Empty));
            _Footer.WriteLine(Text.From(hint).Dim());
        }

        private void RefreshFooter()
        {
            SetFooterHint(BuildFooterHint());
        }

        private static string BuildFooterHint()
        {
            // The chords are terminal-independent (Ctrl/F1/Esc work everywhere), so the hint is stable
            // across platforms; only the modifier labels adapt (e.g. Alt renders as OPTION on macOS).
            string ctrl = ModifierLabel(KeyModifiers.Ctrl);
            return $"Type a prompt and press ENTER to send | {ctrl}+J/newline | F1/help | F12/mouse | Esc/cancel | {ctrl}-Q/quit";
        }

        private static string ModifierLabel(KeyModifiers modifier)
        {
            bool isMac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
            switch (modifier)
            {
                case KeyModifiers.Ctrl: return "CTRL";
                case KeyModifiers.Alt: return isMac ? "OPTION" : "ALT";
                case KeyModifiers.Super: return isMac ? "CMD" : "SUPER";
                case KeyModifiers.Shift: return "SHIFT";
                default: return string.Empty;
            }
        }

        private void RefreshSidebar()
        {
            string model;
            string effortLabel;
            string thinkingLabel;
            ConversationStats stats;
            lock (_Sync)
            {
                model = _Model;
                effortLabel = _EffortLabel;
                thinkingLabel = _ThinkingLabel;
                stats = CloneStatsNoLock();
            }

            _Sidebar.EffortLabel = effortLabel;
            _Sidebar.ThinkingLabel = thinkingLabel;
            _Sidebar.Refresh(model, stats);
        }

        private ConversationStats CloneStatsNoLock()
        {
            Job? planJob = _ActiveJob ?? _JobManager.FocusedJob;
            int taskTotal = 0;
            int taskCompleted = 0;
            if (planJob != null)
            {
                Mux.Core.Tasks.TaskPlan plan = planJob.TaskPlan;
                taskTotal = plan.TotalCount;
                taskCompleted = plan.CompletedCount;
            }

            return new ConversationStats
            {
                Busy = _TurnInFlight,
                Queued = _PendingPrompts.Count,
                Turns = _Stats.Turns,
                LastTtftMs = _Stats.LastTtftMs,
                LastStreamMs = _Stats.LastStreamMs,
                LastContextTokens = _Stats.LastContextTokens,
                SessionStreamMs = _Stats.SessionStreamMs,
                SessionTtftMs = _Stats.SessionTtftMs,
                TtftSamples = _Stats.TtftSamples,
                InputTokens = _Stats.InputTokens,
                OutputTokens = _Stats.OutputTokens,
                CachedTokens = _Stats.CachedTokens,
                TaskTotal = taskTotal,
                TaskCompleted = taskCompleted
            };
        }

        private void WriteHeader()
        {
            _Conversation.WriteLine(Text.From("mux v" + Defaults.ProductVersion).Cyan().Bold());
        }

        #endregion
    }
}
