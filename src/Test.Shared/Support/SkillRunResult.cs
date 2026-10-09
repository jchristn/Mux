namespace Test.Shared.Support
{
    /// <summary>
    /// The outcome of one skill run, with chainable assertions.
    /// </summary>
    public sealed class SkillRunResult
    {
        private readonly string _Label;

        /// <summary>Creates a result.</summary>
        /// <param name="label">The skill and command, for messages.</param>
        /// <param name="stdout">Standard output.</param>
        /// <param name="stderr">Standard error.</param>
        /// <param name="exitCode">The exit code.</param>
        public SkillRunResult(string label, string stdout, string stderr, int exitCode)
        {
            _Label = label;
            Stdout = stdout.Replace("\r\n", "\n");
            Stderr = stderr;
            ExitCode = exitCode;
        }

        /// <summary>Standard output with LF line endings.</summary>
        public string Stdout { get; }

        /// <summary>Standard error.</summary>
        public string Stderr { get; }

        /// <summary>The exit code.</summary>
        public int ExitCode { get; }

        /// <summary>Asserts the exit code.</summary>
        /// <param name="expected">The expected code.</param>
        /// <returns>This result.</returns>
        public SkillRunResult Exit(int expected)
        {
            MuxAssert.AreEqual(expected, ExitCode, _Label + " exit code; stdout: " + Stdout + " stderr: " + Stderr);
            return this;
        }

        /// <summary>Asserts stdout contains the text.</summary>
        /// <param name="text">The text.</param>
        /// <returns>This result.</returns>
        public SkillRunResult Has(string text)
        {
            MuxAssert.Contains(text, Stdout, _Label + " output");
            return this;
        }

        /// <summary>Asserts stdout does not contain the text.</summary>
        /// <param name="text">The text.</param>
        /// <returns>This result.</returns>
        public SkillRunResult Lacks(string text)
        {
            MuxAssert.DoesNotContain(text, Stdout, _Label + " output");
            return this;
        }
    }
}
