namespace Mux.Server.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// The body of <c>POST /v1.0/api/chat/answer</c>: an answer to an <c>ask_user</c> question.
    /// </summary>
    public class ChatAnswerRequest
    {
        /// <summary>The run that asked.</summary>
        public string RunId { get; set; } = string.Empty;

        /// <summary>The question id from the <c>ask_user</c> event.</summary>
        public string QuestionId { get; set; } = string.Empty;

        /// <summary>The selected choice labels (one unless the question allows several).</summary>
        public List<string> Answers { get; set; } = new List<string>();

        /// <summary>A free-text answer, used instead of or in addition to the choices.</summary>
        public string? Other { get; set; }

        /// <summary>True to skip the question; the model then picks a sensible default.</summary>
        public bool Skip { get; set; }
    }
}
