namespace Mux.Core.Skills
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    /// <summary>
    /// The curated set of skills mux seeds into an empty skills directory on first run, so the feature is
    /// useful immediately and every default doubles as a worked example. Each entry is a complete, valid
    /// <c>SKILL.md</c>; <see cref="SeedInto"/> writes any that are missing without overwriting user edits.
    /// The git and .NET skills default to <c>pwsh</c> for cross-platform reach; a real machine needs the
    /// named interpreter installed to run them, but they always validate.
    /// </summary>
    public static class DefaultSkillLibrary
    {
        /// <summary>
        /// Returns the default skills as a map of id to <c>SKILL.md</c> content, merged from the per-category
        /// definition classes so each category stays a focused, self-contained file.
        /// </summary>
        /// <returns>The default skills, keyed by id.</returns>
        /// <exception cref="InvalidOperationException">Thrown when two categories declare the same skill id.</exception>
        public static IReadOnlyDictionary<string, string> All()
        {
            Dictionary<string, string> skills = new Dictionary<string, string>(StringComparer.Ordinal);

            Merge(skills, DefaultGitSkills.All());
            Merge(skills, DefaultDotnetSkills.All());
            Merge(skills, DefaultHygieneSkills.All());
            Merge(skills, DefaultScaffoldDocsSkills.All());
            Merge(skills, DefaultWorkflowUtilitySkills.All());

            return skills;
        }

        private static void Merge(Dictionary<string, string> target, IReadOnlyDictionary<string, string> source)
        {
            foreach (KeyValuePair<string, string> entry in source)
            {
                if (target.ContainsKey(entry.Key))
                {
                    throw new InvalidOperationException("Duplicate default skill id '" + entry.Key + "'.");
                }

                target[entry.Key] = entry.Value;
            }
        }

        /// <summary>
        /// Writes any default skill whose directory does not already exist into <paramref name="skillsDirectory"/>.
        /// Existing skills are left untouched, so user edits and removals survive.
        /// </summary>
        /// <param name="skillsDirectory">The skills directory. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="skillsDirectory"/> is null.</exception>
        public static void SeedInto(string skillsDirectory)
        {
            if (skillsDirectory == null) throw new ArgumentNullException(nameof(skillsDirectory));

            Directory.CreateDirectory(skillsDirectory);
            foreach (KeyValuePair<string, string> skill in All())
            {
                string dir = Path.Combine(skillsDirectory, skill.Key);
                if (Directory.Exists(dir))
                {
                    continue;
                }

                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "SKILL.md"), skill.Value);
            }
        }
    }
}
