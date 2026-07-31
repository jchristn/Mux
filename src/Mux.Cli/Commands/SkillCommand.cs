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
        /// The skill action to perform: list, show, validate, run, new, or add.
        /// </summary>
        [Description("Skill action: list, show, validate, run, new, or add.")]
        [CommandArgument(0, "<action>")]
        public string Action { get; set; } = string.Empty;

        /// <summary>
        /// The skill name (show/validate/run/new), or the source path (add).
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
        /// The working directory for the run action.
        /// </summary>
        [Description("Working directory for the run action.")]
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
            string skillsDirectory = muxSettings.SkillsDirectory ?? SettingsLoader.GetSkillsDirectory();
            string action = (settings.Action ?? string.Empty).Trim().ToLowerInvariant();

            try
            {
                switch (action)
                {
                    case "list":
                        return HandleList(skillsDirectory, json);
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
                    default:
                        Console.Error.WriteLine("Usage: mux skill list|show <name>|validate [name]|run <name> <command>|new <name>|add <path>");
                        return 1;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("skill error: " + ex.Message);
                return 1;
            }
        }

        private static int HandleList(string skillsDirectory, bool json)
        {
            IReadOnlyList<Skill> skills = new SkillLoader(skillsDirectory).Discover();
            Dictionary<string, bool> enabled = LoadEnabledMap();

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
                Console.WriteLine($"{skill.Manifest.Name}\t{state}\t{skill.Manifest.Commands.Count} cmd\t{skill.Manifest.Description}");
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
                    mutating = skill.Manifest.Mutating,
                    valid = skill.IsValid,
                    errors = skill.Validation.Errors,
                    commands,
                    body = skill.Body
                }));
                return skill.IsValid ? 0 : 1;
            }

            Console.WriteLine($"Name:        {skill.Manifest.Name}");
            Console.WriteLine($"Title:       {skill.Manifest.Title}");
            Console.WriteLine($"Description: {skill.Manifest.Description}");
            Console.WriteLine($"Version:     {skill.Manifest.Version}");
            Console.WriteLine($"Mutating:    {skill.Manifest.Mutating}");
            Console.WriteLine($"Valid:       {skill.IsValid}");
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
