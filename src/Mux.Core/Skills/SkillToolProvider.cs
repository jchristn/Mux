namespace Mux.Core.Skills
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Tools;

    /// <summary>
    /// Exposes the skills catalog to the agent loop as an <see cref="IExternalToolProvider"/> through two
    /// tools: <c>skill</c>, which reads a skill's full instructions and command list (read-only, progressive
    /// disclosure), and <c>run_skill</c>, which executes one of a skill's commands deterministically.
    /// <c>run_skill</c> is classified as mutating so a skill's code runs under the write lease and approval
    /// policy, the same posture as <c>run_process</c>.
    /// </summary>
    public sealed class SkillToolProvider : IExternalToolProvider
    {
        #region Private-Members

        private const string SkillToolName = "skill";
        private const string RunSkillToolName = "run_skill";

        private readonly SkillCatalog _Catalog;
        private readonly SkillExecutor _Executor;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="SkillToolProvider"/> class.
        /// </summary>
        /// <param name="catalog">The current skills catalog. Must not be null.</param>
        /// <param name="executor">The executor that runs skill commands. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public SkillToolProvider(SkillCatalog catalog, SkillExecutor executor)
        {
            _Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _Executor = executor ?? throw new ArgumentNullException(nameof(executor));
        }

        #endregion

        #region Public-Members

        /// <inheritdoc/>
        public string Name => "skills";

        #endregion

        #region Public-Methods

        /// <inheritdoc/>
        public IReadOnlyList<ToolDefinition> GetToolDefinitions()
        {
            List<ToolDefinition> definitions = new List<ToolDefinition>();
            if (_Catalog.GetEnabledValidSkills().Count == 0)
            {
                return definitions;
            }

            definitions.Add(new ToolDefinition
            {
                Name = SkillToolName,
                Description = "Read a skill's full instructions and the commands it provides. Call this before run_skill to learn how a skill works.",
                ParametersSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string", description = "The skill name to open." }
                    },
                    required = new[] { "name" }
                }
            });

            definitions.Add(new ToolDefinition
            {
                Name = RunSkillToolName,
                Description = "Run one command from a skill deterministically. Returns stdout, stderr, exit_code, and timed_out.",
                ParametersSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string", description = "The skill name." },
                        command = new { type = "string", description = "The command name within the skill." },
                        args = new { type = "array", description = "Optional arguments appended to the command.", items = new { type = "string" } },
                        working_directory = new { type = "string", description = "Optional working directory; defaults to the agent's working directory." }
                    },
                    required = new[] { "name", "command" }
                }
            });

            return definitions;
        }

        /// <inheritdoc/>
        public bool HasTool(string toolName)
        {
            return string.Equals(toolName, SkillToolName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(toolName, RunSkillToolName, StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc/>
        public ToolMutationKind GetMutationKind(string toolName)
        {
            return string.Equals(toolName, SkillToolName, StringComparison.OrdinalIgnoreCase)
                ? ToolMutationKind.ReadOnly
                : ToolMutationKind.Mutating;
        }

        /// <inheritdoc/>
        public async Task<ToolResult> ExecuteAsync(string toolName, JsonElement arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            if (string.Equals(toolName, SkillToolName, StringComparison.OrdinalIgnoreCase))
            {
                return OpenSkill(toolName, arguments);
            }

            if (string.Equals(toolName, RunSkillToolName, StringComparison.OrdinalIgnoreCase))
            {
                return await RunSkillAsync(toolName, arguments, workingDirectory, cancellationToken).ConfigureAwait(false);
            }

            return Error(toolName, "unknown_skill_tool", $"'{toolName}' is not a skill tool.");
        }

        #endregion

        #region Private-Methods

        private ToolResult OpenSkill(string toolCallId, JsonElement arguments)
        {
            string name = GetString(arguments, "name");
            if (name.Length == 0 || !_Catalog.TryGet(name, out Skill skill) || !skill.IsValid || !skill.Manifest.Enabled)
            {
                return Error(toolCallId, "skill_not_found", $"No enabled skill named '{name}'.");
            }

            List<object> commands = new List<object>();
            foreach (SkillCommand command in skill.Manifest.Commands)
            {
                commands.Add(new { name = command.Name, description = command.Description });
            }

            return new ToolResult
            {
                ToolCallId = toolCallId,
                Success = true,
                Content = JsonSerializer.Serialize(new
                {
                    name = skill.Manifest.Name,
                    title = skill.Manifest.Title,
                    description = skill.Manifest.Description,
                    when_to_use = skill.Manifest.WhenToUse,
                    version = skill.Manifest.Version,
                    mutating = skill.Manifest.Mutating,
                    commands,
                    resources = ListResources(skill),
                    body = skill.Body
                })
            };
        }

        private async Task<ToolResult> RunSkillAsync(string toolCallId, JsonElement arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            string name = GetString(arguments, "name");
            string commandName = GetString(arguments, "command");

            if (!_Catalog.TryGet(name, out Skill skill) || !skill.IsValid || !skill.Manifest.Enabled)
            {
                return Error(toolCallId, "skill_not_found", $"No enabled skill named '{name}'.");
            }

            SkillCommand? command = FindCommand(skill, commandName);
            if (command == null)
            {
                return Error(toolCallId, "command_not_found", $"Skill '{name}' has no command '{commandName}'.");
            }

            List<string> args = ReadArgs(arguments);
            string cwd = GetString(arguments, "working_directory");
            string effectiveCwd = cwd.Length > 0 ? cwd : workingDirectory;

            return await _Executor.ExecuteAsync(toolCallId, skill, command, args, effectiveCwd, cancellationToken).ConfigureAwait(false);
        }

        private static SkillCommand? FindCommand(Skill skill, string commandName)
        {
            foreach (SkillCommand command in skill.Manifest.Commands)
            {
                if (string.Equals(command.Name, commandName, StringComparison.OrdinalIgnoreCase))
                {
                    return command;
                }
            }

            return null;
        }

        private static List<string> ListResources(Skill skill)
        {
            List<string> resources = new List<string>();
            string resourceDir = Path.Combine(skill.DirectoryPath, "resources");
            if (!Directory.Exists(resourceDir))
            {
                return resources;
            }

            foreach (string file in Directory.EnumerateFiles(resourceDir))
            {
                resources.Add(Path.GetFileName(file));
            }

            return resources;
        }

        private static List<string> ReadArgs(JsonElement arguments)
        {
            List<string> args = new List<string>();
            if (arguments.ValueKind == JsonValueKind.Object
                && arguments.TryGetProperty("args", out JsonElement argsElement)
                && argsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement element in argsElement.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.String)
                    {
                        args.Add(element.GetString() ?? string.Empty);
                    }
                }
            }

            return args;
        }

        private static string GetString(JsonElement arguments, string propertyName)
        {
            if (arguments.ValueKind == JsonValueKind.Object
                && arguments.TryGetProperty(propertyName, out JsonElement value)
                && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? string.Empty;
            }

            return string.Empty;
        }

        private static ToolResult Error(string toolCallId, string code, string message)
        {
            return new ToolResult
            {
                ToolCallId = toolCallId,
                Success = false,
                Content = JsonSerializer.Serialize(new { error = code, message })
            };
        }

        #endregion
    }
}
