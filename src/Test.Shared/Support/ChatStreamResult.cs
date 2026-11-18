namespace Test.Shared.Support
{
    /// <summary>
    /// What one streamed dashboard chat turn produced: its approval prompts and its text.
    /// </summary>
    internal sealed class ChatStreamResult
    {
        /// <summary>The number of <c>approval</c> events received.</summary>
        public int Approvals { get; set; }

        /// <summary>The streamed token text plus the terminal event payload.</summary>
        public string Text { get; set; } = string.Empty;
    }
}
