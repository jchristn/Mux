namespace Mux.Core.Skills.Packaging
{
    using System;

    /// <summary>
    /// One skill a pack installed into the user's skills directory, as recorded in <c>.installed-packs.json</c>. The
    /// hash of the installed <c>SKILL.md</c> tells later removals whether the user has edited it since.
    /// </summary>
    public sealed class InstalledPackSkill
    {
        #region Public-Members

        /// <summary>The skill id (its folder name in the skills directory).</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>The pack it came from.</summary>
        public string Pack { get; set; } = string.Empty;

        /// <summary>When it was installed (UTC).</summary>
        public DateTime InstalledUtc { get; set; }

        /// <summary>The SHA-256 of the installed <c>SKILL.md</c> (line endings normalized).</summary>
        public string ContentHash { get; set; } = string.Empty;

        #endregion
    }
}
