namespace Mux.Server.Models
{
    /// <summary>
    /// A file or directory an <c>@path</c> mention attached to the prompt.
    /// </summary>
    public class ChatAttachmentDto
    {
        /// <summary>The path relative to the run's working directory.</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary><c>file</c> or <c>directory</c>.</summary>
        public string Kind { get; set; } = "file";

        /// <summary>Size of the attached text in bytes.</summary>
        public int Bytes { get; set; }

        /// <summary>Line count of the attached text.</summary>
        public int Lines { get; set; }

        /// <summary>Whether the content was inlined (false when only a summary or map was attached).</summary>
        public bool Inlined { get; set; } = true;
    }
}
