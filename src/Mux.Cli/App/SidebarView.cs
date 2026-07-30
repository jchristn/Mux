namespace Mux.Cli.App
{
    using System;
    using TUIKit;
    using TUIKit.Content;

    /// <summary>
    /// Renders the ambient sidebar into a <see cref="Pane"/>: a session header (title + active endpoint
    /// name) followed by conversation telemetry — current status, this-turn timing/tokens, and
    /// session-wide totals. <see cref="Refresh"/> is idempotent (it clears and rewrites the pane from a
    /// snapshot) so it is safe to call on every turn boundary and streaming tick. Calls are serialized
    /// internally so concurrent refreshes do not interleave.
    /// </summary>
    public sealed class SidebarView
    {
        #region Private-Members

        private const int Width = 28;

        private readonly Pane _Pane;
        private readonly object _Sync = new object();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="SidebarView"/> class.
        /// </summary>
        /// <param name="pane">The sidebar pane to render into. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="pane"/> is null.</exception>
        public SidebarView(Pane pane)
        {
            _Pane = pane ?? throw new ArgumentNullException(nameof(pane));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Rewrites the sidebar from the given header and telemetry snapshot.
        /// </summary>
        /// <param name="title">The session title shown in the header.</param>
        /// <param name="endpointName">The active endpoint name shown in the header.</param>
        /// <param name="stats">The conversation telemetry to display. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="stats"/> is null.</exception>
        public void Refresh(string title, string endpointName, ConversationStats stats)
        {
            if (stats is null) throw new ArgumentNullException(nameof(stats));

            lock (_Sync)
            {
                _Pane.Clear();
                _Pane.WriteLine(Text.From(Fit(string.IsNullOrWhiteSpace(title) ? "mux" : title)).Cyan().Bold());
                if (!string.IsNullOrWhiteSpace(endpointName))
                {
                    _Pane.WriteLine(Text.From(Fit(endpointName)).Dim());
                }

                _Pane.WriteLine(Text.From(string.Empty));

                string status = stats.InFlight > 1
                    ? $"{stats.InFlight} running"
                    : (stats.Busy ? "running" : "idle");
                if (stats.Queued > 0)
                {
                    status += $" · {stats.Queued} queued";
                }

                _Pane.WriteLine(Text.From(Fit("STATUS")).Bold());
                _Pane.WriteLine(Text.From(Fit(" " + status)).Dim());

                _Pane.WriteLine(Text.From(string.Empty));
                _Pane.WriteLine(Text.From(Fit("THIS TURN")).Bold());
                _Pane.WriteLine(Text.From(Fit(Row("TTFT", FormatMs(stats.LastTtftMs)))));
                _Pane.WriteLine(Text.From(Fit(Row("Stream", FormatMs(stats.LastStreamMs)))));
                _Pane.WriteLine(Text.From(Fit(Row("Context", FormatTokens(stats.LastContextTokens)))));

                _Pane.WriteLine(Text.From(string.Empty));
                _Pane.WriteLine(Text.From(Fit("SESSION")).Bold());
                _Pane.WriteLine(Text.From(Fit(Row("Turns", stats.Turns.ToString()))));
                _Pane.WriteLine(Text.From(Fit(Row("TTFT avg", FormatMs(AverageTtft(stats))))));
                _Pane.WriteLine(Text.From(Fit(Row("Stream", FormatMs(stats.SessionStreamMs)))));
                _Pane.WriteLine(Text.From(Fit(Row("In tok", FormatTokens(stats.InputTokens)))));
                _Pane.WriteLine(Text.From(Fit(Row("Out tok", FormatTokens(stats.OutputTokens)))));
                _Pane.WriteLine(Text.From(Fit(Row("Cached", FormatTokens(stats.CachedTokens)))));
            }
        }

        #endregion

        #region Private-Methods

        private static long AverageTtft(ConversationStats stats)
        {
            return stats.TtftSamples > 0 ? stats.SessionTtftMs / stats.TtftSamples : -1;
        }

        private static string Row(string label, string value)
        {
            return $" {label,-9}{value}";
        }

        private static string FormatMs(long ms)
        {
            if (ms < 0)
            {
                return "—";
            }

            if (ms < 1000)
            {
                return ms + " ms";
            }

            return (ms / 1000.0).ToString("0.0") + " s";
        }

        private static string FormatTokens(long tokens)
        {
            if (tokens <= 0)
            {
                return "—";
            }

            if (tokens < 1000)
            {
                return tokens.ToString();
            }

            return (tokens / 1000.0).ToString("0.0") + "k";
        }

        private static string Fit(string text)
        {
            string value = text ?? string.Empty;
            return value.Length <= Width ? value : value.Substring(0, Width - 1) + "…";
        }

        #endregion
    }
}
