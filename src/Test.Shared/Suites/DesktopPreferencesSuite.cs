namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Desktop.Services;
    using Touchstone.Core;

    /// <summary>
    /// Touchstone suite for <see cref="DesktopPreferencesStore"/>: default-on-missing, save/load round-trip,
    /// theme-mode normalization, and graceful handling of a corrupt file.
    /// </summary>
    public static class DesktopPreferencesSuite
    {
        /// <summary>
        /// Builds the desktop-preferences suite descriptor.
        /// </summary>
        /// <returns>A <see cref="TestSuiteDescriptor"/> for the preferences cases.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(
                "DesktopPreferences",
                "Desktop preferences store",
                new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("DesktopPreferences", "DefaultWhenMissing", "A missing file yields the dark default", (CancellationToken ct) =>
                    {
                        string dir = NewDir();
                        try
                        {
                            DesktopPreferences prefs = new DesktopPreferencesStore(dir).Load();
                            MuxAssert.AreEqual("dark", prefs.ThemeMode, "default theme mode");
                        }
                        finally
                        {
                            Cleanup(dir);
                        }

                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("DesktopPreferences", "RoundTrip", "Saved preferences load back", (CancellationToken ct) =>
                    {
                        string dir = NewDir();
                        try
                        {
                            DesktopPreferencesStore store = new DesktopPreferencesStore(dir);
                            MuxAssert.IsTrue(store.Save(new DesktopPreferences { ThemeMode = "system" }), "save succeeds");
                            MuxAssert.IsTrue(File.Exists(store.FilePath), "file written");
                            MuxAssert.AreEqual("system", store.Load().ThemeMode, "round-trip theme mode");
                        }
                        finally
                        {
                            Cleanup(dir);
                        }

                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("DesktopPreferences", "ViewTogglesRoundTrip", "Sidebar and thinking view toggles persist", (CancellationToken ct) =>
                    {
                        string dir = NewDir();
                        try
                        {
                            DesktopPreferencesStore store = new DesktopPreferencesStore(dir);
                            MuxAssert.IsFalse(store.Load().SidebarCollapsed, "sidebar defaults expanded");
                            MuxAssert.IsFalse(store.Load().AutoExpandThinking, "thinking defaults collapsed");

                            store.Save(new DesktopPreferences { ThemeMode = "dark", SidebarCollapsed = true, AutoExpandThinking = true });
                            DesktopPreferences loaded = store.Load();
                            MuxAssert.IsTrue(loaded.SidebarCollapsed, "sidebar collapse persisted");
                            MuxAssert.IsTrue(loaded.AutoExpandThinking, "auto-expand thinking persisted");
                            MuxAssert.AreEqual("dark", loaded.ThemeMode, "theme preserved alongside view toggles");
                        }
                        finally
                        {
                            Cleanup(dir);
                        }

                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("DesktopPreferences", "NormalizesUnknownMode", "An unknown mode normalizes to dark", (CancellationToken ct) =>
                    {
                        string dir = NewDir();
                        try
                        {
                            DesktopPreferencesStore store = new DesktopPreferencesStore(dir);
                            store.Save(new DesktopPreferences { ThemeMode = "PURPLE" });
                            MuxAssert.AreEqual("dark", store.Load().ThemeMode, "unknown mode falls back to dark");
                            store.Save(new DesktopPreferences { ThemeMode = "Light" });
                            MuxAssert.AreEqual("light", store.Load().ThemeMode, "case-insensitive light");
                        }
                        finally
                        {
                            Cleanup(dir);
                        }

                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("DesktopPreferences", "CorruptFileYieldsDefault", "A corrupt file yields defaults without throwing", (CancellationToken ct) =>
                    {
                        string dir = NewDir();
                        try
                        {
                            DesktopPreferencesStore store = new DesktopPreferencesStore(dir);
                            File.WriteAllText(store.FilePath, "{ this is not json ");
                            MuxAssert.AreEqual("dark", store.Load().ThemeMode, "corrupt file falls back to default");
                        }
                        finally
                        {
                            Cleanup(dir);
                        }

                        return Task.CompletedTask;
                    }),

                    new TestCaseDescriptor("DesktopPreferences", "NullDirectoryThrows", "A null config directory is rejected", (CancellationToken ct) =>
                    {
                        MuxAssert.Throws<ArgumentNullException>(() => new DesktopPreferencesStore(null!), "null directory");
                        return Task.CompletedTask;
                    })
                });
        }

        private static string NewDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "mux-desktop-prefs-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void Cleanup(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
            catch (IOException)
            {
                // Best-effort.
            }
        }
    }
}
