namespace Mux.Core.Context
{
    using System.Collections.Generic;

    /// <summary>
    /// Produces a line-anchored structural outline for a file. This is the swap point that lets the source of
    /// structure vary: the first cut ships a single generic, coarse provider, and a language-specific parser
    /// (or a caller-supplied outline, such as the VS Code extension's language-server symbols) can be
    /// registered later without touching <see cref="FileContextBuilder"/> or any surface.
    /// </summary>
    public interface IFileOutlineProvider
    {
        /// <summary>
        /// Builds an outline for a file's contents.
        /// </summary>
        /// <param name="path">The file path (used for extension-based heuristics only; never read from disk).</param>
        /// <param name="content">The full file contents.</param>
        /// <returns>The outline entries in document order; never null (may be empty for a tiny or blank file).</returns>
        IReadOnlyList<FileOutlineEntry> GetOutline(string path, string content);
    }
}
