namespace Mux.Server.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// A single chat message in a dashboard chat request.
    /// </summary>
    public class ChatMessageDto
    {
        /// <summary>Role: user, assistant, or system.</summary>
        public string Role { get; set; } = "user";

        /// <summary>Message content.</summary>
        public string Content { get; set; } = string.Empty;
    }

    /// <summary>
    /// A dashboard chat request: run a plain (tool-free) completion against a configured endpoint.
    /// </summary>
    public class ChatRequest
    {
        /// <summary>Configured endpoint name to run against.</summary>
        public string Endpoint { get; set; } = string.Empty;

        /// <summary>Conversation so far (including the new user turn).</summary>
        public List<ChatMessageDto> Messages { get; set; } = new List<ChatMessageDto>();
    }

    /// <summary>
    /// Per-turn statistics for a dashboard chat reply: latency broken into time-to-first-token and
    /// streaming duration, plus provider-reported token counts by type. Values are zero when the provider
    /// or transport did not report them (for example a backend that does not stream token-by-token).
    /// </summary>
    public class ChatStats
    {
        /// <summary>Milliseconds from request send to the first streamed token (−1 when no token streamed).</summary>
        public long TtftMs { get; set; }

        /// <summary>Milliseconds spent streaming, from the first token to completion.</summary>
        public long StreamingMs { get; set; }

        /// <summary>Total wall-clock milliseconds for the turn.</summary>
        public long TotalMs { get; set; }

        /// <summary>Provider-reported prompt/input tokens (0 when unreported).</summary>
        public int InputTokens { get; set; }

        /// <summary>Provider-reported completion/output tokens (0 when unreported).</summary>
        public int OutputTokens { get; set; }

        /// <summary>Provider-reported total tokens (0 when unreported).</summary>
        public int TotalTokens { get; set; }
    }

    /// <summary>
    /// The full contents of a saved conversation, returned when the dashboard opens one to continue it.
    /// </summary>
    public class SessionDetailDto
    {
        /// <summary>Session id.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Session title.</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>Endpoint name captured with the session.</summary>
        public string EndpointName { get; set; } = string.Empty;

        /// <summary>Model captured with the session.</summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>The conversation messages, oldest first.</summary>
        public List<ChatMessageDto> Messages { get; set; } = new List<ChatMessageDto>();
    }

    /// <summary>
    /// A request to create or update (upsert) a saved conversation from the dashboard chat. When
    /// <see cref="Id"/> is empty a new session is created; otherwise the existing session is replaced. A blank
    /// <see cref="Title"/> is derived from the first user message.
    /// </summary>
    public class SessionSaveRequest
    {
        /// <summary>Session id to update; empty to create a new one.</summary>
        public string? Id { get; set; }

        /// <summary>Optional title; blank derives one from the first user message.</summary>
        public string? Title { get; set; }

        /// <summary>Endpoint name to record.</summary>
        public string EndpointName { get; set; } = string.Empty;

        /// <summary>Model to record.</summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>The conversation messages to persist, oldest first.</summary>
        public List<ChatMessageDto> Messages { get; set; } = new List<ChatMessageDto>();
    }

    /// <summary>
    /// A dashboard chat reply.
    /// </summary>
    public class ChatReply
    {
        /// <summary>Always "assistant".</summary>
        public string Role { get; set; } = "assistant";

        /// <summary>Assistant text.</summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>Endpoint the reply came from.</summary>
        public string Endpoint { get; set; } = string.Empty;

        /// <summary>Model the reply came from.</summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>Per-turn timing and token statistics.</summary>
        public ChatStats Stats { get; set; } = new ChatStats();
    }

    /// <summary>
    /// The result of a model-load (warm) request: whether the probe succeeded, whether the endpoint was
    /// reachable at all, and any error detail.
    /// </summary>
    public class ModelLoadReply
    {
        /// <summary>Whether the model responded to the warm probe.</summary>
        public bool Ok { get; set; }

        /// <summary>Whether the endpoint was reachable (a response was returned, even an error one).</summary>
        public bool Reachable { get; set; }

        /// <summary>Error detail when the probe did not succeed, or null.</summary>
        public string? Error { get; set; }
    }
}
