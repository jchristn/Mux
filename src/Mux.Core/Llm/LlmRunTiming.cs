namespace Mux.Core.Llm
{
    /// <summary>
    /// Folds primary model-call timings without mixing preparation, approval, or tool execution into
    /// streaming time. Missing metrics stay unknown rather than being synthesized from wall-clock time.
    /// </summary>
    internal sealed class LlmRunTiming
    {
        private int _Calls;
        private int _TimedStreams;
        private long _StreamingMs;

        internal long? FirstCallTtftMs { get; private set; }
        internal long? StreamingMs => _Calls > 0 && _TimedStreams == _Calls ? _StreamingMs : null;

        internal void Add(LlmCallMetrics? call)
        {
            if (_Calls++ == 0)
            {
                FirstCallTtftMs = call?.TimeToFirstTokenMs >= 0 ? call.TimeToFirstTokenMs : null;
            }

            if (call?.StreamingMs >= 0)
            {
                _StreamingMs += call.StreamingMs.Value;
                _TimedStreams++;
            }
        }
    }
}
