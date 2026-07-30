namespace Mux.Cli.App
{
    using System;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Jobs;
    using TUIKit;
    using TUIKit.Content;

    /// <summary>
    /// Projects a job's <see cref="AgentEvent"/> stream onto a TUIKit <see cref="Pane"/>: assistant
    /// text streams into a single line updated in place, tool calls and errors render as their own
    /// lines. One projection runs per job; the pane is thread-safe so this may run on a background task
    /// while the render loop reads the pane.
    /// </summary>
    public sealed class AgentEventProjector
    {
        #region Private-Members

        private readonly Pane _Pane;

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
        /// Reads the job's event stream to completion, rendering each event to the pane.
        /// </summary>
        /// <param name="job">The job whose events to project. Must not be null.</param>
        /// <param name="cancellationToken">A token to stop projecting.</param>
        /// <returns>A task that completes when the job's event stream ends.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="job"/> is null.</exception>
        public async Task ProjectAsync(Job job, CancellationToken cancellationToken)
        {
            if (job is null) throw new ArgumentNullException(nameof(job));

            StringBuilder assistant = new StringBuilder();
            PaneLineHandle? assistantLine = null;

            try
            {
                await foreach (AgentEvent agentEvent in job.ReadEventsAsync(cancellationToken).ConfigureAwait(false))
                {
                    switch (agentEvent)
                    {
                        case AssistantTextEvent textEvent:
                            assistant.Append(textEvent.Text);
                            StyledText rendered = Text.From(assistant.ToString());
                            if (assistantLine == null || !assistantLine.Update(rendered))
                            {
                                assistantLine = _Pane.WriteLine(rendered);
                            }

                            break;

                        case ToolCallProposedEvent proposedEvent:
                            assistantLine = null;
                            assistant.Clear();
                            _Pane.WriteLine(Text.From("⏵ " + proposedEvent.ToolCall.Name).Yellow());
                            break;

                        case ToolCallCompletedEvent completedEvent:
                            string mark = completedEvent.Result != null && completedEvent.Result.Success ? " ✓" : " ✗";
                            _Pane.WriteLine(Text.From("  " + completedEvent.ToolName + mark + $"  ({completedEvent.ElapsedMs} ms)").Dim());
                            break;

                        case ErrorEvent errorEvent:
                            assistantLine = null;
                            assistant.Clear();
                            _Pane.WriteLine(Text.From($"Error [{errorEvent.Code}]: {errorEvent.Message}").Red());
                            break;

                        case RunCompletedEvent:
                            assistantLine = null;
                            assistant.Clear();
                            break;

                        default:
                            break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _Pane.WriteLine(Text.From("(cancelled)").Dim());
            }
        }

        #endregion
    }
}
