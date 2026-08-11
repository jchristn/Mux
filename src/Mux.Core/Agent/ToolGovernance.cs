namespace Mux.Core.Agent
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Stateless helpers that evaluate the headless tool-governance policy: allow/deny tool lists and the
    /// application-level <see cref="Mux.Core.Enums.SandboxPostureEnum"/> confinement. Applied by
    /// <see cref="AgentLoop"/> both when advertising tools to the model and as a gate before a tool runs.
    /// </summary>
    public static class ToolGovernance
    {
        // The built-in file-mutating tools whose path arguments are confined under the workspace-write
        // posture, mapped to the argument names that carry a filesystem path.
        private static readonly Dictionary<string, string[]> _FileMutatingToolPathArgs =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["write_file"] = new[] { "file_path" },
                ["edit_file"] = new[] { "file_path" },
                ["multi_edit"] = new[] { "file_path" },
                ["delete_file"] = new[] { "file_path" },
                ["manage_directory"] = new[] { "path", "new_path" }
            };

        /// <summary>
        /// Determines whether a tool is permitted by the allow/deny lists. A deny match always wins. When
        /// the allow list is non-empty, a tool must match it to be permitted; an empty or null allow list
        /// permits everything not denied. Patterns support <c>*</c> (any run) and <c>?</c> (one character)
        /// and match the whole tool name case-insensitively.
        /// </summary>
        /// <param name="toolName">The tool name to test. Null or empty is treated as not permitted.</param>
        /// <param name="allow">The allow patterns, or null/empty to allow all non-denied tools.</param>
        /// <param name="deny">The deny patterns, or null/empty for no denials.</param>
        /// <returns><c>true</c> when the tool may be advertised and executed; otherwise <c>false</c>.</returns>
        public static bool IsPermitted(string? toolName, IReadOnlyList<string>? allow, IReadOnlyList<string>? deny)
        {
            if (string.IsNullOrWhiteSpace(toolName))
            {
                return false;
            }

            if (deny != null && MatchesAny(toolName!, deny))
            {
                return false;
            }

            if (allow != null && allow.Count > 0)
            {
                return MatchesAny(toolName!, allow);
            }

            return true;
        }

        /// <summary>
        /// Evaluates the workspace-write posture for a file-mutating built-in tool. Returns a human-readable
        /// reason when the call would write outside the allowed roots, or null when the call is within
        /// bounds or the tool is not a confined file-mutating tool.
        /// </summary>
        /// <param name="toolName">The tool being called. Must not be null.</param>
        /// <param name="arguments">The parsed tool arguments.</param>
        /// <param name="workingDirectory">The primary allowed root. Must not be null.</param>
        /// <param name="additionalRoots">Additional allowed roots, or null when there are none.</param>
        /// <returns>A denial reason, or null when the call is permitted under workspace-write.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="toolName"/> or <paramref name="workingDirectory"/> is null.</exception>
        public static string? CheckWorkspaceWrite(
            string toolName,
            JsonElement arguments,
            string workingDirectory,
            IReadOnlyList<string>? additionalRoots)
        {
            if (toolName is null) throw new ArgumentNullException(nameof(toolName));
            if (workingDirectory is null) throw new ArgumentNullException(nameof(workingDirectory));

            if (!_FileMutatingToolPathArgs.TryGetValue(toolName, out string[]? pathArgs))
            {
                // Not a confined file-mutating tool (for example run_process or an MCP tool): the posture
                // does not path-confine it; the approval policy still gates it.
                return null;
            }

            if (arguments.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            List<string> roots = BuildRoots(workingDirectory, additionalRoots);

            foreach (string argName in pathArgs)
            {
                if (!arguments.TryGetProperty(argName, out JsonElement value) || value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                string raw = value.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                string resolved = ResolveAgainst(workingDirectory, raw);
                if (!IsWithinAnyRoot(resolved, roots))
                {
                    return $"Tool '{toolName}' would write to '{raw}', which escapes the workspace-write sandbox (allowed roots: {string.Join(", ", roots)}).";
                }
            }

            return null;
        }

        /// <summary>
        /// Maps a posture to its stable contract string (<c>none</c>, <c>read-only</c>, or
        /// <c>workspace-write</c>) used on the run's start event and in the CLI.
        /// </summary>
        /// <param name="posture">The posture to name.</param>
        /// <returns>The contract string for the posture.</returns>
        public static string PostureName(Mux.Core.Enums.SandboxPostureEnum posture)
        {
            switch (posture)
            {
                case Mux.Core.Enums.SandboxPostureEnum.ReadOnly:
                    return "read-only";
                case Mux.Core.Enums.SandboxPostureEnum.WorkspaceWrite:
                    return "workspace-write";
                default:
                    return "none";
            }
        }

        /// <summary>
        /// Parses a posture contract string (<c>none</c>, <c>read-only</c>, or <c>workspace-write</c>;
        /// underscores accepted) into its enum value.
        /// </summary>
        /// <param name="value">The value to parse. Null or empty maps to <see cref="Mux.Core.Enums.SandboxPostureEnum.None"/>.</param>
        /// <param name="posture">The parsed posture when the method returns true.</param>
        /// <returns><c>true</c> when the value is a recognized posture; otherwise <c>false</c>.</returns>
        public static bool TryParsePosture(string? value, out Mux.Core.Enums.SandboxPostureEnum posture)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "":
                case "none":
                    posture = Mux.Core.Enums.SandboxPostureEnum.None;
                    return true;
                case "read-only":
                case "read_only":
                case "readonly":
                    posture = Mux.Core.Enums.SandboxPostureEnum.ReadOnly;
                    return true;
                case "workspace-write":
                case "workspace_write":
                case "workspacewrite":
                    posture = Mux.Core.Enums.SandboxPostureEnum.WorkspaceWrite;
                    return true;
                default:
                    posture = Mux.Core.Enums.SandboxPostureEnum.None;
                    return false;
            }
        }

        private static bool MatchesAny(string toolName, IReadOnlyList<string> patterns)
        {
            foreach (string pattern in patterns)
            {
                if (string.IsNullOrWhiteSpace(pattern))
                {
                    continue;
                }

                if (MatchesGlob(toolName, pattern.Trim()))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool MatchesGlob(string value, string pattern)
        {
            string regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase);
        }

        private static List<string> BuildRoots(string workingDirectory, IReadOnlyList<string>? additionalRoots)
        {
            List<string> roots = new List<string> { NormalizeRoot(workingDirectory) };
            if (additionalRoots != null)
            {
                foreach (string root in additionalRoots)
                {
                    if (!string.IsNullOrWhiteSpace(root))
                    {
                        roots.Add(NormalizeRoot(root));
                    }
                }
            }

            return roots;
        }

        private static string NormalizeRoot(string path)
        {
            string full = Path.GetFullPath(path);
            return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static string ResolveAgainst(string workingDirectory, string candidate)
        {
            string combined = Path.IsPathRooted(candidate)
                ? candidate
                : Path.Combine(workingDirectory, candidate);
            return Path.GetFullPath(combined).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static bool IsWithinAnyRoot(string resolvedPath, IReadOnlyList<string> roots)
        {
            foreach (string root in roots)
            {
                if (string.Equals(resolvedPath, root, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                string rootWithSeparator = root + Path.DirectorySeparatorChar;
                if (resolvedPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
