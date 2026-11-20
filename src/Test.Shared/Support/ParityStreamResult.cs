namespace Test.Shared.Support
{
    using System.Collections.Generic;
    using System.Text.Json;

    /// <summary>
    /// What one streamed chat turn produced, for the mobile-parity route tests.
    /// </summary>
    internal sealed class ParityStreamResult
    {
        /// <summary>The streamed token text plus the terminal event payload.</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>The session id from the <c>run</c> event.</summary>
        public string SessionId { get; set; } = string.Empty;

        /// <summary>The <c>ask_user</c> events.</summary>
        public List<JsonElement> Questions { get; } = new List<JsonElement>();

        /// <summary>The <c>tool</c> events.</summary>
        public List<JsonElement> Tools { get; } = new List<JsonElement>();

        /// <summary>The <c>attachments</c> events.</summary>
        public List<JsonElement> Attachments { get; } = new List<JsonElement>();

        /// <summary>The <c>compacted</c> events.</summary>
        public List<JsonElement> Compacted { get; } = new List<JsonElement>();
    }
}
