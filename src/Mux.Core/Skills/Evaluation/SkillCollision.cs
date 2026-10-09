namespace Mux.Core.Skills.Evaluation
{
    /// <summary>
    /// Two skills whose listed descriptions are similar enough that the model may confuse them.
    /// </summary>
    public sealed class SkillCollision
    {
        #region Public-Members

        /// <summary>
        /// The first skill name (alphabetically).
        /// </summary>
        public string First { get; set; } = string.Empty;

        /// <summary>
        /// The second skill name.
        /// </summary>
        public string Second { get; set; } = string.Empty;

        /// <summary>
        /// Cosine similarity of the two descriptions' TF-IDF vectors, from 0 to 1.
        /// </summary>
        public double Similarity { get; set; }

        #endregion
    }
}
