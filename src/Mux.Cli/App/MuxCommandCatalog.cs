namespace Mux.Cli.App
{
    using System;
    using System.Collections.Generic;
    using TUIKit.Hosting;
    using TUIKit.Input;

    /// <summary>
    /// The ordered set of <see cref="CommandDescriptor"/> that make up the mux command surface. The
    /// catalog is the single source of truth: <see cref="ApplyTo"/> wires each command's id and key
    /// chord onto a <see cref="TuiApplication"/>, and later milestones read the same list to build the
    /// command palette, menu bar, and footer hints.
    /// </summary>
    public sealed class MuxCommandCatalog
    {
        #region Private-Members

        private readonly List<CommandDescriptor> _Commands = new List<CommandDescriptor>();
        private readonly Dictionary<string, CommandDescriptor> _ById = new Dictionary<string, CommandDescriptor>(StringComparer.Ordinal);

        #endregion

        #region Public-Members

        /// <summary>
        /// The registered commands in insertion order.
        /// </summary>
        public IReadOnlyList<CommandDescriptor> Commands
        {
            get => _Commands;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Adds a command to the catalog. A later command with an existing id replaces the earlier one.
        /// </summary>
        /// <param name="descriptor">The command to add. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="descriptor"/> is null.</exception>
        public void Add(CommandDescriptor descriptor)
        {
            if (descriptor is null) throw new ArgumentNullException(nameof(descriptor));

            if (_ById.TryGetValue(descriptor.Id, out CommandDescriptor? existing))
            {
                _Commands.Remove(existing);
            }

            _Commands.Add(descriptor);
            _ById[descriptor.Id] = descriptor;
        }

        /// <summary>
        /// Finds a command by id, or null when none is registered.
        /// </summary>
        /// <param name="id">The command id to look up.</param>
        /// <returns>The matching descriptor, or null.</returns>
        public CommandDescriptor? Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return _ById.TryGetValue(id, out CommandDescriptor? descriptor) ? descriptor : null;
        }

        /// <summary>
        /// Registers every command's handler by id, and binds each command's key chord when present.
        /// </summary>
        /// <param name="application">The application to wire. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="application"/> is null.</exception>
        public void ApplyTo(TuiApplication application)
        {
            if (application is null) throw new ArgumentNullException(nameof(application));

            foreach (CommandDescriptor descriptor in _Commands)
            {
                application.RegisterCommand(descriptor.Id, descriptor.Handler);
                if (descriptor.Chord != null)
                {
                    application.Commands.Register(KeyChord.Parse(descriptor.Chord), descriptor.Id);
                }
            }
        }

        #endregion
    }
}
