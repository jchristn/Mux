namespace Mux.Desktop.Conversation
{
    /// <summary>
    /// The lifecycle state of a tool call as it moves through a turn: proposed and running, approved,
    /// completed successfully, or failed.
    /// </summary>
    public enum ToolCallStatus
    {
        /// <summary>Proposed by the model and running (awaiting approval or execution).</summary>
        Running,

        /// <summary>Approved for execution.</summary>
        Approved,

        /// <summary>Executed successfully.</summary>
        Completed,

        /// <summary>Executed but reported failure.</summary>
        Failed
    }
}
