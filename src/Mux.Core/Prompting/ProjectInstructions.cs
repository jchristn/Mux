namespace Mux.Core.Prompting
{
    using System.Collections.Generic;

    /// <summary>
    /// The project instruction files loaded for a working directory (<c>MUX.md</c>, <c>AGENTS.md</c>, or
    /// <c>CLAUDE.md</c> at each level, plus the user-level <c>~/.mux/MUX.md</c>), combined into one block of
    /// text in precedence order: the user file first, then outer directories before inner ones, so the file
    /// nearest the working directory is read last and wins on conflict.
    /// </summary>
    public class ProjectInstructions
    {
        #region Private-Members

        private List<string> _Sources = new List<string>();
        private List<string> _DroppedSources = new List<string>();
        private string _Text = string.Empty;

        #endregion

        #region Public-Members

        /// <summary>
        /// The absolute paths of the files whose content is included, in the order they appear in
        /// <see cref="Text"/>. Never null; empty when nothing was found.
        /// </summary>
        public List<string> Sources
        {
            get => _Sources;
            set => _Sources = value ?? new List<string>();
        }

        /// <summary>
        /// The absolute paths of files that were found but left out because the size cap was reached. The
        /// outermost files are dropped first. Never null.
        /// </summary>
        public List<string> DroppedSources
        {
            get => _DroppedSources;
            set => _DroppedSources = value ?? new List<string>();
        }

        /// <summary>
        /// The combined instruction text, each file introduced by a <c>### path</c> heading. Empty when
        /// nothing was loaded. Never null.
        /// </summary>
        public string Text
        {
            get => _Text;
            set => _Text = value ?? string.Empty;
        }

        /// <summary>
        /// The UTF-8 byte count of the included file contents (headings excluded).
        /// </summary>
        public long TotalBytes { get; set; }

        /// <summary>
        /// Whether the size cap forced a file to be dropped or the innermost file to be cut short.
        /// </summary>
        public bool Truncated { get; set; }

        /// <summary>
        /// Whether any instruction text was loaded.
        /// </summary>
        public bool HasContent => _Text.Length > 0;

        #endregion
    }
}
