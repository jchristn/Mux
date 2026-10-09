namespace Mux.Core.Hosting
{
    using System.Collections.Generic;

    /// <summary>
    /// Where the mux command-line program was found and how to start it: an executable plus any leading arguments
    /// (for example <c>dotnet</c> with the path to <c>Mux.Cli.dll</c>).
    /// </summary>
    public sealed class MuxCliLocation
    {
        #region Public-Members

        /// <summary>The program to run (an absolute path, or a bare name resolved through PATH).</summary>
        public string Executable { get; set; } = string.Empty;

        /// <summary>Arguments that come before any of the caller's own (for example the CLI dll for <c>dotnet</c>).</summary>
        public List<string> LeadingArguments { get; set; } = new List<string>();

        /// <summary>How it was found: <c>override</c>, <c>path</c>, <c>dotnet-tools</c>, <c>beside</c>, or <c>checkout</c>.</summary>
        public string Source { get; set; } = string.Empty;

        #endregion
    }
}
