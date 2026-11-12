namespace Mux.Core.Telemetry
{
    /// <summary>
    /// Where one x-axis label of the token heatmap goes: the columns it names (<see cref="Column"/> and
    /// <see cref="Span"/>) and how its text sits over them (<see cref="Align"/>). Every surface draws its labels from
    /// <see cref="UsageHeatmapLayout.PlaceColumnLabels"/>, so a label is never moved off the columns it names.
    /// </summary>
    public sealed class UsageHeatmapLabel
    {
        #region Public-Members

        /// <summary>The first column the label names (its anchor); the label's text is that column's label.</summary>
        public int Column { get; }

        /// <summary>The columns the label names: <see cref="UsageHeatmapLayout.ColumnGroup"/> (2 for a weekday's AM and PM).</summary>
        public int Span { get; }

        /// <summary>How the text sits over <see cref="Column"/> .. <see cref="Column"/> + <see cref="Span"/> - 1.</summary>
        public UsageHeatmapLabelAlignEnum Align { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Creates a placement.
        /// </summary>
        /// <param name="column">The first column the label names.</param>
        /// <param name="span">The columns it names (at least 1).</param>
        /// <param name="align">How its text sits over them.</param>
        public UsageHeatmapLabel(int column, int span, UsageHeatmapLabelAlignEnum align)
        {
            Column = column;
            Span = span < 1 ? 1 : span;
            Align = align;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The left edge of the label's text, in the same units as the arguments, from the grid's left edge: the
        /// start of its first column, centred over its columns, or ending at the end of its last column.
        /// </summary>
        /// <param name="pitch">The distance from one column's left edge to the next (cell width plus gap).</param>
        /// <param name="cellWidth">A cell's drawn width.</param>
        /// <param name="textWidth">The text's width.</param>
        /// <returns>The text's left edge.</returns>
        public double TextLeft(double pitch, double cellWidth, double textWidth)
        {
            double left = Column * pitch;
            double right = ((Column + Span - 1) * pitch) + cellWidth;
            switch (Align)
            {
                case UsageHeatmapLabelAlignEnum.Center: return (left + right - textWidth) / 2;
                case UsageHeatmapLabelAlignEnum.End: return right - textWidth;
                default: return left;
            }
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Column + (Align == UsageHeatmapLabelAlignEnum.Start ? string.Empty : Align == UsageHeatmapLabelAlignEnum.Center ? "c" : "e");
        }

        #endregion
    }
}
