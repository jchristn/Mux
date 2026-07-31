namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Tools;

    /// <summary>
    /// A minimal <see cref="IExternalToolProvider"/> for exercising the agent loop's provider seam: it
    /// exposes one tool whose execution records that it ran and echoes a marker into the result.
    /// </summary>
    public sealed class FakeExternalToolProvider : IExternalToolProvider
    {
        private readonly string _ToolName;
        private readonly ToolMutationKind _MutationKind;
        private int _ExecuteCount;

        /// <summary>
        /// Initializes a new instance of the <see cref="FakeExternalToolProvider"/> class.
        /// </summary>
        /// <param name="toolName">The single tool name this provider owns. Must not be null.</param>
        /// <param name="mutationKind">The mutation classification reported for the tool.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="toolName"/> is null.</exception>
        public FakeExternalToolProvider(string toolName, ToolMutationKind mutationKind)
        {
            _ToolName = toolName ?? throw new ArgumentNullException(nameof(toolName));
            _MutationKind = mutationKind;
        }

        /// <inheritdoc/>
        public string Name => "fake-provider";

        /// <summary>
        /// The number of times <see cref="ExecuteAsync"/> has been invoked.
        /// </summary>
        public int ExecuteCount => _ExecuteCount;

        /// <inheritdoc/>
        public IReadOnlyList<ToolDefinition> GetToolDefinitions()
        {
            return new List<ToolDefinition>
            {
                new ToolDefinition
                {
                    Name = _ToolName,
                    Description = "A fake external tool used to test provider routing.",
                    ParametersSchema = new { type = "object", properties = new { } }
                }
            };
        }

        /// <inheritdoc/>
        public bool HasTool(string toolName)
        {
            return string.Equals(toolName, _ToolName, StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc/>
        public ToolMutationKind GetMutationKind(string toolName)
        {
            return _MutationKind;
        }

        /// <inheritdoc/>
        public Task<ToolResult> ExecuteAsync(string toolName, JsonElement arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _ExecuteCount);
            ToolResult result = new ToolResult
            {
                ToolCallId = toolName,
                Success = true,
                Content = JsonSerializer.Serialize(new { marker = "PROVIDER_RAN", tool = toolName })
            };

            return Task.FromResult(result);
        }
    }
}
