namespace Mux.Core.Skills.Evaluation
{
    using System.Collections.Generic;

    /// <summary>
    /// The outcome of one <see cref="SkillEvalCase"/>.
    /// </summary>
    public sealed class SkillEvalCaseResult
    {
        #region Public-Members

        /// <summary>
        /// The case that was evaluated.
        /// </summary>
        public SkillEvalCase Case { get; set; } = new SkillEvalCase();

        /// <summary>
        /// The best (lowest) 1-based rank of any expected skill among the listed skills, or 0 when none is listed.
        /// </summary>
        public int Rank { get; set; }

        /// <summary>
        /// The expected skill that achieved <see cref="Rank"/>, or empty.
        /// </summary>
        public string Matched { get; set; } = string.Empty;

        /// <summary>
        /// Expected skills that were not listed for the fixture (a gating problem).
        /// </summary>
        public List<string> NotListed { get; set; } = new List<string>();

        /// <summary>
        /// Skills from <see cref="SkillEvalCase.Absent"/> that were listed anyway (a gating problem).
        /// </summary>
        public List<string> WronglyListed { get; set; } = new List<string>();

        /// <summary>
        /// The highest-ranked listed skills, best first, for the report.
        /// </summary>
        public List<string> Top { get; set; } = new List<string>();

        /// <summary>
        /// How many skills were listed for the fixture.
        /// </summary>
        public int ListedCount { get; set; }

        /// <summary>
        /// Why the case could not be evaluated (an unknown skill, an empty prompt), or empty.
        /// </summary>
        public string Error { get; set; } = string.Empty;

        /// <summary>
        /// Whether an expected skill ranked within the top N used for the run.
        /// </summary>
        public bool RankPassed { get; set; }

        /// <summary>
        /// Whether the case passed: no error, every expected skill listed, no absent skill listed, and the rank
        /// within the top N.
        /// </summary>
        public bool Passed
        {
            get => string.IsNullOrEmpty(Error) && NotListed.Count == 0 && WronglyListed.Count == 0 && RankPassed;
        }

        /// <summary>
        /// Whether the gating checks passed (no error, nothing missing, nothing wrongly listed), whatever the rank.
        /// </summary>
        public bool GatingPassed
        {
            get => string.IsNullOrEmpty(Error) && NotListed.Count == 0 && WronglyListed.Count == 0;
        }

        #endregion
    }
}
