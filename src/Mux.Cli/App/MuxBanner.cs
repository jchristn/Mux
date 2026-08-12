namespace Mux.Cli.App
{
    using System.Collections.Generic;

    /// <summary>
    /// The mux ASCII-art wordmark and the startup splash text.
    /// </summary>
    public static class MuxBanner
    {
        private static readonly string[] Art =
        {
            " _____ _ _ _ _",
            "|     | | |_'_|",
            "|_|_|_|___|_,_|"
        };

        /// <summary>
        /// Builds the startup splash lines: the wordmark, a tagline with the version, the copyright, and the
        /// project URL. The splash modal appends its own blank gap before the "press any key" hint, so the URL
        /// is the final content line here and reads with one blank line above and below it.
        /// </summary>
        /// <param name="version">The product version string.</param>
        /// <returns>The splash content lines.</returns>
        public static IReadOnlyList<string> SplashLines(string version)
        {
            List<string> lines = new List<string>();
            foreach (string row in Art)
            {
                lines.Add(row);
            }

            lines.Add(string.Empty);
            lines.Add("AI agent for local and remote LLMs  ·  v" + (version ?? string.Empty));
            lines.Add("(c)2026 Joel Christner");
            lines.Add(string.Empty);
            lines.Add("https://github.com/jchristn/mux");
            return lines;
        }
    }
}
