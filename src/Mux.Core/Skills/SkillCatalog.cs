namespace Mux.Core.Skills
{
    using System;
    using System.Collections.Generic;
    using Mux.Core.Enums;
    using Mux.Core.Models;

    /// <summary>
    /// An immutable snapshot of the loaded skills. It answers the queries the tool provider and the UI need
    /// (status for every skill, lookup by name, and the set of usable skills the model should be told about)
    /// without touching disk. A refresh builds a new catalog rather than mutating this one. When a catalog
    /// merges project and user skills, a project skill shadows a user skill with the same id.
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
        /// Builds a catalog from project and user skills. Project skills come first and shadow any user skill
        /// with the same id (case-insensitive); each shadowing project skill is marked
        /// <see cref="Skill.ShadowsUserSkill"/> and the hidden user skill is left out.
        /// </summary>
        /// <param name="projectSkills">The project skills. Null is treated as empty.</param>
        /// <param name="userSkills">The user skills. Null is treated as empty.</param>
        /// <returns>The merged catalog.</returns>
        public static SkillCatalog Merge(IReadOnlyList<Skill>? projectSkills, IReadOnlyList<Skill>? userSkills)
        {
            List<Skill> merged = new List<Skill>();
            HashSet<string> projectNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (projectSkills != null)
            {
                foreach (Skill skill in projectSkills)
                {
                    string name = skill.Manifest.Name;
                    if (!string.IsNullOrWhiteSpace(name) && !projectNames.Add(name))
                    {
                        // A duplicate id in a lower-precedence project root is hidden by the first one.
                        continue;
                    }

                    merged.Add(skill);
                }
            }

            if (userSkills != null)
            {
                foreach (Skill skill in userSkills)
                {
                    string name = skill.Manifest.Name;
                    if (!string.IsNullOrWhiteSpace(name) && projectNames.Contains(name))
                    {
                        foreach (Skill projectSkill in merged)
                        {
                            if (string.Equals(projectSkill.Manifest.Name, name, StringComparison.OrdinalIgnoreCase))
                            {
                                projectSkill.ShadowsUserSkill = true;
                            }
                        }

                        continue;
                    }

                    merged.Add(skill);
                }
            }

            return new SkillCatalog(merged);
        }

        /// <summary>
        /// Every skill in the catalog, usable or not, in catalog order.
        /// </summary>
        public IReadOnlyList<Skill> All => _Skills;

        /// <summary>
        /// Returns the usable skills (valid, enabled, and not blocked by project trust): the ones the model
        /// may be told about and allowed to run.
        /// </summary>
        /// <returns>The usable skills.</returns>
        public IReadOnlyList<Skill> GetEnabledValidSkills()
        {
            List<Skill> result = new List<Skill>();
            foreach (Skill skill in _Skills)
            {
                if (skill.IsUsable)
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
                    Error = skill.IsValid || skill.Validation.Errors.Count == 0 ? null : skill.Validation.Errors[0],
                    Scope = skill.Scope == SkillScopeEnum.Project ? "project" : "user",
                    CommandsBlocked = skill.CommandsBlocked,
                    ShadowsUserSkill = skill.ShadowsUserSkill,
                    UserInvocable = skill.Manifest.UserInvocable,
                    ArgumentHint = skill.Manifest.ArgumentHint,
                    Warnings = new List<string>(skill.Validation.Warnings),
                    Category = skill.Category,
                    CategoryOverridden = skill.CategoryOverride != null
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
