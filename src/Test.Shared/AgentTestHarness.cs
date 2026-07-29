namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Models;

    /// <summary>
    /// Shared helpers for exercising the <see cref="AgentLoop"/> against a <see cref="MockHttpServer"/>
    /// from Touchstone descriptors. Centralizes endpoint construction, event collection, and SSE
    /// chunk building so the individual suites stay focused on their assertions.
    /// </summary>
    public static class AgentTestHarness
    {
        /// <summary>
        /// Default per-run timeout, in seconds, applied when collecting agent-loop events so a hung
        /// mock interaction fails fast rather than blocking the suite.
        /// </summary>
        public static int DefaultRunTimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// Builds an endpoint configuration pointing at the supplied mock server base URL.
        /// </summary>
        /// <param name="baseUrl">The mock server base URL. Must not be null.</param>
        /// <returns>An <see cref="EndpointConfig"/> for the OpenAI-compatible mock endpoint.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="baseUrl"/> is null.</exception>
        public static EndpointConfig BuildMockEndpoint(string baseUrl)
        {
            if (baseUrl is null) throw new ArgumentNullException(nameof(baseUrl));

            return new EndpointConfig
            {
                Name = "mock-test",
                AdapterType = AdapterTypeEnum.OpenAiCompatible,
                BaseUrl = baseUrl,
                Model = "test-model",
                Headers = new Dictionary<string, string>()
            };
        }

        /// <summary>
        /// Runs the agent loop with the given options and prompt, collecting every emitted event.
        /// A linked timeout (see <see cref="DefaultRunTimeoutSeconds"/>) is combined with the caller's
        /// token so a stalled run cannot hang the suite.
        /// </summary>
        /// <param name="options">The agent loop options. Must not be null.</param>
        /// <param name="prompt">The prompt to send. Must not be null.</param>
        /// <param name="cancellationToken">A token to observe for cancellation.</param>
        /// <returns>The ordered list of events emitted by the run.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> or <paramref name="prompt"/> is null.</exception>
        public static async Task<List<AgentEvent>> CollectEventsAsync(
            AgentLoopOptions options,
            string prompt,
            CancellationToken cancellationToken)
        {
            if (options is null) throw new ArgumentNullException(nameof(options));
            if (prompt is null) throw new ArgumentNullException(nameof(prompt));

            List<AgentEvent> events = new List<AgentEvent>();
            using (AgentLoop loop = new AgentLoop(options))
            using (CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(DefaultRunTimeoutSeconds)))
            using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token))
            {
                await foreach (AgentEvent agentEvent in loop.RunAsync(prompt, linked.Token).ConfigureAwait(false))
                {
                    events.Add(agentEvent);
                }
            }

            return events;
        }

        /// <summary>
        /// Concatenates the text of every <see cref="AssistantTextEvent"/> in the supplied events.
        /// </summary>
        /// <param name="events">The events to scan. Must not be null.</param>
        /// <returns>The combined assistant text, or an empty string if there is none.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="events"/> is null.</exception>
        public static string CombineAssistantText(IReadOnlyList<AgentEvent> events)
        {
            if (events is null) throw new ArgumentNullException(nameof(events));

            string combined = string.Empty;
            foreach (AgentEvent agentEvent in events)
            {
                if (agentEvent is AssistantTextEvent textEvent)
                {
                    combined += textEvent.Text;
                }
            }

            return combined;
        }

        /// <summary>
        /// Builds a single SSE data chunk for a streaming text response terminated by a stop
        /// finish reason.
        /// </summary>
        /// <param name="text">The assistant text content to embed. Must not be null.</param>
        /// <returns>The serialized SSE chunk body.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="text"/> is null.</exception>
        public static string BuildTextSseChunk(string text)
        {
            if (text is null) throw new ArgumentNullException(nameof(text));

            string escaped = text.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return "{\"choices\":[{\"delta\":{\"content\":\"" + escaped + "\"},\"finish_reason\":\"stop\"}]}";
        }
    }
}
