namespace Mux.Core.Models
{
    using System;

    /// <summary>
    /// A single named, runnable unit declared by a skill. A command executes either a fenced code block
    /// from the skill body (<see cref="BlockId"/>) or a bundled script (<see cref="ScriptPath"/>), never
    /// both, through the interpreter named in <see cref="Interpreter"/>.
    /// </summary>
    public class SkillCommand
    {
        #region Private-Members

        private string _Name = string.Empty;
        private string _Description = string.Empty;
        private string? _ScriptPath = null;
        private string? _BlockId = null;
        private string _Interpreter = "bash";
        private int _TimeoutMs = 120000;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="SkillCommand"/> class.
        /// </summary>
        public SkillCommand()
        {
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The command name, unique within its skill and referenced by <c>run_skill</c>.
        /// </summary>
        public string Name
        {
            get => _Name;
            set => _Name = value ?? throw new ArgumentNullException(nameof(Name));
        }

        /// <summary>
        /// A short human-readable description of what the command does.
        /// </summary>
        public string Description
        {
            get => _Description;
            set => _Description = value ?? string.Empty;
        }

        /// <summary>
        /// The path to a bundled script relative to the skill directory, or null when the command runs an
        /// inline body block instead. Exactly one of <see cref="ScriptPath"/> and <see cref="BlockId"/> is set.
        /// </summary>
        public string? ScriptPath
        {
            get => _ScriptPath;
            set => _ScriptPath = value;
        }

        /// <summary>
        /// The id of a fenced code block in the skill body, or null when the command runs a bundled script
        /// instead. Exactly one of <see cref="ScriptPath"/> and <see cref="BlockId"/> is set.
        /// </summary>
        public string? BlockId
        {
            get => _BlockId;
            set => _BlockId = value;
        }

        /// <summary>
        /// The interpreter used to run the command (for example <c>bash</c>, <c>pwsh</c>, or <c>python</c>).
        /// Must be a member of the interpreter allowlist. Defaults to <c>bash</c>.
        /// </summary>
        public string Interpreter
        {
            get => _Interpreter;
            set => _Interpreter = string.IsNullOrWhiteSpace(value) ? "bash" : value.Trim();
        }

        /// <summary>
        /// The maximum time the command may run before it is killed, in milliseconds. Clamped to a minimum
        /// of 1000. Defaults to 120000 (two minutes).
        /// </summary>
        public int TimeoutMs
        {
            get => _TimeoutMs;
            set => _TimeoutMs = Math.Max(1000, value);
        }

        #endregion
    }
}
