namespace Mux.Cli.Commands
{
    /// <summary>
    /// Supported structured output modes for mux CLI commands.
    /// </summary>
    public enum OutputFormatEnum
    {
        /// <summary>
        /// Human-readable text output.
        /// </summary>
        Text,

        /// <summary>
        /// Single JSON document output.
        /// </summary>
        Json,

        /// <summary>
        /// JSON Lines event stream output.
        /// </summary>
        Jsonl
    }
}
