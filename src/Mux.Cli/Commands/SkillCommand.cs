namespace Mux.Cli.Commands
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Cli.Rendering;
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Mux.Core.Skills;
    using SkillCommandModel = Mux.Core.Models.SkillCommand;

    /// <summary>
    /// Settings for the non-interactive <c>mux skill</c> verb.
    /// </summary>
    public class SkillSettings : CommandSettings
    {
        /// <summary>
        /// The skill action to perform: list, show, validate, run, new, add, or trust.
        /// </summary>
        [Description("Skill action: list, show, validate, run, new, add, or trust.")]
        [CommandArgument(0, "<action>")]
        public string Action { get; set; } = string.Empty;

        /// <summary>
        /// The skill name (show/validate/run/new), the source path (add), or the trust level (trust: all,
        /// playbooks, ignore, or reset; omitted reports the current level).
        /// </summary>
        [Description("Skill name, or source path for add.")]
        [CommandArgument(1, "[name]")]
        public string? Name { get; set; }

        /// <summary>
        /// The command name for the run action.
        /// </summary>
        [Description("Command name for the run action.")]
        [CommandArgument(2, "[command]")]
        public string? Command { get; set; }

        /// <summary>
        /// Arguments appended to a run command. Repeatable via <c>--arg</c>.
        /// </summary>
        public List<string> Args { get; set; } = new List<string>();

        /// <summary>
        /// The working directory for the run action, or the directory whose project the trust action targets.
        /// </summary>
        [Description("Working directory for the run action, or the project for the trust action.")]
        [CommandOption("--cwd")]
        public string? WorkingDirectory { get; set; }

        /// <summary>
        /// Output format for list, show, and validate: text or json.
        /// </summary>
        [Description("Output format: text or json.")]
        [CommandOption("--output-format")]
        public string? OutputFormat { get; set; }

        /// <summary>
        /// Override the active config directory.
        /// </summary>
        [Description("Override the active config directory.")]
        [CommandOption("--config-dir")]
        public string? ConfigDir { get; set; }

        /// <summary>
        /// For list: show only skills in this category.
        /// </summary>
        [Description("For list: show only skills in this category.")]
        [CommandOption("--category")]
        public string? Category { get; set; }

        /// <summary>
        /// For category: clear the override so the skill uses its SKILL.md category again.
        /// </summary>
        [Description("For category: clear the override.")]
        [CommandOption("--clear")]
        public bool Clear { get; set; }
    }

    /// <summary>
    /// Lists, shows, validates, runs, creates, and imports skills from the command line, giving automation
    /// the same deterministic contract the interactive agent uses.
    /// </summary>
    public class SkillCommand : AsyncCommand<SkillSettings>
    {
        /// <inheritdoc />
        public override async Task<int> ExecuteAsync(CommandContext context, SkillSettings settings, CancellationToken cancellationToken)
        {
            using IDisposable configScope = SettingsLoader.PushConfigDirectoryOverride(settings.ConfigDir);

            bool json;
            try
            {
                json = CommandRuntimeResolver.ParseOutputFormat(settings.OutputFormat, OutputFormatEnum.Text, OutputFormatEnum.Json) == OutputFormatEnum.Json;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }

            SettingsLoader.EnsureConfigDirectory();
            MuxSettings muxSettings = SettingsLoader.LoadSettings();
            string skillsDirectory = SettingsLoader.ResolveSkillsDirectory(muxSettings);
            string action = (settings.Action ?? string.Empty).Trim().ToLowerInvariant();

            try
            {
                switch (action)
                {
                    case "list":
                        return HandleList(skillsDirectory, json, settings.Category);
                    case "category":
                        return HandleCategory(skillsDirectory, settings.Name, settings.Command, settings.Clear, json);
                    case "categories":
                        return HandleCategories(skillsDirectory, json);
                    case "show":
                        return HandleShow(skillsDirectory, settings.Name, json);
                    case "validate":
                        return HandleValidate(skillsDirectory, settings.Name, json);
                    case "new":
                        return HandleNew(skillsDirectory, settings.Name);
                    case "add":
                        return HandleAdd(skillsDirectory, settings.Name);
                    case "run":
                        return await HandleRunAsync(skillsDirectory, settings, cancellationToken).ConfigureAwait(false);
                    case "trust":
                        return HandleTrust(settings.Name, settings.WorkingDirectory, json);
                    default:
                        Console.Error.WriteLine("Usage: mux skill list [--category <c>]|show <name>|validate [name]|run <name> <command>|new <name>|add <path>|category <name> [<category>|--clear]|categories|trust [all|playbooks|ignore|reset] [--cwd dir]");
                        return 1;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("skill error: " + ex.Message);
                return 1;
            }
        }

        private static int HandleTrust(string? level, string? workingDirectory, bool json)
        {
            string directory = string.IsNullOrWhiteSpace(workingDirectory) ? Directory.GetCurrentDirectory() : workingDirectory!;
            string root = SkillRuntime.ResolveProjectRoot(directory) ?? directory;
            ProjectTrustStore store = new ProjectTrustStore(SettingsLoader.GetTrustedProjectsPath());

            if (!string.IsNullOrWhiteSpace(level))
            {
                if (!ProjectTrustStore.TryParseLevel(level, out Mux.Core.Enums.ProjectTrustLevelEnum parsed))
                {
                    Console.Error.WriteLine($"Unknown trust level '{level}'. Use all, playbooks, ignore, or reset.");
                    return 1;
                }

                store.SetLevel(root, parsed);
            }

            string current = ProjectTrustStore.ToWireName(store.GetLevel(root));
            if (json)
            {
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { projectRoot = root, level = current }));
            }
            else
            {
                Console.WriteLine($"{root}: {current}");
            }

            return 0;
        }

        private static int HandleCategory(string skillsDirectory, string? name, string? category, bool clear, bool json)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                Console.Error.WriteLine("Usage: mux skill category <name> [<category>|--clear]");
                return 1;
            }

            if (clear && !string.IsNullOrWhiteSpace(category))
            {
                Console.Error.WriteLine("Pass a category or --clear, not both.");
                return 1;
            }

            string path = Path.Combine(skillsDirectory, name);
            if (!SkillManager.IsValidId(name) || !File.Exists(Path.Combine(path, "SKILL.md")))
            {
                Console.Error.WriteLine($"No skill named '{name}' in {skillsDirectory}.");
                return 1;
            }

            SkillManager manager = new SkillManager(skillsDirectory);
            if (clear || !string.IsNullOrWhiteSpace(category))
            {
                if (!SkillCategories.TryParse(category, out string? _, out string error))
                {
                    Console.Error.WriteLine(error);
                    return 1;
                }

                manager.SetCategory(name, clear ? null : category);
            }

            Skill skill = new SkillLoader(skillsDirectory).Load(path);
            skill.CategoryOverride = manager.GetCategoryOverride(name);
            if (json)
            {
                Console.WriteLine(StructuredOutputFormatter.FormatObject(new
                {
                    success = true,
                    name = skill.Manifest.Name,
                    category = skill.Category,
                    fileCategory = skill.Manifest.Category,
                    overridden = skill.CategoryOverride != null
                }));
            }
            else
            {
                Console.WriteLine($"{skill.Manifest.Name}: {skill.Category}" + (skill.CategoryOverride != null ? " (override; SKILL.md says " + (skill.Manifest.Category.Length > 0 ? skill.Manifest.Category : "nothing") + ")" : string.Empty));
            }

            return 0;
        }

        private static int HandleCategories(string skillsDirectory, bool json)
        {
            IReadOnlyList<Skill> skills = new SkillLoader(skillsDirectory).Discover();
            SkillCategories.ApplyOverrides(skills);
            List<SkillCategoryCount> counts = SkillCategories.Count(skills);
            if (json)
            {
                List<object> rows = new List<object>();
                foreach (SkillCategoryCount count in counts)
                {
                    rows.Add(new { category = count.Category, count = count.Count, known = count.Known });
                }

                Console.WriteLine(StructuredOutputFormatter.FormatObject(new { success = true, categories = rows, known = SkillCategories.Known }));
                return 0;
            }

            if (counts.Count == 0)
            {
                Console.WriteLine("No skills in " + skillsDirectory);
                return 0;
            }

            foreach (SkillCategoryCount count in counts)
            {
                Console.WriteLine($"{count.Category}\t{count.Count}");
            }

            return 0;
        }

        private static int HandleList(string skillsDirectory, bool json, string? categoryFilter)
        {
            List<Skill> skills = new List<Skill>(new SkillLoader(skillsDirectory).Discover());
            Dictionary<string, bool> enabled = LoadEnabledMap();
            SkillCategories.ApplyOverrides(skills);
            if (!string.IsNullOrWhiteSpace(categoryFilter))
            {
                string wanted = SkillCategories.Normalize(categoryFilter) ?? string.Empty;
                skills = skills.FindAll(s => string.Equals(s.Category, wanted, StringComparison.Ordinal));
            }

            if (json)
            {
                List<object> rows = new List<object>();
                foreach (Skill skill in skills)
                {
                    rows.Add(new
                    {
                        name = skill.Manifest.Name,
                        valid = skill.IsValid,
                        enabled = IsEnabled(enabled, skill),
                        category = skill.Category,
                        categoryOverridden = skill.CategoryOverride != null,
                        commands = skill.Manifest.Commands.Count,
                        tags = skill.Manifest.Tags
                    });
                }

                Console.WriteLine(StructuredOutputFormatter.FormatObject(new { success = true, skillsDirectory, skills = rows }));
                return 0;
            }

            if (skills.Count == 0)
            {
                Console.WriteLine("No skills in " + skillsDirectory);
                return 0;
            }

            foreach (Skill skill in skills)
            {
                string state = !skill.IsValid ? "invalid" : (IsEnabled(enabled, skill) ? "enabled" : "disabled");
                Console.WriteLine($"{skill.Manifest.Name}\t{state}\t{skill.Category}\t{skill.Manifest.Commands.Count} cmd\t{skill.Manifest.Description}");
            }

            return 0;
        }

        private static int HandleShow(string skillsDirectory, string? name, bool json)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                Console.Error.WriteLine("Usage: mux skill show <name>");
                return 1;
            }

            Skill skill = new SkillLoader(skillsDirectory).Load(Path.Combine(skillsDirectory, name));
            skill.CategoryOverride = new SkillManager(skillsDirectory).GetCategoryOverride(name);

            if (json)
            {
                List<object> commands = new List<object>();
                foreach (SkillCommandModel command in skill.Manifest.Commands)
                {
                    commands.Add(new { name = command.Name, description = command.Description, interpreter = command.Interpreter });
                }

                Console.WriteLine(StructuredOutputFormatter.FormatObject(new
                {
                    success = skill.IsValid,
                    name = skill.Manifest.Name,
                    title = skill.Manifest.Title,
                    description = skill.Manifest.Description,
                    version = skill.Manifest.Version,
                    category = skill.Category,
                    categoryOverridden = skill.CategoryOverride != null,
                    mutating = skill.Manifest.Mutating,
                    source = skill.Manifest.Source,
                    license = skill.Manifest.License,
                    valid = skill.IsValid,
                    errors = skill.Validation.Errors,
                    commands,
                    directory = SkillPathResolver.NormalizeFolder(skill.DirectoryPath),
                    files = SkillPathResolver.ListFiles(skill.DirectoryPath, out _),
                    body = SkillPathResolver.Substitute(skill.Body, skill.DirectoryPath)
                }));
                return skill.IsValid ? 0 : 1;
            }

            Console.WriteLine($"Name:        {skill.Manifest.Name}");
            Console.WriteLine($"Title:       {skill.Manifest.Title}");
            Console.WriteLine($"Description: {skill.Manifest.Description}");
            Console.WriteLine($"Version:     {skill.Manifest.Version}");
            Console.WriteLine($"Category:    {skill.Category}" + (skill.CategoryOverride != null ? " (override)" : string.Empty));
            Console.WriteLine($"Mutating:    {skill.Manifest.Mutating}");
            if (skill.Manifest.Source.Length > 0) Console.WriteLine($"Source:      {skill.Manifest.Source}");
            if (skill.Manifest.License.Length > 0) Console.WriteLine($"License:     {skill.Manifest.License}");
            Console.WriteLine($"Valid:       {skill.IsValid}");
            Console.WriteLine($"Directory:   {SkillPathResolver.NormalizeFolder(skill.DirectoryPath)}");
            List<string> bundledFiles = SkillPathResolver.ListFiles(skill.DirectoryPath, out bool filesTruncated);
            if (bundledFiles.Count > 0)
            {
                Console.WriteLine("Files:       " + string.Join(", ", bundledFiles) + (filesTruncated ? ", ..." : string.Empty));
            }

            if (!skill.IsValid)
            {
                foreach (string error in skill.Validation.Errors)
                {
                    Console.WriteLine("  - " + error);
                }
            }

            Console.WriteLine("Commands:");
            foreach (SkillCommandModel command in skill.Manifest.Commands)
            {
                Console.WriteLine($"  {command.Name} ({command.Interpreter}) — {command.Description}");
            }

            string shownBody = SkillPathResolver.Substitute(skill.Body, skill.DirectoryPath).Trim();
            if (shownBody.Length > 0)
            {
                Console.WriteLine();
                Console.WriteLine(shownBody);
            }

            return skill.IsValid ? 0 : 1;
        }

        private static int HandleValidate(string skillsDirectory, string? name, bool json)
        {
            List<Skill> skills = new List<Skill>();
            if (!string.IsNullOrWhiteSpace(name))
            {
                skills.Add(new SkillLoader(skillsDirectory).Load(Path.Combine(skillsDirectory, name)));
            }
            else
            {
                skills.AddRange(new SkillLoader(skillsDirectory).Discover());
            }

            int invalid = 0;
            List<object> results = new List<object>();
            foreach (Skill skill in skills)
            {
                if (!skill.IsValid)
                {
                    invalid++;
                }

                results.Add(new { name = skill.Manifest.Name, valid = skill.IsValid, errors = skill.Validation.Errors });
                if (!json)
                {
                    Console.WriteLine($"{(skill.IsValid ? "OK" : "INVALID")}\t{skill.Manifest.Name}");
                    foreach (string error in skill.Validation.Errors)
                    {
                        Console.WriteLine("  - " + error);
                    }
                }
            }

            if (json)
            {
                Console.WriteLine(StructuredOutputFormatter.FormatObject(new { success = invalid == 0, invalid, results }));
            }

            return invalid == 0 ? 0 : 1;
        }

        private static int HandleNew(string skillsDirectory, string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                Console.Error.WriteLine("Usage: mux skill new <name>");
                return 1;
            }

            SkillScaffold scaffold = new SkillScaffold { Id = name, Title = name, Description = string.Empty, Mutating = true, Interpreter = "pwsh" };
            string dir = new SkillManager(skillsDirectory).Create(scaffold);
            Console.WriteLine("Created skill at " + Path.Combine(dir, "SKILL.md"));
            return 0;
        }

        private static int HandleAdd(string skillsDirectory, string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                Console.Error.WriteLine("Usage: mux skill add <path>");
                return 1;
            }

            string id = new SkillManager(skillsDirectory).Import(path, null);
            Console.WriteLine("Imported skill " + id);
            return 0;
        }

        private static async Task<int> HandleRunAsync(string skillsDirectory, SkillSettings settings, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(settings.Name) || string.IsNullOrWhiteSpace(settings.Command))
            {
                Console.Error.WriteLine("Usage: mux skill run <name> <command> [--arg value ...] [--cwd dir]");
                return 1;
            }

            Skill skill = new SkillLoader(skillsDirectory).Load(Path.Combine(skillsDirectory, settings.Name));
            if (!skill.IsValid)
            {
                Console.Error.WriteLine($"Skill '{settings.Name}' is invalid: {string.Join("; ", skill.Validation.Errors)}");
                return 1;
            }

            SkillCommandModel? command = FindCommand(skill, settings.Command!);
            if (command == null)
            {
                Console.Error.WriteLine($"Skill '{settings.Name}' has no command '{settings.Command}'.");
                return 1;
            }

            string cwd = string.IsNullOrWhiteSpace(settings.WorkingDirectory) ? Directory.GetCurrentDirectory() : settings.WorkingDirectory!;
            ToolResult result = await new SkillExecutor()
                .ExecuteAsync("cli", skill, command, settings.Args, cwd, cancellationToken)
                .ConfigureAwait(false);

            Console.WriteLine(result.Content);
            return result.Success ? 0 : 1;
        }

        private static SkillCommandModel? FindCommand(Skill skill, string commandName)
        {
            foreach (SkillCommandModel command in skill.Manifest.Commands)
            {
                if (string.Equals(command.Name, commandName, StringComparison.OrdinalIgnoreCase))
                {
                    return command;
                }
            }

            return null;
        }

        private static Dictionary<string, bool> LoadEnabledMap()
        {
            Dictionary<string, bool> map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (SkillIndexEntry entry in SettingsLoader.LoadSkillIndex())
            {
                if (!string.IsNullOrWhiteSpace(entry.Id))
                {
                    map[entry.Id] = entry.Enabled;
                }
            }

            return map;
        }

        private static bool IsEnabled(Dictionary<string, bool> map, Skill skill)
        {
            return map.TryGetValue(skill.Manifest.Name, out bool enabled) ? enabled : skill.Manifest.Enabled;
        }
    }
}
