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

        private readonly Pane _Pane;
        private readonly StringBuilder _AssistantText = new StringBuilder();
        private readonly Dictionary<string, PaneLineHandle> _ToolLines = new Dictionary<string, PaneLineHandle>(StringComparer.Ordinal);
        private PaneLineHandle? _AssistantLine;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="AgentEventProjector"/> class.
        /// </summary>
        /// <param name="pane">The transcript pane to write to. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="pane"/> is null.</exception>
        public AgentEventProjector(Pane pane)
        {
            _Pane = pane ?? throw new ArgumentNullException(nameof(pane));
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
                _Pane.WriteLine(Text.From("(cancelled)").Dim());
            }
        }

        #endregion

        #region Private-Methods

        private void Project(AgentEvent agentEvent)
        {
            switch (agentEvent)
            {
                case AssistantTextEvent textEvent:
                    AppendAssistantText(textEvent.Text);
                    break;

                case ToolCallProposedEvent proposedEvent:
                    FinalizeAssistantBlock();
                    string toolName = proposedEvent.ToolCall.Name;
                    PaneLineHandle line = _Pane.WriteLine(Text.From("⏵ " + toolName + " running…").Yellow());
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
                    _Pane.WriteLine(Text.From($"Error [{errorEvent.Code}]: {errorEvent.Message}").Red());
                    break;

                case RunCompletedEvent:
                    FinalizeAssistantBlock();
                    break;

                default:
                    break;
            }
        }

        private void AppendAssistantText(string text)
        {
            _AssistantText.Append(text);

            // While streaming, show a single live line with the latest content so the user sees
            // progress; the full block is re-rendered as markdown when it finalizes.
            StyledText preview = Text.From(LastLine(_AssistantText.ToString()));
            if (_AssistantLine == null || !_AssistantLine.Update(preview))
            {
                _AssistantLine = _Pane.WriteLine(preview);
            }
        }

        private void FinalizeAssistantBlock()
        {
            if (_AssistantLine == null)
            {
                return;
            }

            IReadOnlyList<StyledText> rendered = MarkdownRenderer.Render(_AssistantText.ToString());

            // The live line becomes the first rendered line; the rest are appended after it.
            _AssistantLine.Update(rendered.Count > 0 ? rendered[0] : StyledText.Empty);
            for (int i = 1; i < rendered.Count; i++)
            {
                _Pane.WriteLine(rendered[i]);
            }

            _AssistantLine = null;
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

            _Pane.WriteLine(styled);
        }

        private static string LastLine(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            int lastNewline = text.LastIndexOf('\n');
            return lastNewline < 0 ? text : text.Substring(lastNewline + 1);
        }

        #endregion
    }
}
