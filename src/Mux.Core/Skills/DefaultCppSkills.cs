namespace Mux.Core.Skills
{
    using System.Collections.Generic;

    /// <summary>
    /// The C and C++ default skills. CMake is the main path (presets win when CMakePresets.json exists, and every
    /// configure exports compile_commands.json so clang-tidy works); Meson and plain Make projects get build and
    /// test support. Build trees live under <c>build/&lt;config&gt;</c>. Commands follow the toolchain conventions
    /// in <see cref="DefaultSkillHelpers"/>.
    /// </summary>
    public static class DefaultCppSkills
    {
        #region Private-Members

        private static readonly List<string> _AppliesTo = new List<string> { "CMakeLists.txt", "CMakePresets.json", "Makefile", "meson.build" };

        private const string Setup = @"$dir = Get-MuxCppProject
$system = Get-MuxCppBuildSystem -Dir $dir
Set-Location -LiteralPath $dir
$cmakeHint = 'Install CMake from https://cmake.org.'
";

        #endregion

        #region Public-Methods

        /// <summary>Returns the C and C++ skill definitions.</summary>
        /// <returns>The definitions.</returns>
        public static IReadOnlyList<DefaultSkillDef> All()
        {
            return new List<DefaultSkillDef>
            {
                Skill("cpp-configure", "Configure the C++ build", "Configures a CMake (or Meson) build tree with compile_commands.json exported.", true,
                    "Before the first build, after changing CMakeLists.txt, or to switch between Debug and Release.",
                    "[preset]",
                    "`debug` and `release` configure `build/debug` or `build/release`; `preset <name>` uses a configure preset from CMakePresets.json. Meson projects get `meson setup`. Plain Make projects have nothing to configure and exit 2.",
                    Command("debug", "Configure a Debug build tree.", @"switch ($system) {
    'cmake' { Invoke-MuxTool -Tool 'cmake' -Arguments @('-S', '.', '-B', 'build/debug', '-DCMAKE_BUILD_TYPE=Debug', '-DCMAKE_EXPORT_COMPILE_COMMANDS=ON') -InstallHint $cmakeHint }
    'meson' { Invoke-MuxTool -Tool 'meson' -Arguments @('setup', 'build/debug', '--buildtype=debug') -InstallHint 'Install Meson: pip install meson ninja.' }
    default { Exit-MuxNotApplicable 'this is a plain Makefile project; there is nothing to configure.' }
}
"),
                    Command("release", "Configure a Release build tree.", @"switch ($system) {
    'cmake' { Invoke-MuxTool -Tool 'cmake' -Arguments @('-S', '.', '-B', 'build/release', '-DCMAKE_BUILD_TYPE=Release', '-DCMAKE_EXPORT_COMPILE_COMMANDS=ON') -InstallHint $cmakeHint }
    'meson' { Invoke-MuxTool -Tool 'meson' -Arguments @('setup', 'build/release', '--buildtype=release') -InstallHint 'Install Meson: pip install meson ninja.' }
    default { Exit-MuxNotApplicable 'this is a plain Makefile project; there is nothing to configure.' }
}
"),
                    Command("preset", "Configure with a CMake preset.", @"if (-not (Test-Path -LiteralPath 'CMakePresets.json')) { Exit-MuxNotApplicable 'no CMakePresets.json in the project.' }
$preset = Get-MuxArg -Arguments $args -Index 0
if (-not $preset) { Invoke-MuxTool -Tool 'cmake' -Arguments @('--list-presets') -InstallHint $cmakeHint; exit 0 }
Invoke-MuxTool -Tool 'cmake' -Arguments @('--preset', $preset) -InstallHint $cmakeHint
")),

                Skill("cpp-build", "Build the C++ project", "Builds with CMake, Meson, or Make using every processor core.", true,
                    "The user asks to build or compile a C or C++ project.",
                    "[debug|release]",
                    "`build [debug|release]` builds the matching tree under build/ (default debug). Configure it first with cpp-configure; an unconfigured CMake tree exits 2 with that instruction.",
                    Command("build", "Build the project.", @"$config = Get-MuxArg -Arguments $args -Index 0 -Default 'debug'
$jobs = [string](Get-MuxProcessorCount)
switch ($system) {
    'cmake' {
        $tree = Join-Path 'build' $config
        if (-not (Test-MuxDryRun) -and -not (Test-Path -LiteralPath (Join-Path $tree 'CMakeCache.txt'))) { Exit-MuxNotApplicable ('build/' + $config + ' is not configured; run cpp-configure ' + $config + ' first.') }
        Invoke-MuxTool -Tool 'cmake' -Arguments @('--build', $tree, '--parallel', $jobs) -InstallHint $cmakeHint
    }
    'meson' { Invoke-MuxTool -Tool 'meson' -Arguments @('compile', '-C', (Join-Path 'build' $config)) -InstallHint 'Install Meson: pip install meson ninja.' }
    default { Invoke-MuxTool -Tool 'make' -Arguments @(('-j' + $jobs)) -InstallHint 'Install make (build-essential, Xcode command line tools, or MSYS2).' }
}
")),

                Skill("cpp-test", "Run the C++ tests", "Runs CTest (or meson test) with output shown for failures.", false,
                    "The user asks to run C or C++ tests.",
                    "[regex]",
                    "`all` runs every test in build/debug; `filter <regex>` passes CTest's -R. Build first with cpp-build.",
                    Command("all", "Run every test.", @"switch ($system) {
    'cmake' { Invoke-MuxTool -Tool 'ctest' -Arguments @('--test-dir', 'build/debug', '--output-on-failure') -InstallHint $cmakeHint }
    'meson' { Invoke-MuxTool -Tool 'meson' -Arguments @('test', '-C', 'build/debug') }
    default { Invoke-MuxTool -Tool 'make' -Arguments @('test') }
}
"),
                    Command("filter", "Run tests matching a regular expression.", @"$filter = Get-MuxArg -Arguments $args -Index 0
if (-not $filter) { Exit-MuxNotApplicable 'pass a regex: cpp-test filter <regex>' }
if ($system -ne 'cmake') { Exit-MuxNotApplicable 'filtering needs CTest (a CMake project).' }
Invoke-MuxTool -Tool 'ctest' -Arguments @('--test-dir', 'build/debug', '--output-on-failure', '-R', $filter) -InstallHint $cmakeHint
")),

                Skill("cpp-format", "Format the C++ code", "Applies or verifies clang-format across the project's C and C++ sources.", true,
                    "The user asks to format C or C++ code or check formatting.",
                    string.Empty,
                    "Formats tracked .c, .cc, .cpp, .cxx, .h, .hh, .hpp, and .hxx files with the project's .clang-format. `verify` exits 1 when any file would change.",
                    Command("apply", "Format files in place.", @"$files = @(Get-MuxCppSources)
if ($files.Count -eq 0) { Write-Output 'mux: no C or C++ sources found.'; exit 0 }
Invoke-MuxTool -Tool 'clang-format' -Arguments (@('-i') + $files) -InstallHint 'Install clang-format (LLVM).'
"),
                    Command("verify", "Fail if any file is not formatted.", @"$files = @(Get-MuxCppSources)
if ($files.Count -eq 0) { Write-Output 'mux: no C or C++ sources found.'; exit 0 }
Invoke-MuxTool -Tool 'clang-format' -Arguments (@('--dry-run', '--Werror') + $files) -InstallHint 'Install clang-format (LLVM).'
")),

                Skill("cpp-tidy", "Run clang-tidy", "Runs clang-tidy against compile_commands.json, on every source or only changed files.", false,
                    "The user asks for static analysis of C or C++ code.",
                    "[changed]",
                    "`check` analyzes every tracked source file; pass `changed` to limit it to files changed from HEAD. Needs a configured tree with compile_commands.json (cpp-configure exports it).",
                    Command("check", "Run clang-tidy.", @"$db = @('build/debug', 'build/release', 'build') | Where-Object { Test-Path -LiteralPath (Join-Path $_ 'compile_commands.json') } | Select-Object -First 1
if (-not $db -and -not (Test-MuxDryRun)) { Exit-MuxNotApplicable 'no compile_commands.json; run cpp-configure debug first.' }
if (-not $db) { $db = 'build/debug' }
$changed = (Get-MuxArg -Arguments $args -Index 0) -eq 'changed'
$files = @(Get-MuxCppSources -ChangedOnly:$changed | Where-Object { $_ -notmatch '\.(h|hh|hpp|hxx)$' })
if ($files.Count -eq 0) { Write-Output 'mux: no C or C++ source files to analyze.'; exit 0 }
Invoke-MuxTool -Tool 'clang-tidy' -Arguments (@('-p', $db) + $files) -InstallHint 'Install clang-tidy (LLVM).'
")),

                Skill("cpp-sanitize", "Build and test with sanitizers", "Configures a separate build tree with AddressSanitizer or UndefinedBehaviorSanitizer, builds it, and runs CTest.", true,
                    "Chasing memory errors, crashes, or undefined behavior in C or C++ code.",
                    string.Empty,
                    "`asan` and `ubsan` each use their own tree (build/asan, build/ubsan) so normal builds are untouched. CMake projects only; GCC and Clang support both sanitizers, MSVC supports ASan only.",
                    Command("asan", "Build and test with AddressSanitizer.", SanitizerScript("asan", "-fsanitize=address -fno-omit-frame-pointer")),
                    Command("ubsan", "Build and test with UndefinedBehaviorSanitizer.", SanitizerScript("ubsan", "-fsanitize=undefined -fno-omit-frame-pointer")))
            };
        }

        #endregion

        #region Private-Methods

        private static string SanitizerScript(string tree, string flags)
        {
            return @"if ($system -ne 'cmake') { Exit-MuxNotApplicable 'sanitizer builds need a CMake project.' }
$flags = '" + flags + @"'
$tree = 'build/" + tree + @"'
Invoke-MuxTool -Tool 'cmake' -Arguments @('-S', '.', '-B', $tree, '-DCMAKE_BUILD_TYPE=Debug', ('-DCMAKE_C_FLAGS=' + $flags), ('-DCMAKE_CXX_FLAGS=' + $flags), ('-DCMAKE_EXE_LINKER_FLAGS=' + $flags), '-DCMAKE_EXPORT_COMPILE_COMMANDS=ON') -InstallHint $cmakeHint
Invoke-MuxTool -Tool 'cmake' -Arguments @('--build', $tree, '--parallel', [string](Get-MuxProcessorCount)) -InstallHint $cmakeHint
Invoke-MuxTool -Tool 'ctest' -Arguments @('--test-dir', $tree, '--output-on-failure') -InstallHint $cmakeHint
";
        }

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
                Tags = new List<string> { "cpp", "c", "cmake" },
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
