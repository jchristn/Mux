namespace Mux.Core.Context
{
    /// <summary>
    /// A directory waiting to be indexed for <c>@</c> completion, with its depth below the working directory.
    /// </summary>
    internal sealed class DirectoryLevel
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="DirectoryLevel"/> class.
        /// </summary>
        /// <param name="path">The absolute directory path.</param>
        /// <param name="depth">The depth below the working directory.</param>
        public DirectoryLevel(string path, int depth)
        {
            Path = path;
            Depth = depth;
        }

        #endregion

        #region Public-Members

        /// <summary>The absolute directory path.</summary>
        public string Path { get; }

        /// <summary>The depth below the working directory.</summary>
        public int Depth { get; }

        #endregion
    }
}
