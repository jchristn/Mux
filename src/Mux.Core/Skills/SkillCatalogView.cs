namespace Mux.Core.Skills
{
    using System;
    using System.Collections.Generic;
    using Mux.Core.Enums;
    using Mux.Core.Models;

    /// <summary>
    /// The skills visible from one working directory: the merged catalog (project skills shadowing user
    /// skills), the tool provider over it, the project root it was resolved for, and that project's trust
    /// level. Produced by <see cref="SkillRuntime.GetView(string?)"/>; immutable once built.
    /// </summary>
    public sealed class SkillCatalogView
    {
        #region Private-Members

        private readonly SkillCatalog _Catalog;
        private readonly SkillToolProvider _Provider;
        private readonly string? _ProjectRoot;
        private readonly ProjectTrustLevelEnum _TrustLevel;
        private readonly string _Signature;
        private readonly DateTime _BuiltUtc;
        private readonly Dictionary<string, bool> _Relevance = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly object _RelevanceSync = new object();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="SkillCatalogView"/> class.
        /// </summary>
        /// <param name="catalog">The merged catalog. Must not be null.</param>
        /// <param name="provider">The tool provider over <paramref name="catalog"/>. Must not be null.</param>
        /// <param name="projectRoot">The project root, or null for a user-only view.</param>
        /// <param name="trustLevel">The project's trust level.</param>
        /// <param name="signature">A fingerprint of the catalog contents, used to detect changes. Null is treated as empty.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="catalog"/> or <paramref name="provider"/> is null.</exception>
        public SkillCatalogView(SkillCatalog catalog, SkillToolProvider provider, string? projectRoot, ProjectTrustLevelEnum trustLevel, string? signature)
        {
            _Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _Provider = provider ?? throw new ArgumentNullException(nameof(provider));
            _ProjectRoot = projectRoot;
            _TrustLevel = trustLevel;
            _Signature = signature ?? string.Empty;
            _BuiltUtc = DateTime.UtcNow;
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The merged catalog.
        /// </summary>
        public SkillCatalog Catalog => _Catalog;

        /// <summary>
        /// The tool provider over <see cref="Catalog"/>.
        /// </summary>
        public SkillToolProvider Provider => _Provider;

        /// <summary>
        /// The project root this view was resolved for, or null for a user-only view.
        /// </summary>
        public string? ProjectRoot => _ProjectRoot;

        /// <summary>
        /// The project's trust level (always <see cref="ProjectTrustLevelEnum.Unknown"/> for a user-only view).
        /// </summary>
        public ProjectTrustLevelEnum TrustLevel => _TrustLevel;

        /// <summary>
        /// A fingerprint of the catalog contents.
        /// </summary>
        public string Signature => _Signature;

        /// <summary>
        /// When the view was built, in UTC.
        /// </summary>
        public DateTime BuiltUtc => _BuiltUtc;

        /// <summary>
        /// The number of project skills whose commands are blocked until the project is trusted.
        /// </summary>
        public int BlockedCount
        {
            get
            {
                int count = 0;
                foreach (Skill skill in _Catalog.All)
                {
                    if (skill.CommandsBlocked)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Returns whether a skill should be listed for a project root: every <c>requiresTools</c> entry is on PATH
        /// and, when the skill has <c>appliesTo</c> globs, at least one matches under the root. Glob results are
        /// cached per root and skill for the life of the view; tool presence is cached by
        /// <paramref name="tools"/>. A null root skips the glob check. Thread-safe.
        /// </summary>
        /// <param name="skill">The skill. Must not be null.</param>
        /// <param name="projectRoot">The project root the globs are evaluated against, or null.</param>
        /// <param name="matcher">The glob matcher. Must not be null.</param>
        /// <param name="tools">The tool presence cache. Must not be null.</param>
        /// <returns><c>true</c> when the skill is relevant.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="skill"/>, <paramref name="matcher"/>, or <paramref name="tools"/> is null.</exception>
        public bool IsRelevant(Skill skill, string? projectRoot, AppliesToMatcher matcher, ToolPresenceCache tools)
        {
            if (skill == null) throw new ArgumentNullException(nameof(skill));
            if (matcher == null) throw new ArgumentNullException(nameof(matcher));
            if (tools == null) throw new ArgumentNullException(nameof(tools));

            if (!tools.AllAvailable(skill.Manifest.RequiresTools))
            {
                return false;
            }

            if (skill.Manifest.AppliesTo.Count == 0 || projectRoot == null)
            {
                return true;
            }

            string key = projectRoot + "|" + skill.Manifest.Name;
            lock (_RelevanceSync)
            {
                if (_Relevance.TryGetValue(key, out bool cached))
                {
                    return cached;
                }
            }

            bool relevant = matcher.AnyMatch(projectRoot, skill.Manifest.AppliesTo);
            lock (_RelevanceSync)
            {
                _Relevance[key] = relevant;
            }

            return relevant;
        }

        #endregion
    }
}
