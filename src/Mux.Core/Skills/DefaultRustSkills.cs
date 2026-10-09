namespace Mux.Core.Skills
{
    using System.Collections.Generic;

    /// <summary>
    /// The Rust default skills. Every command runs Cargo from the nearest directory with <c>Cargo.toml</c>.
    /// Commands follow the toolchain conventions in <see cref="DefaultSkillHelpers"/>.
    /// </summary>
    public static class DefaultRustSkills
    {
        #region Private-Members

        private static readonly List<string> _AppliesTo = new List<string> { "Cargo.toml" };

        private const string Setup = @"$dir = Get-MuxRustProject
Set-Location -LiteralPath $dir
$cargoHint = 'Install Rust with rustup from https://rustup.rs.'
";

        #endregion

        #region Public-Methods

        /// <summary>Returns the Rust skill definitions.</summary>
        /// <returns>The definitions.</returns>
        public static IReadOnlyList<DefaultSkillDef> All()
        {
            return new List<DefaultSkillDef>
            {
                Skill("cargo-build", "Build the Rust crate", "Builds the crate or workspace in debug or release.", true,
                    "The user asks to build or compile Rust code.",
                    string.Empty,
                    "`debug` is the fast development build; `release` is optimized.",
                    Command("debug", "Build in debug.", @"Invoke-MuxTool -Tool 'cargo' -Arguments @('build') -InstallHint $cargoHint
"),
                    Command("release", "Build in release.", @"Invoke-MuxTool -Tool 'cargo' -Arguments @('build', '--release') -InstallHint $cargoHint
")),

                Skill("cargo-test", "Run the Rust tests", "Runs cargo test for everything or for tests whose names match a filter.", false,
                    "The user asks to run Rust tests.",
                    "[name filter]",
                    "`all` runs unit, integration, and doc tests; `filter <name>` runs tests whose names contain the filter.",
                    Command("all", "Run every test.", @"Invoke-MuxTool -Tool 'cargo' -Arguments @('test') -InstallHint $cargoHint
"),
                    Command("filter", "Run tests whose names match.", @"$filter = Get-MuxArg -Arguments $args -Index 0
if (-not $filter) { Exit-MuxNotApplicable 'pass a test name filter: cargo-test filter <name>' }
Invoke-MuxTool -Tool 'cargo' -Arguments @('test', $filter) -InstallHint $cargoHint
")),

                Skill("cargo-clippy", "Lint the Rust code", "Runs Clippy with warnings as errors, or applies its fixes.", true,
                    "The user asks to lint Rust code or fix Clippy warnings.",
                    string.Empty,
                    "`check` treats every warning as an error so it exits 1 on any finding; `fix` applies machine-applicable suggestions, including on uncommitted files.",
                    Command("check", "Report Clippy findings.", @"Invoke-MuxTool -Tool 'cargo' -Arguments @('clippy', '--all-targets', '--', '-D', 'warnings') -InstallHint 'Install Clippy: rustup component add clippy.'
"),
                    Command("fix", "Apply Clippy's automatic fixes.", @"Invoke-MuxTool -Tool 'cargo' -Arguments @('clippy', '--fix', '--allow-dirty', '--allow-staged', '--all-targets') -InstallHint 'Install Clippy: rustup component add clippy.'
")),

                Skill("cargo-fmt", "Format the Rust code", "Applies or verifies rustfmt formatting.", true,
                    "The user asks to format Rust code or check formatting.",
                    string.Empty,
                    "`apply` formats in place; `verify` exits 1 when any file would change.",
                    Command("apply", "Format files in place.", @"Invoke-MuxTool -Tool 'cargo' -Arguments @('fmt') -InstallHint 'Install rustfmt: rustup component add rustfmt.'
"),
                    Command("verify", "Fail if any file is not formatted.", @"Invoke-MuxTool -Tool 'cargo' -Arguments @('fmt', '--check') -InstallHint 'Install rustfmt: rustup component add rustfmt.'
"))
            };
        }

        #endregion

        #region Private-Methods

        private static DefaultSkillDef Skill(string id, string title, string description, bool mutating, string whenToUse, string argumentHint, string body, params DefaultSkillCommandDef[] commands)
        {
            List<DefaultSkillCommandDef> withSetup = new List<DefaultSkillCommandDef>();
            foreach (DefaultSkillCommandDef command in commands)
            {
                withSetup.Add(new DefaultSkillCommandDef(command.Name, command.Description, command.Interpreter, Setup + command.Code));
            }

            return DefaultSkillHelpers.Attach(new DefaultSkillDef
            {
                Id = id,
                Title = title,
                Description = description,
                Mutating = mutating,
                Tags = new List<string> { "rust", "cargo" },
                WhenToUse = whenToUse,
                AppliesTo = new List<string>(_AppliesTo),
                ArgumentHint = argumentHint,
                Body = body + " Exit codes: 0 success, 1 the tool reported problems, 2 the tool or project is missing.",
                Commands = withSetup
            });
        }

        private static DefaultSkillCommandDef Command(string name, string description, string code)
        {
            return new DefaultSkillCommandDef(name, description, "pwsh", code);
        }

        #endregion
    }
}
