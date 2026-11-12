namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for the generated desktop string catalogs: <c>node tools/i18n/gen.js --check</c> passes
    /// against the checked-in <c>DesktopStrings.cs</c> and <c>DashboardStrings.cs</c>, and the check fails (naming
    /// the key) when a catalog is edited by hand. Skipped when <c>node</c> is not on PATH or the source tree is not
    /// next to the test build.
    /// </summary>
    public static class I18nCatalogsSuite
    {
        #region Private-Members

        private const string SuiteId = "I18nCatalogs";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Builds the suite descriptor.
        /// </summary>
        /// <returns>The descriptor.</returns>
        public static TestSuiteDescriptor Create()
        {
            string? repo = FindRepoRoot();
            string? node = FindOnPath("node");
            string? skip = repo == null ? "the source tree is not next to the test build" : node == null ? "node is not on PATH" : null;

            return new TestSuiteDescriptor(
                SuiteId,
                "Generated i18n catalogs",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor(SuiteId, "CatalogsMatchGenerator", "DesktopStrings.cs and DashboardStrings.cs are exactly what tools/i18n/gen.js generates (gen.js --check)", (CancellationToken ct) =>
                    {
                        (int exit, string output) = RunCheck(node!, Path.Combine(repo!, "tools", "i18n", "gen.js"));
                        MuxAssert.AreEqual(0, exit, "gen.js --check passes; run 'node tools/i18n/gen.js' after moving hand edits into the sources:\n" + output);
                        return Task.CompletedTask;
                    }, skipReason: skip),

                    new TestCaseDescriptor(SuiteId, "CheckCatchesHandEdits", "gen.js --check fails and names the key when a catalog is edited by hand", (CancellationToken ct) =>
                    {
                        string dir = Path.Combine(Path.GetTempPath(), "mux-i18n-" + Guid.NewGuid().ToString("N"));
                        try
                        {
                            string[] files =
                            {
                                Path.Combine("tools", "i18n", "gen.js"),
                                Path.Combine("tools", "i18n", "desktop-i18n.js"),
                                Path.Combine("tools", "i18n", "dashboard-keys.js"),
                                Path.Combine("src", "Mux.Server", "DashboardTemplate.cs"),
                                Path.Combine("src", "Mux.Desktop.Core", "I18n", "DesktopStrings.cs"),
                                Path.Combine("src", "Mux.Desktop.Core", "I18n", "DashboardStrings.cs"),
                            };
                            foreach (string rel in files)
                            {
                                string target = Path.Combine(dir, rel);
                                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                                File.Copy(Path.Combine(repo!, rel), target);
                            }

                            string gen = Path.Combine(dir, "tools", "i18n", "gen.js");
                            (int cleanExit, string cleanOutput) = RunCheck(node!, gen);
                            MuxAssert.AreEqual(0, cleanExit, "a copied tree passes the check:\n" + cleanOutput);

                            string desktop = Path.Combine(dir, "src", "Mux.Desktop.Core", "I18n", "DesktopStrings.cs");
                            string text = File.ReadAllText(desktop);
                            int at = text.IndexOf("all[\"en\"]", StringComparison.Ordinal);
                            MuxAssert.IsTrue(at >= 0, "found the en block");
                            int open = text.IndexOf('{', at);
                            int lineEnd = text.IndexOf('\n', open) + 1;
                            string eol = text[lineEnd - 2] == '\r' ? "\r\n" : "\n";
                            File.WriteAllText(desktop, text.Insert(lineEnd, "                { \"zz.handEdited\", \"Hand edited\" }," + eol));
                            (int exit, string output) = RunCheck(node!, gen);
                            MuxAssert.AreEqual(1, exit, "a hand-added key fails the check:\n" + output);
                            MuxAssert.IsTrue(output.Contains("zz.handEdited", StringComparison.Ordinal), "the report names the key:\n" + output);
                        }
                        finally
                        {
                            try
                            {
                                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                            }
                            catch (Exception)
                            {
                            }
                        }

                        return Task.CompletedTask;
                    }, skipReason: skip),
                });
        }

        #endregion

        #region Private-Methods

        private static (int Exit, string Output) RunCheck(string node, string genJs)
        {
            ProcessStartInfo info = new ProcessStartInfo(node) { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false };
            info.ArgumentList.Add(genJs);
            info.ArgumentList.Add("--check");
            using Process process = Process.Start(info)!;
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(60000))
            {
                try { process.Kill(true); } catch (Exception) { }
                return (-1, "gen.js --check timed out");
            }

            return (process.ExitCode, stdout.Result + stderr.Result);
        }

        /// <summary>
        /// The repository root (the directory holding <c>tools/i18n/gen.js</c> and <c>src/</c>) found by walking up
        /// from the test build, or null when the source tree is not next to it.
        /// </summary>
        private static string? FindRepoRoot()
        {
            DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "tools", "i18n", "gen.js"))
                    && File.Exists(Path.Combine(directory.FullName, "src", "Mux.Desktop.Core", "I18n", "DesktopStrings.cs")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            return null;
        }

        private static string? FindOnPath(string executable)
        {
            string[] names = OperatingSystem.IsWindows() ? new[] { executable + ".exe", executable + ".cmd" } : new[] { executable };
            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
            {
                foreach (string name in names)
                {
                    string candidate = Path.Combine(directory, name);
                    if (!string.IsNullOrWhiteSpace(directory) && File.Exists(candidate)) return candidate;
                }
            }

            return null;
        }

        #endregion
    }
}
