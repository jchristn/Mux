namespace Mux.Core.Enums
{
    using System;

    /// <summary>
    /// Conversions between <see cref="RoleEnum"/> and its lowercase wire string ("system" / "user" /
    /// "assistant" / "tool"), shared so front ends and routes do not each hand-roll the same switch.
    /// </summary>
    public static class RoleEnumExtensions
    {
        /// <summary>
        /// Returns the lowercase wire string for a role.
        /// </summary>
        /// <param name="role">The role.</param>
        /// <returns>The wire string.</returns>
        public static string ToWire(this RoleEnum role)
        {
            switch (role)
            {
                case RoleEnum.System: return "system";
                case RoleEnum.Assistant: return "assistant";
                case RoleEnum.Tool: return "tool";
                default: return "user";
            }
        }

        /// <summary>
        /// Parses a role string tolerantly (case- and whitespace-insensitive), defaulting to
        /// <see cref="RoleEnum.User"/> for null, empty, or unrecognized input.
        /// </summary>
        /// <param name="role">The role string, or null.</param>
        /// <returns>The parsed role, or <see cref="RoleEnum.User"/>.</returns>
        public static RoleEnum ParseRole(string? role)
        {
            switch ((role ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "system": return RoleEnum.System;
                case "assistant": return RoleEnum.Assistant;
                case "tool": return RoleEnum.Tool;
                default: return RoleEnum.User;
            }
        }
    }
}
