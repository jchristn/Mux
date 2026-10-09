namespace Mux.Core.Skills.Packaging
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The folder-based default skills shipped inside mux (<c>src/Mux.Core/Skills/Bundled/&lt;id&gt;/</c>). They are
    /// seeded exactly like the C#-defined defaults: on first run, topped up on upgrade, never overwriting user edits,
    /// and not resurrected after the user deletes them.
    /// </summary>
    public sealed class BundledSkillSet
    {
        #region Private-Members

        private static readonly Lazy<BundledSkillSet> _Embedded = new Lazy<BundledSkillSet>(() => FromFiles(EmbeddedSkillFiles.ReadEmbedded(EmbeddedSkillFiles.BundledPrefix)));

        private readonly List<BundledSkill> _Skills;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="BundledSkillSet"/> class.
        /// </summary>
        /// <param name="skills">The skills. Null is treated as none.</param>
        public BundledSkillSet(IEnumerable<BundledSkill>? skills)
        {
            _Skills = new List<BundledSkill>(skills ?? Array.Empty<BundledSkill>());
            _Skills.Sort((BundledSkill a, BundledSkill b) => string.CompareOrdinal(a.Id, b.Id));
        }

        /// <summary>
        /// The bundled skills embedded in this build of mux.
        /// </summary>
        public static BundledSkillSet Embedded => _Embedded.Value;

        /// <summary>
        /// An empty set.
        /// </summary>
        public static BundledSkillSet Empty { get; } = new BundledSkillSet(null);

        /// <summary>
        /// Reads the same layout from a folder on disk: one subfolder per skill, each with a <c>SKILL.md</c>.
        /// </summary>
        /// <param name="directory">The folder.</param>
        /// <returns>The set; subfolders without a <c>SKILL.md</c> are ignored.</returns>
        public static BundledSkillSet FromDirectory(string directory)
        {
            return FromFiles(EmbeddedSkillFiles.ReadDirectory(directory));
        }

        /// <summary>
        /// Builds a set from files keyed <c>&lt;id&gt;/&lt;relative path&gt;</c>.
        /// </summary>
        /// <param name="files">The files.</param>
        /// <returns>The set; groups without a <c>SKILL.md</c> are ignored.</returns>
        public static BundledSkillSet FromFiles(IReadOnlyDictionary<string, byte[]> files)
        {
            List<BundledSkill> skills = new List<BundledSkill>();
            foreach (KeyValuePair<string, Dictionary<string, byte[]>> group in EmbeddedSkillFiles.GroupByFolder(files))
            {
                if (group.Value.ContainsKey("SKILL.md"))
                {
                    skills.Add(new BundledSkill(group.Key, group.Value));
                }
            }

            return new BundledSkillSet(skills);
        }

        #endregion

        #region Public-Members

        /// <summary>The skills, sorted by id.</summary>
        public IReadOnlyList<BundledSkill> Skills => _Skills;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Finds a skill by id.
        /// </summary>
        /// <param name="id">The id (case-sensitive, as the folder is named).</param>
        /// <returns>The skill, or null.</returns>
        public BundledSkill? Find(string id)
        {
            return _Skills.Find((BundledSkill skill) => string.Equals(skill.Id, id, StringComparison.Ordinal));
        }

        #endregion
    }
}
