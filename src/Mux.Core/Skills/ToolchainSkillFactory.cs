namespace Mux.Core.Skills
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Builds toolchain skill definitions that share a setup block, tags, listing gates, and the exit-code note,
    /// so each family declares only what differs. Every command is a pwsh block prefixed with the family's setup
    /// and, through <see cref="DefaultSkillHelpers.Attach"/>, the helper prelude. Stateless and thread-safe.
    /// </summary>
    public sealed class ToolchainSkillFactory
    {
        #region Private-Members

        private readonly string _Setup;
        private readonly List<string> _Tags;
        private readonly List<string> _AppliesTo;
        private readonly List<string> _RequiresTools;
        private readonly string _ExitNote;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="ToolchainSkillFactory"/> class.
        /// </summary>
        /// <param name="setup">PowerShell run before every command (after the helper prelude). Null is treated as empty.</param>
        /// <param name="tags">Tags for every skill. Null is treated as empty.</param>
        /// <param name="appliesTo">File globs that gate listing. Null is treated as empty.</param>
        /// <param name="requiresTools">Executables that gate listing. Null is treated as empty.</param>
        /// <param name="exitNote">The exit-code sentence appended to every body. Null uses the standard note.</param>
        public ToolchainSkillFactory(string? setup, IEnumerable<string>? tags, IEnumerable<string>? appliesTo, IEnumerable<string>? requiresTools, string? exitNote = null)
        {
            _Setup = setup ?? string.Empty;
            _Tags = new List<string>(tags ?? Array.Empty<string>());
            _AppliesTo = new List<string>(appliesTo ?? Array.Empty<string>());
            _RequiresTools = new List<string>(requiresTools ?? Array.Empty<string>());
            _ExitNote = exitNote ?? StandardExitNote;
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The exit-code note for skills that change infrastructure.
        /// </summary>
        public const string GuardedExitNote = " Exit codes: 0 success, 1 the tool reported a failure, 2 the tool is missing, not signed in, or does not apply here, 3 refused by the production guard.";

        /// <summary>
        /// The exit-code note for ordinary toolchain skills.
        /// </summary>
        public const string StandardExitNote = " Exit codes: 0 success, 1 the tool reported problems, 2 the tool or project is missing.";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Creates one skill definition.
        /// </summary>
        /// <param name="id">The skill id.</param>
        /// <param name="title">The title.</param>
        /// <param name="description">The description.</param>
        /// <param name="mutating">Whether any command changes state.</param>
        /// <param name="whenToUse">When the model should reach for the skill.</param>
        /// <param name="argumentHint">The argument hint, or empty.</param>
        /// <param name="body">The prose body (the exit-code note is appended).</param>
        /// <param name="commands">The commands; each is (name, description, code) via <see cref="Command"/>.</param>
        /// <returns>The definition.</returns>
        public DefaultSkillDef Skill(string id, string title, string description, bool mutating, string whenToUse, string argumentHint, string body, params DefaultSkillCommandDef[] commands)
        {
            return Skill(id, title, description, mutating, whenToUse, argumentHint, body, null, null, commands);
        }

        /// <summary>
        /// Creates one skill definition with its own listing gates instead of the family's.
        /// </summary>
        /// <param name="id">The skill id.</param>
        /// <param name="title">The title.</param>
        /// <param name="description">The description.</param>
        /// <param name="mutating">Whether any command changes state.</param>
        /// <param name="whenToUse">When the model should reach for the skill.</param>
        /// <param name="argumentHint">The argument hint, or empty.</param>
        /// <param name="body">The prose body (the exit-code note is appended).</param>
        /// <param name="appliesTo">File globs for this skill, or null for the family's.</param>
        /// <param name="requiresTools">Executables for this skill, or null for the family's.</param>
        /// <param name="commands">The commands.</param>
        /// <returns>The definition.</returns>
        public DefaultSkillDef Skill(string id, string title, string description, bool mutating, string whenToUse, string argumentHint, string body, IEnumerable<string>? appliesTo, IEnumerable<string>? requiresTools, params DefaultSkillCommandDef[] commands)
        {
            List<DefaultSkillCommandDef> withSetup = new List<DefaultSkillCommandDef>();
            foreach (DefaultSkillCommandDef command in commands)
            {
                withSetup.Add(new DefaultSkillCommandDef(command.Name, command.Description, command.Interpreter, _Setup + command.Code) { TimeoutMs = command.TimeoutMs });
            }

            return DefaultSkillHelpers.Attach(new DefaultSkillDef
            {
                Id = id,
                Title = title,
                Description = description,
                Mutating = mutating,
                Tags = new List<string>(_Tags),
                WhenToUse = whenToUse,
                AppliesTo = new List<string>(appliesTo ?? _AppliesTo),
                RequiresTools = new List<string>(requiresTools ?? _RequiresTools),
                ArgumentHint = argumentHint,
                Body = body + _ExitNote,
                Commands = withSetup
            });
        }

        /// <summary>
        /// Creates a pwsh command.
        /// </summary>
        /// <param name="name">The command name.</param>
        /// <param name="description">The command description.</param>
        /// <param name="code">The PowerShell code (run after the family setup).</param>
        /// <returns>The command definition.</returns>
        public static DefaultSkillCommandDef Command(string name, string description, string code)
        {
            return new DefaultSkillCommandDef(name, description, "pwsh", code.EndsWith("\n", StringComparison.Ordinal) ? code : code + "\n");
        }

        #endregion
    }
}
