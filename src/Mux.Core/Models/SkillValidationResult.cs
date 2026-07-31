namespace Mux.Core.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// The outcome of validating a skill: whether it is usable and, when not, the ordered list of
    /// human-readable problems that made it invalid.
    /// </summary>
    public class SkillValidationResult
    {
        #region Private-Members

        private List<string> _Errors = new List<string>();

        #endregion

        #region Public-Members

        /// <summary>
        /// Whether the skill is valid — true when there are no recorded errors.
        /// </summary>
        public bool IsValid => _Errors.Count == 0;

        /// <summary>
        /// The validation errors, in the order they were discovered. Never null; empty when valid.
        /// </summary>
        public List<string> Errors
        {
            get => _Errors;
            set => _Errors = value ?? new List<string>();
        }

        #endregion
    }
}
