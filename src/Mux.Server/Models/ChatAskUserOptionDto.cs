namespace Mux.Server.Models
{
    /// <summary>
    /// One choice in an <c>ask_user</c> question.
    /// </summary>
    public class ChatAskUserOptionDto
    {
        /// <summary>The short choice label (what an answer names).</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>An optional explanation of the choice.</summary>
        public string Description { get; set; } = string.Empty;
    }
}
