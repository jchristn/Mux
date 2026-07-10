namespace Mux.Cli.Rendering
{
    /// <summary>
    /// Physical console cell position relative to a rendered prompt block.
    /// </summary>
    public class ConsoleCellPosition
    {
        #region Private-Members

        private int _RowOffset = 0;
        private int _Column = 0;

        #endregion

        #region Public-Members

        /// <summary>
        /// The zero-based physical row offset.
        /// </summary>
        public int RowOffset
        {
            get => _RowOffset;
            set => _RowOffset = value;
        }

        /// <summary>
        /// The zero-based physical column.
        /// </summary>
        public int Column
        {
            get => _Column;
            set => _Column = value;
        }

        #endregion
    }
}
