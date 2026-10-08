namespace Mux.Core.Skills
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;

    /// <summary>
    /// The shared PowerShell helper (<c>resources/mux-skill.ps1</c>) that every default toolchain skill ships with,
    /// and the one-line prelude each command block uses to load it. The helper implements the conventions all
    /// toolchain skills follow: <c>MUX_SKILL_DRY_RUN=1</c> prints commands instead of running them, a missing tool
    /// exits 2 with an install hint, and tool failures exit with the tool's own code. Thread-safe.
    /// </summary>
    public static class DefaultSkillHelpers
    {
        #region Private-Members

        private const string ResourceName = "Mux.Core.Skills.mux-skill.ps1";

        private static readonly Lazy<string> _Script = new Lazy<string>(LoadScript);

        #endregion

        #region Public-Members

        /// <summary>
        /// The helper's path relative to a skill directory.
        /// </summary>
        public const string HelperPath = "resources/mux-skill.ps1";

        /// <summary>
        /// The first line of every toolchain command block: dot-sources the helper from the skill directory.
        /// </summary>
        public const string Prelude = ". (Join-Path $env:MUX_SKILL_DIR 'resources/mux-skill.ps1')\n";

        /// <summary>
        /// The environment variable that turns on dry-run mode for toolchain skills.
        /// </summary>
        public const string DryRunVariable = "MUX_SKILL_DRY_RUN";

        /// <summary>
        /// The helper script's text, with LF line endings.
        /// </summary>
        public static string Script => _Script.Value;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Prepares a toolchain skill definition: adds the helper as a resource and prefixes each pwsh command block
        /// with <see cref="Prelude"/>. Returns the same instance.
        /// </summary>
        /// <param name="definition">The definition. Must not be null.</param>
        /// <returns>The definition, for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="definition"/> is null.</exception>
        public static DefaultSkillDef Attach(DefaultSkillDef definition)
        {
            ArgumentNullException.ThrowIfNull(definition);

            definition.Resources[HelperPath] = Script;
            List<DefaultSkillCommandDef> commands = new List<DefaultSkillCommandDef>();
            foreach (DefaultSkillCommandDef command in definition.Commands)
            {
                bool needsPrelude = command.Interpreter == "pwsh" && !command.Code.StartsWith(Prelude, StringComparison.Ordinal);
                commands.Add(needsPrelude
                    ? new DefaultSkillCommandDef(command.Name, command.Description, command.Interpreter, Prelude + command.Code)
                    : command);
            }

            definition.Commands = commands;
            return definition;
        }

        #endregion

        #region Private-Methods

        private static string LoadScript()
        {
            Assembly assembly = typeof(DefaultSkillHelpers).Assembly;
            using (Stream? stream = assembly.GetManifestResourceStream(ResourceName))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("The embedded skill helper '" + ResourceName + "' is missing from Mux.Core.");
                }

                using (StreamReader reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd().Replace("\r\n", "\n");
                }
            }
        }

        #endregion
    }
}
