namespace Mux.Core.Context
{
    using System.Collections.Generic;

    /// <summary>
    /// The outcome of resolving the <c>@path</c> mentions in a prompt: the prompt to send (the original text plus a
    /// delimited block of attached files), what was attached, and what could not be.
    /// </summary>
    public sealed class FileMentionResult
    {
        #region Public-Members

        /// <summary>The prompt as typed.</summary>
        public string OriginalPrompt { get; set; } = string.Empty;

        /// <summary>The prompt to send to the model: the original text, followed by the attachment block when anything was attached.</summary>
        public string Prompt { get; set; } = string.Empty;

        /// <summary>The delimited <c>&lt;mentioned-files&gt;</c> block alone, or empty when nothing was attached. Callers that
        /// rewrite the prompt (for example a skill invocation) append this to their own text.</summary>
        public string Block { get; set; } = string.Empty;

        /// <summary>The attached files and directories, in mention order, without duplicates.</summary>
        public List<FileMentionAttachment> Attachments { get; set; } = new List<FileMentionAttachment>();

        /// <summary>Mentions left as typed, each with the reason (for example <c>@foo.cs: not found</c>).</summary>
        public List<string> Unresolved { get; set; } = new List<string>();

        /// <summary>Notes about limits, for example when the attachment budget ran out.</summary>
        public List<string> Notes { get; set; } = new List<string>();

        /// <summary>The total UTF-8 size of the attached text.</summary>
        public int TotalBytes { get; set; }

        /// <summary>Whether the prompt contained any mention at all.</summary>
        public bool HadMentions { get; set; }

        #endregion
    }
}
