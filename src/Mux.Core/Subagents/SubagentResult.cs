namespace Mux.Core.Subagents
{
    /// <summary>
    /// The outcome of a subagent run: the final assistant text the child produced, whether it completed
    /// successfully, and an optional error message when it did not. Returned by
    /// <see cref="ISubagentExecutor"/> and serialized back to the parent model as the
    /// <c>spawn_subagent</c> tool result.
    /// </summary>
    public sealed class SubagentResult
    {
        #region Public-Members

        /// <summary>
        /// The subagent's final assistant text (its answer to the delegated task), or an empty string
        /// when it produced none.
        /// </summary>
        public string FinalText { get; set; } = string.Empty;

        /// <summary>
        /// Whether the subagent run completed successfully.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// The number of agent-loop iterations the subagent used.
        /// </summary>
        public int Iterations { get; set; }

        /// <summary>
        /// An error message when <see cref="Success"/> is false; otherwise null.
        /// </summary>
        public string? Error { get; set; }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Creates a failed result carrying the supplied error message.
        /// </summary>
        /// <param name="error">The failure reason.</param>
        /// <returns>A failed <see cref="SubagentResult"/>.</returns>
        public static SubagentResult Failed(string error)
        {
            return new SubagentResult
            {
                Success = false,
                FinalText = string.Empty,
                Error = error
            };
        }

        #endregion
    }
}
