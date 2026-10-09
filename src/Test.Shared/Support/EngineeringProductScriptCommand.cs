namespace Test.Shared.Support
{
    /// <summary>
    /// One script-backed command expected on an imported engineering (m through z) or product pack skill:
    /// where the skill lives, the command name, the script it runs, and its timeout.
    /// </summary>
    public sealed class EngineeringProductScriptCommand
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="EngineeringProductScriptCommand"/> class.
        /// </summary>
        /// <param name="pack">The pack id.</param>
        /// <param name="skill">The skill id.</param>
        /// <param name="command">The command name.</param>
        /// <param name="script">The script path relative to the skill folder.</param>
        /// <param name="timeoutMs">The declared timeout, or 0 for the default.</param>
        public EngineeringProductScriptCommand(string pack, string skill, string command, string script, int timeoutMs)
        {
            Pack = pack;
            Skill = skill;
            Command = command;
            Script = script;
            TimeoutMs = timeoutMs;
        }

        #endregion

        #region Public-Members

        /// <summary>The pack id.</summary>
        public string Pack { get; }

        /// <summary>The skill id.</summary>
        public string Skill { get; }

        /// <summary>The command name.</summary>
        public string Command { get; }

        /// <summary>The script path relative to the skill folder.</summary>
        public string Script { get; }

        /// <summary>The declared timeout in milliseconds, or 0 for the loader default.</summary>
        public int TimeoutMs { get; }

        #endregion
    }
}
