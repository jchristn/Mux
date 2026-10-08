namespace Mux.Core.Skills
{
    using System.Collections.Generic;

    /// <summary>
    /// The JavaScript and TypeScript default skills. Every command finds the nearest <c>package.json</c>, picks the
    /// package manager from the <c>packageManager</c> field or the lockfile (bun, pnpm, yarn, npm), prefers the
    /// project's own scripts, and falls back to the tool directly. Commands follow the toolchain conventions in
    /// <see cref="DefaultSkillHelpers"/> (dry runs, exit 2 when the tool or project is missing).
    /// </summary>
    public static class DefaultJavaScriptSkills
    {
        #region Private-Members

        private static readonly List<string> _AppliesTo = new List<string> { "package.json" };

        private const string Setup = @"$dir = Get-MuxNodeProject
$pm = Get-MuxNodePackageManager -Dir $dir
$pkg = Get-MuxPackageJson -Dir $dir
Set-Location -LiteralPath $dir
";

        #endregion

        #region Public-Methods

        /// <summary>Returns the JavaScript and TypeScript skill definitions.</summary>
        /// <returns>The definitions.</returns>
        public static IReadOnlyList<DefaultSkillDef> All()
        {
            return new List<DefaultSkillDef>
            {
                Skill("js-install", "Install JavaScript dependencies", "Installs dependencies with the project's package manager (npm, pnpm, yarn, or bun).", true,
                    "Dependencies are missing (node_modules absent, a module cannot be resolved) or the lockfile changed.",
                    string.Empty,
                    "Use `ci` in automation or when the lockfile must not change; use `install` when adding or updating packages is acceptable.",
                    Command("install", "Install dependencies, updating the lockfile if needed.", @"Invoke-MuxTool -Tool $pm -Arguments @('install') -InstallHint (Get-MuxNodeInstallHint $pm)
"),
                    Command("ci", "Install exactly what the lockfile pins (frozen lockfile).", @"switch ($pm) {
    'npm' { $arguments = @('ci') }
    'pnpm' { $arguments = @('install', '--frozen-lockfile') }
    'yarn' { $arguments = if (Test-Path -LiteralPath (Join-Path $dir '.yarnrc.yml')) { @('install', '--immutable') } else { @('install', '--frozen-lockfile') } }
    'bun' { $arguments = @('install', '--frozen-lockfile') }
}
Invoke-MuxTool -Tool $pm -Arguments $arguments -InstallHint (Get-MuxNodeInstallHint $pm)
")),

                Skill("js-build", "Build the JavaScript project", "Runs the project's build script.", true,
                    "The user asks to build, compile, or bundle a JavaScript or TypeScript project.",
                    string.Empty,
                    "Runs the `build` script from package.json through the detected package manager. Projects without a build script exit 2; check `js-scripts list` for the right name.",
                    Command("build", "Run the build script.", @"if (-not (Test-MuxPackageScript $pkg 'build')) { Exit-MuxNotApplicable 'package.json has no build script. Run js-scripts list to see the available scripts.' }
Invoke-MuxPackageScript -Manager $pm -Script 'build' -Extra @($args)
")),

                Skill("js-test", "Run the JavaScript tests", "Runs Vitest, Jest, Mocha, or node --test in non-watch mode.", false,
                    "The user asks to run, filter, or measure coverage of JavaScript or TypeScript tests.",
                    "[filter]",
                    "`all` runs the project's test script (forced out of watch mode with CI=1). `filter <pattern>` runs only matching test files or names, and `coverage` adds coverage. The runner is detected from devDependencies: Vitest, Jest, Mocha, then node's built-in runner.",
                    Command("all", "Run every test.", TestRunner + @"if ((Test-MuxPackageScript $pkg 'test') -and ($pkg['scripts']['test'] -notmatch 'no test specified')) {
    Invoke-MuxPackageScript -Manager $pm -Script 'test'
} else {
    Invoke-MuxTestRunner -Mode 'all'
}
"),
                    Command("filter", "Run tests matching a file or name pattern.", TestRunner + @"$filter = Get-MuxArg -Arguments $args -Index 0
if (-not $filter) { Exit-MuxNotApplicable 'pass a pattern: js-test filter <pattern>' }
Invoke-MuxTestRunner -Mode 'filter' -Filter $filter
"),
                    Command("coverage", "Run every test with coverage.", TestRunner + @"Invoke-MuxTestRunner -Mode 'coverage'
")),

                Skill("js-lint", "Lint the JavaScript code", "Checks or fixes lint problems with ESLint or Biome.", true,
                    "The user asks to lint, or to fix lint errors, in a JavaScript or TypeScript project.",
                    string.Empty,
                    "`check` reports problems and exits 1 when there are any; `fix` applies safe automatic fixes. Biome is used when it is configured, otherwise ESLint; the project's `lint` script wins for `check`.",
                    Command("check", "Report lint problems.", Linter + @"if (Test-MuxPackageScript $pkg 'lint') { Invoke-MuxPackageScript -Manager $pm -Script 'lint'; exit 0 }
if ($linter -eq 'biome') { Invoke-MuxPackageBin -Manager $pm -Bin 'biome' -Arguments @('lint', '.') } else { Invoke-MuxPackageBin -Manager $pm -Bin 'eslint' -Arguments @('.') }
"),
                    Command("fix", "Apply automatic lint fixes.", Linter + @"if (Test-MuxPackageScript $pkg 'lint:fix') { Invoke-MuxPackageScript -Manager $pm -Script 'lint:fix'; exit 0 }
if ($linter -eq 'biome') { Invoke-MuxPackageBin -Manager $pm -Bin 'biome' -Arguments @('lint', '--write', '.') } else { Invoke-MuxPackageBin -Manager $pm -Bin 'eslint' -Arguments @('.', '--fix') }
")),

                Skill("js-typecheck", "Type-check the TypeScript code", "Runs the TypeScript compiler without emitting files.", false,
                    "The user asks whether the TypeScript code type-checks, or a change might have broken types.",
                    string.Empty,
                    "Runs the `typecheck` (or `type-check`) script when there is one, otherwise `tsc --noEmit`. A project without tsconfig.json has nothing to check and exits 0 with a note.",
                    Command("check", "Type-check without emitting.", @"foreach ($name in @('typecheck', 'type-check', 'check-types', 'tsc')) {
    if (Test-MuxPackageScript $pkg $name) { Invoke-MuxPackageScript -Manager $pm -Script $name; exit 0 }
}
if (-not (Test-Path -LiteralPath (Join-Path $dir 'tsconfig.json'))) { Write-Output 'mux: no tsconfig.json; nothing to type-check.'; exit 0 }
Invoke-MuxPackageBin -Manager $pm -Bin 'tsc' -Arguments @('--noEmit', '-p', '.')
")),

                Skill("js-format", "Format the JavaScript code", "Applies or verifies formatting with Prettier or Biome.", true,
                    "The user asks to format code, or to check formatting before committing.",
                    string.Empty,
                    "`apply` rewrites files in place; `verify` exits 1 when any file would change. Biome is used when configured, otherwise Prettier; `format` and `format:check` scripts win when present.",
                    Command("apply", "Format files in place.", Formatter + @"if (Test-MuxPackageScript $pkg 'format') { Invoke-MuxPackageScript -Manager $pm -Script 'format'; exit 0 }
if ($formatter -eq 'biome') { Invoke-MuxPackageBin -Manager $pm -Bin 'biome' -Arguments @('format', '--write', '.') } else { Invoke-MuxPackageBin -Manager $pm -Bin 'prettier' -Arguments @('--write', '.') }
"),
                    Command("verify", "Fail if any file is not formatted.", Formatter + @"foreach ($name in @('format:check', 'check-format', 'format-check')) { if (Test-MuxPackageScript $pkg $name) { Invoke-MuxPackageScript -Manager $pm -Script $name; exit 0 } }
if ($formatter -eq 'biome') { Invoke-MuxPackageBin -Manager $pm -Bin 'biome' -Arguments @('format', '.') } else { Invoke-MuxPackageBin -Manager $pm -Bin 'prettier' -Arguments @('--check', '.') }
")),

                Skill("js-deps", "Inspect JavaScript dependencies", "Lists outdated or vulnerable packages, or explains why a package is installed.", false,
                    "The user asks which packages are outdated or vulnerable, or why a package is in node_modules.",
                    "[package]",
                    "`outdated` and `audit` exit 1 when they find something, which is a report, not a crash. `why <package>` shows the dependency chain that pulls a package in.",
                    Command("outdated", "List outdated packages.", @"Invoke-MuxTool -Tool $pm -Arguments @('outdated') -InstallHint (Get-MuxNodeInstallHint $pm)
"),
                    Command("audit", "List packages with known vulnerabilities.", @"if ($pm -eq 'bun') { Invoke-MuxTool -Tool 'bun' -Arguments @('audit') } else { Invoke-MuxTool -Tool $pm -Arguments @('audit') -InstallHint (Get-MuxNodeInstallHint $pm) }
"),
                    Command("why", "Explain why a package is installed.", @"$name = Get-MuxArg -Arguments $args -Index 0
if (-not $name) { Exit-MuxNotApplicable 'pass a package name: js-deps why <package>' }
switch ($pm) {
    'npm' { Invoke-MuxTool -Tool 'npm' -Arguments @('explain', $name) }
    'bun' { Invoke-MuxTool -Tool 'bun' -Arguments @('why', $name) }
    default { Invoke-MuxTool -Tool $pm -Arguments @('why', $name) -InstallHint (Get-MuxNodeInstallHint $pm) }
}
")),

                Skill("js-scripts", "Run package.json scripts", "Lists the project's package.json scripts or runs one.", true,
                    "The user asks to run a project script (dev tasks, codegen, migrations) or wants to know which scripts exist.",
                    "[script] [args...]",
                    "`list` prints every script with its command. `run <name> [args...]` runs one through the detected package manager. Long-running scripts such as dev servers will hit the command timeout; ask the user to run those themselves for now.",
                    Command("list", "List the scripts in package.json.", @"if (-not $pkg.ContainsKey('scripts') -or $pkg['scripts'].Count -eq 0) { Write-Output 'package.json has no scripts.'; exit 0 }
Write-Output ('Package manager: ' + $pm)
foreach ($entry in $pkg['scripts'].GetEnumerator() | Sort-Object Key) { Write-Output ('  ' + $entry.Key + ': ' + $entry.Value) }
"),
                    Command("run", "Run one script with optional arguments.", @"$name = Get-MuxArg -Arguments $args -Index 0
if (-not $name) { Exit-MuxNotApplicable 'pass a script name: js-scripts run <name> [args...]' }
if (-not (Test-MuxPackageScript $pkg $name)) { Exit-MuxNotApplicable ('package.json has no script named ' + $name + '. Run js-scripts list.') }
$extra = @($args | Select-Object -Skip 1)
Invoke-MuxPackageScript -Manager $pm -Script $name -Extra $extra
"))
            };
        }

        #endregion

        #region Private-Methods

        private const string TestRunner = @"$env:CI = '1'
function Get-MuxJsTestRunner {
    foreach ($runner in @('vitest', 'jest', 'mocha')) { if (Test-MuxPackageDependency $pkg $runner) { return $runner } }
    return 'node'
}
function Invoke-MuxTestRunner([string]$Mode, [string]$Filter = '') {
    $runner = Get-MuxJsTestRunner
    switch ($runner) {
        'vitest' {
            $a = @('run')
            if ($Mode -eq 'filter') { $a += $Filter }
            if ($Mode -eq 'coverage') { $a += '--coverage' }
            Invoke-MuxPackageBin -Manager $pm -Bin 'vitest' -Arguments $a
        }
        'jest' {
            $a = @('--ci')
            if ($Mode -eq 'filter') { $a += $Filter }
            if ($Mode -eq 'coverage') { $a += '--coverage' }
            Invoke-MuxPackageBin -Manager $pm -Bin 'jest' -Arguments $a
        }
        'mocha' {
            $a = @()
            if ($Mode -eq 'filter') { $a += @('--grep', $Filter) }
            if ($Mode -eq 'coverage') { Invoke-MuxPackageBin -Manager $pm -Bin 'c8' -Arguments (@('mocha') + $a) } else { Invoke-MuxPackageBin -Manager $pm -Bin 'mocha' -Arguments $a }
        }
        default {
            $a = @('--test')
            if ($Mode -eq 'filter') { $a += ('--test-name-pattern=' + $Filter) }
            if ($Mode -eq 'coverage') { $a += '--experimental-test-coverage' }
            Invoke-MuxTool -Tool 'node' -Arguments $a -InstallHint 'Install Node.js from https://nodejs.org.'
        }
    }
}
";

        private const string Linter = @"$linter = 'eslint'
if ((Test-MuxPackageDependency $pkg '@biomejs/biome') -or (Test-Path -LiteralPath (Join-Path $dir 'biome.json')) -or (Test-Path -LiteralPath (Join-Path $dir 'biome.jsonc'))) { $linter = 'biome' }
elseif (-not (Test-MuxPackageDependency $pkg 'eslint') -and -not (Test-MuxPackageScript $pkg 'lint') -and -not (Get-ChildItem -LiteralPath $dir -Filter 'eslint.config.*' -ErrorAction SilentlyContinue) -and -not (Get-ChildItem -LiteralPath $dir -Filter '.eslintrc*' -Force -ErrorAction SilentlyContinue)) {
    Exit-MuxNotApplicable 'no linter is configured (no ESLint or Biome dependency, config, or lint script).'
}
";

        private const string Formatter = @"$formatter = 'prettier'
if ((Test-MuxPackageDependency $pkg '@biomejs/biome') -or (Test-Path -LiteralPath (Join-Path $dir 'biome.json')) -or (Test-Path -LiteralPath (Join-Path $dir 'biome.jsonc'))) { $formatter = 'biome' }
elseif (-not (Test-MuxPackageDependency $pkg 'prettier') -and -not (Test-MuxPackageScript $pkg 'format') -and -not (Get-ChildItem -LiteralPath $dir -Filter '.prettierrc*' -Force -ErrorAction SilentlyContinue) -and -not (Get-ChildItem -LiteralPath $dir -Filter 'prettier.config.*' -ErrorAction SilentlyContinue)) {
    Exit-MuxNotApplicable 'no formatter is configured (no Prettier or Biome dependency, config, or format script).'
}
";

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
                Tags = new List<string> { "javascript", "typescript", "node" },
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
