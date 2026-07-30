namespace Mux.Cli.App
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using TUIKit;
    using TUIKit.Content;

    /// <summary>
    /// Projects a single job's <see cref="AgentEvent"/> stream onto a TUIKit <see cref="Pane"/>.
    /// Assistant text is buffered per block and, when the block ends (a tool call, an error, or run
    /// completion), re-rendered through <see cref="MarkdownRenderer"/>; while a block is streaming a
    /// single live line shows the latest content. Tool calls render as one line per call that is
    /// updated in place from "running" to a success/failure result. One projector instance drives one
    /// pane; the pane is thread-safe so projection may run on a background task while the render loop
    /// reads the pane.
    /// </summary>
    public sealed class AgentEventProjector
    {
        #region Private-Members

        private static readonly byte[] MarkerPalette = { 12, 13, 14, 10, 11, 9 };

        private readonly Pane _Pane;
        private readonly int _TurnNumber;
        private readonly Func<bool>? _Attribute;
        private readonly StringBuilder _AssistantText = new StringBuilder();
        private readonly StringBuilder _RunAssistantText = new StringBuilder();
        private readonly Dictionary<string, PaneLineHandle> _ToolLines = new Dictionary<string, PaneLineHandle>(StringComparer.Ordinal);
        private readonly List<PaneLineHandle> _AssistantLines = new List<PaneLineHandle>();
        private bool _FirstTokenSeen;
        private bool _ModelResponded;
        private RunCompletedEvent? _LastRunCompleted;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="AgentEventProjector"/> class.
        /// </summary>
        /// <param name="pane">The transcript pane to write to. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="pane"/> is null.</exception>
        public AgentEventProjector(Pane pane)
            : this(pane, 0, null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AgentEventProjector"/> class that attributes its
        /// output to a turn. When several turns stream into one shared pane, a compact colored
        /// <c>#N</c> marker prefixes each line this projector writes so interleaved output stays readable.
        /// </summary>
        /// <param name="pane">The transcript pane to write to. Must not be null.</param>
        /// <param name="turnNumber">The turn's display number; when zero, no marker is ever shown.</param>
        /// <param name="attribute">
        /// A predicate evaluated at each write; the marker is shown only when it returns true (typically
        /// "more than one turn is in flight"), so a lone turn renders exactly as it did before.
        /// </param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="pane"/> is null.</exception>
        public AgentEventProjector(Pane pane, int turnNumber, Func<bool>? attribute)
        {
            _Pane = pane ?? throw new ArgumentNullException(nameof(pane));
            _TurnNumber = turnNumber;
            _Attribute = attribute;
        }

        #endregion

        #region Public-Events

        /// <summary>
        /// Raised once, when the first assistant token of the run is projected. Used by the shell to
        /// stamp time-to-first-token.
        /// </summary>
        public event Action? FirstTokenReceived;

        /// <summary>
        /// Raised once, when the run produces its first observable output (assistant text, a tool call,
        /// or an error). Used by the shell to dismiss the "thinking" indicator the moment results begin.
        /// </summary>
        public event Action? ModelResponded;

        #endregion

        #region Public-Members

        /// <summary>
        /// The full assistant text emitted across the run, accumulated verbatim (all blocks joined).
        /// Consumed by the shell to append the assistant turn to conversation history.
        /// </summary>
        public string CapturedAssistantText
        {
            get => _RunAssistantText.ToString();
        }

        /// <summary>
        /// The final <see cref="RunCompletedEvent"/> observed for this run, or null when the run produced
        /// none. Carries the engine's end-of-run summary (estimated tokens, duration, counts).
        /// </summary>
        public RunCompletedEvent? LastRunCompleted
        {
            get => _LastRunCompleted;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Reads an agent event stream to completion, rendering each event to the pane. Any assistant
        /// text still buffered when the stream ends is finalized.
        /// </summary>
        /// <param name="events">The event stream (typically a job's <c>ReadEventsAsync</c>). Must not be null.</param>
        /// <param name="cancellationToken">A token to stop projecting.</param>
        /// <returns>A task that completes when the stream ends.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="events"/> is null.</exception>
        public async Task ProjectAsync(IAsyncEnumerable<AgentEvent> events, CancellationToken cancellationToken)
        {
            if (events is null) throw new ArgumentNullException(nameof(events));

            try
            {
                await foreach (AgentEvent agentEvent in events.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    Project(agentEvent);
                }

                FinalizeAssistantBlock();
            }
            catch (OperationCanceledException)
            {
                FinalizeAssistantBlock();
                _Pane.WriteLine(Tag(Text.From("(cancelled)").Dim()));
            }
        }

        #endregion

        #region Private-Methods

        private void Project(AgentEvent agentEvent)
        {
            SignalRespondedIfMeaningful(agentEvent);

            switch (agentEvent)
            {
                case AssistantTextEvent textEvent:
                    AppendAssistantText(textEvent.Text);
                    break;

                case ToolCallProposedEvent proposedEvent:
                    FinalizeAssistantBlock();
                    string toolName = proposedEvent.ToolCall.Name;
                    PaneLineHandle line = _Pane.WriteLine(Tag(Text.From("⏵ " + toolName + " running…").Yellow()));
                    if (!string.IsNullOrEmpty(proposedEvent.ToolCall.Id))
                    {
                        _ToolLines[proposedEvent.ToolCall.Id] = line;
                    }

                    break;

                case ToolCallCompletedEvent completedEvent:
                    FinalizeAssistantBlock();
                    ProjectToolCompleted(completedEvent);
                    break;

                case ErrorEvent errorEvent:
                    FinalizeAssistantBlock();
                    _Pane.WriteLine(Tag(Text.From($"Error [{errorEvent.Code}]: {errorEvent.Message}").Red()));
                    break;

                case RunCompletedEvent runCompleted:
                    FinalizeAssistantBlock();
                    _LastRunCompleted = runCompleted;
                    break;

                default:
                    break;
            }
        }

        private void SignalRespondedIfMeaningful(AgentEvent agentEvent)
        {
            if (_ModelResponded)
            {
                return;
            }

            if (agentEvent is AssistantTextEvent
                || agentEvent is ToolCallProposedEvent
                || agentEvent is ToolCallCompletedEvent
                || agentEvent is ErrorEvent)
            {
                _ModelResponded = true;
                ModelResponded?.Invoke();
            }
        }

        private void AppendAssistantText(string text)
        {
            _AssistantText.Append(text);
            _RunAssistantText.Append(text);

            if (!_FirstTokenSeen && !string.IsNullOrEmpty(text))
            {
                _FirstTokenSeen = true;
                FirstTokenReceived?.Invoke();
            }

            // Stream the full response as it grows, one pane line per text line, so it fills the
            // available vertical space; the block is re-rendered as markdown when it finalizes.
            string[] lines = _AssistantText.ToString().Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                StyledText styled = Tag(Text.From(lines[i]));
                if (i < _AssistantLines.Count)
                {
                    if (!_AssistantLines[i].Update(styled))
                    {
                        _AssistantLines[i] = _Pane.WriteLine(styled);
                    }
                }
                else
                {
                    _AssistantLines.Add(_Pane.WriteLine(styled));
                }
            }
        }

        private void FinalizeAssistantBlock()
        {
            if (_AssistantLines.Count == 0)
            {
                _AssistantText.Clear();
                return;
            }

            IReadOnlyList<StyledText> rendered = MarkdownRenderer.Render(_AssistantText.ToString());

            int i = 0;
            for (; i < rendered.Count; i++)
            {
                if (i < _AssistantLines.Count)
                {
                    _AssistantLines[i].Update(Tag(rendered[i]));
                }
                else
                {
                    _AssistantLines.Add(_Pane.WriteLine(Tag(rendered[i])));
                }
            }

            // Markdown often yields fewer lines than the raw stream (e.g. code fences removed); blank the
            // leftover streamed lines so no stale raw text remains.
            for (; i < _AssistantLines.Count; i++)
            {
                _AssistantLines[i].Update(StyledText.Empty);
            }

            _AssistantLines.Clear();
            _AssistantText.Clear();
        }

        private void ProjectToolCompleted(ToolCallCompletedEvent completedEvent)
        {
            bool success = completedEvent.Result != null && completedEvent.Result.Success;
            string mark = success ? "✓" : "✗";
            string text = $"{mark} {completedEvent.ToolName}  ({completedEvent.ElapsedMs} ms)";
            StyledText styled = success ? Text.From(text).Green() : Text.From(text).Red();

            if (!string.IsNullOrEmpty(completedEvent.ToolCallId)
                && _ToolLines.TryGetValue(completedEvent.ToolCallId, out PaneLineHandle? line)
                && line.Update(styled))
            {
                _ToolLines.Remove(completedEvent.ToolCallId);
                return;
            }

            _Pane.WriteLine(Tag(styled));
        }

        // Prefixes a line with the turn's colored "#N " marker when attribution is active. A lone turn
        // (attribute predicate false, or turn number zero) renders unchanged.
        private StyledText Tag(StyledText body)
        {
            if (_TurnNumber <= 0 || _Attribute == null || !_Attribute())
            {
                return body;
            }

            return Marker(_TurnNumber).Append(body);
        }

        #endregion

        #region Public-Statics

        /// <summary>
        /// Builds the compact colored <c>#N </c> turn marker used to attribute a line to a turn. The color
        /// is stable per turn number so the shell's own lines (the echoed prompt, the thinking indicator)
        /// match the marker this projector prefixes onto the turn's output.
        /// </summary>
        /// <param name="turnNumber">The turn's display number.</param>
        /// <returns>The styled marker, including the trailing space.</returns>
        public static StyledText Marker(int turnNumber)
        {
            Color color = Color.FromPalette(MarkerPalette[Math.Max(0, turnNumber - 1) % MarkerPalette.Length]);
            return Text.From("#" + turnNumber + " ").Foreground(color);
        }

        #endregion
    }
}
