namespace Mux.Server.Models
{
    /// <summary>
    /// Recent output of a background process (the tail of its buffer) and its state.
    /// </summary>
    public class ProcessOutputDto
    {
        /// <summary>The process id.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>The command line.</summary>
        public string Command { get; set; } = string.Empty;

        /// <summary>The most recent output text (at most the requested number of characters).</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>Whether the process is still running.</summary>
        public bool Running { get; set; }

        /// <summary>The exit code once the process exited, else null.</summary>
        public int? ExitCode { get; set; }
    }
}
