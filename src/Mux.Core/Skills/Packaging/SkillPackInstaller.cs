namespace Mux.Core.Skills.Packaging
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;

    /// <summary>
    /// Installs skill packs (or single skills from a pack) into the user's skills directory and removes them again.
    /// Every installed skill is recorded in <c>.installed-packs.json</c> with the hash of its <c>SKILL.md</c>, so the
    /// installer only ever removes or replaces folders it wrote itself, never a skill the user created, and never a
    /// pack skill the user has edited unless <c>force</c> is passed. Not thread-safe across processes; callers in one
    /// process should not install and remove concurrently.
    /// </summary>
    public sealed class SkillPackInstaller
    {
        #region Private-Members

        private static readonly JsonSerializerOptions _Json = new JsonSerializerOptions { WriteIndented = true };

        private readonly string _SkillsDirectory;
        private readonly SkillPackCatalog _Catalog;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="SkillPackInstaller"/> class.
        /// </summary>
        /// <param name="skillsDirectory">The user's skills directory. Must not be blank.</param>
        /// <param name="catalog">The packs to install from; null uses the packs embedded in mux.</param>
        /// <exception cref="ArgumentException">Thrown when the directory is blank.</exception>
        public SkillPackInstaller(string skillsDirectory, SkillPackCatalog? catalog = null)
        {
            if (string.IsNullOrWhiteSpace(skillsDirectory)) throw new ArgumentException("A skills directory is required.", nameof(skillsDirectory));
            _SkillsDirectory = skillsDirectory;
            _Catalog = catalog ?? SkillPackCatalog.Embedded;
        }

        #endregion

        #region Public-Members

        /// <summary>The manifest file, inside the skills directory, recording installed pack skills.</summary>
        public const string ManifestFileName = ".installed-packs.json";

        /// <summary>The catalog this installer installs from.</summary>
        public SkillPackCatalog Catalog => _Catalog;

        /// <summary>The skills directory.</summary>
        public string SkillsDirectory => _SkillsDirectory;

        #endregion

        #region Public-Methods

        /// <summary>
        /// The pack skills currently recorded as installed whose folders still exist.
        /// </summary>
        /// <returns>The entries, sorted by pack then id.</returns>
        public List<InstalledPackSkill> Installed()
        {
            List<InstalledPackSkill> entries = LoadManifest().FindAll((InstalledPackSkill e) => Directory.Exists(Path.Combine(_SkillsDirectory, e.Id)));
            entries.Sort((InstalledPackSkill a, InstalledPackSkill b) =>
            {
                int byPack = string.CompareOrdinal(a.Pack, b.Pack);
                return byPack != 0 ? byPack : string.CompareOrdinal(a.Id, b.Id);
            });
            return entries;
        }

        /// <summary>
        /// Whether a pack skill is installed (recorded and its folder present).
        /// </summary>
        /// <param name="packId">The pack id.</param>
        /// <param name="skillId">The skill id.</param>
        /// <returns>True when installed.</returns>
        public bool IsInstalled(string packId, string skillId)
        {
            return Installed().Exists((InstalledPackSkill e) => string.Equals(e.Pack, packId, StringComparison.OrdinalIgnoreCase) && string.Equals(e.Id, skillId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// How many of a pack's skills are installed.
        /// </summary>
        /// <param name="packId">The pack id.</param>
        /// <returns>The count.</returns>
        public int InstalledCount(string packId)
        {
            return Installed().FindAll((InstalledPackSkill e) => string.Equals(e.Pack, packId, StringComparison.OrdinalIgnoreCase)).Count;
        }

        /// <summary>
        /// Installs every skill in a pack, or one of them. A folder that already exists is skipped: a skill the user
        /// created (or that another pack installed) is never replaced; a skill this pack installed is replaced only with
        /// <paramref name="force"/>.
        /// </summary>
        /// <param name="packId">The pack id.</param>
        /// <param name="skillId">One skill to install, or null for the whole pack.</param>
        /// <param name="force">Whether to replace this pack's previously installed copies (including edited ones).</param>
        /// <returns>What was installed and skipped.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when the pack, or the named skill in it, does not exist.</exception>
        public SkillPackResult Install(string packId, string? skillId = null, bool force = false)
        {
            SkillPack pack = _Catalog.Find(packId) ?? throw new KeyNotFoundException("No skill pack named '" + packId + "'. mux skill pack list shows the packs.");
            List<BundledSkill> targets = SelectSkills(pack, skillId);
            Directory.CreateDirectory(_SkillsDirectory);
            List<InstalledPackSkill> manifest = LoadManifest();
            SkillPackResult result = new SkillPackResult { Pack = pack.Id };
            foreach (BundledSkill skill in targets)
            {
                string folder = Path.Combine(_SkillsDirectory, skill.Id);
                InstalledPackSkill? record = manifest.Find((InstalledPackSkill e) => string.Equals(e.Id, skill.Id, StringComparison.OrdinalIgnoreCase));
                if (Directory.Exists(folder))
                {
                    if (record == null)
                    {
                        result.Skipped.Add(skill.Id + ": a skill with this name already exists and was not installed by a pack; it is left alone");
                        continue;
                    }

                    if (!string.Equals(record.Pack, pack.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Skipped.Add(skill.Id + ": already installed by the " + record.Pack + " pack");
                        continue;
                    }

                    if (!force)
                    {
                        result.Skipped.Add(skill.Id + ": already installed; pass --force to reinstall it");
                        continue;
                    }

                    Directory.Delete(folder, true);
                }

                SkillFileWriter.WriteAll(folder, skill.Files, skill.Id);
                manifest.RemoveAll((InstalledPackSkill e) => string.Equals(e.Id, skill.Id, StringComparison.OrdinalIgnoreCase));
                manifest.Add(new InstalledPackSkill
                {
                    Id = skill.Id,
                    Pack = pack.Id,
                    InstalledUtc = DateTime.UtcNow,
                    ContentHash = SkillFileWriter.HashMarkdown(skill.SkillMarkdown)
                });
                result.Installed.Add(skill.Id);
            }

            SaveManifest(manifest);
            return result;
        }

        /// <summary>
        /// Removes a pack's installed skills, or one of them. Only folders recorded as installed by this pack are
        /// removed; one whose <c>SKILL.md</c> was edited since install is kept unless <paramref name="force"/> is set.
        /// </summary>
        /// <param name="packId">The pack id.</param>
        /// <param name="skillId">One skill to remove, or null for every installed skill of the pack.</param>
        /// <param name="force">Whether to remove edited copies too.</param>
        /// <returns>What was removed and kept.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when the pack is unknown and nothing from it is installed.</exception>
        public SkillPackResult Remove(string packId, string? skillId = null, bool force = false)
        {
            List<InstalledPackSkill> manifest = LoadManifest();
            SkillPack? pack = _Catalog.Find(packId);
            List<InstalledPackSkill> owned = manifest.FindAll((InstalledPackSkill e) => string.Equals(e.Pack, (packId ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
            if (pack == null && owned.Count == 0)
            {
                throw new KeyNotFoundException("No skill pack named '" + packId + "'. mux skill pack list shows the packs.");
            }

            SkillPackResult result = new SkillPackResult { Pack = pack?.Id ?? packId!.Trim() };
            if (!string.IsNullOrWhiteSpace(skillId))
            {
                InstalledPackSkill? one = owned.Find((InstalledPackSkill e) => string.Equals(e.Id, skillId.Trim(), StringComparison.OrdinalIgnoreCase));
                if (one == null)
                {
                    if (pack != null && pack.Find(skillId) == null)
                    {
                        throw new KeyNotFoundException("The " + result.Pack + " pack has no skill named '" + skillId + "'.");
                    }

                    result.Skipped.Add(skillId.Trim() + ": not installed from the " + result.Pack + " pack");
                    return result;
                }

                owned = new List<InstalledPackSkill> { one };
            }

            foreach (InstalledPackSkill entry in owned)
            {
                string folder = Path.Combine(_SkillsDirectory, entry.Id);
                if (!Directory.Exists(folder))
                {
                    manifest.Remove(entry);
                    result.Skipped.Add(entry.Id + ": its folder was already gone; forgotten");
                    continue;
                }

                if (!force && IsEdited(folder, entry))
                {
                    result.Skipped.Add(entry.Id + ": edited since it was installed; pass --force to remove it anyway");
                    continue;
                }

                Directory.Delete(folder, true);
                manifest.Remove(entry);
                result.Removed.Add(entry.Id);
            }

            SaveManifest(manifest);
            return result;
        }

        #endregion

        #region Private-Methods

        private static List<BundledSkill> SelectSkills(SkillPack pack, string? skillId)
        {
            if (string.IsNullOrWhiteSpace(skillId))
            {
                return new List<BundledSkill>(pack.Skills);
            }

            BundledSkill one = pack.Find(skillId) ?? throw new KeyNotFoundException("The " + pack.Id + " pack has no skill named '" + skillId + "'.");
            return new List<BundledSkill> { one };
        }

        private static bool IsEdited(string folder, InstalledPackSkill entry)
        {
            string skillFile = Path.Combine(folder, "SKILL.md");
            if (!File.Exists(skillFile))
            {
                return true;
            }

            string hash = SkillFileWriter.HashMarkdown(File.ReadAllText(skillFile).TrimStart('﻿'));
            return !string.Equals(hash, entry.ContentHash, StringComparison.Ordinal);
        }

        private List<InstalledPackSkill> LoadManifest()
        {
            string path = Path.Combine(_SkillsDirectory, ManifestFileName);
            if (!File.Exists(path))
            {
                return new List<InstalledPackSkill>();
            }

            try
            {
                List<InstalledPackSkill>? entries = JsonSerializer.Deserialize<List<InstalledPackSkill>>(File.ReadAllText(path));
                return entries == null ? new List<InstalledPackSkill>() : entries.FindAll((InstalledPackSkill e) => e != null && !string.IsNullOrWhiteSpace(e.Id));
            }
            catch (JsonException)
            {
                // A corrupt manifest means nothing is known to be pack-installed, so nothing is removed by mistake.
                return new List<InstalledPackSkill>();
            }
        }

        private void SaveManifest(List<InstalledPackSkill> entries)
        {
            Directory.CreateDirectory(_SkillsDirectory);
            string path = Path.Combine(_SkillsDirectory, ManifestFileName);
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(entries, _Json));
            File.Move(temp, path, true);
        }

        #endregion
    }
}
