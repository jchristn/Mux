namespace Test.Shared.Support
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Mux.Core.Models;
    using Mux.Core.Skills;

    /// <summary>
    /// A temporary on-disk layout for project-skill tests: a user skills directory, a git project, and a
    /// trust file path, all under one root the caller deletes when done.
    /// </summary>
    public sealed class ProjectSkillsFixture
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Creates the layout under a fresh temporary directory.
        /// </summary>
        public ProjectSkillsFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "mux-projskills-" + Guid.NewGuid().ToString("N"));
            UserSkills = Path.Combine(Root, "user-skills");
            Project = Path.Combine(Root, "project");
            TrustPath = Path.Combine(Root, "trusted-projects.json");
            Directory.CreateDirectory(UserSkills);
            Directory.CreateDirectory(Path.Combine(Project, ".git"));
        }

        #endregion

        #region Public-Members

        /// <summary>The temporary root holding everything else.</summary>
        public string Root { get; }

        /// <summary>The user skills directory.</summary>
        public string UserSkills { get; }

        /// <summary>The project root (contains a <c>.git</c> directory).</summary>
        public string Project { get; }

        /// <summary>The trust decisions file.</summary>
        public string TrustPath { get; }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Creates a runtime over the user skills with project skills enabled and the fixture's trust store.
        /// </summary>
        /// <returns>The runtime; the caller disposes it.</returns>
        public SkillRuntime CreateRuntime()
        {
            return new SkillRuntime(UserSkills, () => new List<SkillIndexEntry>(), () => { }, TimeSpan.FromSeconds(30))
            {
                ProjectSkillsEnabled = true,
                TrustStore = new ProjectTrustStore(TrustPath)
            };
        }

        #endregion
    }
}
