namespace Mux.Core.Models
{
    using System;

    /// <summary>
    /// The inputs collected by the create-skill flow, used to generate a starter <c>SKILL.md</c>. Kept as a
    /// small value object so the wizard can build it up step by step and preview the result before writing.
    /// </summary>
    public class SkillScaffold
    {
        #region Private-Members

        private string _Id = string.Empty;
        private string _Title = string.Empty;
        private string _Description = string.Empty;
        private bool _Mutating = true;
        private string _Interpreter = "pwsh";

        #endregion

        #region Public-Members

        /// <summary>
        /// The skill identifier and folder name (lowercase, hyphen-separated).
        /// </summary>
        public string Id
        {
            get => _Id;
            set => _Id = value ?? string.Empty;
        }

        /// <summary>
        /// The human-readable title. Falls back to the id when empty.
        /// </summary>
        public string Title
        {
            get => string.IsNullOrWhiteSpace(_Title) ? _Id : _Title;
            set => _Title = value ?? string.Empty;
        }

        /// <summary>
        /// A one- or two-sentence description of what the skill does.
        /// </summary>
        public string Description
        {
            get => _Description;
            set => _Description = value ?? string.Empty;
        }

        /// <summary>
        /// Whether the skill's commands mutate the workspace. Defaults to true.
        /// </summary>
        public bool Mutating
        {
            get => _Mutating;
            set => _Mutating = value;
        }

        /// <summary>
        /// The interpreter for the starter command. Defaults to <c>pwsh</c>.
        /// </summary>
        public string Interpreter
        {
            get => _Interpreter;
            set => _Interpreter = string.IsNullOrWhiteSpace(value) ? "pwsh" : value.Trim();
        }

        #endregion
    }
}
