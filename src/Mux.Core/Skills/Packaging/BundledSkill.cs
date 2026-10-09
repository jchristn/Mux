namespace Mux.Core.Skills.Packaging
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// One folder-based skill shipped inside mux (a bundled default or a pack skill): its id and every file in its
    /// folder, keyed by forward-slash path relative to the folder (including <c>SKILL.md</c>).
    /// </summary>
    public sealed class BundledSkill
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="BundledSkill"/> class.
        /// </summary>
        /// <param name="id">The skill id (its folder name). Must not be blank.</param>
        /// <param name="files">The files by relative path. Must contain <c>SKILL.md</c>.</param>
        /// <exception cref="ArgumentException">Thrown when the id is blank or <c>SKILL.md</c> is missing.</exception>
        public BundledSkill(string id, IReadOnlyDictionary<string, byte[]> files)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A bundled skill needs an id.", nameof(id));
            if (files == null || !files.ContainsKey("SKILL.md")) throw new ArgumentException("Bundled skill '" + id + "' has no SKILL.md.", nameof(files));
            Id = id;
            Files = new Dictionary<string, byte[]>(files, StringComparer.Ordinal);
        }

        #endregion

        #region Public-Members

        /// <summary>The skill id (its folder name).</summary>
        public string Id { get; }

        /// <summary>Every file by forward-slash path relative to the skill folder.</summary>
        public IReadOnlyDictionary<string, byte[]> Files { get; }

        /// <summary>The <c>SKILL.md</c> text (UTF-8).</summary>
        public string SkillMarkdown => new UTF8Encoding(false).GetString(Files["SKILL.md"]).TrimStart('﻿');

        #endregion
    }
}
