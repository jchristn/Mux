namespace Mux.Cli.App
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Jobs;
    using TUIKit;
    using TUIKit.Content;
    using TUIKit.Hosting;
    using TUIKit.Input;
    using TUIKit.Layout;
    using TUIKit.Terminal;
    using TUIKit.Widgets;

    /// <summary>
    /// The TUIKit-hosted interactive shell for mux. Lays out a transcript region, a composer editor, and
    /// a footer hint line. Each job owns its own transcript <see cref="Pane"/>; only the focused job's
    /// pane is bound to the transcript region, so concurrent jobs never write over one another. Keys are
    /// forwarded to the composer; <c>Enter</c> submits the prompt as a new job and projects that job's
    /// <see cref="Mux.Core.Agent.AgentEvent"/> stream onto its pane. The terminal backend is injected so
    /// the shell can be driven headlessly in tests.
    /// </summary>
    public sealed class MuxTuiApp : IDisposable
    {
        #region Private-Members

        private const string TranscriptRegion = "transcript";
        private const string ComposerRegion = "composer";
        private const string FooterRegion = "footer";

        private readonly TuiApplication _App;
        private readonly JobManager _JobManager;
        private readonly ApprovalPolicyEnum _ApprovalPolicy;
        private readonly Pane _HomePane;
        private readonly Pane _Footer;
        private readonly TextEditor _Composer;
        private readonly MuxCommandCatalog _Catalog;
        private readonly Dictionary<string, Pane> _JobPanes = new Dictionary<string, Pane>(StringComparer.Ordinal);
        private readonly List<string> _JobOrder = new List<string>();
        private readonly List<Task> _ProjectorTasks = new List<Task>();
        private readonly object _Sync = new object();
        private readonly CancellationTokenSource _Cts = new CancellationTokenSource();
        private Pane _CurrentPane;
        private string? _FocusedJobId;
        private bool _Disposed;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="MuxTuiApp"/> class.
        /// </summary>
        /// <param name="backend">The terminal backend (console for production, headless for tests). Must not be null.</param>
        /// <param name="jobManager">The job manager that runs submitted prompts. Must not be null.</param>
        /// <param name="title">A short session title shown in the transcript header.</param>
        /// <param name="approvalPolicy">The approval policy applied to submitted jobs.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="backend"/> or <paramref name="jobManager"/> is null.</exception>
        public MuxTuiApp(
            ITerminalBackend backend,
            JobManager jobManager,
            string title,
            ApprovalPolicyEnum approvalPolicy)
        {
            if (backend is null) throw new ArgumentNullException(nameof(backend));
            _JobManager = jobManager ?? throw new ArgumentNullException(nameof(jobManager));
            _ApprovalPolicy = approvalPolicy;

            _App = new TuiApplication(backend);
            _App.CtrlCPolicy = CtrlCPolicy.DoubleTapToExit;

            _App.Layout = Layout.Create()
                .Add(TranscriptRegion, r => r.FillWidth().FillHeight(0, 4).WithPadding(0))
                .Add(ComposerRegion, r => r.FillWidth().BottomAnchored(1, 3).WithPadding(0))
                .Add(FooterRegion, r => r.FillWidth().BottomAnchored(0, 1).WithPadding(0))
                .Build();

            _HomePane = new Pane("home");
            _Footer = new Pane(FooterRegion);
            _Composer = new TextEditor { IsFocused = true };
            _CurrentPane = _HomePane;

            _App.BindPane(TranscriptRegion, _HomePane);
            _App.BindPane(FooterRegion, _Footer);
            _App.Bind(ComposerRegion, _Composer);

            _Catalog = new MuxCommandCatalog();
            _Catalog.Add(new CommandDescriptor("mux.quit", "Quit", "ctrl+q", () => _App.RequestStop()));
            _Catalog.Add(new CommandDescriptor("mux.clear", "Clear transcript", "ctrl+l", ClearTranscript));
            // Focus-next is bound to Ctrl+N rather than the conventional Ctrl+J: Ctrl+J is byte 0x0A (LF),
            // which terminals and the input parser deliver as Enter, so it cannot be distinguished from a
            // submit in legacy keyboard mode. Revisit when the enhanced-keyboard keymap lands (M10).
            _Catalog.Add(new CommandDescriptor("mux.focus.next", "Focus next job", "ctrl+n", FocusNext));
            _Catalog.ApplyTo(_App);

            _App.KeyReceived += OnKeyReceived;

            WriteHeader(title ?? string.Empty);
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
        /// Runs the interactive input and render loop until the user quits or the token is cancelled.
        /// </summary>
        /// <param name="cancellationToken">A token used to stop the loop.</param>
        /// <returns>A task that completes when the loop exits.</returns>
        public async Task RunAsync(CancellationToken cancellationToken)
        {
            using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _Cts.Token))
            {
                await _App.RunAsync(linked.Token).ConfigureAwait(false);
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
        /// Returns a plain-text snapshot of the footer lines. Test helper.
        /// </summary>
        /// <returns>The committed footer lines without styling.</returns>
        public IReadOnlyList<string> FooterSnapshot()
        {
            return _Footer.SnapshotPlainLines();
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

            _App.KeyReceived -= OnKeyReceived;
            _App.Stop();
            _App.Dispose();
            _Cts.Dispose();
        }

        #endregion

        #region Private-Methods

        private void OnKeyReceived(KeyEvent key)
        {
            if (key.Code == KeyCode.Enter
                && (key.Modifiers & KeyModifiers.Alt) == 0
                && (key.Modifiers & KeyModifiers.Shift) == 0)
            {
                SubmitCurrentPrompt();
                return;
            }

            if (key.Code == KeyCode.Escape)
            {
                CancelFocusedJob();
                return;
            }

            _Composer.HandleKey(key);
        }

        private void SubmitCurrentPrompt()
        {
            string prompt = _Composer.Text;
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return;
            }

            _Composer.Text = string.Empty;

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

        private void WriteHeader(string title)
        {
            string heading = string.IsNullOrWhiteSpace(title) ? "mux" : "mux · " + title;
            _HomePane.WriteLine(Text.From(heading).Cyan().Bold());
            _HomePane.WriteLine(Text.From("Type a prompt and press Enter. Alt+Enter for a newline.").Dim());
            _Footer.WriteLine(Text.From("Ctrl+Q quit · Ctrl+L clear · Ctrl+N next job · Enter send · Esc cancel").Dim());
        }

        #endregion
    }
}
