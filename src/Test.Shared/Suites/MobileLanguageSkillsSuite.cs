namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Skills;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the Ruby, PHP, Swift, Android, and Flutter skills: definitions and categories, and dry runs
    /// that check each family picks the right tool, runner, and project directory, plus the refusals. Positive and
    /// negative cases.
    /// </summary>
    public static class MobileLanguageSkillsSuite
    {
        #region Private-Members

        private const string SuiteId = "MobileLanguageSkills";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the suite descriptor.
        /// </summary>
        /// <returns>The descriptor.</returns>
        public static TestSuiteDescriptor Create()
        {
            bool ready = IsOnPath("pwsh");
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            void Add(string id, string name, Func<SkillTestContext, Task> body)
            {
                cases.Add(new TestCaseDescriptor(SuiteId, id, name, (CancellationToken ct) => RunWithContextAsync(body, ct), skip: !ready, skipReason: "pwsh is not on PATH"));
            }

            cases.Add(new TestCaseDescriptor(SuiteId, "SkillsDefined", "Fifteen skills: Ruby and PHP in languages, Swift, Android, and Flutter in the new mobile category", (CancellationToken ct) =>
            {
                Dictionary<string, DefaultSkillDef> defs = DefaultSkillLibrary.Definitions().ToDictionary(d => d.Id, StringComparer.Ordinal);
                string[] languages = { "ruby-deps", "ruby-test", "ruby-lint", "php-deps", "php-test", "php-lint" };
                string[] mobile = { "swift-build", "swift-test", "swift-format", "android-build", "android-test", "android-lint", "flutter-analyze", "flutter-test", "flutter-build" };
                foreach (string id in languages) MuxAssert.AreEqual("languages", DefaultSkillCategories.For(id), id);
                foreach (string id in mobile) MuxAssert.AreEqual("mobile", DefaultSkillCategories.For(id), id);
                MuxAssert.IsTrue(SkillCategories.Known.Contains("mobile"), "mobile is a known category");
                MuxAssert.AreEqual("Gemfile", string.Join(",", defs["ruby-test"].AppliesTo), "Ruby gate");
                MuxAssert.AreEqual("**/AndroidManifest.xml", string.Join(",", defs["android-build"].AppliesTo), "Android gate");
                MuxAssert.IsFalse(defs["ruby-test"].Mutating, "tests are read-only");
                MuxAssert.IsTrue(defs["php-lint"].Mutating, "format writes");
                return Task.CompletedTask;
            }));

            Add("RubyPicksTheRunner", "ruby-test uses RSpec, rails test, or rake test as the project dictates, from a subdirectory", async (SkillTestContext c) =>
            {
                Write(c, ".git/HEAD", "ref: refs/heads/main");
                Write(c, "Gemfile", "source 'https://rubygems.org'");
                Write(c, "lib/x.rb", "");
                (await c.RunIn("lib", true, "ruby-test", "all").ConfigureAwait(false)).Exit(0).Has("DRYRUN: bundle exec rake test");
                Write(c, "bin/rails", "#!/usr/bin/env ruby");
                (await c.Run(true, "ruby-test", "all").ConfigureAwait(false)).Exit(0).Has("DRYRUN: ruby bin/rails test");
                (await c.Run(true, "ruby-test", "filter", "login").ConfigureAwait(false)).Exit(0).Has("-n /login/");
                Directory.CreateDirectory(Path.Combine(c.Project, "spec"));
                (await c.Run(true, "ruby-test", "all").ConfigureAwait(false)).Exit(0).Has("DRYRUN: bundle exec rspec");
                (await c.Run(true, "ruby-test", "filter", "login").ConfigureAwait(false)).Exit(0).Has("rspec -e login");
                (await c.Run(true, "ruby-test", "filter").ConfigureAwait(false)).Exit(2).Has("pass a test name");
                (await c.Run(true, "ruby-lint", "fix").ConfigureAwait(false)).Exit(0).Has("rubocop -a");
                (await c.Run(true, "ruby-deps", "outdated").ConfigureAwait(false)).Exit(0).Has("bundle outdated --strict");
            });
            Add("RubyAndPhpNeedTheirProject", "Without a Gemfile or composer.json the skills exit 2", async (SkillTestContext c) =>
            {
                Write(c, "README.md", "x");
                (await c.Run(true, "ruby-test", "all").ConfigureAwait(false)).Exit(2).Has("no Gemfile");
                (await c.Run(true, "php-test", "all").ConfigureAwait(false)).Exit(2).Has("no composer.json");
                (await c.Run(true, "swift-build", "debug").ConfigureAwait(false)).Exit(2).Has("not a Swift project");
                (await c.Run(true, "flutter-test", "all").ConfigureAwait(false)).Exit(2).Has("no pubspec.yaml");
            });
            Add("PhpPicksTheRunner", "php-test prefers Pest over PHPUnit from vendor/bin, and php-lint needs its tools installed", async (SkillTestContext c) =>
            {
                Write(c, "composer.json", "{}");
                (await c.Run(true, "php-test", "all").ConfigureAwait(false)).Exit(2).Has("run php-deps install");
                Write(c, "vendor/bin/phpunit", "#!/bin/sh");
                (await c.Run(true, "php-test", "filter", "Login").ConfigureAwait(false)).Exit(0).Has("phpunit").Has("--filter Login");
                Write(c, "vendor/bin/pest", "#!/bin/sh");
                (await c.Run(true, "php-test", "all").ConfigureAwait(false)).Exit(0).Has("vendor/bin/pest").Lacks("phpunit");
                (await c.Run(true, "php-lint", "analyze").ConfigureAwait(false)).Exit(2).Has("phpstan is not installed");
                Write(c, "vendor/bin/php-cs-fixer", "#!/bin/sh");
                (await c.Run(true, "php-lint", "format-check").ConfigureAwait(false)).Exit(0).Has("fix --dry-run --diff");
                (await c.Run(true, "php-deps", "audit").ConfigureAwait(false)).Exit(0).Has("DRYRUN: composer audit");
            });
            Add("SwiftPackagesAndXcode", "swift-build and swift-test use swift for a package and xcodebuild for an Xcode project", async (SkillTestContext c) =>
            {
                Write(c, "App.xcodeproj/project.pbxproj", "");
                (await c.Run(true, "swift-build", "debug").ConfigureAwait(false)).Exit(2).Has("use schemes");
                (await c.Run(true, "swift-build", "schemes").ConfigureAwait(false)).Exit(0).Has("DRYRUN: xcodebuild -list");
                (await c.Run(true, "swift-test", "scheme", "App").ConfigureAwait(false)).Exit(0).Has("xcodebuild -scheme App test");
                (await c.Run(true, "swift-build", "scheme").ConfigureAwait(false)).Exit(2).Has("pass the scheme");
                Write(c, "Package.swift", "// swift-tools-version:5.9");
                (await c.Run(true, "swift-build", "release").ConfigureAwait(false)).Exit(0).Has("DRYRUN: swift build -c release");
                (await c.Run(true, "swift-test", "filter", "ParserTests").ConfigureAwait(false)).Exit(0).Has("swift test --filter ParserTests");
                (await c.Run(true, "swift-format", "check").ConfigureAwait(false)).Exit(0).Has("lint").Has("--recursive");
            });
            Add("AndroidUsesTheWrapper", "android skills run the Gradle wrapper from the project root", async (SkillTestContext c) =>
            {
                Write(c, ".git/HEAD", "ref: refs/heads/main");
                Write(c, "gradlew", "#!/bin/sh");
                Write(c, "gradlew.bat", "@echo off");
                Write(c, "settings.gradle.kts", "");
                Write(c, "app/src/main/AndroidManifest.xml", "<manifest />");
                (await c.RunIn("app", true, "android-build", "debug").ConfigureAwait(false)).Exit(0).Has("gradlew").Has("assembleDebug");
                (await c.Run(true, "android-test", "unit").ConfigureAwait(false)).Exit(0).Has("testDebugUnitTest");
                (await c.Run(true, "android-test", "device").ConfigureAwait(false)).Exit(0).Has("connectedDebugAndroidTest");
                (await c.Run(true, "android-lint", "check").ConfigureAwait(false)).Exit(0).Has("lintDebug");
            });
            Add("FlutterOrDart", "Flutter projects use flutter, plain Dart packages use dart, and only Flutter apps build", async (SkillTestContext c) =>
            {
                Write(c, "pubspec.yaml", "name: app\ndependencies:\n  flutter:\n    sdk: flutter\n");
                (await c.Run(true, "flutter-analyze", "check").ConfigureAwait(false)).Exit(0).Has("DRYRUN: flutter analyze");
                (await c.Run(true, "flutter-test", "filter", "login").ConfigureAwait(false)).Exit(0).Has("flutter test --name login");
                (await c.Run(true, "flutter-build", "build", "apk").ConfigureAwait(false)).Exit(0).Has("DRYRUN: flutter build apk");
                (await c.Run(true, "flutter-build", "build", "desktop").ConfigureAwait(false)).Exit(2).Has("apk | appbundle | ios | web");
                (await c.Run(true, "flutter-analyze", "format-check").ConfigureAwait(false)).Exit(0).Has("--set-exit-if-changed");
                Write(c, "pubspec.yaml", "name: lib\nenvironment:\n  sdk: ^3.0.0\n");
                (await c.Run(true, "flutter-analyze", "check").ConfigureAwait(false)).Exit(0).Has("DRYRUN: dart analyze");
                (await c.Run(true, "flutter-test", "all").ConfigureAwait(false)).Exit(0).Has("DRYRUN: dart test");
                (await c.Run(true, "flutter-build", "build", "web").ConfigureAwait(false)).Exit(2).Has("plain Dart package");
            });

            return new TestSuiteDescriptor(SuiteId, "Ruby, PHP, Swift, Android, and Flutter skills", cases);
        }

        #endregion

        #region Private-Methods

        private static string Write(SkillTestContext c, string relative, string content)
        {
            string path = Path.Combine(c.Project, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        private static bool IsOnPath(string executable)
        {
            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory)) continue;
                foreach (string candidate in new[] { executable, executable + ".exe", executable + ".cmd" })
                {
                    if (File.Exists(Path.Combine(directory, candidate))) return true;
                }
            }

            return false;
        }

        private static async Task RunWithContextAsync(Func<SkillTestContext, Task> body, CancellationToken ct)
        {
            string root = Path.Combine(Path.GetTempPath(), "mux-mobilelang-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string skills = Path.Combine(root, "skills");
                DefaultSkillLibrary.SeedInto(skills);
                Directory.CreateDirectory(Path.Combine(root, "project"));
                await body(new SkillTestContext(root, skills, ct)).ConfigureAwait(false);
            }
            finally
            {
                try { Directory.Delete(root, true); } catch (Exception) { }
            }
        }

        #endregion
    }
}
