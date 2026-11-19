namespace Mux.Server.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// The <c>attachments</c> event streamed once per turn when the prompt contained <c>@path</c> mentions: what was
    /// attached for the model, what was skipped, and any notes (for example a size limit).
    /// </summary>
    public class ChatAttachmentsEvent
    {
        /// <summary>The attached files and directories.</summary>
        public List<ChatAttachmentDto> Attached { get; set; } = new List<ChatAttachmentDto>();

        /// <summary>Mentions that were not attached.</summary>
        public List<ChatSkippedAttachmentDto> Skipped { get; set; } = new List<ChatSkippedAttachmentDto>();

        /// <summary>Other notes (for example "mentions are turned off" or a size limit).</summary>
        public List<string> Notes { get; set; } = new List<string>();

        /// <summary>Total bytes attached.</summary>
        public int TotalBytes { get; set; }
    }
}
