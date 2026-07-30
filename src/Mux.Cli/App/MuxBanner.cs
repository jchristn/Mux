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
        /// Builds the startup splash lines: the wordmark, a tagline with the version, and the copyright.
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
            return lines;
        }
    }
}
