namespace Mux.Core.Skills
{
    using System.Collections.Generic;

    /// <summary>
    /// The Java default skills for Maven and Gradle projects. The build tool is detected per command: the Maven or
    /// Gradle wrapper wins (it pins the version the project tested with), then <c>pom.xml</c> with <c>mvn</c>, then
    /// a Gradle build file with <c>gradle</c>. Commands follow the toolchain conventions in
    /// <see cref="DefaultSkillHelpers"/>.
    /// </summary>
    public static class DefaultJavaSkills
    {
        #region Private-Members

        private static readonly List<string> _AppliesTo = new List<string> { "pom.xml", "build.gradle", "build.gradle.kts", "mvnw", "gradlew" };

        private const string Setup = @"$dir = Get-MuxJavaProject
$build = Get-MuxJavaBuild -Dir $dir
Set-Location -LiteralPath $dir
";

        #endregion

        #region Public-Methods

        /// <summary>Returns the Java skill definitions.</summary>
        /// <returns>The definitions.</returns>
        public static IReadOnlyList<DefaultSkillDef> All()
        {
            return new List<DefaultSkillDef>
            {
                Skill("java-build", "Build the Java project", "Compiles, packages, or cleans a Maven or Gradle project.", true,
                    "The user asks to compile, build, package, or clean a Java or JVM project.",
                    string.Empty,
                    "`compile` compiles main sources, `package` produces the artifact without running tests (use java-test for those), and `clean` removes build output.",
                    Command("compile", "Compile the main sources.", @"Invoke-MuxJavaBuild -Build $build -MavenArguments @('-q', 'compile') -GradleArguments @('classes')
"),
                    Command("package", "Build the artifact, skipping tests.", @"Invoke-MuxJavaBuild -Build $build -MavenArguments @('-q', 'package', '-DskipTests') -GradleArguments @('assemble')
"),
                    Command("clean", "Remove build output.", @"Invoke-MuxJavaBuild -Build $build -MavenArguments @('-q', 'clean') -GradleArguments @('clean')
")),

                Skill("java-test", "Run the Java tests", "Runs JUnit tests through Maven Surefire or Gradle, all or filtered.", false,
                    "The user asks to run Java tests, or one test class or method.",
                    "[Class or Class#method]",
                    "`all` runs every test. `filter <pattern>` runs matching tests: Maven takes `-Dtest=` patterns such as `UserServiceTest` or `UserServiceTest#saves*`; Gradle takes `--tests` patterns such as `com.example.UserServiceTest`. Failing runs exit 1; the reports are under target/surefire-reports or build/reports/tests.",
                    Command("all", "Run every test.", @"Invoke-MuxJavaBuild -Build $build -MavenArguments @('test') -GradleArguments @('test')
"),
                    Command("filter", "Run tests matching a pattern.", @"$filter = Get-MuxArg -Arguments $args -Index 0
if (-not $filter) { Exit-MuxNotApplicable 'pass a pattern: java-test filter <Class or Class#method>' }
Invoke-MuxJavaBuild -Build $build -MavenArguments @('test', ('-Dtest=' + $filter), '-Dsurefire.failIfNoSpecifiedTests=false') -GradleArguments @('test', '--tests', $filter)
")),

                Skill("java-format", "Format the Java code", "Applies or verifies formatting with Spotless.", true,
                    "The user asks to format Java code or check formatting.",
                    string.Empty,
                    "Uses the Spotless plugin configured in the build. A build without Spotless exits 2; add it (com.diffplug.spotless) to get consistent formatting.",
                    Command("apply", "Format files in place.", @"if (-not (Test-MuxBuildFileMentions $build 'spotless')) { Exit-MuxNotApplicable 'Spotless is not configured in the build file (add com.diffplug.spotless).' }
Invoke-MuxJavaBuild -Build $build -MavenArguments @('-q', 'spotless:apply') -GradleArguments @('spotlessApply')
"),
                    Command("verify", "Fail if any file is not formatted.", @"if (-not (Test-MuxBuildFileMentions $build 'spotless')) { Exit-MuxNotApplicable 'Spotless is not configured in the build file (add com.diffplug.spotless).' }
Invoke-MuxJavaBuild -Build $build -MavenArguments @('-q', 'spotless:check') -GradleArguments @('spotlessCheck')
")),

                Skill("java-lint", "Run Java static analysis", "Runs whichever of Checkstyle, SpotBugs, and PMD the build configures.", false,
                    "The user asks for static analysis or lint results on Java code.",
                    string.Empty,
                    "`check` runs each configured analyzer in turn and exits 1 when any reports problems. A build with none of them exits 2.",
                    Command("check", "Run the configured analyzers.", @"$ran = $false
$failed = $false
foreach ($tool in @('checkstyle', 'spotbugs', 'pmd')) {
    if (-not (Test-MuxBuildFileMentions $build $tool)) { continue }
    $ran = $true
    $maven = @{ checkstyle = 'checkstyle:check'; spotbugs = 'spotbugs:check'; pmd = 'pmd:check' }[$tool]
    $gradle = @{ checkstyle = 'checkstyleMain'; spotbugs = 'spotbugsMain'; pmd = 'pmdMain' }[$tool]
    Invoke-MuxJavaBuild -Build $build -MavenArguments @('-q', $maven) -GradleArguments @($gradle) -AllowFailure
    if ($script:MuxLastExit -ne 0) { $failed = $true }
}
if (-not $ran) { Exit-MuxNotApplicable 'no Checkstyle, SpotBugs, or PMD configuration found in the build file.' }
if ($failed) { exit 1 }
")),

                Skill("java-deps", "Inspect Java dependencies", "Prints the dependency tree or available dependency updates.", false,
                    "The user asks where a dependency comes from or which dependencies have newer versions.",
                    string.Empty,
                    "`tree` prints the resolved dependency tree. `updates` uses the versions-maven-plugin on Maven, or the ben-manes versions plugin on Gradle (exits 2 when the Gradle plugin is not applied).",
                    Command("tree", "Print the dependency tree.", @"Invoke-MuxJavaBuild -Build $build -MavenArguments @('-q', 'dependency:tree') -GradleArguments @('dependencies', '--configuration', 'runtimeClasspath')
"),
                    Command("updates", "List dependencies with newer versions.", @"if ($build.Kind -eq 'gradle' -and -not (Test-MuxBuildFileMentions $build 'com.github.ben-manes.versions')) { Exit-MuxNotApplicable 'apply the com.github.ben-manes.versions Gradle plugin to list updates.' }
Invoke-MuxJavaBuild -Build $build -MavenArguments @('-q', 'versions:display-dependency-updates') -GradleArguments @('dependencyUpdates')
")),

                Skill("java-new-class", "Scaffold a Java type", "Creates a class, interface, record, or enum in the standard source layout, with a JUnit test for classes.", true,
                    "The user asks for a new Java class, interface, record, or enum.",
                    "<com.example.Name> [class|interface|record|enum]",
                    "`create <fully.qualified.Name> [kind]` writes `src/main/java/<package path>/<Name>.java` and, for a class when JUnit is in the build, a matching test under `src/test/java`. Existing files are never overwritten.",
                    Command("create", "Create the type (and a test for classes).", @"$fqcn = Get-MuxArg -Arguments $args -Index 0
$kind = Get-MuxArg -Arguments $args -Index 1 -Default 'class'
if ($fqcn -cnotmatch '^([a-z_][a-z0-9_]*\.)*[A-Z][A-Za-z0-9_]*$') { Exit-MuxNotApplicable 'pass a fully qualified name like com.example.UserService: java-new-class create <name> [kind]' }
if ($kind -notin @('class', 'interface', 'record', 'enum')) { Exit-MuxNotApplicable 'kind must be class, interface, record, or enum.' }
$lastDot = $fqcn.LastIndexOf('.')
$package = if ($lastDot -gt 0) { $fqcn.Substring(0, $lastDot) } else { '' }
$name = $fqcn.Substring($lastDot + 1)
$packagePath = $package -replace '\.', '/'
$header = if ($package) { 'package ' + $package + ';' + [Environment]::NewLine + [Environment]::NewLine } else { '' }
$body = switch ($kind) {
    'record' { 'public record ' + $name + '() {' + [Environment]::NewLine + '}' }
    'enum' { 'public enum ' + $name + ' {' + [Environment]::NewLine + '}' }
    'interface' { 'public interface ' + $name + ' {' + [Environment]::NewLine + '}' }
    default { 'public class ' + $name + ' {' + [Environment]::NewLine + '}' }
}
New-MuxFile -Path (Join-Path 'src/main/java' (Join-Path $packagePath ($name + '.java'))) -Content ($header + $body + [Environment]::NewLine)
if ($kind -eq 'class' -and (Test-MuxBuildFileMentions $build 'junit')) {
    $junit5 = Test-MuxBuildFileMentions $build 'junit-jupiter'
    $imports = if ($junit5) { 'import org.junit.jupiter.api.Test;' + [Environment]::NewLine + 'import static org.junit.jupiter.api.Assertions.assertNotNull;' } else { 'import org.junit.Test;' + [Environment]::NewLine + 'import static org.junit.Assert.assertNotNull;' }
    $test = $header + $imports + [Environment]::NewLine + [Environment]::NewLine + 'public class ' + $name + 'Test {' + [Environment]::NewLine +
        '    @Test' + [Environment]::NewLine + '    public void createsInstance() {' + [Environment]::NewLine +
        '        assertNotNull(new ' + $name + '());' + [Environment]::NewLine + '    }' + [Environment]::NewLine + '}' + [Environment]::NewLine
    New-MuxFile -Path (Join-Path 'src/test/java' (Join-Path $packagePath ($name + 'Test.java'))) -Content $test
}
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
                Tags = new List<string> { "java", "jvm" },
                WhenToUse = whenToUse,
                AppliesTo = new List<string>(_AppliesTo),
                ArgumentHint = argumentHint,
                Body = body + " Exit codes: 0 success, 1 the build reported problems, 2 the tool or project is missing.",
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
