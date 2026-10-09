namespace Mux.Core.Skills.Packaging
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.Json;

    /// <summary>
    /// The skill packs available to install: those embedded in this build of mux, or the same layout read from a
    /// folder (one subfolder per pack holding <c>pack.json</c> and one subfolder per skill).
    /// </summary>
    public sealed class SkillPackCatalog
    {
        #region Private-Members

        private static readonly Lazy<SkillPackCatalog> _Embedded = new Lazy<SkillPackCatalog>(() => FromFiles(EmbeddedSkillFiles.ReadEmbedded(EmbeddedSkillFiles.PacksPrefix)));

        private readonly List<SkillPack> _Packs;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="SkillPackCatalog"/> class.
        /// </summary>
        /// <param name="packs">The packs. Null is treated as none.</param>
        public SkillPackCatalog(IEnumerable<SkillPack>? packs)
        {
            _Packs = new List<SkillPack>(packs ?? Array.Empty<SkillPack>());
            _Packs.Sort((SkillPack a, SkillPack b) => string.CompareOrdinal(a.Id, b.Id));
        }

        /// <summary>The packs embedded in this build of mux.</summary>
        public static SkillPackCatalog Embedded => _Embedded.Value;

        /// <summary>
        /// Reads packs from a folder on disk.
        /// </summary>
        /// <param name="directory">The folder.</param>
        /// <returns>The catalog.</returns>
        public static SkillPackCatalog FromDirectory(string directory)
        {
            return FromFiles(EmbeddedSkillFiles.ReadDirectory(directory));
        }

        /// <summary>
        /// Builds a catalog from files keyed <c>&lt;pack&gt;/pack.json</c> and <c>&lt;pack&gt;/&lt;skill&gt;/&lt;path&gt;</c>.
        /// A pack without <c>pack.json</c> still loads, titled by its id.
        /// </summary>
        /// <param name="files">The files.</param>
        /// <returns>The catalog.</returns>
        public static SkillPackCatalog FromFiles(IReadOnlyDictionary<string, byte[]> files)
        {
            List<SkillPack> packs = new List<SkillPack>();
            foreach (KeyValuePair<string, Dictionary<string, byte[]>> group in EmbeddedSkillFiles.GroupByFolder(files))
            {
                SkillPack pack = new SkillPack { Id = group.Key, Title = group.Key };
                if (group.Value.TryGetValue("pack.json", out byte[]? json))
                {
                    ApplyMetadata(pack, json);
                }

                foreach (KeyValuePair<string, Dictionary<string, byte[]>> skill in EmbeddedSkillFiles.GroupByFolder(group.Value))
                {
                    if (skill.Value.ContainsKey("SKILL.md"))
                    {
                        pack.Skills.Add(new BundledSkill(skill.Key, skill.Value));
                    }
                }

                pack.Skills.Sort((BundledSkill a, BundledSkill b) => string.CompareOrdinal(a.Id, b.Id));
                packs.Add(pack);
            }

            return new SkillPackCatalog(packs);
        }

        #endregion

        #region Public-Members

        /// <summary>The packs, sorted by id.</summary>
        public IReadOnlyList<SkillPack> Packs => _Packs;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Finds a pack by id.
        /// </summary>
        /// <param name="id">The pack id (case-insensitive).</param>
        /// <returns>The pack, or null.</returns>
        public SkillPack? Find(string id)
        {
            return _Packs.Find((SkillPack pack) => string.Equals(pack.Id, (id ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
        }

        #endregion

        #region Private-Methods

        private static void ApplyMetadata(SkillPack pack, byte[] json)
        {
            try
            {
                using (JsonDocument document = JsonDocument.Parse(Encoding.UTF8.GetString(json).TrimStart('﻿')))
                {
                    JsonElement root = document.RootElement;
                    pack.Title = Read(root, "title") ?? pack.Title;
                    pack.Description = Read(root, "description") ?? string.Empty;
                    pack.Category = Read(root, "category") ?? string.Empty;
                    pack.Source = Read(root, "source") ?? string.Empty;
                    pack.License = Read(root, "license") ?? string.Empty;
                }
            }
            catch (JsonException)
            {
                // A malformed pack.json leaves the defaults (the id as the title).
            }
        }

        private static string? Read(JsonElement root, string name)
        {
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String)
                {
                    string? value = property.Value.GetString();
                    return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                }
            }

            return null;
        }

        #endregion
    }
}
