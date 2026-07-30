namespace Mux.Cli.App
{
    using System;

    /// <summary>
    /// A single command in the mux command catalog: a stable id, a human-readable title, an optional
    /// key chord (TUIKit syntax, e.g. <c>"ctrl+q"</c>), and the handler to run. The catalog is the single
    /// source consumed by keybindings today and by the palette / menu bar / footer in later milestones.
    /// </summary>
    public sealed class CommandDescriptor
    {
        #region Public-Members

        /// <summary>
        /// The stable command identifier.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// The human-readable command title.
        /// </summary>
        public string Title { get; }

        /// <summary>
        /// The key chord that triggers the command (TUIKit chord syntax), or null for no binding.
        /// </summary>
        public string? Chord { get; }

        /// <summary>
        /// The handler invoked when the command runs.
        /// </summary>
        public Action Handler { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="CommandDescriptor"/> class.
        /// </summary>
        /// <param name="id">The stable command id. Must not be null or empty.</param>
        /// <param name="title">The human-readable title. Must not be null.</param>
        /// <param name="chord">The optional key chord (e.g. "ctrl+q"); null for no keybinding.</param>
        /// <param name="handler">The handler to run. Must not be null.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="id"/> is null or empty.</exception>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="handler"/> is null.</exception>
        public CommandDescriptor(string id, string title, string? chord, Action handler)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Command id cannot be null or empty.", nameof(id));
            Id = id;
            Title = title ?? string.Empty;
            Chord = string.IsNullOrWhiteSpace(chord) ? null : chord;
            Handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        #endregion
    }
}
