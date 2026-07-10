namespace Mux.Cli.Rendering
{
    using System;

    /// <summary>
    /// Physical prompt-block layout derived from the draft buffer and console width.
    /// </summary>
    public class PromptLayout
    {
        /// <summary>
        /// Total physical rows occupied by the prompt block, including any trailing wrapped cursor row.
        /// </summary>
        public int TotalRows { get; set; }

        /// <summary>
        /// The cursor row offset within the prompt block.
        /// </summary>
        public int CursorRowOffset { get; set; }

        /// <summary>
        /// The cursor column within the cursor row.
        /// </summary>
        public int CursorColumn { get; set; }

        /// <summary>
        /// The top-row offset for each logical draft line within the prompt block.
        /// </summary>
        public int[] LineRowOffsets { get; set; } = Array.Empty<int>();
    }
}
