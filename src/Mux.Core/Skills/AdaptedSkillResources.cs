namespace Mux.Core.Skills
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;

    /// <summary>
    /// Files merged into existing default skills from imported skill collections (the "Adapt" items in
    /// SKILLS_TO_CONSIDER.md): review rule sets, analyzers, and templates. They are embedded under
    /// <c>Mux.Core.Skills.Adapted/&lt;skill-id&gt;/&lt;path&gt;</c> and seeded into the skill's <c>resources/</c> folder,
    /// where the skill body reaches them as <c>${SKILL_DIR}/resources/&lt;path&gt;</c>.
    /// </summary>
    public static class AdaptedSkillResources
    {
        #region Private-Members

        private static readonly Lazy<Dictionary<string, Dictionary<string, string>>> _BySkill =
            new Lazy<Dictionary<string, Dictionary<string, string>>>(Load);

        #endregion

        #region Public-Members

        /// <summary>The logical-name prefix of the embedded adapted files.</summary>
        public const string Prefix = "Mux.Core.Skills.Adapted/";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Returns the adapted files for one skill, keyed by their path under <c>resources/</c>.
        /// </summary>
        /// <param name="skillId">The skill id.</param>
        /// <returns>The files (path to text); empty when the skill has none.</returns>
        public static IReadOnlyDictionary<string, string> For(string skillId)
        {
            if (!string.IsNullOrEmpty(skillId) && _BySkill.Value.TryGetValue(skillId, out Dictionary<string, string>? files))
            {
                return files;
            }

            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        /// <summary>
        /// Adds the skill's adapted files to a definition's resources (as <c>resources/&lt;path&gt;</c>). Returns the
        /// same instance.
        /// </summary>
        /// <param name="definition">The definition. Must not be null.</param>
        /// <returns>The definition, for chaining.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="definition"/> is null.</exception>
        public static DefaultSkillDef AddTo(DefaultSkillDef definition)
        {
            ArgumentNullException.ThrowIfNull(definition);
            foreach (KeyValuePair<string, string> file in For(definition.Id))
            {
                definition.Resources["resources/" + file.Key] = file.Value;
            }

            return definition;
        }

        #endregion

        #region Private-Methods

        private static Dictionary<string, Dictionary<string, string>> Load()
        {
            Dictionary<string, Dictionary<string, string>> bySkill = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            Assembly assembly = typeof(AdaptedSkillResources).Assembly;
            foreach (string name in assembly.GetManifestResourceNames())
            {
                string normalized = name.Replace('\\', '/');
                if (!normalized.StartsWith(Prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                string relative = normalized.Substring(Prefix.Length);
                int slash = relative.IndexOf('/');
                if (slash <= 0 || slash == relative.Length - 1)
                {
                    continue;
                }

                using (Stream? stream = assembly.GetManifestResourceStream(name))
                {
                    if (stream == null)
                    {
                        continue;
                    }

                    using (StreamReader reader = new StreamReader(stream))
                    {
                        string skillId = relative.Substring(0, slash);
                        if (!bySkill.TryGetValue(skillId, out Dictionary<string, string>? files))
                        {
                            files = new Dictionary<string, string>(StringComparer.Ordinal);
                            bySkill[skillId] = files;
                        }

                        files[relative.Substring(slash + 1)] = reader.ReadToEnd().Replace("\r\n", "\n");
                    }
                }
            }

            return bySkill;
        }

        #endregion
    }
}
