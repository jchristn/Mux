namespace Mux.Core.Branding
{
    /// <summary>How a terminal draws block-character art such as the mux wordmark.</summary>
    public enum BlockArtMode
    {
        /// <summary>As the block glyphs <c>█</c>, <c>▀</c>, and <c>▄</c>, which terminals that draw block elements
        /// themselves (iTerm2, most others) stretch to fill each cell.</summary>
        Glyphs,

        /// <summary>As background-coloured spaces, one terminal row per half-row and two cells per column, which fill
        /// every cell even where the terminal takes block glyphs from the font (macOS Terminal.app).</summary>
        Cells
    }
}
