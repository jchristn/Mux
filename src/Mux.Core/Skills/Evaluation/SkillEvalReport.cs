namespace Mux.Core.Skills.Evaluation
{
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// The results of a skill selection evaluation run.
    /// </summary>
    public sealed class SkillEvalReport
    {
        #region Public-Members

        /// <summary>
        /// The rank cutoff a case had to meet.
        /// </summary>
        public int TopN { get; set; }

        /// <summary>
        /// Per-case results, in case order.
        /// </summary>
        public List<SkillEvalCaseResult> Results { get; set; } = new List<SkillEvalCaseResult>();

        /// <summary>
        /// Cases that passed every check.
        /// </summary>
        public int Passed
        {
            get => Results.Count(r => r.Passed);
        }

        /// <summary>
        /// The share of evaluable cases whose expected skill ranked first, from 0 to 1.
        /// </summary>
        public double Top1Rate
        {
            get => Rate(r => r.Rank == 1);
        }

        /// <summary>
        /// The share of evaluable cases whose expected skill ranked within <see cref="TopN"/>, from 0 to 1.
        /// </summary>
        public double TopNRate
        {
            get => Rate(r => r.RankPassed);
        }

        /// <summary>
        /// Cases that failed a gating check or could not be evaluated.
        /// </summary>
        public IReadOnlyList<SkillEvalCaseResult> GatingFailures
        {
            get => Results.Where(r => !r.GatingPassed).ToList();
        }

        #endregion

        #region Private-Methods

        private double Rate(System.Func<SkillEvalCaseResult, bool> predicate)
        {
            List<SkillEvalCaseResult> evaluable = Results.Where(r => string.IsNullOrEmpty(r.Error)).ToList();
            return evaluable.Count == 0 ? 0 : (double)evaluable.Count(predicate) / evaluable.Count;
        }

        #endregion
    }
}
