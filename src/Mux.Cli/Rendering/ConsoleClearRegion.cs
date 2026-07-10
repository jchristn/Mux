namespace Mux.Cli.Rendering
{
    /// <summary>
    /// Physical console row range that should be cleared before prompt redraw.
    /// </summary>
    public class ConsoleClearRegion
    {
        #region Private-Members

        private int _Top = 0;
        private int _RowCount = 0;

        #endregion

        #region Public-Members

        /// <summary>
        /// The first row to clear.
        /// </summary>
        public int Top
        {
            get => _Top;
            set => _Top = value;
        }

        /// <summary>
        /// The total number of rows to clear.
        /// </summary>
        public int RowCount
        {
            get => _RowCount;
            set => _RowCount = value;
        }

        #endregion
    }
}
