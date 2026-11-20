namespace Mux.Server.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// The <c>ask_user</c> event streamed when the model asks the user a multiple-choice question mid-turn. The run
    /// waits until the client answers with <c>POST /v1.0/api/chat/answer</c> (or an <c>answer</c> WebSocket frame),
    /// skips, or the question times out.
    /// </summary>
    public class ChatAskUserEvent
    {
        /// <summary>The run that asked.</summary>
        public string RunId { get; set; } = string.Empty;

        /// <summary>The question id to answer.</summary>
        public string QuestionId { get; set; } = string.Empty;

        /// <summary>The question text.</summary>
        public string Question { get; set; } = string.Empty;

        /// <summary>The choices (2 to 4).</summary>
        public List<ChatAskUserOptionDto> Options { get; set; } = new List<ChatAskUserOptionDto>();

        /// <summary>Whether more than one choice may be selected.</summary>
        public bool MultiSelect { get; set; }

        /// <summary>Whether a free-text answer is accepted (always true).</summary>
        public bool AllowOther { get; set; } = true;

        /// <summary>Whether the question may be skipped (always true).</summary>
        public bool AllowSkip { get; set; } = true;

        /// <summary>Seconds until the question is treated as unanswered.</summary>
        public int TimeoutSeconds { get; set; }
    }
}
