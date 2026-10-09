namespace Mux.Core.Skills
{
    using System.Collections.Generic;

    /// <summary>
    /// Toolchain skills for Ruby, PHP, Swift, Android, and Flutter (or plain Dart). Each family finds the nearest project
    /// file, runs the ecosystem's standard tools, and follows the toolchain exit-code conventions.
    /// </summary>
    public static class DefaultMobileLanguageSkills
    {
        #region Private-Members

        private const string RubySetup = @"$dir = Find-MuxUp -Names @('Gemfile')
if (-not $dir) { Exit-MuxNotApplicable 'no Gemfile found; this is not a Ruby project.' }
Set-Location -LiteralPath $dir
$rubyHint = 'Install Ruby and Bundler (gem install bundler).'
$usesRspec = (Test-Path -LiteralPath 'spec' -PathType Container) -or ((Test-Path -LiteralPath 'Gemfile.lock') -and (Select-String -LiteralPath 'Gemfile.lock' -Pattern '^\s+rspec-core\b' -Quiet))
$isRails = Test-Path -LiteralPath 'bin/rails'
";

        private const string PhpSetup = @"$dir = Find-MuxUp -Names @('composer.json')
if (-not $dir) { Exit-MuxNotApplicable 'no composer.json found; this is not a PHP project.' }
Set-Location -LiteralPath $dir
$phpHint = 'Install PHP and Composer (https://getcomposer.org).'
function Get-MuxPhpBin {
    param([string]$Name)
    foreach ($candidate in @(('vendor/bin/' + $Name + '.bat'), ('vendor/bin/' + $Name))) {
        if (($candidate.EndsWith('.bat') -and -not $IsWindows)) { continue }
        if (Test-Path -LiteralPath $candidate) { return (Join-Path (Get-Location).Path $candidate) }
    }
    return $null
}
";

        private const string SwiftSetup = @"$dir = Find-MuxUp -Names @('Package.swift', '*.xcworkspace', '*.xcodeproj')
if (-not $dir) { Exit-MuxNotApplicable 'no Package.swift or Xcode project found; this is not a Swift project.' }
Set-Location -LiteralPath $dir
$isPackage = Test-Path -LiteralPath 'Package.swift'
$swiftHint = 'Install the Swift toolchain (https://swift.org/install) or Xcode.'
";

        private const string AndroidSetup = @"$dir = Find-MuxUp -Names @('gradlew', 'settings.gradle', 'settings.gradle.kts')
if (-not $dir) { Exit-MuxNotApplicable 'no Gradle wrapper or settings.gradle found; this is not an Android Gradle project.' }
Set-Location -LiteralPath $dir
$gradle = if ($IsWindows -and (Test-Path -LiteralPath 'gradlew.bat')) { Join-Path $dir 'gradlew.bat' } elseif (Test-Path -LiteralPath 'gradlew') { Join-Path $dir 'gradlew' } else { 'gradle' }
$androidHint = 'Use the Gradle wrapper (gradlew) with the Android SDK installed and ANDROID_HOME set.'
";

        private const string FlutterSetup = @"$dir = Find-MuxUp -Names @('pubspec.yaml')
if (-not $dir) { Exit-MuxNotApplicable 'no pubspec.yaml found; this is not a Flutter or Dart project.' }
Set-Location -LiteralPath $dir
$isFlutter = [bool](Select-String -LiteralPath 'pubspec.yaml' -Pattern '^\s*sdk:\s*flutter\b' -Quiet)
$tool = if ($isFlutter) { 'flutter' } else { 'dart' }
$flutterHint = if ($isFlutter) { 'Install Flutter (https://docs.flutter.dev/get-started/install).' } else { 'Install the Dart SDK (https://dart.dev/get-dart).' }
";

        #endregion

        #region Public-Methods

        /// <summary>Returns the Ruby, PHP, Swift, Android, and Flutter skill definitions.</summary>
        /// <returns>The definitions.</returns>
        public static IReadOnlyList<DefaultSkillDef> All()
        {
            ToolchainSkillFactory ruby = new ToolchainSkillFactory(RubySetup, new[] { "ruby" }, new[] { "Gemfile" }, null);
            ToolchainSkillFactory php = new ToolchainSkillFactory(PhpSetup, new[] { "php" }, new[] { "composer.json" }, null);
            ToolchainSkillFactory swift = new ToolchainSkillFactory(SwiftSetup, new[] { "swift", "ios" }, new[] { "Package.swift", "*.xcodeproj", "*.xcworkspace" }, null);
            ToolchainSkillFactory android = new ToolchainSkillFactory(AndroidSetup, new[] { "android" }, new[] { "**/AndroidManifest.xml" }, null);
            ToolchainSkillFactory flutter = new ToolchainSkillFactory(FlutterSetup, new[] { "flutter", "dart" }, new[] { "pubspec.yaml" }, null);

            return new List<DefaultSkillDef>
            {
                ruby.Skill("ruby-deps", "Manage Ruby gems", "Ruby: installs the Gemfile's gems with Bundler, or lists outdated gems.", true,
                    "The user asks to install Ruby dependencies or see which gems are out of date.", string.Empty,
                    "`install` runs `bundle install`; `outdated` runs `bundle outdated --strict`.",
                    ToolchainSkillFactory.Command("install", "Install the gems.", @"Invoke-MuxTool -Tool 'bundle' -Arguments @('install') -InstallHint $rubyHint"),
                    ToolchainSkillFactory.Command("outdated", "List outdated gems.", @"Invoke-MuxTool -Tool 'bundle' -Arguments @('outdated', '--strict') -InstallHint $rubyHint")),
                ruby.Skill("ruby-test", "Run the Ruby tests", "Ruby: runs RSpec, or Minitest through rails test or rake test, for everything or a name filter.", false,
                    "The user asks to run Ruby or Rails tests.", "[pattern]",
                    "`all` runs `bundle exec rspec` when the project uses RSpec, otherwise `bin/rails test` for Rails or `bundle exec rake test`; `filter <pattern>` passes `-e` to RSpec or `-n /pattern/` to Minitest.",
                    ToolchainSkillFactory.Command("all", "Run every test.", @"if ($usesRspec) { Invoke-MuxTool -Tool 'bundle' -Arguments @('exec', 'rspec') -InstallHint $rubyHint }
elseif ($isRails) { Invoke-MuxTool -Tool 'ruby' -Arguments @('bin/rails', 'test') -InstallHint $rubyHint }
else { Invoke-MuxTool -Tool 'bundle' -Arguments @('exec', 'rake', 'test') -InstallHint $rubyHint }"),
                    ToolchainSkillFactory.Command("filter", "Run tests matching a name.", @"$pattern = Get-MuxArg -Arguments $args -Index 0
if (-not $pattern) { Exit-MuxNotApplicable 'pass a test name: ruby-test filter <pattern>' }
if ($usesRspec) { Invoke-MuxTool -Tool 'bundle' -Arguments @('exec', 'rspec', '-e', $pattern) -InstallHint $rubyHint }
elseif ($isRails) { Invoke-MuxTool -Tool 'ruby' -Arguments @('bin/rails', 'test', '-n', ('/' + $pattern + '/')) -InstallHint $rubyHint }
else { Invoke-MuxTool -Tool 'bundle' -Arguments @('exec', 'rake', 'test', ('TESTOPTS=-n /' + $pattern + '/')) -InstallHint $rubyHint }")),
                ruby.Skill("ruby-lint", "Lint the Ruby code", "Ruby: checks style and correctness with RuboCop, or applies its safe corrections.", true,
                    "The user asks to lint or format Ruby code.", string.Empty,
                    "`check` runs `bundle exec rubocop`; `fix` runs `bundle exec rubocop -a` (safe autocorrections only).",
                    ToolchainSkillFactory.Command("check", "Run RuboCop.", @"Invoke-MuxTool -Tool 'bundle' -Arguments @('exec', 'rubocop') -InstallHint $rubyHint"),
                    ToolchainSkillFactory.Command("fix", "Apply RuboCop's safe corrections.", @"Invoke-MuxTool -Tool 'bundle' -Arguments @('exec', 'rubocop', '-a') -InstallHint $rubyHint")),

                php.Skill("php-deps", "Manage PHP packages", "PHP: installs Composer packages, lists outdated ones, or audits them for known vulnerabilities.", true,
                    "The user asks to install PHP dependencies, or which Composer packages are outdated or vulnerable.", string.Empty,
                    "`install` runs `composer install`; `outdated` runs `composer outdated --direct`; `audit` runs `composer audit`.",
                    ToolchainSkillFactory.Command("install", "Install the packages.", @"Invoke-MuxTool -Tool 'composer' -Arguments @('install') -InstallHint $phpHint"),
                    ToolchainSkillFactory.Command("outdated", "List outdated direct dependencies.", @"Invoke-MuxTool -Tool 'composer' -Arguments @('outdated', '--direct') -InstallHint $phpHint"),
                    ToolchainSkillFactory.Command("audit", "Audit packages for known vulnerabilities.", @"Invoke-MuxTool -Tool 'composer' -Arguments @('audit') -InstallHint $phpHint")),
                php.Skill("php-test", "Run the PHP tests", "PHP: runs Pest or PHPUnit from vendor/bin, for everything or a name filter.", false,
                    "The user asks to run PHP or Laravel tests.", "[pattern]",
                    "`all` runs `vendor/bin/pest` when Pest is installed, otherwise `vendor/bin/phpunit`; `filter <pattern>` passes `--filter`. Run php-deps install first when vendor/ is missing.",
                    ToolchainSkillFactory.Command("all", "Run every test.", @"$runner = Get-MuxPhpBin 'pest'
if (-not $runner) { $runner = Get-MuxPhpBin 'phpunit' }
if (-not $runner) { Exit-MuxNotApplicable 'neither vendor/bin/pest nor vendor/bin/phpunit exists; run php-deps install.' }
Invoke-MuxTool -Tool $runner -InstallHint $phpHint"),
                    ToolchainSkillFactory.Command("filter", "Run tests matching a name.", @"$pattern = Get-MuxArg -Arguments $args -Index 0
if (-not $pattern) { Exit-MuxNotApplicable 'pass a test name: php-test filter <pattern>' }
$runner = Get-MuxPhpBin 'pest'
if (-not $runner) { $runner = Get-MuxPhpBin 'phpunit' }
if (-not $runner) { Exit-MuxNotApplicable 'neither vendor/bin/pest nor vendor/bin/phpunit exists; run php-deps install.' }
Invoke-MuxTool -Tool $runner -Arguments @('--filter', $pattern) -InstallHint $phpHint")),
                php.Skill("php-lint", "Analyze and format PHP", "PHP: runs PHPStan static analysis, or checks or applies PHP-CS-Fixer formatting.", true,
                    "The user asks to lint, statically analyze, or format PHP code.", string.Empty,
                    "`analyze` runs `vendor/bin/phpstan analyse`; `format-check` runs `vendor/bin/php-cs-fixer fix --dry-run --diff`; `format` applies the fixes. Each exits 2 when its tool is not in vendor/bin.",
                    ToolchainSkillFactory.Command("analyze", "Run PHPStan.", @"$tool = Get-MuxPhpBin 'phpstan'
if (-not $tool) { Exit-MuxNotApplicable 'vendor/bin/phpstan is not installed (composer require --dev phpstan/phpstan).' }
Invoke-MuxTool -Tool $tool -Arguments @('analyse', '--no-progress') -InstallHint $phpHint"),
                    ToolchainSkillFactory.Command("format-check", "Show formatting differences.", @"$tool = Get-MuxPhpBin 'php-cs-fixer'
if (-not $tool) { Exit-MuxNotApplicable 'vendor/bin/php-cs-fixer is not installed (composer require --dev friendsofphp/php-cs-fixer).' }
Invoke-MuxTool -Tool $tool -Arguments @('fix', '--dry-run', '--diff') -InstallHint $phpHint"),
                    ToolchainSkillFactory.Command("format", "Apply formatting.", @"$tool = Get-MuxPhpBin 'php-cs-fixer'
if (-not $tool) { Exit-MuxNotApplicable 'vendor/bin/php-cs-fixer is not installed (composer require --dev friendsofphp/php-cs-fixer).' }
Invoke-MuxTool -Tool $tool -Arguments @('fix') -InstallHint $phpHint")),

                swift.Skill("swift-build", "Build the Swift package or Xcode project", "Swift and iOS: builds a Swift package in debug or release, or lists an Xcode project's schemes and builds one.", false,
                    "The user asks to build Swift code, an iOS or macOS app, or a Swift package.", "[scheme]",
                    "`debug` and `release` run `swift build` for a Swift package; for an Xcode project, `schemes` runs `xcodebuild -list` and `scheme <name>` runs `xcodebuild -scheme <name> build`.",
                    ToolchainSkillFactory.Command("debug", "Build the package in debug.", @"if (-not $isPackage) { Exit-MuxNotApplicable 'no Package.swift; for an Xcode project use schemes, then scheme <name>.' }
Invoke-MuxTool -Tool 'swift' -Arguments @('build') -InstallHint $swiftHint"),
                    ToolchainSkillFactory.Command("release", "Build the package in release.", @"if (-not $isPackage) { Exit-MuxNotApplicable 'no Package.swift; for an Xcode project use schemes, then scheme <name>.' }
Invoke-MuxTool -Tool 'swift' -Arguments @('build', '-c', 'release') -InstallHint $swiftHint"),
                    ToolchainSkillFactory.Command("schemes", "List the Xcode project's schemes.", @"Invoke-MuxTool -Tool 'xcodebuild' -Arguments @('-list') -InstallHint 'Install Xcode.'"),
                    ToolchainSkillFactory.Command("scheme", "Build one Xcode scheme.", @"$scheme = Get-MuxArg -Arguments $args -Index 0
if (-not $scheme) { Exit-MuxNotApplicable 'pass the scheme: swift-build scheme <name> (list them with schemes).' }
Invoke-MuxTool -Tool 'xcodebuild' -Arguments @('-scheme', $scheme, 'build') -InstallHint 'Install Xcode.'")),
                swift.Skill("swift-test", "Run the Swift tests", "Swift and iOS: runs swift test for a package, filtered or not, or xcodebuild test for an Xcode scheme.", false,
                    "The user asks to run Swift, iOS, or macOS tests.", "[filter | scheme]",
                    "`all` runs `swift test`; `filter <pattern>` passes `--filter`; `scheme <name>` runs `xcodebuild -scheme <name> test` for an Xcode project (simulator destinations come from the scheme).",
                    ToolchainSkillFactory.Command("all", "Run every package test.", @"if (-not $isPackage) { Exit-MuxNotApplicable 'no Package.swift; for an Xcode project use scheme <name>.' }
Invoke-MuxTool -Tool 'swift' -Arguments @('test') -InstallHint $swiftHint"),
                    ToolchainSkillFactory.Command("filter", "Run package tests matching a filter.", @"$filter = Get-MuxArg -Arguments $args -Index 0
if (-not $filter) { Exit-MuxNotApplicable 'pass a filter: swift-test filter <pattern>' }
Invoke-MuxTool -Tool 'swift' -Arguments @('test', '--filter', $filter) -InstallHint $swiftHint"),
                    ToolchainSkillFactory.Command("scheme", "Test one Xcode scheme.", @"$scheme = Get-MuxArg -Arguments $args -Index 0
if (-not $scheme) { Exit-MuxNotApplicable 'pass the scheme: swift-test scheme <name>' }
Invoke-MuxTool -Tool 'xcodebuild' -Arguments @('-scheme', $scheme, 'test') -InstallHint 'Install Xcode.'")),
                swift.Skill("swift-format", "Format Swift code", "Swift: checks or applies swift-format formatting across the package.", true,
                    "The user asks to format or lint Swift code.", string.Empty,
                    "`check` runs `swift format lint --recursive .`; `apply` runs `swift format --in-place --recursive .` (the swift-format bundled with Swift 6, or the standalone swift-format).",
                    ToolchainSkillFactory.Command("check", "Report formatting problems.", @"if (Test-MuxTool 'swift-format') { Invoke-MuxTool -Tool 'swift-format' -Arguments @('lint', '--recursive', '.') -InstallHint $swiftHint }
else { Invoke-MuxTool -Tool 'swift' -Arguments @('format', 'lint', '--recursive', '.') -InstallHint $swiftHint }"),
                    ToolchainSkillFactory.Command("apply", "Format in place.", @"if (Test-MuxTool 'swift-format') { Invoke-MuxTool -Tool 'swift-format' -Arguments @('--in-place', '--recursive', '.') -InstallHint $swiftHint }
else { Invoke-MuxTool -Tool 'swift' -Arguments @('format', '--in-place', '--recursive', '.') -InstallHint $swiftHint }")),

                android.Skill("android-build", "Build the Android app", "Android: builds debug or release APKs, or cleans, through the Gradle wrapper.", false,
                    "The user asks to build or assemble an Android app.", string.Empty,
                    "`debug` runs `gradlew assembleDebug`, `release` runs `gradlew assembleRelease` (signing comes from the build), and `clean` runs `gradlew clean`.",
                    ToolchainSkillFactory.Command("debug", "Assemble the debug build.", @"Invoke-MuxTool -Tool $gradle -Arguments @('assembleDebug') -InstallHint $androidHint"),
                    ToolchainSkillFactory.Command("release", "Assemble the release build.", @"Invoke-MuxTool -Tool $gradle -Arguments @('assembleRelease') -InstallHint $androidHint"),
                    ToolchainSkillFactory.Command("clean", "Clean the build.", @"Invoke-MuxTool -Tool $gradle -Arguments @('clean') -InstallHint $androidHint")),
                android.Skill("android-test", "Run the Android tests", "Android: runs local unit tests, or instrumented tests on a connected device or emulator.", false,
                    "The user asks to run Android tests.", string.Empty,
                    "`unit` runs `gradlew testDebugUnitTest`; `device` runs `gradlew connectedDebugAndroidTest`, which needs a running emulator or connected device.",
                    ToolchainSkillFactory.Command("unit", "Run local unit tests.", @"Invoke-MuxTool -Tool $gradle -Arguments @('testDebugUnitTest') -InstallHint $androidHint"),
                    ToolchainSkillFactory.Command("device", "Run instrumented tests on a device.", @"Invoke-MuxTool -Tool $gradle -Arguments @('connectedDebugAndroidTest') -InstallHint $androidHint")),
                android.Skill("android-lint", "Lint the Android app", "Android: runs Android Lint on the debug variant.", false,
                    "The user asks to lint an Android app.", string.Empty,
                    "`check` runs `gradlew lintDebug`; the HTML and XML reports land under each module's build/reports.",
                    ToolchainSkillFactory.Command("check", "Run Android Lint.", @"Invoke-MuxTool -Tool $gradle -Arguments @('lintDebug') -InstallHint $androidHint")),

                flutter.Skill("flutter-analyze", "Analyze and format Flutter or Dart", "Flutter and Dart: runs the analyzer, or checks or applies dart format.", true,
                    "The user asks to analyze, lint, or format Flutter or Dart code.", string.Empty,
                    "`check` runs `flutter analyze` (or `dart analyze` for a plain Dart package); `format-check` runs `dart format --output=none --set-exit-if-changed .`; `format` runs `dart format .`.",
                    ToolchainSkillFactory.Command("check", "Run the analyzer.", @"Invoke-MuxTool -Tool $tool -Arguments @('analyze') -InstallHint $flutterHint"),
                    ToolchainSkillFactory.Command("format-check", "Check formatting.", @"Invoke-MuxTool -Tool 'dart' -Arguments @('format', '--output=none', '--set-exit-if-changed', '.') -InstallHint $flutterHint"),
                    ToolchainSkillFactory.Command("format", "Apply formatting.", @"Invoke-MuxTool -Tool 'dart' -Arguments @('format', '.') -InstallHint $flutterHint")),
                flutter.Skill("flutter-test", "Run the Flutter or Dart tests", "Flutter and Dart: runs the tests, all or those whose names match.", false,
                    "The user asks to run Flutter or Dart tests.", "[name]",
                    "`all` runs `flutter test` (or `dart test`); `filter <name>` passes `--name`.",
                    ToolchainSkillFactory.Command("all", "Run every test.", @"Invoke-MuxTool -Tool $tool -Arguments @('test') -InstallHint $flutterHint"),
                    ToolchainSkillFactory.Command("filter", "Run tests whose names match.", @"$name = Get-MuxArg -Arguments $args -Index 0
if (-not $name) { Exit-MuxNotApplicable 'pass a test name: flutter-test filter <name>' }
Invoke-MuxTool -Tool $tool -Arguments @('test', '--name', $name) -InstallHint $flutterHint")),
                flutter.Skill("flutter-build", "Build or manage a Flutter app", "Flutter: fetches packages, lists outdated ones, or builds an APK, app bundle, iOS app, or web build.", true,
                    "The user asks to build a Flutter app or update its packages.", "<apk | appbundle | ios | web>",
                    "`pub-get` runs `flutter pub get` (or `dart pub get`); `outdated` runs `pub outdated`; `build <apk | appbundle | ios | web>` runs `flutter build <target>` (ios needs macOS and Xcode).",
                    ToolchainSkillFactory.Command("pub-get", "Fetch packages.", @"Invoke-MuxTool -Tool $tool -Arguments @('pub', 'get') -InstallHint $flutterHint"),
                    ToolchainSkillFactory.Command("outdated", "List outdated packages.", @"Invoke-MuxTool -Tool $tool -Arguments @('pub', 'outdated') -InstallHint $flutterHint"),
                    ToolchainSkillFactory.Command("build", "Build for a target platform.", @"$target = (Get-MuxArg -Arguments $args -Index 0).ToLowerInvariant()
if (@('apk', 'appbundle', 'ios', 'web') -notcontains $target) { Exit-MuxNotApplicable 'pass the target: flutter-build build <apk | appbundle | ios | web>' }
if (-not $isFlutter) { Exit-MuxNotApplicable 'this is a plain Dart package; there is no app to build.' }
Invoke-MuxTool -Tool 'flutter' -Arguments @('build', $target) -InstallHint $flutterHint"))
            };
        }

        #endregion
    }
}
