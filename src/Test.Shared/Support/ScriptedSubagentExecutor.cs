namespace Test.Shared.Support
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Subagents;

    /// <summary>
    /// An <see cref="ISubagentExecutor"/> for tests: records the working directory each call received and runs a
    /// caller-supplied action there (for example writing a file), then returns a successful result.
    /// </summary>
    public sealed class ScriptedSubagentExecutor : ISubagentExecutor
    {
        #region Private-Members

        private readonly Action<string> _Action;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="ScriptedSubagentExecutor"/> class.
        /// </summary>
        /// <param name="action">Runs with the working directory the subagent was given; may throw to simulate a failure.</param>
        public ScriptedSubagentExecutor(Action<string> action)
        {
            _Action = action ?? throw new ArgumentNullException(nameof(action));
        }

        #endregion

        #region Public-Members

        /// <summary>The working directories received, in call order.</summary>
        public List<string> WorkingDirectories { get; } = new List<string>();

        #endregion

        #region Public-Methods

        /// <inheritdoc/>
        public Task<SubagentResult> ExecuteAsync(SubagentDefinition definition, string prompt, string workingDirectory, CancellationToken cancellationToken)
        {
            WorkingDirectories.Add(workingDirectory);
            _Action(workingDirectory);
            return Task.FromResult(new SubagentResult { Success = true, FinalText = "done in " + workingDirectory, Iterations = 1 });
        }

        #endregion
    }
}
