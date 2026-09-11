namespace Mux.Desktop.Views
{
    using System;

    /// <summary>
    /// A single entry in the <see cref="CommandPaletteWindow"/>: a label, an optional one-line hint, and the
    /// action to run when it is chosen.
    /// </summary>
    public sealed class PaletteCommand
    {
        /// <summary>
        /// Instantiate a palette command.
        /// </summary>
        /// <param name="label">The primary label (matched against the search text).</param>
        /// <param name="hint">An optional one-line description.</param>
        /// <param name="invoke">The action to run when chosen.</param>
        public PaletteCommand(string label, string? hint, Action invoke)
        {
            Label = label ?? string.Empty;
            Hint = hint;
            Invoke = invoke ?? throw new ArgumentNullException(nameof(invoke));
        }

        /// <summary>The primary label.</summary>
        public string Label { get; }

        /// <summary>An optional one-line description.</summary>
        public string? Hint { get; }

        /// <summary>The action to run when chosen.</summary>
        public Action Invoke { get; }
    }
}
