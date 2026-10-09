namespace Mux.Core.Hosting
{
    using System.Collections.Generic;

    /// <summary>
    /// One way to open a terminal window: the program to start and its arguments, passed as a list so nothing is
    /// re-split by a shell.
    /// </summary>
    public sealed class TerminalCommand
    {
        #region Public-Members

        /// <summary>The program to start.</summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>The arguments, used when <see cref="RawArguments"/> is null.</summary>
        public List<string> Arguments { get; set; } = new List<string>();

        /// <summary>
        /// A preformatted argument string, for programs (such as <c>cmd.exe</c> with <c>start</c>) whose parsing needs
        /// exact quoting. Null uses <see cref="Arguments"/>.
        /// </summary>
        public string? RawArguments { get; set; }

        #endregion
    }
}
