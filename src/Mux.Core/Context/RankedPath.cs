namespace Mux.Core.Context
{
    /// <summary>
    /// A completion candidate and its rank (lower is better), used by <see cref="FileMentionResolver.Complete"/>.
    /// </summary>
    internal sealed class RankedPath
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="RankedPath"/> class.
        /// </summary>
        /// <param name="path">The relative path.</param>
        /// <param name="rank">The rank.</param>
        public RankedPath(string path, int rank)
        {
            Path = path;
            Rank = rank;
        }

        #endregion

        #region Public-Members

        /// <summary>The relative path.</summary>
        public string Path { get; }

        /// <summary>The rank; lower sorts first.</summary>
        public int Rank { get; }

        #endregion
    }
}
