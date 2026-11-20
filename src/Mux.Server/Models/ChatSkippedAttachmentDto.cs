namespace Mux.Server.Models
{
    /// <summary>
    /// An <c>@path</c> mention that was not attached, and why.
    /// </summary>
    public class ChatSkippedAttachmentDto
    {
        /// <summary>The mention as typed (for example <c>@src/missing.cs</c>).</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>Why it was skipped (for example <c>not found</c> or <c>binary file, not attached</c>).</summary>
        public string Reason { get; set; } = string.Empty;
    }
}
