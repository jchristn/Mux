namespace Mux.Cli.App
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Jobs;
    using Mux.Core.Models;
    using Mux.Core.Sessions;
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
    /// The TUIKit-hosted interactive shell for mux. Lays out a sidebar, a transcript region, a composer
    /// editor, and a footer hint line. Each job owns its own transcript <see cref="Pane"/>; only the
    /// focused job's pane is bound to the transcript region, so concurrent jobs never write over one
    /// another. The sidebar lists all jobs and tracks focus. Below a width threshold (or via a manual
    /// toggle) the sidebar collapses and the transcript reclaims the width. The terminal backend is
    /// injected so the shell can be driven headlessly in tests.
    /// </summary>
    public sealed class MuxTuiApp : IDisposable
    {
        #region Private-Members

        private const string TranscriptRegion = "transcript";
        private const string SidebarRegion = "sidebar";
        private const string ComposerRegion = "composer";
        private const string FooterRegion = "footer";
        private const int SidebarWidth = 28;
        private const int CollapseThreshold = 100;
        private const string FooterHint = "^Q quit · ^L clear · ^N next · ^B sidebar · Alt+# focus · Enter send · Esc cancel";

        private readonly ITerminalBackend _Backend;
        private readonly TuiApplication _App;
        private readonly JobManager _JobManager;
        private readonly ApprovalPolicyEnum _ApprovalPolicy;
        private readonly SessionStore? _Store;
        private readonly string _EndpointName;
        private readonly string _Model;
        private readonly string _Title;
        private readonly Pane _HomePane;
        private readonly Pane _SidebarPane;
        private readonly Pane _Footer;
        private readonly TextEditor _Composer;
        private readonly SidebarView _Sidebar;
        private readonly MuxCommandCatalog _Catalog;
        private Layout _ExpandedLayout = null!;
        private Layout _CollapsedLayout = null!;
        private readonly Dictionary<string, Pane> _JobPanes = new Dictionary<string, Pane>(StringComparer.Ordinal);
        private readonly List<string> _JobOrder = new List<string>();
        private readonly List<Task> _ProjectorTasks = new List<Task>();
        private readonly object _Sync = new object();
        private readonly SemaphoreSlim _SaveGate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _Cts = new CancellationTokenSource();
        private readonly EventHandler<JobManagerEvent> _JobEventHandler;
        private readonly PromptHistory _History = new PromptHistory();
        private readonly MenuBar _MenuBar;
        private Pane _CurrentPane;
        private CommandPalette? _Palette;
        private bool _PaletteActive;
        private string? _FocusedJobId;
        private EnqueueBehavior _DefaultEnqueueBehavior = EnqueueBehavior.Ask;
        private EnqueueBehavior? _SessionEnqueueDefault;
        private Func<string, bool>? _SlashHandler;
        private bool _ChooserActive;
        private bool _RememberChoice;
        private string? _PendingPrompt;
        private bool _ManualCollapsed;
        private bool _CollapsedApplied;
        private bool _Compact;
        private int _ThemeIndex;
        private bool _Disposed;

        private static readonly Theme[] _Themes = { Theme.Dark, Theme.Light, Theme.HighContrast };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="MuxTuiApp"/> class.
        /// </summary>
        /// <param name="backend">The terminal backend (console for production, headless for tests). Must not be null.</param>
        /// <param name="jobManager">The job manager that runs submitted prompts. Must not be null.</param>
        /// <param name="title">A short session title shown in the transcript header and sidebar.</param>
        /// <param name="approvalPolicy">The approval policy applied to submitted jobs.</param>
        /// <param name="sessionStore">Optional session store; when supplied, the session autosaves at turn boundaries and can be saved/browsed. Null disables persistence.</param>
        /// <param name="endpointName">The effective endpoint name recorded in saved sessions.</param>
        /// <param name="model">The effective model recorded in saved sessions.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="backend"/> or <paramref name="jobManager"/> is null.</exception>
        public MuxTuiApp(
            ITerminalBackend backend,
            JobManager jobManager,
            string title,
            ApprovalPolicyEnum approvalPolicy,
            SessionStore? sessionStore = null,
            string endpointName = "",
            string model = "")
        {
            _Backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _JobManager = jobManager ?? throw new ArgumentNullException(nameof(jobManager));
            _ApprovalPolicy = approvalPolicy;
            _Store = sessionStore;
            _EndpointName = endpointName ?? string.Empty;
            _Model = model ?? string.Empty;
            _Title = string.IsNullOrWhiteSpace(title) ? "mux" : title;

            _App = new TuiApplication(backend);
            _App.CtrlCPolicy = CtrlCPolicy.DoubleTapToExit;

            BuildLayouts(3);

            _HomePane = new Pane("home");
            _SidebarPane = new Pane(SidebarRegion);
            _Footer = new Pane(FooterRegion);
            _Composer = new TextEditor { IsFocused = true };
            _CurrentPane = _HomePane;
            _Sidebar = new SidebarView(_SidebarPane);

            _App.BindPane(TranscriptRegion, _HomePane);
            _App.BindPane(SidebarRegion, _SidebarPane);
            _App.BindPane(FooterRegion, _Footer);
            _App.Bind(ComposerRegion, _Composer);

            _Catalog = new MuxCommandCatalog();
            _Catalog.Add(new CommandDescriptor("mux.quit", "Quit", "ctrl+q", () => _App.RequestStop(), "Session", new[] { "quit", "exit", "q" }));
            _Catalog.Add(new CommandDescriptor("mux.clear", "Clear transcript", "ctrl+l", ClearTranscript, "View", new[] { "clear" }));
            // Focus-next is bound to Ctrl+N rather than the conventional Ctrl+J: Ctrl+J is byte 0x0A (LF),
            // which terminals and the input parser deliver as Enter, so it cannot be distinguished from a
            // submit in legacy keyboard mode. Revisit when the enhanced-keyboard keymap lands.
            _Catalog.Add(new CommandDescriptor("mux.focus.next", "Focus next job", "ctrl+n", FocusNext, "Jobs", new[] { "next" }));
            _Catalog.Add(new CommandDescriptor("mux.sidebar.toggle", "Toggle sidebar", "ctrl+b", ToggleSidebar, "View", new[] { "sidebar" }));
            _Catalog.Add(new CommandDescriptor("mux.palette", "Command palette", "ctrl+k", OpenPalette, "Session", new[] { "commands", "palette" }));
            _Catalog.Add(new CommandDescriptor("mux.jobs", "Jobs", "f2", OpenJobsModal, "Jobs", new[] { "jobs" }));
            _Catalog.Add(new CommandDescriptor("mux.save", "Save session", "ctrl+s", SaveSession, "Session", new[] { "save" }));
            _Catalog.Add(new CommandDescriptor("mux.sessions", "Sessions", null, OpenSessionBrowser, "Session", new[] { "sessions" }));
            _Catalog.Add(new CommandDescriptor("mux.theme", "Cycle theme", null, CycleTheme, "View", new[] { "theme" }));
            _Catalog.Add(new CommandDescriptor("mux.density", "Toggle density", null, ToggleDensity, "View", new[] { "density" }));
            _Catalog.Add(new CommandDescriptor("mux.mouse", "Toggle mouse capture", "f12", ToggleMouseCapture, "View", new[] { "mouse" }));
            _Catalog.Add(new CommandDescriptor("mux.help", "Help", "f1", ShowHelp, "Help", new[] { "help", "?" }));
            _Catalog.ApplyTo(_App);
            _MenuBar = MenuBarBuilder.Build(_Catalog);
            _SlashHandler = new SlashCommandParser(_Catalog).TryHandle;

            _App.KeyReceived += OnKeyReceived;
            _JobEventHandler = (object? sender, JobManagerEvent e) =>
            {
                RefreshSidebar();
                RefreshFooter();
                if (e is JobCompletedEvent)
                {
                    AutoSave();
                }
            };
            _JobManager.EventPublished += _JobEventHandler;

            WriteHeader();
            RefreshSidebar();
            RefreshFooter();
            ApplyResponsiveLayout();
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
        /// The id of the job whose pane is currently bound to the transcript region, or null when the
        /// home pane is shown.
        /// </summary>
        public string? FocusedJobId
        {
            get
            {
                lock (_Sync)
                {
                    return _FocusedJobId;
                }
            }
        }

        /// <summary>
        /// The ids of jobs that have a transcript pane, in submission order.
        /// </summary>
        public IReadOnlyList<string> JobIds
        {
            get
            {
                lock (_Sync)
                {
                    return new List<string>(_JobOrder);
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
        /// The active theme's name (e.g. "Dark", "Light", "HighContrast").
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
        /// How a submit is handled while another job is active. Defaults to
        /// <see cref="EnqueueBehavior.Ask"/> (show the chooser).
        /// </summary>
        public EnqueueBehavior DefaultEnqueueBehavior
        {
            get
            {
                lock (_Sync)
                {
                    return _DefaultEnqueueBehavior;
                }
            }
            set
            {
                lock (_Sync)
                {
                    _DefaultEnqueueBehavior = value;
                }
            }
        }

        /// <summary>
        /// Whether the submit chooser is currently awaiting a choice.
        /// </summary>
        public bool IsChooserActive
        {
            get
            {
                lock (_Sync)
                {
                    return _ChooserActive;
                }
            }
        }

        /// <summary>
        /// Handler invoked for a composer submission that begins with <c>/</c>. Returns true when the
        /// input was handled as a command (so it is not submitted as a prompt). When unset, a built-in
        /// stub reports the command as unknown. Replaced by the slash-command router in a later milestone.
        /// </summary>
        public Func<string, bool>? SlashHandler
        {
            get => _SlashHandler;
            set => _SlashHandler = value;
        }

        /// <summary>
        /// The catalog-derived menu bar (menus grouped by command category, items wired to command
        /// handlers). Interactive dropdown activation is a follow-up; the menu is a view over the catalog.
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
        /// Whether a modal (approval, jobs, message, …) is currently active and trapping input.
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
        /// Binds the given job's pane to the transcript region and makes it the focused job.
        /// </summary>
        /// <param name="jobId">The job id to focus.</param>
        /// <returns>True when the job has a pane and was focused; otherwise false.</returns>
        public bool FocusJob(string jobId)
        {
            if (string.IsNullOrEmpty(jobId))
            {
                return false;
            }

            lock (_Sync)
            {
                if (!_JobPanes.TryGetValue(jobId, out Pane? pane))
                {
                    return false;
                }

                _App.BindPane(TranscriptRegion, pane);
                _CurrentPane = pane;
                _FocusedJobId = jobId;
            }

            _JobManager.Focus(jobId);
            RefreshSidebar();
            RefreshFooter();
            return true;
        }

        /// <summary>
        /// Focuses the next job in submission order, wrapping around. No-op when there are no jobs.
        /// </summary>
        public void FocusNext()
        {
            string? target = null;
            lock (_Sync)
            {
                if (_JobOrder.Count == 0)
                {
                    return;
                }

                int current = _FocusedJobId == null ? -1 : _JobOrder.IndexOf(_FocusedJobId);
                target = _JobOrder[(current + 1) % _JobOrder.Count];
            }

            FocusJob(target);
        }

        /// <summary>
        /// Focuses the job at the given 1-based position in submission order.
        /// </summary>
        /// <param name="oneBasedIndex">The 1-based job position.</param>
        /// <returns>True when a job exists at that position and was focused; otherwise false.</returns>
        public bool FocusByIndex(int oneBasedIndex)
        {
            string? target = null;
            lock (_Sync)
            {
                if (oneBasedIndex >= 1 && oneBasedIndex <= _JobOrder.Count)
                {
                    target = _JobOrder[oneBasedIndex - 1];
                }
            }

            return target != null && FocusJob(target);
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
                BuildLayouts(_Compact ? 2 : 3);
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
        /// Builds a snapshot of the current session (jobs, histories, prompt history, metadata).
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
                _History.Snapshot(),
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
        /// Rebuilds transcript panes and prompt history from a resumed session. Completed jobs render
        /// read-only; interrupted jobs render with a re-run notice (nothing is auto-run). Replaces the
        /// current transcript view.
        /// </summary>
        /// <param name="resume">The resumed session state. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="resume"/> is null.</exception>
        public void RestoreSession(SessionResumeResult resume)
        {
            if (resume is null) throw new ArgumentNullException(nameof(resume));

            lock (_Sync)
            {
                _JobPanes.Clear();
                _JobOrder.Clear();
                _FocusedJobId = null;
                _CurrentPane = _HomePane;
            }

            _App.BindPane(TranscriptRegion, _HomePane);
            _History.Restore(resume.PromptHistory);

            foreach (PersistedJobSnapshot job in resume.CompletedJobs)
            {
                RestoreJobPane(job, interrupted: false);
            }

            foreach (PersistedJobSnapshot job in resume.InterruptedJobs)
            {
                RestoreJobPane(job, interrupted: true);
            }

            string? last;
            lock (_Sync)
            {
                last = _JobOrder.Count > 0 ? _JobOrder[_JobOrder.Count - 1] : null;
            }

            if (last != null)
            {
                FocusJob(last);
            }

            RefreshSidebar();
            RefreshFooter();
        }

        /// <summary>
        /// Awaits all in-flight job projections. Test helper that makes projected transcript content
        /// deterministic before asserting.
        /// </summary>
        /// <returns>A task that completes when every started projection has finished.</returns>
        public Task DrainProjectorsAsync()
        {
            Task[] snapshot;
            lock (_Sync)
            {
                snapshot = _ProjectorTasks.ToArray();
            }

            return Task.WhenAll(snapshot);
        }

        /// <summary>
        /// Returns a plain-text snapshot of the currently focused transcript pane. Test helper.
        /// </summary>
        /// <returns>The committed transcript lines without styling.</returns>
        public IReadOnlyList<string> TranscriptSnapshot()
        {
            lock (_Sync)
            {
                return _CurrentPane.SnapshotPlainLines();
            }
        }

        /// <summary>
        /// Returns a plain-text snapshot of a specific job's transcript pane. Test helper.
        /// </summary>
        /// <param name="jobId">The job id.</param>
        /// <returns>The job's transcript lines without styling, or an empty list when unknown.</returns>
        public IReadOnlyList<string> JobTranscriptSnapshot(string jobId)
        {
            lock (_Sync)
            {
                return _JobPanes.TryGetValue(jobId, out Pane? pane)
                    ? pane.SnapshotPlainLines()
                    : new List<string>();
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

            IWidget? widget;
            lock (_Sync)
            {
                widget = regionId switch
                {
                    TranscriptRegion => _CurrentPane,
                    SidebarRegion => _SidebarPane,
                    FooterRegion => _Footer,
                    ComposerRegion => _Composer,
                    _ => (IWidget?)null
                };
            }

            return widget == null ? string.Empty : Snapshot.RenderWidget(widget, width, height);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;

            _JobManager.EventPublished -= _JobEventHandler;

            try
            {
                _Cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            _App.KeyReceived -= OnKeyReceived;
            _App.Stop();
            _App.Dispose();
            _Cts.Dispose();
            _SaveGate.Dispose();
        }

        #endregion

        #region Private-Methods

        private void BuildLayouts(int composerHeight)
        {
            int reserve = composerHeight + 1; // composer + 1-row footer
            _ExpandedLayout = Layout.Create()
                .Add(SidebarRegion, r => r.LeftAnchored(0, SidebarWidth).FillHeight(0, reserve).WithPadding(0))
                .Add(TranscriptRegion, r => r.FillWidth(SidebarWidth, 0).FillHeight(0, reserve).WithPadding(0))
                .Add(ComposerRegion, r => r.FillWidth().BottomAnchored(1, composerHeight).WithPadding(0))
                .Add(FooterRegion, r => r.FillWidth().BottomAnchored(0, 1).WithPadding(0))
                .Build();

            _CollapsedLayout = Layout.Create()
                .Add(TranscriptRegion, r => r.FillWidth().FillHeight(0, reserve).WithPadding(0))
                .Add(ComposerRegion, r => r.FillWidth().BottomAnchored(1, composerHeight).WithPadding(0))
                .Add(FooterRegion, r => r.FillWidth().BottomAnchored(0, 1).WithPadding(0))
                .Build();
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

        private void OnKeyReceived(KeyEvent key)
        {
            // While the command palette is open it captures keys.
            if (_PaletteActive)
            {
                HandlePaletteKey(key);
                return;
            }

            // While the submit chooser is open it captures keys.
            if (IsChooserActive)
            {
                HandleChooserKey(key);
                return;
            }

            // Plain Enter (no modifiers) submits. Modified Enter arrives as a carriage-return character
            // event (CSI-u in enhanced mode, ESC+CR for Alt in legacy mode), never as KeyCode.Enter.
            if (key.Code == KeyCode.Enter && key.Modifiers == KeyModifiers.None)
            {
                OnEnter();
                return;
            }

            if (key.Code == KeyCode.Escape)
            {
                CancelFocusedJob();
                return;
            }

            if (IsCarriageReturn(key))
            {
                if ((key.Modifiers & KeyModifiers.Ctrl) != 0)
                {
                    // Ctrl+Enter: submit as a new job, bypassing the chooser (enhanced keyboard only).
                    SubmitDecided(EnqueueBehavior.NewJob);
                }
                else
                {
                    // Alt/Shift+Enter: insert a newline into the composer.
                    _Composer.HandleKey(KeyEvent.Special(KeyCode.Enter));
                }

                return;
            }

            if (key.Code == KeyCode.Character
                && (key.Modifiers & KeyModifiers.Alt) != 0
                && key.Rune >= '1' && key.Rune <= '9')
            {
                FocusByIndex(key.Rune - '0');
                return;
            }

            if (key.Code == KeyCode.Up && _Composer.CaretRow == 0 && RecallPrevious())
            {
                return;
            }

            if (key.Code == KeyCode.Down && _Composer.CaretRow == ComposerLineCount() - 1 && RecallNext())
            {
                return;
            }

            _Composer.HandleKey(key);
        }

        private void OnEnter()
        {
            string prompt = _Composer.Text;
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return;
            }

            _Composer.Text = string.Empty;
            _History.Add(prompt);

            if (prompt.TrimStart().StartsWith("/", StringComparison.Ordinal))
            {
                RouteSlash(prompt.TrimStart());
                return;
            }

            EnqueueBehavior configured;
            lock (_Sync)
            {
                configured = _SessionEnqueueDefault ?? _DefaultEnqueueBehavior;
            }

            EnqueueBehavior behavior = HasActiveJob() ? configured : EnqueueBehavior.NewJob;

            if (behavior == EnqueueBehavior.Ask)
            {
                OpenChooser(prompt);
                return;
            }

            SubmitDecidedWith(behavior, prompt);
        }

        private void SubmitDecided(EnqueueBehavior behavior)
        {
            string prompt = _Composer.Text;
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return;
            }

            _Composer.Text = string.Empty;
            _History.Add(prompt);
            SubmitDecidedWith(behavior, prompt);
        }

        private void SubmitDecidedWith(EnqueueBehavior behavior, string prompt)
        {
            if (behavior == EnqueueBehavior.AddToFocused && TryAddToFocused(prompt))
            {
                return;
            }

            SubmitNewJob(prompt);
        }

        private void SubmitNewJob(string prompt)
        {
            Job job = _JobManager
                .SubmitAsync(prompt, _ApprovalPolicy, null, _Cts.Token)
                .GetAwaiter()
                .GetResult();

            Pane pane = new Pane("job:" + job.Id);
            pane.WriteLine(Text.From("› " + prompt).Cyan().Bold());

            lock (_Sync)
            {
                _JobPanes[job.Id] = pane;
                _JobOrder.Add(job.Id);
            }

            FocusJob(job.Id);

            AgentEventProjector projector = new AgentEventProjector(pane);
            Task projection = Task.Run(() => projector.ProjectAsync(job.ReadEventsAsync(_Cts.Token), _Cts.Token));
            lock (_Sync)
            {
                _ProjectorTasks.Add(projection);
            }
        }

        private bool TryAddToFocused(string prompt)
        {
            string? focusedId;
            Pane? pane;
            lock (_Sync)
            {
                focusedId = _FocusedJobId;
                if (focusedId == null || !_JobPanes.TryGetValue(focusedId, out pane))
                {
                    return false;
                }
            }

            if (!IsJobActive(focusedId))
            {
                return false;
            }

            bool added = _JobManager.AddFollowUpAsync(focusedId, prompt, _Cts.Token).GetAwaiter().GetResult();
            if (!added)
            {
                return false;
            }

            pane!.WriteLine(Text.From("› " + prompt).Cyan().Bold());
            return true;
        }

        private void OpenChooser(string prompt)
        {
            lock (_Sync)
            {
                _PendingPrompt = prompt;
                _ChooserActive = true;
                _RememberChoice = false;
            }

            ShowChooserHint();
        }

        private void HandleChooserKey(KeyEvent key)
        {
            if (key.Code == KeyCode.Escape)
            {
                CancelChooser();
                return;
            }

            if (key.Code == KeyCode.Character && (key.Rune == 'r' || key.Rune == 'R'))
            {
                lock (_Sync)
                {
                    _RememberChoice = !_RememberChoice;
                }

                ShowChooserHint();
                return;
            }

            if (key.Code == KeyCode.Character && key.Rune == '1')
            {
                ResolveChooser(EnqueueBehavior.NewJob);
                return;
            }

            if (key.Code == KeyCode.Character && key.Rune == '2')
            {
                ResolveChooser(EnqueueBehavior.AddToFocused);
                return;
            }
        }

        private void ResolveChooser(EnqueueBehavior behavior)
        {
            string prompt;
            lock (_Sync)
            {
                prompt = _PendingPrompt ?? string.Empty;
                _PendingPrompt = null;
                _ChooserActive = false;
                if (_RememberChoice)
                {
                    _SessionEnqueueDefault = behavior;
                }

                _RememberChoice = false;
            }

            SetFooterHint(FooterHint);

            if (!string.IsNullOrWhiteSpace(prompt))
            {
                SubmitDecidedWith(behavior, prompt);
            }
        }

        private void CancelChooser()
        {
            lock (_Sync)
            {
                _PendingPrompt = null;
                _ChooserActive = false;
                _RememberChoice = false;
            }

            SetFooterHint(FooterHint);
        }

        private void RouteSlash(string input)
        {
            bool handled = _SlashHandler != null && _SlashHandler(input);
            if (!handled)
            {
                lock (_Sync)
                {
                    _CurrentPane.WriteLine(Text.From($"Unknown command: {input}").Yellow());
                }
            }
        }

        private bool RecallPrevious()
        {
            if (_History.TryPrevious(out string entry))
            {
                _Composer.Text = entry;
                return true;
            }

            return false;
        }

        private bool RecallNext()
        {
            if (_History.TryNext(out string entry))
            {
                _Composer.Text = entry;
                return true;
            }

            return false;
        }

        private bool HasActiveJob()
        {
            List<string> ids;
            lock (_Sync)
            {
                ids = new List<string>(_JobOrder);
            }

            foreach (string id in ids)
            {
                if (IsJobActive(id))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsJobActive(string jobId)
        {
            foreach (Job job in _JobManager.Jobs)
            {
                if (string.Equals(job.Id, jobId, StringComparison.Ordinal))
                {
                    return job.State != JobState.Completed
                        && job.State != JobState.Failed
                        && job.State != JobState.Cancelled;
                }
            }

            return false;
        }

        private void ShowChooserHint()
        {
            bool remember;
            lock (_Sync)
            {
                remember = _RememberChoice;
            }

            string hint = "Job active — [1] new job · [2] add to focused · [r] remember"
                + (remember ? " ✓" : string.Empty)
                + " · [Esc] cancel";
            SetFooterHint(hint);
        }

        private void SetFooterHint(string hint)
        {
            _Footer.Clear();
            _Footer.WriteLine(Text.From(hint).Dim());
        }

        private void RefreshFooter()
        {
            if (IsPaletteActive || IsChooserActive)
            {
                return;
            }

            int jobCount;
            string focused;
            lock (_Sync)
            {
                jobCount = _JobOrder.Count;
                focused = _FocusedJobId ?? "-";
            }

            SetFooterHint($"jobs:{jobCount} focused:{focused} · {FooterHint}");
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

        private void RestoreJobPane(PersistedJobSnapshot job, bool interrupted)
        {
            Pane pane = new Pane("job:" + job.Id);
            string label = string.IsNullOrWhiteSpace(job.Title) ? job.Prompt : job.Title;
            pane.WriteLine(Text.From($"« resumed: {label} »").Dim());

            foreach (ConversationMessage message in job.ConversationHistory)
            {
                if (message.Role == RoleEnum.User && !string.IsNullOrEmpty(message.Content))
                {
                    pane.WriteLine(Text.From("› " + message.Content).Cyan());
                }
                else if (message.Role == RoleEnum.Assistant && !string.IsNullOrEmpty(message.Content))
                {
                    pane.WriteLine(Text.From(message.Content));
                }
            }

            if (interrupted)
            {
                pane.WriteLine(Text.From("⚠ interrupted — re-run required").Yellow());
            }

            lock (_Sync)
            {
                _JobPanes[job.Id] = pane;
                _JobOrder.Add(job.Id);
            }
        }

        private void WriteNotice(string text)
        {
            lock (_Sync)
            {
                _CurrentPane.WriteLine(Text.From(text).Dim());
            }
        }

        private void OpenJobsModal()
        {
            List<string> ids;
            lock (_Sync)
            {
                ids = new List<string>(_JobOrder);
            }

            if (ids.Count == 0)
            {
                _App.Modals.Push(new MessageModal("Jobs", "No jobs yet.", new List<string> { "OK" }));
                return;
            }

            List<string> labels = new List<string>();
            for (int i = 0; i < ids.Count; i++)
            {
                labels.Add($"{i + 1}. {JobLabel(ids[i])}");
            }

            SelectModal modal = new SelectModal("Jobs — select to focus", labels);
            _App.Modals.Push(modal);
            _ = ResolveJobsModalAsync(modal, ids);
        }

        private async Task ResolveJobsModalAsync(SelectModal modal, IReadOnlyList<string> ids)
        {
            object? result = await modal.Completion.ConfigureAwait(false);
            int index = result is int value ? value : -1;
            if (index >= 0 && index < ids.Count)
            {
                FocusJob(ids[index]);
            }
        }

        private string JobLabel(string jobId)
        {
            foreach (Job job in _JobManager.Jobs)
            {
                if (string.Equals(job.Id, jobId, StringComparison.Ordinal))
                {
                    string title = string.IsNullOrWhiteSpace(job.Title) ? job.Prompt : job.Title;
                    return $"{job.State} · {title}";
                }
            }

            return jobId;
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
            Pane current;
            lock (_Sync)
            {
                _PaletteActive = false;
                current = _CurrentPane;
            }

            _App.BindPane(TranscriptRegion, current);
            _Palette = null;
            RefreshFooter();
        }

        private void ShowHelp()
        {
            lock (_Sync)
            {
                _CurrentPane.WriteLine(Text.From("Commands").Bold());
                foreach (CommandDescriptor descriptor in _Catalog.Commands)
                {
                    string keys = descriptor.Chord ?? string.Empty;
                    string aliases = descriptor.SlashAliases.Count > 0 ? "/" + string.Join(" /", descriptor.SlashAliases) : string.Empty;
                    _CurrentPane.WriteLine(Text.From($"  {descriptor.Title,-18} {keys,-8} {aliases}").Dim());
                }
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

        private void CancelFocusedJob()
        {
            Job? focused = _JobManager.FocusedJob;
            if (focused == null)
            {
                return;
            }

            _ = _JobManager.CancelAsync(focused.Id, _Cts.Token);
        }

        private void ClearTranscript()
        {
            lock (_Sync)
            {
                _CurrentPane.Clear();
            }
        }

        private void RefreshSidebar()
        {
            string? focused;
            lock (_Sync)
            {
                focused = _FocusedJobId;
            }

            _Sidebar.Refresh(_JobManager.Jobs, focused, _Title, _JobManager.SessionId);
        }

        private void WriteHeader()
        {
            _HomePane.WriteLine(Text.From("mux · " + _Title).Cyan().Bold());
            _HomePane.WriteLine(Text.From("Type a prompt and press Enter. Alt+Enter for a newline.").Dim());
        }

        #endregion
    }
}
