namespace Mux.Core.Interaction
{
    /// <summary>
    /// One choice offered by the <c>ask_user</c> tool.
    /// </summary>
    public sealed class AskUserOption
    {
        #region Public-Members

        /// <summary>The short label the user picks (1 to 5 words). Never null.</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>An optional explanation of what choosing this option means, or empty.</summary>
        public string Description { get; set; } = string.Empty;

        #endregion
    }
}
