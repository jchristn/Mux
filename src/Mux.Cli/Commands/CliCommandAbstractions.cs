namespace Mux.Cli.Commands
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Minimal command context used by mux's local command dispatcher.
    /// </summary>
    public sealed class CommandContext
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CommandContext"/> class.
        /// </summary>
        /// <param name="commandName">The command name being executed.</param>
        /// <param name="arguments">The original command-line arguments.</param>
        public CommandContext(string commandName, string[] arguments)
        {
            CommandName = commandName ?? string.Empty;
            Arguments = arguments ?? Array.Empty<string>();
        }

        /// <summary>
        /// The command name being executed.
        /// </summary>
        public string CommandName { get; }

        /// <summary>
        /// The original command-line arguments.
        /// </summary>
        public string[] Arguments { get; }
    }

    /// <summary>
    /// Base settings type for mux commands.
    /// </summary>
    public abstract class CommandSettings
    {
    }

    /// <summary>
    /// Base type for asynchronous mux commands.
    /// </summary>
    /// <typeparam name="TSettings">The command settings type.</typeparam>
    public abstract class AsyncCommand<TSettings>
        where TSettings : CommandSettings
    {
        /// <summary>
        /// Executes the command.
        /// </summary>
        /// <param name="context">The command context.</param>
        /// <param name="settings">The command settings.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The process exit code.</returns>
        public abstract Task<int> ExecuteAsync(CommandContext context, TSettings settings, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Documents an option mapping for mux's local command parser.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class CommandOptionAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CommandOptionAttribute"/> class.
        /// </summary>
        /// <param name="template">The option template.</param>
        public CommandOptionAttribute(string template)
        {
            Template = template ?? string.Empty;
        }

        /// <summary>
        /// The option template.
        /// </summary>
        public string Template { get; }
    }

    /// <summary>
    /// Documents a positional argument mapping for mux's local command parser.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class CommandArgumentAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CommandArgumentAttribute"/> class.
        /// </summary>
        /// <param name="position">The positional argument index.</param>
        /// <param name="template">The argument template.</param>
        public CommandArgumentAttribute(int position, string template)
        {
            Position = position;
            Template = template ?? string.Empty;
        }

        /// <summary>
        /// The positional argument index.
        /// </summary>
        public int Position { get; }

        /// <summary>
        /// The argument template.
        /// </summary>
        public string Template { get; }
    }
}
