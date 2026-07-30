namespace Mux.Cli.App
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Jobs;
    using Mux.Core.Models;
    using Mux.Core.Sessions;
    using Mux.Core.Settings;
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
    /// types a prompt and it runs; typing another prompt while a turn is running queues it to run when the
    /// current turn finishes (like a chat client). One transcript <see cref="Pane"/> holds the whole
    /// conversation; a sidebar shows the active endpoint and per-turn / session telemetry; the bottom row
    /// is a green <c>mux&gt;</c> prompt and the composer. The terminal backend is injected so the shell can
    /// be driven headlessly in tests.
    /// </summary>
    public sealed class MuxTuiApp : IDisposable
    {
        #region Private-Members

        private const string TranscriptRegion = "transcript";
        private const string SidebarRegion = "sidebar";
        private const string ComposerRegion = "composer";
        private const string PromptLabelRegion = "promptlabel";
        private const string FooterRegion = "footer";
        private const string PromptText = "mux> ";
        private const int SidebarWidth = 28;
        private const int CollapseThreshold = 100;
        private const string FooterHint = "^Q quit · ^L clear · ^K palette · F1 menu · Shift+Enter newline · Enter send · Esc cancel";

        private readonly ITerminalBackend _Backend;
        private readonly TuiApplication _App;
        private readonly JobManager _JobManager;
        private readonly ApprovalPolicyEnum _ApprovalPolicy;
        private readonly SessionStore? _Store;
        private readonly Action<EndpointConfig>? _OnEndpointSelected;
        private string _EndpointName;
        private string _Model;
        private readonly string _Title;
        private readonly Pane _Conversation;
        private readonly Pane _SidebarPane;
        private readonly Pane _PromptLabel;
        private readonly Pane _Footer;
        private readonly TextEditor _Composer;
        private readonly SidebarView _Sidebar;
        private readonly MuxCommandCatalog _Catalog;
        private Layout _ExpandedLayout = null!;
        private Layout _CollapsedLayout = null!;
        private readonly List<ConversationMessage> _ConversationHistory = new List<ConversationMessage>();
        private readonly Queue<string> _PendingPrompts = new Queue<string>();
        private readonly List<string> _TurnJobIds = new List<string>();
        private readonly List<Task> _ProjectorTasks = new List<Task>();
        private readonly ConversationStats _Stats = new ConversationStats();
        private readonly object _Sync = new object();
        private readonly SemaphoreSlim _SaveGate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _Cts = new CancellationTokenSource();
        private readonly PromptHistory _PromptHistory = new PromptHistory();
        private readonly MenuBar _MenuBar;
        private CommandPalette? _Palette;
        private bool _PaletteActive;
        private Job? _ActiveJob;
        private bool _TurnInFlight;
        private Func<string, bool>? _SlashHandler;
        private bool _ManualCollapsed;
        private bool _CollapsedApplied;
        private bool _Compact;
        private int _ThemeIndex;
        private bool _Disposed;

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
        /// <param name="showSplash">When true, opens the startup splash modal.</param>
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
            bool showSplash = false)
        {
            _Backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _JobManager = jobManager ?? throw new ArgumentNullException(nameof(jobManager));
            _ApprovalPolicy = approvalPolicy;
            _Store = sessionStore;
            _OnEndpointSelected = onEndpointSelected;
            _EndpointName = endpointName ?? string.Empty;
            _Model = model ?? string.Empty;
            _Title = string.IsNullOrWhiteSpace(title) ? "mux" : title;

            _App = new TuiApplication(backend);
            _App.CtrlCPolicy = CtrlCPolicy.DoubleTapToExit;
            _App.Theme = MuxTheme; // no background color; the terminal's own background shows through

            BuildLayouts(1);

            _Conversation = new Pane(TranscriptRegion);
            _SidebarPane = new Pane(SidebarRegion);
            _PromptLabel = new Pane(PromptLabelRegion);
            _Footer = new Pane(FooterRegion);
            _Composer = new TextEditor { IsFocused = true };
            _Sidebar = new SidebarView(_SidebarPane);

            _PromptLabel.WriteLine(Text.From(PromptText).Green().Bold());

            _App.BindPane(TranscriptRegion, _Conversation);
            _App.BindPane(SidebarRegion, _SidebarPane);
            _App.BindPane(PromptLabelRegion, _PromptLabel);
            _App.BindPane(FooterRegion, _Footer);
            _App.Bind(ComposerRegion, _Composer);

            _Catalog = new MuxCommandCatalog();
            _Catalog.Add(new CommandDescriptor("mux.quit", "Quit", "ctrl+q", RequestQuit, "Session", new[] { "quit", "exit", "q" }));
            _Catalog.Add(new CommandDescriptor("mux.endpoint", "Endpoints / models", null, OpenEndpointModal, "Model", new[] { "endpoint", "endpoints", "model", "models" }));
            _Catalog.Add(new CommandDescriptor("mux.clear", "Clear transcript", "ctrl+l", ClearTranscript, "View", new[] { "clear" }));
            _Catalog.Add(new CommandDescriptor("mux.sidebar.toggle", "Toggle sidebar", "ctrl+b", ToggleSidebar, "View", new[] { "sidebar" }));
            _Catalog.Add(new CommandDescriptor("mux.palette", "Command palette", "ctrl+k", OpenPalette, "Session", new[] { "commands", "palette" }));
            _Catalog.Add(new CommandDescriptor("mux.save", "Save session", "ctrl+s", SaveSession, "Session", new[] { "save" }));
            _Catalog.Add(new CommandDescriptor("mux.sessions", "Sessions", null, OpenSessionBrowser, "Session", new[] { "sessions" }));
            _Catalog.Add(new CommandDescriptor("mux.theme", "Cycle theme", null, CycleTheme, "View", new[] { "theme" }));
            _Catalog.Add(new CommandDescriptor("mux.density", "Toggle density", null, ToggleDensity, "View", new[] { "density" }));
            _Catalog.Add(new CommandDescriptor("mux.mouse", "Toggle mouse capture", "f12", ToggleMouseCapture, "View", new[] { "mouse" }));
            _Catalog.Add(new CommandDescriptor("mux.menu", "Command menu", "f1", OpenCommandMenu, "Help", new[] { "menu" }));
            _Catalog.Add(new CommandDescriptor("mux.help", "Help (keymap)", null, ShowHelp, "Help", new[] { "help", "?" }));
            _Catalog.ApplyTo(_App);
            _MenuBar = MenuBarBuilder.Build(_Catalog);
            _SlashHandler = new SlashCommandParser(_Catalog).TryHandle;

            // Own input via the pre-widget KeyFilter. The composer is a focusable TextEditor that would
            // otherwise consume Enter (as a newline) and every Ctrl+key (swallowing command chords) before
            // they could reach us, so we intercept here — dispatching submits, chords, and edits ourselves —
            // and forward the remaining keys to the composer. Modals and the Ctrl+C policy still run first.
            _App.KeyFilter = OnKeyFilter;

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
        /// Whether the compact density is active (shorter composer).
        /// </summary>
        public bool IsCompact
        {
            get
            {
                lock (_Sync)
                {
                    return _Compact;
                }
            }
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
        /// Whether the command palette is currently open.
        /// </summary>
        public bool IsPaletteActive
        {
            get
            {
                lock (_Sync)
                {
                    return _PaletteActive;
                }
            }
        }

        /// <summary>
        /// The number of catalog entries matching the palette's current query, or zero when closed.
        /// </summary>
        public int PaletteMatchCount
        {
            get => _Palette?.MatchCount ?? 0;
        }

        /// <summary>
        /// The command id currently selected in the palette, or null when closed or unmatched.
        /// </summary>
        public string? PaletteSelectionId
        {
            get => _Palette?.Selected?.Id;
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
        /// Cycles the active theme (Dark → Light → HighContrast → …).
        /// </summary>
        public void CycleTheme()
        {
            lock (_Sync)
            {
                _ThemeIndex = (_ThemeIndex + 1) % _Themes.Length;
                _App.Theme = _Themes[_ThemeIndex];
            }
        }

        /// <summary>
        /// Toggles between comfortable and compact density (compact uses a shorter composer). Rebuilds
        /// and re-applies the layouts.
        /// </summary>
        public void ToggleDensity()
        {
            lock (_Sync)
            {
                _Compact = !_Compact;
                BuildLayouts(_Compact ? 1 : 2);
                _App.Layout = _CollapsedApplied ? _CollapsedLayout : _ExpandedLayout;
            }
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
            }

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

        private void BuildLayouts(int composerHeight)
        {
            int promptWidth = PromptText.Length;
            int reserve = composerHeight + 1; // input row(s) + 1 status row
            _ExpandedLayout = Layout.Create()
                .Add(TranscriptRegion, r => r.FillWidth(0, SidebarWidth).FillHeight(0, reserve).WithPadding(0))
                .Add(SidebarRegion, r => r.RightAnchored(0, SidebarWidth).FillHeight(0, reserve).WithPadding(0))
                .Add(FooterRegion, r => r.FillWidth().BottomAnchored(composerHeight, 1).WithPadding(0))
                .Add(PromptLabelRegion, r => r.LeftAnchored(0, promptWidth).BottomAnchored(0, composerHeight).WithPadding(0))
                .Add(ComposerRegion, r => r.FillWidth(promptWidth, 0).BottomAnchored(0, composerHeight).WithPadding(0))
                .Build();

            _CollapsedLayout = Layout.Create()
                .Add(TranscriptRegion, r => r.FillWidth().FillHeight(0, reserve).WithPadding(0))
                .Add(FooterRegion, r => r.FillWidth().BottomAnchored(composerHeight, 1).WithPadding(0))
                .Add(PromptLabelRegion, r => r.LeftAnchored(0, promptWidth).BottomAnchored(0, composerHeight).WithPadding(0))
                .Add(ComposerRegion, r => r.FillWidth(promptWidth, 0).BottomAnchored(0, composerHeight).WithPadding(0))
                .Build();
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
            // While the command palette is open it captures every key.
            if (_PaletteActive)
            {
                HandlePaletteKey(key);
                return true;
            }

            // Plain Enter (no modifiers) submits.
            if (key.Code == KeyCode.Enter && key.Modifiers == KeyModifiers.None)
            {
                OnEnter();
                return true;
            }

            // Shift+Enter (and any other modified Enter, which arrives as a carriage-return character in
            // the enhanced keyboard protocol) inserts a newline. Alt+Enter is intercepted by Windows
            // Terminal for the full-screen toggle and never reaches us.
            if (IsCarriageReturn(key))
            {
                _Composer.InsertNewline();
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
                    case 'q': RequestQuit(); return true;
                    case 'l': ClearTranscript(); return true;
                    case 'k': OpenPalette(); return true;
                    case 'b': ToggleSidebar(); return true;
                    case 's': SaveSession(); return true;
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
            return true;
        }

        private void OnEnter()
        {
            string prompt = _Composer.Text;
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return;
            }

            _Composer.Text = string.Empty;
            _PromptHistory.Add(prompt);

            if (prompt.TrimStart().StartsWith("/", StringComparison.Ordinal))
            {
                RouteSlash(prompt.TrimStart());
                return;
            }

            // Echo the submitted prompt into the transcript immediately, so the user always sees what they
            // sent even before the model responds.
            EchoPrompt(prompt);
            EnqueueOrRun(prompt);
        }

        private void EnqueueOrRun(string prompt)
        {
            bool startNow;
            lock (_Sync)
            {
                if (_TurnInFlight)
                {
                    _PendingPrompts.Enqueue(prompt);
                    startNow = false;
                }
                else
                {
                    _TurnInFlight = true;
                    startNow = true;
                }
            }

            if (startNow)
            {
                RunTurn(prompt);
            }
            else
            {
                RefreshSidebar();
                RefreshFooter();
            }
        }

        private void RunTurn(string prompt)
        {
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

            lock (_Sync)
            {
                _ActiveJob = job;
                _TurnJobIds.Add(job.Id);
            }

            RefreshSidebar();
            RefreshFooter();

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
                }

                _ActiveJob = null;
                next = _PendingPrompts.Count > 0 ? _PendingPrompts.Dequeue() : null;
                if (next == null)
                {
                    _TurnInFlight = false;
                }
            }

            RefreshSidebar();
            RefreshFooter();
            AutoSave();

            if (next != null)
            {
                RunTurn(next);
            }
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
                return true;
            }

            return false;
        }

        private bool RecallNext()
        {
            if (_PromptHistory.TryNext(out string entry))
            {
                _Composer.Text = entry;
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
        }

        private void EchoPrompt(string prompt)
        {
            // "mux> <prompt>" with a leading and trailing blank line; "mux>" in green, the prompt in grey.
            _Conversation.WriteLine(Text.From(string.Empty));
            _Conversation.WriteLine(Text.From(PromptText).Green().Bold().Append(Text.From(prompt ?? string.Empty)));
            _Conversation.WriteLine(Text.From(string.Empty));
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

        private void OpenCommandMenu()
        {
            List<CommandDescriptor> commands = new List<CommandDescriptor>();
            List<string> labels = new List<string>();
            foreach (CommandDescriptor descriptor in _Catalog.Commands)
            {
                if (string.Equals(descriptor.Id, "mux.menu", StringComparison.Ordinal))
                {
                    continue; // don't list the menu inside itself
                }

                commands.Add(descriptor);
                string keys = string.IsNullOrEmpty(descriptor.Chord) ? string.Empty : descriptor.Chord;
                labels.Add($"{descriptor.Title,-22} {keys}");
            }

            if (commands.Count == 0)
            {
                return;
            }

            SelectModal modal = new SelectModal("Commands — ↑↓ then Enter to run", labels);
            _App.Modals.Push(modal);
            _ = ResolveCommandMenuAsync(modal, commands);
        }

        private async Task ResolveCommandMenuAsync(SelectModal modal, List<CommandDescriptor> commands)
        {
            object? result = await modal.Completion.ConfigureAwait(false);
            int index = result is int value ? value : -1;
            if (index >= 0 && index < commands.Count)
            {
                commands[index].Handler();
            }
        }

        private void OpenPalette()
        {
            _Palette = new CommandPalette(_Catalog);
            lock (_Sync)
            {
                _PaletteActive = true;
            }

            _App.Bind(TranscriptRegion, _Palette.Widget);
            SetFooterHint("Palette — type to filter · Enter run · Esc cancel");
        }

        private void HandlePaletteKey(KeyEvent key)
        {
            if (key.Code == KeyCode.Escape)
            {
                ClosePalette();
                return;
            }

            if (key.Code == KeyCode.Enter)
            {
                RunPaletteSelection();
                return;
            }

            _Palette?.HandleKey(key);
        }

        private void RunPaletteSelection()
        {
            CommandDescriptor? command = _Palette?.Selected;
            ClosePalette();
            command?.Handler();
        }

        private void ClosePalette()
        {
            lock (_Sync)
            {
                _PaletteActive = false;
            }

            _App.BindPane(TranscriptRegion, _Conversation);
            _Palette = null;
            RefreshFooter();
        }

        private void ShowHelp()
        {
            List<string> lines = new List<string>();
            foreach (CommandDescriptor descriptor in _Catalog.Commands)
            {
                string keys = descriptor.Chord ?? string.Empty;
                string aliases = descriptor.SlashAliases.Count > 0 ? "/" + string.Join(" /", descriptor.SlashAliases) : string.Empty;
                lines.Add($"{descriptor.Title,-20} {keys,-8} {aliases}");
            }

            _App.Modals.Push(new MuxBoxModal("Commands", lines));
        }

        private void RequestQuit()
        {
            MessageModal modal = new MessageModal(
                "Quit mux?",
                "Exit mux? A running turn will be cancelled.",
                new List<string> { "Quit", "Cancel" });
            _App.Modals.Push(modal);
            _ = ResolveQuitAsync(modal);
        }

        private async Task ResolveQuitAsync(MessageModal modal)
        {
            object? result = await modal.Completion.ConfigureAwait(false);
            if (result is int index && index == 0)
            {
                _App.RequestStop();
            }
        }

        private void OpenEndpointModal()
        {
            List<EndpointConfig> endpoints = LoadEndpointsSafe();
            List<string> options = new List<string>();
            foreach (EndpointConfig endpoint in endpoints)
            {
                string marker = string.Equals(endpoint.Name, _EndpointName, StringComparison.OrdinalIgnoreCase) ? "● " : "  ";
                options.Add($"{marker}{endpoint.Name}  ({endpoint.AdapterType} · {endpoint.Model})");
            }

            options.Add("+ Add endpoint…");
            if (endpoints.Count > 0)
            {
                options.Add("- Remove endpoint…");
            }

            SelectModal modal = new SelectModal("Endpoints / models — Enter to switch", options);
            _App.Modals.Push(modal);
            _ = ResolveEndpointModalAsync(modal, endpoints);
        }

        private async Task ResolveEndpointModalAsync(SelectModal modal, List<EndpointConfig> endpoints)
        {
            object? result = await modal.Completion.ConfigureAwait(false);
            int index = result is int value ? value : -1;
            if (index < 0)
            {
                return;
            }

            if (index < endpoints.Count)
            {
                SwitchEndpoint(endpoints[index]);
                return;
            }

            int extra = index - endpoints.Count;
            if (extra == 0)
            {
                await AddEndpointWizardAsync().ConfigureAwait(false);
            }
            else if (extra == 1)
            {
                await RemoveEndpointAsync(endpoints).ConfigureAwait(false);
            }
        }

        private void SwitchEndpoint(EndpointConfig endpoint)
        {
            _OnEndpointSelected?.Invoke(endpoint);
            lock (_Sync)
            {
                _EndpointName = endpoint.Name;
                _Model = endpoint.Model;
            }

            WriteNotice($"Switched to endpoint {endpoint.Name} ({endpoint.Model}).");
            RefreshFooter();
            RefreshSidebar();
        }

        private async Task AddEndpointWizardAsync()
        {
            string? name = await PromptAsync("Endpoint name", string.Empty).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            string[] adapters = { "openai-compatible", "ollama", "openai", "vllm" };
            SelectModal adapterModal = new SelectModal($"Adapter for {name}", adapters);
            _App.Modals.Push(adapterModal);
            object? adapterResult = await adapterModal.Completion.ConfigureAwait(false);
            int adapterIndex = adapterResult is int a ? a : -1;
            if (adapterIndex < 0)
            {
                return;
            }

            string? baseUrl = await PromptAsync("Base URL", "http://localhost:11434/v1").ConfigureAwait(false);
            if (baseUrl == null)
            {
                return;
            }

            string? model = await PromptAsync("Model", string.Empty).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(model))
            {
                return;
            }

            EndpointConfig endpoint = new EndpointConfig
            {
                Name = name!.Trim(),
                AdapterType = ParseAdapter(adapters[adapterIndex]),
                BaseUrl = baseUrl.Trim(),
                Model = model!.Trim()
            };

            List<EndpointConfig> endpoints = LoadEndpointsSafe();
            endpoints.RemoveAll(e => string.Equals(e.Name, endpoint.Name, StringComparison.OrdinalIgnoreCase));
            endpoints.Add(endpoint);
            try
            {
                SettingsLoader.SaveEndpoints(endpoints);
                WriteNotice($"Saved endpoint {endpoint.Name}. Use /endpoint to switch to it.");
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

            string name = endpoints[pickIndex].Name;
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

        private async Task<string?> PromptAsync(string title, string initial)
        {
            PromptModal modal = new PromptModal(title, initial);
            _App.Modals.Push(modal);
            object? result = await modal.Completion.ConfigureAwait(false);
            return result as string;
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

        private static AdapterTypeEnum ParseAdapter(string adapter)
        {
            switch (adapter)
            {
                case "ollama": return AdapterTypeEnum.Ollama;
                case "openai": return AdapterTypeEnum.OpenAi;
                case "vllm": return AdapterTypeEnum.Vllm;
                default: return AdapterTypeEnum.OpenAiCompatible;
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
            _Footer.Clear();
            _Footer.WriteLine(Text.From(hint).Dim());
        }

        private void RefreshFooter()
        {
            if (IsPaletteActive)
            {
                return;
            }

            SetFooterHint(FooterHint);
        }

        private void RefreshSidebar()
        {
            string endpointName;
            ConversationStats stats;
            lock (_Sync)
            {
                endpointName = _EndpointName;
                stats = CloneStatsNoLock();
            }

            _Sidebar.Refresh(_Title, endpointName, stats);
        }

        private ConversationStats CloneStatsNoLock()
        {
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
                CachedTokens = _Stats.CachedTokens
            };
        }

        private void WriteHeader()
        {
            _Conversation.WriteLine(Text.From("mux · " + _Title).Cyan().Bold());
            _Conversation.WriteLine(Text.From("Type a prompt and press Enter. Shift+Enter for a newline.").Dim());
        }

        #endregion
    }
}
