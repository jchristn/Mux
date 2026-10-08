namespace Mux.Core.Skills
{
    using System.Collections.Generic;

    /// <summary>
    /// The Python default skills. Every command finds the nearest Python project, picks the environment manager
    /// (uv, poetry, pipenv, or a project <c>.venv</c> with pip), and runs tools as <c>python -m &lt;tool&gt;</c>
    /// inside that environment, so a skill never falls back to the wrong interpreter. Commands follow the
    /// toolchain conventions in <see cref="DefaultSkillHelpers"/>.
    /// </summary>
    public static class DefaultPythonSkills
    {
        #region Private-Members

        private static readonly List<string> _AppliesTo = new List<string> { "pyproject.toml", "requirements*.txt", "setup.py", "setup.cfg", "Pipfile" };

        private const string Setup = @"$dir = Get-MuxPythonProject
$manager = Get-MuxPythonManager -Dir $dir
Set-Location -LiteralPath $dir
";

        #endregion

        #region Public-Methods

        /// <summary>Returns the Python skill definitions.</summary>
        /// <returns>The definitions.</returns>
        public static IReadOnlyList<DefaultSkillDef> All()
        {
            return new List<DefaultSkillDef>
            {
                Skill("py-env", "Inspect or create the Python environment", "Shows which interpreter and environment manager the project uses, or creates the environment.", true,
                    "Before running Python tools, when imports fail, or when the user asks which Python or virtual environment the project uses.",
                    "Run `info` first when Python behaves unexpectedly: most failures come from running the system interpreter instead of the project's environment. `create` makes the environment with the project's manager (uv venv, poetry install, pipenv install, or python -m venv .venv).",
                    Command("info", "Print the environment manager, interpreter, and version.", @"Write-Output ('Project: ' + $dir)
Write-Output ('Manager: ' + $manager)
$venv = Get-MuxVenvPython -Dir $dir
Write-Output ('Virtual environment: ' + $(if ($venv) { $venv } else { 'none (.venv not found)' }))
Invoke-MuxPython -Dir $dir -Manager $manager -Arguments @('--version')
"),
                    Command("create", "Create the project's environment.", @"switch ($manager) {
    'uv' { Invoke-MuxTool -Tool 'uv' -Arguments @('venv') -InstallHint 'Install uv from https://docs.astral.sh/uv/.' }
    'poetry' { Invoke-MuxTool -Tool 'poetry' -Arguments @('install') -InstallHint 'Install Poetry from https://python-poetry.org.' }
    'pipenv' { Invoke-MuxTool -Tool 'pipenv' -Arguments @('install', '--dev') -InstallHint 'Install pipenv: pip install --user pipenv.' }
    default { Invoke-MuxTool -Tool (Get-MuxSystemPython) -Arguments @('-m', 'venv', '.venv') -InstallHint 'Install Python 3 from https://www.python.org.' }
}
")),

                Skill("py-install", "Install Python dependencies", "Installs the project's dependencies, or adds a package, with its environment manager.", true,
                    "Dependencies are missing (ModuleNotFoundError) or the user asks to add a package.",
                    "`install` syncs the environment with the project's lockfile or requirements. `add <package>` uses the manager's add command so the lockfile or pyproject is updated; with plain pip it installs into .venv and reminds you to record it in requirements.",
                    Command("install", "Install the project's dependencies.", @"switch ($manager) {
    'uv' { Invoke-MuxTool -Tool 'uv' -Arguments @('sync') -InstallHint 'Install uv from https://docs.astral.sh/uv/.' }
    'poetry' { Invoke-MuxTool -Tool 'poetry' -Arguments @('install') -InstallHint 'Install Poetry from https://python-poetry.org.' }
    'pipenv' { Invoke-MuxTool -Tool 'pipenv' -Arguments @('install', '--dev') -InstallHint 'Install pipenv: pip install --user pipenv.' }
    default {
        if (-not (Get-MuxVenvPython -Dir $dir)) { Invoke-MuxTool -Tool (Get-MuxSystemPython) -Arguments @('-m', 'venv', '.venv') }
        $requirements = @(Get-ChildItem -LiteralPath $dir -Filter 'requirements*.txt' -ErrorAction SilentlyContinue | Sort-Object Name)
        if ($requirements.Count -gt 0) {
            foreach ($file in $requirements) { Invoke-MuxPython -Dir $dir -Manager $manager -Arguments @('-m', 'pip', 'install', '-r', $file.Name) }
        } elseif ((Test-Path -LiteralPath 'pyproject.toml') -or (Test-Path -LiteralPath 'setup.py')) {
            Invoke-MuxPython -Dir $dir -Manager $manager -Arguments @('-m', 'pip', 'install', '-e', '.')
        } else {
            Exit-MuxNotApplicable 'no requirements*.txt, pyproject.toml, or setup.py to install from.'
        }
    }
}
"),
                    Command("add", "Add a package to the project.", @"$package = Get-MuxArg -Arguments $args -Index 0
if (-not $package) { Exit-MuxNotApplicable 'pass a package: py-install add <package>' }
switch ($manager) {
    'uv' { Invoke-MuxTool -Tool 'uv' -Arguments @('add', $package) }
    'poetry' { Invoke-MuxTool -Tool 'poetry' -Arguments @('add', $package) }
    'pipenv' { Invoke-MuxTool -Tool 'pipenv' -Arguments @('install', $package) }
    default {
        Invoke-MuxPython -Dir $dir -Manager $manager -Arguments @('-m', 'pip', 'install', $package)
        Write-Output ('mux: installed into the environment only. Add ' + $package + ' to requirements.txt or pyproject.toml so it is recorded.')
    }
}
")),

                Skill("py-test", "Run the Python tests", "Runs pytest (or unittest when pytest is not installed) inside the project's environment.", false,
                    "The user asks to run, filter, re-run failures of, or measure coverage of Python tests.",
                    "`all` runs every test. `filter <expression>` passes pytest's -k. `last-failed` re-runs only what failed last time. `coverage` needs pytest-cov. Without pytest, `all` falls back to unittest discovery and the other commands exit 2.",
                    Command("all", "Run every test.", @"if (Test-MuxPythonModule -Dir $dir -Manager $manager -Module 'pytest') { Invoke-MuxPython -Dir $dir -Manager $manager -Arguments @('-m', 'pytest') }
else { Invoke-MuxPython -Dir $dir -Manager $manager -Arguments @('-m', 'unittest', 'discover') }
"),
                    Command("filter", "Run tests matching a pytest -k expression.", @"$expression = Get-MuxArg -Arguments $args -Index 0
if (-not $expression) { Exit-MuxNotApplicable 'pass an expression: py-test filter <expression>' }
if (-not (Test-MuxPythonModule -Dir $dir -Manager $manager -Module 'pytest')) { Exit-MuxNotApplicable 'pytest is not installed in the project environment.' }
Invoke-MuxPython -Dir $dir -Manager $manager -Arguments @('-m', 'pytest', '-k', $expression)
"),
                    Command("coverage", "Run every test with coverage.", @"if (-not (Test-MuxPythonModule -Dir $dir -Manager $manager -Module 'pytest_cov')) { Exit-MuxNotApplicable 'pytest-cov is not installed in the project environment.' }
Invoke-MuxPython -Dir $dir -Manager $manager -Arguments @('-m', 'pytest', '--cov', '--cov-report=term-missing')
"),
                    Command("last-failed", "Re-run only the tests that failed last time.", @"if (-not (Test-MuxPythonModule -Dir $dir -Manager $manager -Module 'pytest')) { Exit-MuxNotApplicable 'pytest is not installed in the project environment.' }
Invoke-MuxPython -Dir $dir -Manager $manager -Arguments @('-m', 'pytest', '--lf')
")),

                Skill("py-lint", "Lint the Python code", "Checks or fixes lint problems with Ruff, falling back to flake8.", true,
                    "The user asks to lint Python code or fix lint errors.",
                    "`check` reports problems and exits 1 when there are any; `fix` applies Ruff's safe fixes. flake8 can only check.",
                    Command("check", "Report lint problems.", @"if (Test-MuxPythonModule -Dir $dir -Manager $manager -Module 'ruff') { Invoke-MuxPython -Dir $dir -Manager $manager -Arguments @('-m', 'ruff', 'check', '.') }
elseif (Test-MuxPythonModule -Dir $dir -Manager $manager -Module 'flake8') { Invoke-MuxPython -Dir $dir -Manager $manager -Arguments @('-m', 'flake8') }
else { Exit-MuxNotApplicable 'neither ruff nor flake8 is installed in the project environment.' }
"),
                    Command("fix", "Apply automatic lint fixes.", @"if (-not (Test-MuxPythonModule -Dir $dir -Manager $manager -Module 'ruff')) { Exit-MuxNotApplicable 'automatic fixes need ruff in the project environment.' }
Invoke-MuxPython -Dir $dir -Manager $manager -Arguments @('-m', 'ruff', 'check', '--fix', '.')
")),

                Skill("py-format", "Format the Python code", "Applies or verifies formatting with Ruff, falling back to Black.", true,
                    "The user asks to format Python code or check formatting before committing.",
                    "`apply` rewrites files in place; `verify` exits 1 when any file would change.",
                    Command("apply", "Format files in place.", @"if (Test-MuxPythonModule -Dir $dir -Manager $manager -Module 'ruff') { Invoke-MuxPython -Dir $dir -Manager $manager -Arguments @('-m', 'ruff', 'format', '.') }
elseif (Test-MuxPythonModule -Dir $dir -Manager $manager -Module 'black') { Invoke-MuxPython -Dir $dir -Manager $manager -Arguments @('-m', 'black', '.') }
else { Exit-MuxNotApplicable 'neither ruff nor black is installed in the project environment.' }
"),
                    Command("verify", "Fail if any file is not formatted.", @"if (Test-MuxPythonModule -Dir $dir -Manager $manager -Module 'ruff') { Invoke-MuxPython -Dir $dir -Manager $manager -Arguments @('-m', 'ruff', 'format', '--check', '.') }
elseif (Test-MuxPythonModule -Dir $dir -Manager $manager -Module 'black') { Invoke-MuxPython -Dir $dir -Manager $manager -Arguments @('-m', 'black', '--check', '.') }
else { Exit-MuxNotApplicable 'neither ruff nor black is installed in the project environment.' }
")),

                Skill("py-typecheck", "Type-check the Python code", "Runs mypy or pyright, whichever the project configures.", false,
                    "The user asks whether Python type hints check out, or a change might have broken types.",
                    "Uses pyright when pyrightconfig.json or [tool.pyright] exists, otherwise mypy. Exits 2 when neither is installed.",
                    Command("check", "Run the configured type checker.", @"$pyproject = if (Test-Path -LiteralPath 'pyproject.toml') { Get-Content -LiteralPath 'pyproject.toml' -Raw } else { '' }
$usePyright = (Test-Path -LiteralPath 'pyrightconfig.json') -or ($pyproject -match '(?m)^\[tool\.pyright\]')
if ($usePyright -and (Test-MuxPythonModule -Dir $dir -Manager $manager -Module 'pyright')) { Invoke-MuxPython -Dir $dir -Manager $manager -Arguments @('-m', 'pyright') }
elseif (Test-MuxPythonModule -Dir $dir -Manager $manager -Module 'mypy') { Invoke-MuxPython -Dir $dir -Manager $manager -Arguments @('-m', 'mypy', '.') }
else { Exit-MuxNotApplicable 'neither mypy nor pyright is installed in the project environment.' }
")),

                Skill("py-deps", "Inspect Python dependencies", "Lists outdated packages or known vulnerabilities in the project's environment.", false,
                    "The user asks which Python packages are outdated or vulnerable.",
                    "`outdated` uses the manager's own report where it has one. `audit` needs pip-audit in the environment.",
                    Command("outdated", "List outdated packages.", @"switch ($manager) {
    'uv' { Invoke-MuxTool -Tool 'uv' -Arguments @('pip', 'list', '--outdated') }
    'poetry' { Invoke-MuxTool -Tool 'poetry' -Arguments @('show', '--outdated') }
    'pipenv' { Invoke-MuxTool -Tool 'pipenv' -Arguments @('update', '--outdated') -AllowFailure }
    default { Invoke-MuxPython -Dir $dir -Manager $manager -Arguments @('-m', 'pip', 'list', '--outdated') }
}
"),
                    Command("audit", "List packages with known vulnerabilities.", @"if (-not (Test-MuxPythonModule -Dir $dir -Manager $manager -Module 'pip_audit')) { Exit-MuxNotApplicable 'pip-audit is not installed in the project environment (pip install pip-audit).' }
Invoke-MuxPython -Dir $dir -Manager $manager -Arguments @('-m', 'pip_audit')
"))
            };
        }

        #endregion

        #region Private-Methods

        private static DefaultSkillDef Skill(string id, string title, string description, bool mutating, string whenToUse, string body, params DefaultSkillCommandDef[] commands)
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
                Tags = new List<string> { "python" },
                WhenToUse = whenToUse,
                AppliesTo = new List<string>(_AppliesTo),
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
