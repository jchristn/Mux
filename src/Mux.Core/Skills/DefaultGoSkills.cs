namespace Mux.Core.Skills
{
    using System.Collections.Generic;

    /// <summary>
    /// The Go default skills. Every command runs from the nearest directory with <c>go.mod</c> and targets all
    /// packages (<c>./...</c>). Commands follow the toolchain conventions in <see cref="DefaultSkillHelpers"/>.
    /// </summary>
    public static class DefaultGoSkills
    {
        #region Private-Members

        private static readonly List<string> _AppliesTo = new List<string> { "go.mod" };

        private const string Setup = @"$dir = Get-MuxGoProject
Set-Location -LiteralPath $dir
$goHint = 'Install Go from https://go.dev/dl.'
";

        #endregion

        #region Public-Methods

        /// <summary>Returns the Go skill definitions.</summary>
        /// <returns>The definitions.</returns>
        public static IReadOnlyList<DefaultSkillDef> All()
        {
            return new List<DefaultSkillDef>
            {
                Skill("go-build", "Build the Go module", "Builds or vets every package in the module.", false,
                    "The user asks to build Go code or check it with go vet.",
                    string.Empty,
                    "`build` compiles every package; `vet` runs go vet's checks for suspicious constructs.",
                    Command("build", "Build every package.", @"Invoke-MuxTool -Tool 'go' -Arguments @('build', './...') -InstallHint $goHint
"),
                    Command("vet", "Run go vet on every package.", @"Invoke-MuxTool -Tool 'go' -Arguments @('vet', './...') -InstallHint $goHint
")),

                Skill("go-test", "Run the Go tests", "Runs go test for every package, filtered, or with the race detector.", false,
                    "The user asks to run Go tests.",
                    "[regex]",
                    "`all` tests every package; `filter <regex>` passes -run; `race` adds the race detector (needs cgo).",
                    Command("all", "Run every test.", @"Invoke-MuxTool -Tool 'go' -Arguments @('test', './...') -InstallHint $goHint
"),
                    Command("filter", "Run tests matching a regular expression.", @"$filter = Get-MuxArg -Arguments $args -Index 0
if (-not $filter) { Exit-MuxNotApplicable 'pass a regex: go-test filter <regex>' }
Invoke-MuxTool -Tool 'go' -Arguments @('test', './...', '-run', $filter) -InstallHint $goHint
"),
                    Command("race", "Run every test with the race detector.", @"Invoke-MuxTool -Tool 'go' -Arguments @('test', '-race', './...') -InstallHint $goHint
")),

                Skill("go-lint", "Lint the Go code", "Runs golangci-lint when the project configures it, otherwise staticcheck.", false,
                    "The user asks to lint Go code.",
                    string.Empty,
                    "`check` uses golangci-lint when a .golangci config exists (or it is the only linter installed), otherwise staticcheck. Exits 2 when neither is installed.",
                    Command("check", "Run the Go linter.", @"$hasConfig = [bool](Get-ChildItem -LiteralPath $dir -Filter '.golangci*' -Force -ErrorAction SilentlyContinue | Select-Object -First 1)
if ($hasConfig -or ((Test-MuxTool 'golangci-lint') -and -not (Test-MuxTool 'staticcheck'))) {
    Invoke-MuxTool -Tool 'golangci-lint' -Arguments @('run') -InstallHint 'Install golangci-lint from https://golangci-lint.run.'
} else {
    Invoke-MuxTool -Tool 'staticcheck' -Arguments @('./...') -InstallHint 'Install staticcheck: go install honnef.co/go/tools/cmd/staticcheck@latest'
}
")),

                Skill("go-mod", "Manage Go module dependencies", "Tidies go.mod and go.sum, or lists dependencies with available updates.", true,
                    "After adding or removing imports, or when the user asks which modules are outdated.",
                    string.Empty,
                    "`tidy` adds missing and removes unused requirements; `outdated` lists modules with newer versions.",
                    Command("tidy", "Tidy go.mod and go.sum.", @"Invoke-MuxTool -Tool 'go' -Arguments @('mod', 'tidy') -InstallHint $goHint
"),
                    Command("outdated", "List modules with available updates.", @"Invoke-MuxTool -Tool 'go' -Arguments @('list', '-u', '-m', 'all') -InstallHint $goHint
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
                Tags = new List<string> { "go" },
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
