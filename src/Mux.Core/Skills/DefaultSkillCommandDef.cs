namespace Mux.Core.Skills
{
    using System;

    /// <summary>
    /// One command in a default skill definition: its name, description, interpreter, and the code the
    /// command runs. Used only to assemble the seeded default library through <see cref="DefaultSkillBuilder"/>.
    /// </summary>
    public sealed class DefaultSkillCommandDef
    {
        #region Private-Members

        private string _Name = string.Empty;
        private string _Description = string.Empty;
        private string _Interpreter = "pwsh";
        private string _Code = string.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="DefaultSkillCommandDef"/> class.
        /// </summary>
        /// <param name="name">The command name.</param>
        /// <param name="description">The command description.</param>
        /// <param name="interpreter">The interpreter (an allowlisted name).</param>
        /// <param name="code">The code the command runs.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public DefaultSkillCommandDef(string name, string description, string interpreter, string code)
        {
            _Name = name ?? throw new ArgumentNullException(nameof(name));
            _Description = description ?? throw new ArgumentNullException(nameof(description));
            _Interpreter = interpreter ?? throw new ArgumentNullException(nameof(interpreter));
            _Code = code ?? throw new ArgumentNullException(nameof(code));
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The command name (and the id of its body block).
        /// </summary>
        public string Name => _Name;

        /// <summary>
        /// The command description.
        /// </summary>
        public string Description => _Description;

        /// <summary>
        /// The interpreter that runs the command's code.
        /// </summary>
        public string Interpreter => _Interpreter;

        /// <summary>
        /// The code the command runs.
        /// </summary>
        public string Code => _Code;

        #endregion
    }
}
