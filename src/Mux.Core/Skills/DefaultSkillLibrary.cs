namespace Mux.Core.Skills
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    /// <summary>
    /// The curated set of skills mux seeds into the skills directory, so the feature is useful immediately
    /// and every default doubles as a worked example. Each entry is a complete, valid <c>SKILL.md</c>.
    /// <see cref="SeedNewInto"/> is the startup path — it seeds newly shipped defaults on upgrade while
    /// honoring deletions via a manifest; <see cref="SeedInto"/> is the simpler write-any-missing form.
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
            return All(Packaging.BundledSkillSet.Embedded);
        }

        /// <summary>
        /// Returns the default skills (the C#-defined ones plus the given folder-based bundled skills) as a map of id
        /// to <c>SKILL.md</c> content.
        /// </summary>
        /// <param name="bundled">The folder-based bundled skills to include. Null is treated as none.</param>
        /// <returns>The default skills, keyed by id.</returns>
        /// <exception cref="InvalidOperationException">Thrown when two skills declare the same id.</exception>
        public static IReadOnlyDictionary<string, string> All(Packaging.BundledSkillSet? bundled)
        {
            Dictionary<string, string> skills = new Dictionary<string, string>(StringComparer.Ordinal);

            Merge(skills, DefaultGitSkills.All());
            Merge(skills, DefaultDotnetSkills.All());
            Merge(skills, DefaultHygieneSkills.All());
            Merge(skills, DefaultScaffoldDocsSkills.All());
            Merge(skills, DefaultWorkflowUtilitySkills.All());

            Dictionary<string, string> built = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (DefaultSkillDef definition in Definitions())
            {
                if (built.ContainsKey(definition.Id))
                {
                    throw new InvalidOperationException("Duplicate default skill id '" + definition.Id + "'.");
                }

                built[definition.Id] = DefaultSkillBuilder.Build(definition);
            }

            Merge(skills, built);

            // Folder-based defaults (Skills/Bundled/<id>/) carry their own SKILL.md and any scripts, references, or
            // assets beside it; WriteSkill writes those files too.
            Dictionary<string, string> folders = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Packaging.BundledSkill skill in (bundled ?? Packaging.BundledSkillSet.Empty).Skills)
            {
                folders[skill.Id] = skill.SkillMarkdown;
            }

            Merge(skills, folders);
            return skills;
        }

        /// <summary>
        /// Returns the default skills that are declared as <see cref="DefaultSkillDef"/> data (the toolchain and
        /// playbook families), in catalog order. The older categories declare <c>SKILL.md</c> text directly and are
        /// only reachable through <see cref="All"/>.
        /// </summary>
        /// <returns>The definitions.</returns>
        public static IReadOnlyList<DefaultSkillDef> Definitions()
        {
            List<DefaultSkillDef> definitions = new List<DefaultSkillDef>();
            definitions.AddRange(DefaultProjectSkills.All());
            definitions.AddRange(DefaultJavaScriptSkills.All());
            definitions.AddRange(DefaultPythonSkills.All());
            definitions.AddRange(DefaultReactSkills.All());
            definitions.AddRange(DefaultJavaSkills.All());
            definitions.AddRange(DefaultCppSkills.All());
            definitions.AddRange(DefaultGoSkills.All());
            definitions.AddRange(DefaultRustSkills.All());
            definitions.AddRange(DefaultContainerSkills.All());
            definitions.AddRange(DefaultKubernetesSkills.All());
            definitions.AddRange(DefaultOpenStackSkills.All());
            definitions.AddRange(DefaultAwsSkills.All());
            definitions.AddRange(DefaultAzureSkills.All());
            definitions.AddRange(DefaultGcpSkills.All());
            definitions.AddRange(DefaultDigitalOceanSkills.All());
            definitions.AddRange(DefaultRackspaceSkills.All());
            definitions.AddRange(DefaultEdgePlatformSkills.All());
            definitions.AddRange(DefaultRegionalCloudSkills.All());
            definitions.AddRange(DefaultIacSkills.All());
            definitions.AddRange(DefaultReviewSkills.All());
            definitions.AddRange(DefaultAgentPlaybookSkills.All());
            definitions.AddRange(DefaultLoopSkills.All());
            return definitions;
        }

        /// <summary>
        /// Returns the extra files each default skill ships with (beyond <c>SKILL.md</c>), keyed by skill id and
        /// then by path relative to the skill directory. Skills without extra files are omitted.
        /// </summary>
        /// <returns>The resources by skill id.</returns>
        public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> AllResources()
        {
            Dictionary<string, IReadOnlyDictionary<string, string>> resources = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
            foreach (DefaultSkillDef definition in Definitions())
            {
                if (definition.Resources.Count > 0)
                {
                    resources[definition.Id] = new Dictionary<string, string>(definition.Resources, StringComparer.Ordinal);
                }
            }

            return resources;
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
            SeedInto(skillsDirectory, Packaging.BundledSkillSet.Embedded);
        }

        /// <summary>
        /// Writes any default skill (C#-defined or from <paramref name="bundled"/>) whose directory does not already
        /// exist into <paramref name="skillsDirectory"/>.
        /// </summary>
        /// <param name="skillsDirectory">The skills directory. Must not be null.</param>
        /// <param name="bundled">The folder-based bundled skills. Null is treated as none.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="skillsDirectory"/> is null.</exception>
        public static void SeedInto(string skillsDirectory, Packaging.BundledSkillSet? bundled)
        {
            if (skillsDirectory == null) throw new ArgumentNullException(nameof(skillsDirectory));

            Directory.CreateDirectory(skillsDirectory);
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> resources = AllResources();
            foreach (KeyValuePair<string, string> skill in All(bundled))
            {
                string dir = Path.Combine(skillsDirectory, skill.Key);
                if (Directory.Exists(dir))
                {
                    continue;
                }

                WriteSkill(dir, skill.Key, skill.Value, resources, bundled);
            }
        }

        /// <summary>
        /// The name of the manifest file, stored inside the skills directory, that records which default ids
        /// have ever been seeded. It is a dot-prefixed plain-text file (one id per line) and is ignored by
        /// skill discovery, which only considers subdirectories containing a <c>SKILL.md</c>.
        /// </summary>
        public const string SeededManifestFileName = ".seeded-defaults";

        /// <summary>
        /// Seeds default skills that have never been seeded before, so newly shipped defaults arrive on
        /// upgrade while a default the user has deliberately deleted is not resurrected. A default is seeded
        /// only when its id is absent from the manifest; its id is then recorded regardless of whether its
        /// directory already existed. Existing skill directories are never overwritten. The first time this
        /// runs against a pre-existing library (no manifest yet), every default is treated as new, which is
        /// what upgrades an older install to the full catalog.
        /// </summary>
        /// <param name="skillsDirectory">The skills directory. Must not be null.</param>
        /// <returns>The ids of the defaults written by this call (empty when nothing was added).</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="skillsDirectory"/> is null.</exception>
        public static IReadOnlyList<string> SeedNewInto(string skillsDirectory)
        {
            return SeedNewInto(skillsDirectory, Packaging.BundledSkillSet.Embedded);
        }

        /// <summary>
        /// Seeds default skills (C#-defined or from <paramref name="bundled"/>) that have never been seeded before,
        /// honoring deletions through the manifest. See <see cref="SeedNewInto(string)"/>.
        /// </summary>
        /// <param name="skillsDirectory">The skills directory. Must not be null.</param>
        /// <param name="bundled">The folder-based bundled skills. Null is treated as none.</param>
        /// <returns>The ids of the defaults written by this call.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="skillsDirectory"/> is null.</exception>
        public static IReadOnlyList<string> SeedNewInto(string skillsDirectory, Packaging.BundledSkillSet? bundled)
        {
            if (skillsDirectory == null) throw new ArgumentNullException(nameof(skillsDirectory));

            Directory.CreateDirectory(skillsDirectory);
            string manifestPath = Path.Combine(skillsDirectory, SeededManifestFileName);
            HashSet<string> seeded = LoadSeededManifest(manifestPath);
            List<string> added = new List<string>();
            bool manifestChanged = false;
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> resources = AllResources();

            foreach (KeyValuePair<string, string> skill in All(bundled))
            {
                if (seeded.Contains(skill.Key))
                {
                    continue;
                }

                string dir = Path.Combine(skillsDirectory, skill.Key);
                if (!Directory.Exists(dir))
                {
                    WriteSkill(dir, skill.Key, skill.Value, resources, bundled);
                    added.Add(skill.Key);
                }

                seeded.Add(skill.Key);
                manifestChanged = true;
            }

            if (manifestChanged)
            {
                SaveSeededManifest(manifestPath, seeded);
            }

            return added;
        }

        private static void WriteSkill(string dir, string id, string content, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> resources, Packaging.BundledSkillSet? bundled)
        {
            Packaging.BundledSkill? folder = bundled?.Find(id);
            if (folder != null)
            {
                Packaging.SkillFileWriter.WriteAll(dir, folder.Files, id);
                return;
            }

            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "SKILL.md"), content);
            if (!resources.TryGetValue(id, out IReadOnlyDictionary<string, string>? files))
            {
                return;
            }

            string root = Path.GetFullPath(dir) + Path.DirectorySeparatorChar;
            foreach (KeyValuePair<string, string> file in files)
            {
                string target = Path.GetFullPath(Path.Combine(dir, file.Key.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(root, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Default skill '" + id + "' resource '" + file.Key + "' escapes the skill directory.");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target, file.Value);
            }
        }

        private static HashSet<string> LoadSeededManifest(string manifestPath)
        {
            HashSet<string> seeded = new HashSet<string>(StringComparer.Ordinal);
            if (!File.Exists(manifestPath))
            {
                return seeded;
            }

            try
            {
                foreach (string line in File.ReadAllLines(manifestPath))
                {
                    string id = line.Trim();
                    if (id.Length > 0)
                    {
                        seeded.Add(id);
                    }
                }
            }
            catch (IOException)
            {
            }

            return seeded;
        }

        private static void SaveSeededManifest(string manifestPath, HashSet<string> seeded)
        {
            List<string> ids = new List<string>(seeded);
            ids.Sort(StringComparer.Ordinal);
            try
            {
                File.WriteAllLines(manifestPath, ids);
            }
            catch (IOException)
            {
            }
        }
    }
}
