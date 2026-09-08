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
    }
}
