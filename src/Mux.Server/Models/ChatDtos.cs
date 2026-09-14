namespace Mux.Server.Models
{
    using System.Collections.Generic;
    using System.Linq;
    using Mux.Core.Enums;
    using Mux.Core.Models;

    /// <summary>
    /// A tool call requested by the assistant, projected for the dashboard so a persisted conversation's
    /// tool-call structure survives a round trip through the web surface.
    /// </summary>
    public class ChatToolCallDto
    {
        /// <summary>The tool-call id (correlates the call with its result message).</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>The tool name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>The raw JSON arguments string.</summary>
        public string Arguments { get; set; } = string.Empty;
    }

    /// <summary>
    /// A single chat message in a dashboard chat request or a persisted conversation. Carries optional tool
    /// calls (assistant) and a tool-call id (tool result) so tool-using transcripts authored on any surface
    /// round-trip through the web surface without losing structure.
    /// </summary>
    public class ChatMessageDto
    {
        /// <summary>Role: user, assistant, system, or tool.</summary>
        public string Role { get; set; } = "user";

        /// <summary>Message content.</summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>Tool calls requested by the assistant, or null when none.</summary>
        public List<ChatToolCallDto>? ToolCalls { get; set; } = null;

        /// <summary>The id of the tool call this message is a result for, or null when not a tool result.</summary>
        public string? ToolCallId { get; set; } = null;
    }

    /// <summary>
    /// Maps between the web <see cref="ChatMessageDto"/> and the core <see cref="ConversationMessage"/>,
    /// preserving tool-call structure in both directions so persisted transcripts stay full-fidelity.
    /// </summary>
    public static class ChatMessageMapper
    {
        /// <summary>Projects a core message to its DTO, carrying tool calls and tool-call id.</summary>
        /// <param name="message">The core message.</param>
        /// <returns>The DTO projection.</returns>
        public static ChatMessageDto ToDto(ConversationMessage message)
        {
            return new ChatMessageDto
            {
                Role = message.Role.ToWire(),
                Content = message.Content ?? string.Empty,
                ToolCalls = message.ToolCalls == null || message.ToolCalls.Count == 0
                    ? null
                    : message.ToolCalls.Select(tc => new ChatToolCallDto { Id = tc.Id ?? string.Empty, Name = tc.Name ?? string.Empty, Arguments = tc.Arguments ?? string.Empty }).ToList(),
                ToolCallId = message.ToolCallId
            };
        }

        /// <summary>Builds a core message from its DTO, carrying tool calls and tool-call id.</summary>
        /// <param name="dto">The DTO.</param>
        /// <returns>The core message.</returns>
        public static ConversationMessage ToModel(ChatMessageDto dto)
        {
            return new ConversationMessage
            {
                Role = RoleEnumExtensions.ParseRole(dto.Role),
                Content = dto.Content ?? string.Empty,
                ToolCalls = dto.ToolCalls == null || dto.ToolCalls.Count == 0
                    ? null
                    : dto.ToolCalls.Select(tc => new ToolCall { Id = tc.Id ?? string.Empty, Name = tc.Name ?? string.Empty, Arguments = tc.Arguments ?? string.Empty }).ToList(),
                ToolCallId = dto.ToolCallId
            };
        }

        /// <summary>Projects a list of core messages to DTOs.</summary>
        /// <param name="messages">The core messages.</param>
        /// <returns>The DTO list.</returns>
        public static List<ChatMessageDto> ToDtoList(IEnumerable<ConversationMessage> messages)
        {
            return messages.Select(ToDto).ToList();
        }

        /// <summary>Builds a list of core messages from DTOs.</summary>
        /// <param name="messages">The DTOs.</param>
        /// <returns>The core message list.</returns>
        public static List<ConversationMessage> ToModelList(IEnumerable<ChatMessageDto> messages)
        {
            return messages.Select(ToModel).ToList();
        }
    }

    /// <summary>
    /// A dashboard chat request: run a plain (tool-free) completion against a configured endpoint.
    /// </summary>
    public class ChatRequest
    {
        /// <summary>Configured endpoint name to run against.</summary>
        public string Endpoint { get; set; } = string.Empty;

        /// <summary>Optional conversation/session id, used to tag usage telemetry per conversation.</summary>
        public string? Id { get; set; }

        /// <summary>
        /// Optional working directory the run's tools resolve paths against. When set it must exist. When
        /// omitted, the run resolves against the persisted session's working directory, then the server's
        /// current directory. Lets an editor or automation run mux in a specific workspace.
        /// </summary>
        public string? WorkingDirectory { get; set; }

        /// <summary>Conversation so far (including the new user turn).</summary>
        public List<ChatMessageDto> Messages { get; set; } = new List<ChatMessageDto>();
    }

    /// <summary>
    /// A tool-call lifecycle event streamed to the dashboard chat: the call's id and name and its status
    /// ("running", "ok", or "fail"), plus elapsed time when completed.
    /// </summary>
    public class ChatToolEvent
    {
        /// <summary>The tool-call id (correlates running → completed).</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>The tool name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>"running", "ok", or "fail".</summary>
        public string Status { get; set; } = string.Empty;

        /// <summary>Elapsed execution time in milliseconds (0 until completed).</summary>
        public long ElapsedMs { get; set; }
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

        /// <summary>The session id the turn was persisted under (server-authored; the browser adopts it).</summary>
        public string Id { get; set; } = string.Empty;

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
    /// An approval prompt streamed to the dashboard when a web chat run proposes a tool that requires
    /// approval (only when the server was started with interactive web tools enabled). The browser answers
    /// by POSTing a <see cref="ChatApproveRequest"/> to <c>/v1.0/api/chat/approve</c>.
    /// </summary>
    public class ChatApprovalRequest
    {
        /// <summary>The run id correlating this prompt with its decision.</summary>
        public string RunId { get; set; } = string.Empty;

        /// <summary>The proposed tool call's id.</summary>
        public string ToolCallId { get; set; } = string.Empty;

        /// <summary>The proposed tool name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>The proposed tool's raw JSON arguments.</summary>
        public string Arguments { get; set; } = string.Empty;
    }

    /// <summary>
    /// A decision answering a <see cref="ChatApprovalRequest"/>: approve once, approve always, or deny.
    /// </summary>
    public class ChatApproveRequest
    {
        /// <summary>The run id from the prompt.</summary>
        public string RunId { get; set; } = string.Empty;

        /// <summary>The tool-call id from the prompt.</summary>
        public string ToolCallId { get; set; } = string.Empty;

        /// <summary>The decision: "y" (approve), "always" (approve and remember), or "n" (deny).</summary>
        public string Decision { get; set; } = "n";
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
