namespace Mux.Cli.App
{
    using System;
    using System.Collections.Generic;
    using Mux.Core.Skills.Packaging;

    /// <summary>
    /// The terminal's <c>/packs</c> command: turns <c>list</c>, <c>show &lt;pack&gt;</c>,
    /// <c>install &lt;pack&gt; [skill] [--force]</c>, and <c>remove &lt;pack&gt; [skill] [--force]</c> into notice lines,
    /// so the shell and its pack picker share one implementation.
    /// </summary>
    internal static class SkillPackPresenter
    {
        #region Internal-Members

        /// <summary>The usage line.</summary>
        internal const string Usage = "Usage: /packs [list | show <pack> | install <pack> [skill] [--force] | remove <pack> [skill] [--force]]";

        #endregion

        #region Internal-Methods

        /// <summary>
        /// Runs a <c>/packs</c> argument.
        /// </summary>
        /// <param name="installer">The installer for the user's skills directory.</param>
        /// <param name="argument">The text after <c>/packs</c>.</param>
        /// <param name="changed">True when skills were installed or removed (the caller rescans skills).</param>
        /// <returns>The notice lines.</returns>
        internal static List<string> Execute(SkillPackInstaller installer, string? argument, out bool changed)
        {
            changed = false;
            List<string> words = new List<string>((argument ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            bool force = words.RemoveAll((string w) => string.Equals(w, "--force", StringComparison.OrdinalIgnoreCase)) > 0;
            string verb = words.Count > 0 ? words[0].ToLowerInvariant() : "list";
            string? pack = words.Count > 1 ? words[1] : null;
            string? skill = words.Count > 2 ? words[2] : null;
            try
            {
                switch (verb)
                {
                    case "list":
                        return ListLines(installer);
                    case "show":
                        return pack == null ? new List<string> { "Name a pack: /packs show <pack>." } : ShowLines(installer, pack);
                    case "install":
                        if (pack == null) return new List<string> { "Name a pack: /packs install <pack> [skill]." };
                        SkillPackResult installed = installer.Install(pack, skill, force);
                        changed = installed.Installed.Count > 0;
                        return ResultLines(installed, installed.Installed, "Installed");
                    case "remove":
                    case "uninstall":
                        if (pack == null) return new List<string> { "Name a pack: /packs remove <pack> [skill]." };
                        SkillPackResult removed = installer.Remove(pack, skill, force);
                        changed = removed.Removed.Count > 0;
                        return ResultLines(removed, removed.Removed, "Removed");
                    default:
                        return new List<string> { Usage };
                }
            }
            catch (KeyNotFoundException ex)
            {
                return new List<string> { "⚠ " + ex.Message };
            }
            catch (InvalidOperationException ex)
            {
                return new List<string> { "⚠ " + ex.Message };
            }
            catch (System.IO.IOException ex)
            {
                return new List<string> { "⚠ " + ex.Message };
            }
        }

        /// <summary>
        /// One line per pack with its installed count.
        /// </summary>
        /// <param name="installer">The installer.</param>
        /// <returns>The lines.</returns>
        internal static List<string> ListLines(SkillPackInstaller installer)
        {
            List<string> lines = new List<string>();
            if (installer.Catalog.Packs.Count == 0)
            {
                lines.Add("No skill packs are bundled with this build.");
                return lines;
            }

            lines.Add("Skill packs (installed/total):");
            foreach (SkillPack pack in installer.Catalog.Packs)
            {
                lines.Add("  " + pack.Id + "  " + installer.InstalledCount(pack.Id) + "/" + pack.Skills.Count + "  " + pack.Title + (pack.Description.Length > 0 ? ": " + pack.Description : string.Empty));
            }

            lines.Add("/packs show <pack> lists its skills; /packs install <pack> [skill] adds them.");
            return lines;
        }

        /// <summary>
        /// A pack's skills with their installed state.
        /// </summary>
        /// <param name="installer">The installer.</param>
        /// <param name="packId">The pack id.</param>
        /// <returns>The lines.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when the pack does not exist.</exception>
        internal static List<string> ShowLines(SkillPackInstaller installer, string packId)
        {
            SkillPack pack = installer.Catalog.Find(packId) ?? throw new KeyNotFoundException("No skill pack named '" + packId + "'. /packs lists them.");
            List<string> lines = new List<string> { pack.Title + " (" + pack.Id + ")" + (pack.Description.Length > 0 ? ": " + pack.Description : string.Empty) };
            foreach (BundledSkill skill in pack.Skills)
            {
                string mark = installer.IsInstalled(pack.Id, skill.Id) ? "[x]" : "[ ]";
                lines.Add("  " + mark + " " + skill.Id + ": " + (SkillImportNormalizer.ReadFrontmatterValue(skill.SkillMarkdown, "description") ?? string.Empty));
            }

            return lines;
        }

        #endregion

        #region Private-Methods

        private static List<string> ResultLines(SkillPackResult result, List<string> changed, string verb)
        {
            List<string> lines = new List<string>
            {
                (changed.Count > 0 ? "✓ " : string.Empty) + verb + " " + changed.Count + " skill(s) from the " + result.Pack + " pack" + (changed.Count > 0 ? ": " + string.Join(", ", changed) : ".")
            };
            foreach (string skipped in result.Skipped)
            {
                lines.Add("  skipped " + skipped);
            }

            return lines;
        }

        #endregion
    }
}
