namespace Mux.Core.Skills
{
    /// <summary>
    /// The result of expanding a <c>/&lt;skill&gt; args</c> invocation: the skill that matched, the raw
    /// arguments, and the user message to submit in place of the slash text.
    /// </summary>
    public class SkillInvocation
    {
        #region Private-Members

        private string _SkillName = string.Empty;
        private string _Arguments = string.Empty;
        private string _Prompt = string.Empty;

        #endregion

        #region Public-Members

        /// <summary>
        /// The invoked skill's name. Never null.
        /// </summary>
        public string SkillName
        {
            get => _SkillName;
            set => _SkillName = value ?? string.Empty;
        }

        /// <summary>
        /// The argument text after the skill name, trimmed. Empty when none was given. Never null.
        /// </summary>
        public string Arguments
        {
            get => _Arguments;
            set => _Arguments = value ?? string.Empty;
        }

        /// <summary>
        /// The user message to submit to the model. Never null.
        /// </summary>
        public string Prompt
        {
            get => _Prompt;
            set => _Prompt = value ?? string.Empty;
        }

        /// <summary>
        /// Whether the invoked skill is a playbook (it declares no commands).
        /// </summary>
        public bool IsPlaybook { get; set; }

        #endregion
    }
}
