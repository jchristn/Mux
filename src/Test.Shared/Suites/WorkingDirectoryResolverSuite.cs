namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Utility;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="WorkingDirectoryResolver"/> — the shared /cwd path resolver used by the
    /// TUI and desktop. Assertions derive expected values from the same <see cref="Path"/> APIs so they hold
    /// on every OS.
    /// </summary>
    public static class WorkingDirectoryResolverSuite
    {
        /// <summary>Builds the resolver suite descriptor.</summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/>.</returns>
        public static TestSuiteDescriptor Create()
        {
            string baseDir = TrimTrailing(Path.GetFullPath(Path.GetTempPath()));

            return new TestSuiteDescriptor(
                "WorkingDirectoryResolver",
                "Shared /cwd path resolution",
                new List<TestCaseDescriptor>
                {
                    Case("ResolvesRelativeAgainstCurrent", "A relative path resolves against the current directory", ct =>
                    {
                        string expected = Path.GetFullPath("sub", baseDir);
                        MuxAssert.AreEqual(expected, WorkingDirectoryResolver.Normalize(baseDir, "sub"), "relative resolved");
                        return Task.CompletedTask;
                    }),

                    Case("StripsSurroundingQuotes", "Double and single surrounding quotes are stripped", ct =>
                    {
                        string expected = Path.GetFullPath("my dir", baseDir);
                        MuxAssert.AreEqual(expected, WorkingDirectoryResolver.Normalize(baseDir, "\"my dir\""), "double quotes");
                        MuxAssert.AreEqual(expected, WorkingDirectoryResolver.Normalize(baseDir, "'my dir'"), "single quotes");
                        return Task.CompletedTask;
                    }),

                    Case("TrimsTrailingSeparator", "A trailing separator is trimmed", ct =>
                    {
                        string expected = Path.GetFullPath("x", baseDir);
                        MuxAssert.AreEqual(expected, WorkingDirectoryResolver.Normalize(baseDir, "x" + Path.DirectorySeparatorChar), "trailing sep trimmed");
                        return Task.CompletedTask;
                    }),

                    Case("ExpandsLeadingTilde", "A leading ~ expands to the user profile", ct =>
                    {
                        string expected = TrimTrailing(Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
                        string actual = WorkingDirectoryResolver.Normalize(baseDir, "~");
                        MuxAssert.AreEqual(expected, actual, "~ expands to home");
                        MuxAssert.IsFalse(actual.Contains("~", StringComparison.Ordinal), "no tilde remains");
                        return Task.CompletedTask;
                    }),

                    Case("BlankInputFails", "Empty, null, or whitespace input fails with a required-path message", ct =>
                    {
                        WorkingDirectoryResolution empty = WorkingDirectoryResolver.Resolve(baseDir, string.Empty, _ => true);
                        MuxAssert.IsFalse(empty.Ok, "empty not ok");
                        MuxAssert.Contains("required", empty.Error, "required message");
                        MuxAssert.IsFalse(WorkingDirectoryResolver.Resolve(baseDir, null, _ => true).Ok, "null not ok");
                        MuxAssert.IsFalse(WorkingDirectoryResolver.Resolve(baseDir, "   ", _ => true).Ok, "whitespace not ok");
                        return Task.CompletedTask;
                    }),

                    Case("MissingDirectoryFails", "A non-existent directory fails with a no-such-directory message", ct =>
                    {
                        WorkingDirectoryResolution missing = WorkingDirectoryResolver.Resolve(baseDir, "ghost", _ => false);
                        MuxAssert.IsFalse(missing.Ok, "missing not ok");
                        MuxAssert.Contains("No such directory", missing.Error, "message");
                        return Task.CompletedTask;
                    }),

                    Case("ExistingDirectorySucceeds", "An existing directory resolves to its normalized absolute path", ct =>
                    {
                        string expected = Path.GetFullPath("sub", baseDir);
                        WorkingDirectoryResolution ok = WorkingDirectoryResolver.Resolve(baseDir, "sub", _ => true);
                        MuxAssert.IsTrue(ok.Ok, "ok");
                        MuxAssert.AreEqual(expected, ok.Path, "normalized path");
                        MuxAssert.AreEqual(string.Empty, ok.Error, "no error");
                        return Task.CompletedTask;
                    })
                });
        }

        private static string TrimTrailing(string path)
        {
            string root = Path.GetPathRoot(path) ?? string.Empty;
            string trimmed = path;
            while (trimmed.Length > root.Length
                && (trimmed[trimmed.Length - 1] == Path.DirectorySeparatorChar || trimmed[trimmed.Length - 1] == Path.AltDirectorySeparatorChar))
            {
                trimmed = trimmed.Substring(0, trimmed.Length - 1);
            }

            return trimmed;
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor("WorkingDirectoryResolver", caseId, displayName, body);
        }
    }
}
