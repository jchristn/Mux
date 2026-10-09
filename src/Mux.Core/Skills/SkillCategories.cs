namespace Mux.Core.Skills
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.RegularExpressions;
    using Mux.Core.Models;
    using Mux.Core.Settings;

    /// <summary>
    /// The skill categorization rules shared by every surface. A skill's effective category is, in order: the
    /// per-user override in <c>~/.mux/skills.json</c>, the <c>category:</c> field of its <c>SKILL.md</c>, the shipped
    /// category of a default skill with that name, a category inferred from its tags, and finally <see cref="General"/>. Categories are kebab-case strings; the canonical
    /// values are in <see cref="Known"/>, and other well-formed values are allowed.
    /// </summary>
    public static class SkillCategories
    {
        #region Private-Members

        private static readonly Regex _Format = new Regex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // Tag to category, checked in order so the most specific tag wins (a "react" skill is frontend, not languages).
        private static readonly string[][] _TagRules =
        {
            new[] { "review", "review" },
            new[] { "security", "security" },
            new[] { "loop", "loops" },
            new[] { "debug", "debugging" },
            new[] { "test", "testing" },
            new[] { "testing", "testing" },
            new[] { "react", "frontend" },
            new[] { "frontend", "frontend" },
            new[] { "kubernetes", "kubernetes" },
            new[] { "helm", "kubernetes" },
            new[] { "openstack", "cloud" },
            new[] { "docker", "containers" },
            new[] { "containers", "containers" },
            new[] { "compose", "containers" },
            new[] { "terraform", "infrastructure" },
            new[] { "pulumi", "infrastructure" },
            new[] { "iac", "infrastructure" },
            new[] { "aws", "cloud" },
            new[] { "azure", "cloud" },
            new[] { "gcp", "cloud" },
            new[] { "cloud", "cloud" },
            new[] { "ci", "devops" },
            new[] { "devops", "devops" },
            new[] { "git", "git" },
            new[] { "github", "git" },
            new[] { "scaffold", "scaffolding" },
            new[] { "docs", "docs" },
            new[] { "documentation", "docs" },
            new[] { "hygiene", "hygiene" },
            new[] { "dotnet", "languages" },
            new[] { "javascript", "languages" },
            new[] { "typescript", "languages" },
            new[] { "python", "languages" },
            new[] { "java", "languages" },
            new[] { "cpp", "languages" },
            new[] { "go", "languages" },
            new[] { "rust", "languages" },
            new[] { "data", "data" },
            new[] { "workflow", "workflow" }
        };

        #endregion

        #region Public-Members

        /// <summary>The category used when nothing else applies.</summary>
        public const string General = "general";

        /// <summary>
        /// The canonical categories, in the order surfaces offer them. Other kebab-case values are allowed.
        /// </summary>
        public static readonly IReadOnlyList<string> Known = new[]
        {
            "git", "review", "testing", "debugging", "languages", "frontend", "devops", "containers", "kubernetes", "cloud",
            "infrastructure", "security", "data", "docs", "scaffolding", "hygiene", "workflow", "loops", "engineering",
            "product", "productivity", "research", "marketing", "compliance", "business", "general"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a value is a well-formed category: lowercase letters and digits separated by single hyphens.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>True when well-formed.</returns>
        public static bool IsValidFormat(string? value)
        {
            return !string.IsNullOrEmpty(value) && _Format.IsMatch(value);
        }

        /// <summary>
        /// Whether a value is one of <see cref="Known"/>.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>True when canonical.</returns>
        public static bool IsKnown(string? value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            foreach (string known in Known)
            {
                if (string.Equals(known, value, StringComparison.Ordinal)) return true;
            }

            return false;
        }

        /// <summary>
        /// Turns user input into a category: trims, lowercases, and turns spaces and underscores into hyphens
        /// (<c>"Code Review"</c> becomes <c>code-review</c>).
        /// </summary>
        /// <param name="value">The input.</param>
        /// <returns>The normalized category, or null when the input is blank.</returns>
        public static string? Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            StringBuilder builder = new StringBuilder();
            bool lastHyphen = false;
            foreach (char c in value.Trim().ToLowerInvariant())
            {
                char mapped = c == ' ' || c == '_' || c == '-' || c == '\t' ? '-' : c;
                if (mapped == '-')
                {
                    if (builder.Length == 0 || lastHyphen) continue;
                    lastHyphen = true;
                }
                else
                {
                    lastHyphen = false;
                }

                builder.Append(mapped);
            }

            string result = builder.ToString().TrimEnd('-');
            return result.Length == 0 ? null : result;
        }

        /// <summary>
        /// Normalizes user input and checks it, for surfaces that set an override.
        /// </summary>
        /// <param name="value">The input.</param>
        /// <param name="category">The normalized category, or null when the input is blank (meaning clear).</param>
        /// <param name="error">Why the input was refused, or empty.</param>
        /// <returns>True when the input is blank or normalizes to a well-formed category.</returns>
        public static bool TryParse(string? value, out string? category, out string error)
        {
            error = string.Empty;
            category = Normalize(value);
            if (category == null) return true;
            if (!IsValidFormat(category) || category.Length > 40)
            {
                error = "A category uses letters, digits, and single hyphens, at most 40 characters (for example code-review).";
                category = null;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Infers a category from a skill's tags, or returns null when no tag matches.
        /// </summary>
        /// <param name="tags">The tags.</param>
        /// <returns>The inferred category, or null.</returns>
        public static string? Infer(IEnumerable<string>? tags)
        {
            if (tags == null) return null;
            List<string> lowered = new List<string>();
            foreach (string tag in tags)
            {
                if (!string.IsNullOrWhiteSpace(tag)) lowered.Add(tag.Trim().ToLowerInvariant());
            }

            foreach (string[] rule in _TagRules)
            {
                if (lowered.Contains(rule[0])) return rule[1];
            }

            return null;
        }

        /// <summary>
        /// The effective category for a manifest and an optional override.
        /// </summary>
        /// <param name="manifest">The skill manifest. Must not be null.</param>
        /// <param name="overrideCategory">The per-user override, or null.</param>
        /// <returns>The effective category.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="manifest"/> is null.</exception>
        public static string Resolve(SkillManifest manifest, string? overrideCategory)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            string? fromOverride = Normalize(overrideCategory);
            if (fromOverride != null && IsValidFormat(fromOverride)) return fromOverride;
            string? fromFile = Normalize(manifest.Category);
            if (fromFile != null && IsValidFormat(fromFile)) return fromFile;

            // Default skills seeded before categories existed have no category line; use the shipped category.
            return DefaultSkillCategories.For(manifest.Name) ?? Infer(manifest.Tags) ?? General;
        }

        /// <summary>
        /// The effective category for a loaded skill, honoring its override.
        /// </summary>
        /// <param name="skill">The skill. Must not be null.</param>
        /// <returns>The effective category.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="skill"/> is null.</exception>
        public static string Resolve(Skill skill)
        {
            if (skill == null) throw new ArgumentNullException(nameof(skill));
            return Resolve(skill.Manifest, skill.CategoryOverride);
        }

        /// <summary>
        /// The per-user overrides in <c>skills.json</c>, keyed by skill id (case-insensitive).
        /// </summary>
        /// <returns>The overrides.</returns>
        public static Dictionary<string, string> LoadOverrides()
        {
            Dictionary<string, string> overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (SkillIndexEntry entry in SettingsLoader.LoadSkillIndex())
            {
                if (!string.IsNullOrWhiteSpace(entry.Id) && !string.IsNullOrWhiteSpace(entry.Category))
                {
                    overrides[entry.Id] = entry.Category!;
                }
            }

            return overrides;
        }

        /// <summary>
        /// Copies the overrides onto loaded skills so <see cref="Resolve(Skill)"/> sees them.
        /// </summary>
        /// <param name="skills">The skills.</param>
        /// <param name="overrides">The overrides by skill id, or null to load them.</param>
        public static void ApplyOverrides(IEnumerable<Skill> skills, IReadOnlyDictionary<string, string>? overrides = null)
        {
            if (skills == null) return;
            IReadOnlyDictionary<string, string> map = overrides ?? LoadOverrides();
            foreach (Skill skill in skills)
            {
                skill.CategoryOverride = map.TryGetValue(skill.Manifest.Name, out string? value) ? value : null;
            }
        }

        /// <summary>
        /// Counts skills per effective category, ordered by the canonical order and then by name.
        /// </summary>
        /// <param name="skills">The skills (with overrides applied).</param>
        /// <returns>Category and count pairs.</returns>
        public static List<SkillCategoryCount> Count(IEnumerable<Skill> skills)
        {
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (Skill skill in skills ?? Array.Empty<Skill>())
            {
                string category = Resolve(skill);
                counts[category] = counts.TryGetValue(category, out int n) ? n + 1 : 1;
            }

            List<SkillCategoryCount> result = new List<SkillCategoryCount>();
            foreach (KeyValuePair<string, int> pair in counts)
            {
                result.Add(new SkillCategoryCount { Category = pair.Key, Count = pair.Value, Known = IsKnown(pair.Key) });
            }

            result.Sort((SkillCategoryCount a, SkillCategoryCount b) => Order(a.Category).CompareTo(Order(b.Category)) != 0
                ? Order(a.Category).CompareTo(Order(b.Category))
                : string.CompareOrdinal(a.Category, b.Category));
            return result;
        }

        /// <summary>
        /// The sort position of a category: canonical categories first in canonical order, then others.
        /// </summary>
        /// <param name="category">The category.</param>
        /// <returns>The position.</returns>
        public static int Order(string? category)
        {
            for (int i = 0; i < Known.Count; i++)
            {
                if (string.Equals(Known[i], category, StringComparison.Ordinal)) return i;
            }

            return Known.Count;
        }

        #endregion
    }
}
