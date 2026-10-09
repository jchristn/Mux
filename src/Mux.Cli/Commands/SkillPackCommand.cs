namespace Mux.Cli.Commands
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using Mux.Cli.Rendering;
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Mux.Core.Skills.Packaging;

    /// <summary>
    /// <c>mux skill pack list|show|install|remove</c> and <c>mux skill import</c>: browse and install the opt-in skill
    /// packs shipped with mux, and import Claude-format skill folders (local or from git) with normalization.
    /// </summary>
    public static class SkillPackCommand
    {
        #region Public-Members

        /// <summary>The usage text.</summary>
        public const string Usage = "Usage: mux skill pack list | show <pack> | install <pack> [--skill <id>] [--force] | remove <pack> [--skill <id>] [--force]\n"
            + "       mux skill import <folder|git-url> [--pack <id>] [--into <dir>] [--dry-run] [--force]\n"
            + "Options: --output-format text|json, --config-dir <dir>";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether <paramref name="args"/> (after <c>skill</c>) is a pack or import command handled here.
        /// </summary>
        /// <param name="args">The arguments after <c>skill</c>.</param>
        /// <returns>True for <c>pack</c> and <c>import</c>.</returns>
        public static bool Handles(string[] args)
        {
            return args != null && args.Length > 0
                && (string.Equals(args[0], "pack", StringComparison.OrdinalIgnoreCase) || string.Equals(args[0], "packs", StringComparison.OrdinalIgnoreCase) || string.Equals(args[0], "import", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Runs a pack or import command.
        /// </summary>
        /// <param name="args">The arguments after <c>skill</c>.</param>
        /// <param name="catalog">The packs to use; null uses the packs embedded in mux.</param>
        /// <returns>The exit code: 0 success, 1 failure or bad usage.</returns>
        public static int Run(string[] args, SkillPackCatalog? catalog = null)
        {
            List<string> positionals = new List<string>();
            string? skill = null;
            string? pack = null;
            string? into = null;
            string? configDir = null;
            bool force = false;
            bool dryRun = false;
            bool json = false;
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                string? Next()
                {
                    if (i + 1 >= args.Length) return null;
                    i++;
                    return args[i];
                }

                switch (arg)
                {
                    case "--skill": skill = Next(); if (skill == null) return Fail("--skill needs a skill id."); break;
                    case "--pack": pack = Next(); if (pack == null) return Fail("--pack needs a pack id."); break;
                    case "--into": into = Next(); if (into == null) return Fail("--into needs a folder."); break;
                    case "--config-dir": configDir = Next(); if (configDir == null) return Fail("--config-dir needs a folder."); break;
                    case "--force": force = true; break;
                    case "--dry-run": dryRun = true; break;
                    case "--output-format":
                        string? format = Next();
                        if (format == null || (format != "json" && format != "text")) return Fail("--output-format must be text or json.");
                        json = format == "json";
                        break;
                    default:
                        if (arg.StartsWith("--", StringComparison.Ordinal)) return Fail("Unknown option '" + arg + "'.");
                        positionals.Add(arg);
                        break;
                }
            }

            using IDisposable configScope = SettingsLoader.PushConfigDirectoryOverride(configDir);
            SettingsLoader.EnsureConfigDirectory();
            string skillsDirectory = SettingsLoader.ResolveSkillsDirectory(SettingsLoader.LoadSettings());
            SkillPackInstaller installer = new SkillPackInstaller(skillsDirectory, catalog);
            try
            {
                if (string.Equals(positionals[0], "import", StringComparison.OrdinalIgnoreCase))
                {
                    if (positionals.Count < 2) return Fail("Name a folder or git URL to import.");
                    return Import(positionals[1], into, pack, skillsDirectory, dryRun, force, json);
                }

                string verb = positionals.Count > 1 ? positionals[1].ToLowerInvariant() : "list";
                string? packId = positionals.Count > 2 ? positionals[2] : null;
                switch (verb)
                {
                    case "list":
                        return List(installer, json);
                    case "show":
                        if (packId == null) return Fail("Name a pack: mux skill pack show <pack>.");
                        return Show(installer, packId, json);
                    case "install":
                        if (packId == null) return Fail("Name a pack: mux skill pack install <pack> [--skill <id>].");
                        return Report(installer.Install(packId, skill, force), json, "Installed");
                    case "remove":
                    case "uninstall":
                        if (packId == null) return Fail("Name a pack: mux skill pack remove <pack> [--skill <id>].");
                        return Report(installer.Remove(packId, skill, force), json, "Removed");
                    default:
                        return Fail("Unknown pack action '" + verb + "'.");
                }
            }
            catch (KeyNotFoundException ex)
            {
                return Fail(ex.Message);
            }
            catch (ArgumentException ex)
            {
                return Fail(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return Fail(ex.Message);
            }
            catch (IOException ex)
            {
                return Fail(ex.Message);
            }
        }

        #endregion

        #region Private-Methods

        private static int List(SkillPackInstaller installer, bool json)
        {
            List<object> rows = new List<object>();
            foreach (SkillPack pack in installer.Catalog.Packs)
            {
                rows.Add(new { id = pack.Id, title = pack.Title, description = pack.Description, category = pack.Category, skills = pack.Skills.Count, installed = installer.InstalledCount(pack.Id), license = pack.License });
            }

            if (json)
            {
                Console.WriteLine(StructuredOutputFormatter.FormatObject(new { success = true, packs = rows }));
                return 0;
            }

            if (installer.Catalog.Packs.Count == 0)
            {
                Console.WriteLine("No skill packs are bundled with this build.");
                return 0;
            }

            foreach (SkillPack pack in installer.Catalog.Packs)
            {
                Console.WriteLine($"{pack.Id,-14} {installer.InstalledCount(pack.Id),3}/{pack.Skills.Count,-3} installed  {pack.Title}: {pack.Description}");
            }

            Console.WriteLine("Install with: mux skill pack install <pack> [--skill <id>]");
            return 0;
        }

        private static int Show(SkillPackInstaller installer, string packId, bool json)
        {
            SkillPack pack = installer.Catalog.Find(packId) ?? throw new KeyNotFoundException("No skill pack named '" + packId + "'. mux skill pack list shows the packs.");
            List<object> skills = new List<object>();
            foreach (BundledSkill skill in pack.Skills)
            {
                skills.Add(new { id = skill.Id, description = SkillImportNormalizer.ReadFrontmatterValue(skill.SkillMarkdown, "description") ?? string.Empty, installed = installer.IsInstalled(pack.Id, skill.Id), files = skill.Files.Count });
            }

            if (json)
            {
                Console.WriteLine(StructuredOutputFormatter.FormatObject(new { success = true, id = pack.Id, title = pack.Title, description = pack.Description, category = pack.Category, source = pack.Source, license = pack.License, skills }));
                return 0;
            }

            Console.WriteLine($"{pack.Title} ({pack.Id}): {pack.Description}");
            if (pack.Source.Length > 0) Console.WriteLine("Source:  " + pack.Source);
            if (pack.License.Length > 0) Console.WriteLine("License: " + pack.License);
            foreach (BundledSkill skill in pack.Skills)
            {
                string mark = installer.IsInstalled(pack.Id, skill.Id) ? "[x]" : "[ ]";
                Console.WriteLine($"  {mark} {skill.Id}: {SkillImportNormalizer.ReadFrontmatterValue(skill.SkillMarkdown, "description")}");
            }

            return 0;
        }

        private static int Report(SkillPackResult result, bool json, string verb)
        {
            if (json)
            {
                Console.WriteLine(StructuredOutputFormatter.FormatObject(new { success = true, pack = result.Pack, installed = result.Installed, removed = result.Removed, skipped = result.Skipped }));
                return 0;
            }

            List<string> changed = verb == "Installed" ? result.Installed : result.Removed;
            Console.WriteLine(verb + " " + changed.Count + " skill(s) from the " + result.Pack + " pack" + (changed.Count > 0 ? ": " + string.Join(", ", changed) : "."));
            foreach (string skipped in result.Skipped)
            {
                Console.WriteLine("  skipped " + skipped);
            }

            return 0;
        }

        private static int Import(string source, string? into, string? pack, string skillsDirectory, bool dryRun, bool force, bool json)
        {
            string destination = string.IsNullOrWhiteSpace(into) ? skillsDirectory : Path.GetFullPath(into!);
            bool packLayout = !string.IsNullOrWhiteSpace(into) && !string.IsNullOrWhiteSpace(pack);
            List<SkillImportReport> reports = new SkillImporter().Import(source, destination, pack, packLayout, dryRun, force, CancellationToken.None);
            bool failed = reports.Exists((SkillImportReport r) => r.Status == "invalid");
            if (json)
            {
                Console.WriteLine(StructuredOutputFormatter.FormatObject(new { success = !failed && reports.Count > 0, destination, dryRun, skills = reports }));
                return failed || reports.Count == 0 ? 1 : 0;
            }

            if (reports.Count == 0)
            {
                Console.Error.WriteLine("No skills (folders with a SKILL.md) were found in " + source + ".");
                return 1;
            }

            foreach (SkillImportReport report in reports)
            {
                Console.WriteLine($"{report.Status,-13} {report.Id}  ({report.SourcePath}) category {report.Category}");
                foreach (string change in report.Changes) Console.WriteLine("    changed: " + change);
                foreach (string flag in report.Flags) Console.WriteLine("    review:  " + flag);
                foreach (string error in report.Errors) Console.WriteLine("    error:   " + error);
            }

            int imported = reports.FindAll((SkillImportReport r) => r.Status == "imported" || r.Status == "would-import").Count;
            Console.WriteLine((dryRun ? "Would import " : "Imported ") + imported + " of " + reports.Count + " skill(s) into " + destination + ".");
            return failed ? 1 : 0;
        }

        private static int Fail(string message)
        {
            Console.Error.WriteLine(message);
            Console.Error.WriteLine(Usage);
            return 1;
        }

        #endregion
    }
}
