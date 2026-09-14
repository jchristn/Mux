namespace Test.Shared.Suites
{
    using System;
    using System.IO;

    /// <summary>Locates repository paths from a test runner's working directory.</summary>
    public static class TestPaths
    {
        /// <summary>
        /// Walks up from the test assembly location to find the repository root (the directory that
        /// contains <c>publisher.json</c>). Returns null when not found (for example in a packaged runner).
        /// </summary>
        /// <returns>The repo root path, or null.</returns>
        public static string? FindRepoRoot()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "publisher.json"))) return dir.FullName;
                dir = dir.Parent;
            }

            return null;
        }
    }
}
