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
    /// The TUIKit-hosted interactive shell for mux. Lays out a transcript pane, a composer editor, and a
    /// footer hint line; forwards keys to the composer; submits prompts to a <see cref="JobManager"/> on
    /// <c>Enter</c>; and projects each job's <see cref="Mux.Core.Agent.AgentEvent"/> stream onto the
    /// transcript. The terminal backend is injected so the shell can be driven headlessly in tests.
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
        private readonly Pane _Transcript;
        private readonly Pane _Footer;
        private readonly TextEditor _Composer;
        private readonly AgentEventProjector _Projector;
        private readonly MuxCommandCatalog _Catalog;
        private readonly List<Task> _ProjectorTasks = new List<Task>();
        private readonly object _ProjectorSync = new object();
        private readonly CancellationTokenSource _Cts = new CancellationTokenSource();
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

            _Transcript = new Pane(TranscriptRegion);
            _Footer = new Pane(FooterRegion);
            _Composer = new TextEditor { IsFocused = true };

            _App.BindPane(TranscriptRegion, _Transcript);
            _App.BindPane(FooterRegion, _Footer);
            _App.Bind(ComposerRegion, _Composer);

            _Projector = new AgentEventProjector(_Transcript);

            _Catalog = new MuxCommandCatalog();
            _Catalog.Add(new CommandDescriptor("mux.quit", "Quit", "ctrl+q", () => _App.RequestStop()));
            _Catalog.Add(new CommandDescriptor("mux.clear", "Clear transcript", "ctrl+l", ClearTranscript));
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
        /// Awaits all in-flight job projections. Test helper that makes projected transcript content
        /// deterministic before asserting.
        /// </summary>
        /// <returns>A task that completes when every started projection has finished.</returns>
        public Task DrainProjectorsAsync()
        {
            Task[] snapshot;
            lock (_ProjectorSync)
            {
                snapshot = _ProjectorTasks.ToArray();
            }

            return Task.WhenAll(snapshot);
        }

        /// <summary>
        /// Returns a plain-text snapshot of the transcript lines. Test helper.
        /// </summary>
        /// <returns>The committed transcript lines without styling.</returns>
        public IReadOnlyList<string> TranscriptSnapshot()
        {
            return _Transcript.SnapshotPlainLines();
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
            _Transcript.WriteLine(Text.From("› " + prompt).Cyan().Bold());

            Job job = _JobManager
                .SubmitAsync(prompt, _ApprovalPolicy, null, _Cts.Token)
                .GetAwaiter()
                .GetResult();

            Task projection = Task.Run(() => _Projector.ProjectAsync(job, _Cts.Token));
            lock (_ProjectorSync)
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
            _Transcript.Clear();
        }

        private void WriteHeader(string title)
        {
            string heading = string.IsNullOrWhiteSpace(title) ? "mux" : "mux · " + title;
            _Transcript.WriteLine(Text.From(heading).Cyan().Bold());
            _Transcript.WriteLine(Text.From("Type a prompt and press Enter. Alt+Enter for a newline.").Dim());
            _Footer.WriteLine(Text.From("Ctrl+Q quit · Ctrl+L clear · Enter send · Esc cancel").Dim());
        }

        #endregion
    }
}
