namespace Test.Shared.Support
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One table-driven toolchain-skill check: a fixture project, the skill command to dry-run in it, and what the
    /// output and exit code must be.
    /// </summary>
    public sealed class ToolchainCase
    {
        /// <summary>The test case id.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>The human-readable test name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Populates the fixture project directory (which already contains a .git folder).</summary>
        public Action<string> Fixture { get; set; } = (string dir) => { };

        /// <summary>A subdirectory of the fixture to run in, or empty for the fixture root.</summary>
        public string RunIn { get; set; } = string.Empty;

        /// <summary>The skill id.</summary>
        public string Skill { get; set; } = string.Empty;

        /// <summary>The command name.</summary>
        public string Command { get; set; } = string.Empty;

        /// <summary>Arguments passed to the command.</summary>
        public List<string> Arguments { get; set; } = new List<string>();

        /// <summary>Substrings that must all appear in stdout.</summary>
        public List<string> Expected { get; set; } = new List<string>();

        /// <summary>Substrings that must not appear in stdout.</summary>
        public List<string> Unexpected { get; set; } = new List<string>();

        /// <summary>Extra environment variables for the run (for example the dry-run target name).</summary>
        public Dictionary<string, string> Environment { get; set; } = new Dictionary<string, string>();

        /// <summary>The expected exit code.</summary>
        public int ExpectedExit { get; set; }
    }
}
