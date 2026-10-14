namespace Mux.Cli.App
{
    using TUIKit.Content;

    /// <summary>
    /// The one place the interactive shell decides how Markdown looks. Every Markdown surface in the TUI (the
    /// streamed assistant answer, replayed history, and the Markdown viewer modal) lays its block out through
    /// TUIKit's width-aware engine with these options, so they reflow on resize and stay consistent.
    /// </summary>
    public static class MarkdownOptions
    {
        #region Public-Methods

        /// <summary>
        /// Creates the options used for Markdown rendered in the TUI. A new instance is returned each call so
        /// callers may adjust it without affecting other surfaces.
        /// </summary>
        /// <returns>A new <see cref="MarkdownRenderOptions"/>. Never null.</returns>
        public static MarkdownRenderOptions Create()
        {
            MarkdownRenderOptions options = new MarkdownRenderOptions();
            options.Table.Glyphs = MarkdownTableGlyphs.Rounded;
            return options;
        }

        #endregion
    }
}
