namespace Mux.Core.Skills
{
    using System;
    using System.Collections.Generic;
    using Mux.Core.Models;

    /// <summary>
    /// An immutable snapshot of the loaded skills. It answers the queries the tool provider and the UI need
    /// — status for every skill, lookup by name, and the set of enabled, valid skills that the model should
    /// be told about — without touching disk. A refresh builds a new catalog rather than mutating this one.
    /// </summary>
    public sealed class SkillCatalog
    {
        #region Private-Members

        private readonly List<Skill> _Skills;
        private readonly Dictionary<string, Skill> _ByName;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="SkillCatalog"/> class.
        /// </summary>
        /// <param name="skills">The loaded skills, valid and invalid. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="skills"/> is null.</exception>
        public SkillCatalog(IReadOnlyList<Skill> skills)
        {
            if (skills == null) throw new ArgumentNullException(nameof(skills));

            _Skills = new List<Skill>(skills);
            _ByName = new Dictionary<string, Skill>(StringComparer.OrdinalIgnoreCase);
            foreach (Skill skill in _Skills)
            {
                string name = skill.Manifest.Name;
                if (!string.IsNullOrWhiteSpace(name) && !_ByName.ContainsKey(name))
                {
                    _ByName[name] = skill;
                }
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Returns the enabled, valid skills — the ones the model should be told about and allowed to run.
        /// </summary>
        /// <returns>The enabled, valid skills.</returns>
        public IReadOnlyList<Skill> GetEnabledValidSkills()
        {
            List<Skill> result = new List<Skill>();
            foreach (Skill skill in _Skills)
            {
                if (skill.IsValid && skill.Manifest.Enabled)
                {
                    result.Add(skill);
                }
            }

            return result;
        }

        /// <summary>
        /// Returns a detached status snapshot for every loaded skill, valid or not.
        /// </summary>
        /// <returns>The per-skill status list.</returns>
        public IReadOnlyList<SkillStatus> GetStatus()
        {
            List<SkillStatus> statuses = new List<SkillStatus>();
            foreach (Skill skill in _Skills)
            {
                statuses.Add(new SkillStatus
                {
                    Name = skill.Manifest.Name,
                    Title = skill.Manifest.Title,
                    Enabled = skill.Manifest.Enabled,
                    Valid = skill.IsValid,
                    CommandCount = skill.Manifest.Commands.Count,
                    Tags = new List<string>(skill.Manifest.Tags),
                    Error = skill.IsValid || skill.Validation.Errors.Count == 0 ? null : skill.Validation.Errors[0]
                });
            }

            return statuses;
        }

        /// <summary>
        /// Looks up a skill by name.
        /// </summary>
        /// <param name="name">The skill name.</param>
        /// <param name="skill">The matching skill when found.</param>
        /// <returns><c>true</c> when a skill with the name exists; otherwise <c>false</c>.</returns>
        public bool TryGet(string name, out Skill skill)
        {
            if (name != null && _ByName.TryGetValue(name, out Skill? found))
            {
                skill = found;
                return true;
            }

            skill = new Skill();
            return false;
        }

        #endregion
    }
}
