namespace Mux.Server.Runs
{
    /// <summary>
    /// An inbound WebSocket control frame from a bridge client. One of three actions: a subscription request
    /// (<c>action: "subscribe"</c> with a <c>runId</c> or <c>sessionId</c>), a tool-approval decision
    /// (<c>action: "approve"</c> with a <c>toolCallId</c> and <c>decision</c>), or a producer publishing a
    /// run event (<c>action: "publish"</c> with <c>runId</c>/<c>sessionId</c>/<c>endpointName</c>/<c>model</c>
    /// and a pre-serialized canonical envelope in <c>frame</c>).
    /// </summary>
    public sealed class ClientFrame
    {
        /// <summary>The control action: <c>subscribe</c>, <c>approve</c>, <c>publish</c>, or <c>notify</c>.</summary>
        public string? Action { get; set; }

        /// <summary>When true on a subscribe, receive global <c>sessions_changed</c> notifications (list changes).</summary>
        public bool All { get; set; }

        /// <summary>The run id to subscribe to, or the run being published.</summary>
        public string? RunId { get; set; }

        /// <summary>The session id to subscribe to (when no run id), or the published run's session.</summary>
        public string? SessionId { get; set; }

        /// <summary>The tool-call id being answered (approve action).</summary>
        public string? ToolCallId { get; set; }

        /// <summary>The approval decision: <c>y</c>, <c>always</c>, or <c>n</c> (approve action).</summary>
        public string? Decision { get; set; }

        /// <summary>The published run's endpoint name (publish action).</summary>
        public string? EndpointName { get; set; }

        /// <summary>The published run's model (publish action).</summary>
        public string? Model { get; set; }

        /// <summary>A pre-serialized canonical envelope frame to relay to subscribers (publish action).</summary>
        public string? Frame { get; set; }
    }
}
