namespace Mux.Cli.Commands
{
    /// <summary>
    /// The resolved headless output behavior for a single <c>mux print</c> run. Collapses the two
    /// orthogonal axes the caller controls -- <c>--output-format</c> (text/json/jsonl), <c>--buffer</c>
    /// (stream vs. hold), and <c>--stats</c>/<c>--no-stats</c> -- into the concrete decisions the print
    /// loop acts on. This keeps the mode matrix in one pure, unit-testable place.
    ///
    /// <para>The four public output shapes map as follows:</para>
    /// <list type="bullet">
    ///   <item><description><c>text</c> (default): streamed answer text; statless on stdout. <c>--buffer</c> holds it to the end; <c>--stats</c> adds a stderr footer.</description></item>
    ///   <item><description><c>jsonl</c>: streamed structured events; stats-bearing unless <c>--no-stats</c>.</description></item>
    ///   <item><description><c>json</c>: one buffered structured object; stats-bearing unless <c>--no-stats</c>.</description></item>
    /// </list>
    ///
    /// <para>Token statistics only exist on the structured (json/jsonl) shapes and on the opt-in text
    /// stderr footer; a plain text answer never carries them inline.</para>
    /// </summary>
    public readonly struct HeadlessOutputPlan
    {
        private HeadlessOutputPlan(OutputFormatEnum format, bool streamed, bool includeStats, bool emitTextStatsFooter)
        {
            Format = format;
            Streamed = streamed;
            IncludeStats = includeStats;
            EmitTextStatsFooter = emitTextStatsFooter;
        }

        /// <summary>
        /// The resolved output format.
        /// </summary>
        public OutputFormatEnum Format { get; }

        /// <summary>
        /// True when output is emitted incrementally as the run proceeds: streamed text (unless
        /// <c>--buffer</c>) and the jsonl event stream. False for the buffered json summary and buffered text.
        /// </summary>
        public bool Streamed { get; }

        /// <summary>
        /// True when run statistics and token usage are surfaced: on the structured json/jsonl output, and as
        /// the driver of the text-mode stderr footer.
        /// </summary>
        public bool IncludeStats { get; }

        /// <summary>
        /// True only for text output that should print the one-line token/statistics footer to stderr after
        /// the answer (text mode with stats requested). Never set for json/jsonl.
        /// </summary>
        public bool EmitTextStatsFooter { get; }

        /// <summary>
        /// Resolves the plan for a run.
        /// </summary>
        /// <param name="format">The parsed <c>--output-format</c> value.</param>
        /// <param name="statsRequested">
        /// The tri-state <c>--stats</c>/<c>--no-stats</c> selection: true forces stats on, false forces them
        /// off, and null selects the per-format default (off for text, on for json/jsonl).
        /// </param>
        /// <param name="buffer">The <c>--buffer</c>/<c>--no-stream</c> selection (text only; ignored elsewhere).</param>
        /// <returns>The resolved <see cref="HeadlessOutputPlan"/>.</returns>
        public static HeadlessOutputPlan Resolve(OutputFormatEnum format, bool? statsRequested, bool buffer)
        {
            bool includeStats = statsRequested ?? (format != OutputFormatEnum.Text);

            bool streamed = format switch
            {
                OutputFormatEnum.Text => !buffer,
                OutputFormatEnum.Json => false,
                OutputFormatEnum.Jsonl => true,
                _ => true
            };

            bool emitTextStatsFooter = format == OutputFormatEnum.Text && includeStats;

            return new HeadlessOutputPlan(format, streamed, includeStats, emitTextStatsFooter);
        }
    }
}
