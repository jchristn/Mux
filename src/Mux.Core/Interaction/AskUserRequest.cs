namespace Mux.Core.Interaction
{
    using System.Collections.Generic;

    /// <summary>
    /// A question the model asks the user mid-turn through the <c>ask_user</c> tool: the question, 2 to 4 options,
    /// and whether several may be chosen. The user can always answer in free text instead ("Other").
    /// </summary>
    public sealed class AskUserRequest
    {
        #region Public-Members

        /// <summary>The question. Never null.</summary>
        public string Question { get; set; } = string.Empty;

        /// <summary>The options, in the order the model gave them. Never null.</summary>
        public List<AskUserOption> Options { get; set; } = new List<AskUserOption>();

        /// <summary>Whether the user may pick more than one option.</summary>
        public bool MultiSelect { get; set; }

        #endregion
    }
}
