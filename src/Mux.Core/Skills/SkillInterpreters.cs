namespace Mux.Core.Skills
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The allowlist of interpreters a skill command may declare, and the metadata needed to materialize
    /// and run an inline code block. The allowlist is the single source of truth shared by validation and
    /// execution — a skill that names an interpreter outside this set is rejected at load, so a malformed
    /// manifest can never ask the harness to launch an arbitrary binary.
    /// </summary>
    public static class SkillInterpreters
    {
        private static readonly Dictionary<string, string> _Extensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "bash", ".sh" },
            { "sh", ".sh" },
            { "pwsh", ".ps1" },
            { "python", ".py" },
            { "node", ".js" },
            { "dotnet-script", ".csx" }
        };

        /// <summary>
        /// The interpreter names a skill command may use.
        /// </summary>
        public static IReadOnlyCollection<string> Allowed => _Extensions.Keys;

        /// <summary>
        /// Indicates whether the named interpreter is on the allowlist.
        /// </summary>
        /// <param name="interpreter">The interpreter name to check. Null or unknown returns <c>false</c>.</param>
        /// <returns><c>true</c> when the interpreter is allowed; otherwise <c>false</c>.</returns>
        public static bool IsAllowed(string? interpreter)
        {
            return interpreter != null && _Extensions.ContainsKey(interpreter);
        }

        /// <summary>
        /// Returns the temp-file extension used when materializing an inline block for the named interpreter.
        /// </summary>
        /// <param name="interpreter">The interpreter name.</param>
        /// <returns>The file extension (including the leading dot), or <c>.txt</c> for an unknown interpreter.</returns>
        public static string FileExtension(string? interpreter)
        {
            if (interpreter != null && _Extensions.TryGetValue(interpreter, out string? extension))
            {
                return extension;
            }

            return ".txt";
        }
    }
}
