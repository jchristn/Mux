namespace Mux.Core.Memory
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Tools;

    /// <summary>
    /// Exposes persistent memory to the model: <c>remember</c> and <c>forget</c> (mutating, so they hold the write
    /// lease and follow the approval policy) and <c>recall</c> (read-only search and read). Also contributes the memory
    /// index to the system prompt through <see cref="IPromptSectionProvider"/>.
    /// </summary>
    public sealed class MemoryToolProvider : IExternalToolProvider, IPromptSectionProvider
    {
        #region Private-Members

        private readonly MemoryStore _Store;
        private int _MaxPromptBytes;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="MemoryToolProvider"/> class.
        /// </summary>
        /// <param name="store">The memory store. Must not be null.</param>
        /// <param name="maxPromptBytes">The byte budget for the prompt index (<c>memoryMaxBytes</c>).</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="store"/> is null.</exception>
        public MemoryToolProvider(MemoryStore store, int maxPromptBytes)
        {
            _Store = store ?? throw new ArgumentNullException(nameof(store));
            _MaxPromptBytes = Math.Max(0, maxPromptBytes);
        }

        #endregion

        #region Public-Members

        /// <summary>The tool that saves a memory.</summary>
        public const string RememberToolName = "remember";

        /// <summary>The tool that deletes a memory.</summary>
        public const string ForgetToolName = "forget";

        /// <summary>The tool that searches or reads memories.</summary>
        public const string RecallToolName = "recall";

        /// <inheritdoc/>
        public string Name => "memory";

        /// <summary>The store behind the tools.</summary>
        public MemoryStore Store => _Store;

        /// <summary>The byte budget for the prompt index.</summary>
        public int MaxPromptBytes
        {
            get => _MaxPromptBytes;
            set => _MaxPromptBytes = Math.Max(0, value);
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc/>
        public string BuildPromptSection(string workingDirectory)
        {
            try
            {
                return MemoryPromptBuilder.Build(_Store, workingDirectory, _MaxPromptBytes);
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <inheritdoc/>
        public IReadOnlyList<ToolDefinition> GetToolDefinitions()
        {
            object scopeProperty = new { type = "string", @enum = new[] { "project", "global" }, description = "project (default): this repository only. global: every project." };
            return new List<ToolDefinition>
            {
                new ToolDefinition
                {
                    Name = RememberToolName,
                    Description = "Save a durable fact, preference, or decision to persistent memory so future sessions know it. Saving with an existing name updates that memory. Never store secrets.",
                    ParametersSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            name = new { type = "string", description = "Short name, for example 'test-command' or 'prefers-tabs'." },
                            description = new { type = "string", description = "One line shown in the memory index." },
                            content = new { type = "string", description = "The full fact, with any reason or context. Defaults to the description." },
                            scope = scopeProperty
                        },
                        required = new[] { "name", "description" }
                    }
                },
                new ToolDefinition
                {
                    Name = ForgetToolName,
                    Description = "Delete a memory that is wrong or no longer true.",
                    ParametersSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            name = new { type = "string", description = "The memory's name." },
                            scope = scopeProperty
                        },
                        required = new[] { "name" }
                    }
                },
                new ToolDefinition
                {
                    Name = RecallToolName,
                    Description = "Read one memory in full by name, or search memories by text (every word must match). With neither, lists all memories.",
                    ParametersSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            name = new { type = "string", description = "A memory name to read in full." },
                            query = new { type = "string", description = "Words to search for in names, descriptions, and content." }
                        }
                    }
                }
            };
        }

        /// <inheritdoc/>
        public bool HasTool(string toolName)
        {
            return Is(toolName, RememberToolName) || Is(toolName, ForgetToolName) || Is(toolName, RecallToolName);
        }

        /// <inheritdoc/>
        public ToolMutationKind GetMutationKind(string toolName)
        {
            return Is(toolName, RecallToolName) ? ToolMutationKind.ReadOnly : ToolMutationKind.Mutating;
        }

        /// <inheritdoc/>
        public Task<ToolResult> ExecuteAsync(string toolName, JsonElement arguments, string workingDirectory, CancellationToken cancellationToken)
        {
            try
            {
                if (Is(toolName, RememberToolName)) return Task.FromResult(Remember(toolName, arguments, workingDirectory));
                if (Is(toolName, ForgetToolName)) return Task.FromResult(Forget(toolName, arguments, workingDirectory));
                if (Is(toolName, RecallToolName)) return Task.FromResult(Recall(toolName, arguments, workingDirectory));
                return Task.FromResult(Error(toolName, "unknown_tool", "'" + toolName + "' is not a memory tool."));
            }
            catch (ArgumentException ex)
            {
                return Task.FromResult(Error(toolName, "invalid_arguments", ex.Message));
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException)
            {
                return Task.FromResult(Error(toolName, "memory_io_error", ex.Message));
            }
        }

        #endregion

        #region Private-Methods

        private ToolResult Remember(string toolName, JsonElement arguments, string workingDirectory)
        {
            string name = ReadString(arguments, "name") ?? throw new ArgumentException("name is required.");
            MemoryScopeEnum scope = ReadScope(arguments) ?? MemoryScopeEnum.Project;
            MemoryEntry entry = _Store.Save(name, ReadString(arguments, "description"), ReadString(arguments, "content"), scope, workingDirectory, out bool created);
            return Ok(toolName, new { saved = true, created, name = entry.Slug, scope = ScopeName(entry.Scope), description = entry.Description });
        }

        private ToolResult Forget(string toolName, JsonElement arguments, string workingDirectory)
        {
            string name = ReadString(arguments, "name") ?? throw new ArgumentException("name is required.");
            MemoryEntry? deleted = _Store.Delete(name, ReadScope(arguments), workingDirectory);
            if (deleted == null)
            {
                return Error(toolName, "not_found", "No memory named '" + name + "'. Use recall to list them.");
            }

            return Ok(toolName, new { deleted = true, name = deleted.Slug, scope = ScopeName(deleted.Scope) });
        }

        private ToolResult Recall(string toolName, JsonElement arguments, string workingDirectory)
        {
            string? name = ReadString(arguments, "name");
            if (name != null)
            {
                MemoryEntry? entry = _Store.Get(name, null, workingDirectory);
                if (entry == null)
                {
                    return Error(toolName, "not_found", "No memory named '" + name + "'.");
                }

                return Ok(toolName, new
                {
                    name = entry.Slug,
                    scope = ScopeName(entry.Scope),
                    description = entry.Description,
                    content = entry.Content,
                    updated_utc = entry.UpdatedUtc.ToString("o", CultureInfo.InvariantCulture)
                });
            }

            List<object> matches = new List<object>();
            foreach (MemoryEntry entry in _Store.Search(ReadString(arguments, "query"), workingDirectory))
            {
                matches.Add(new { name = entry.Slug, scope = ScopeName(entry.Scope), description = entry.Description, content = Clip(entry.Content, 500) });
            }

            return Ok(toolName, new { count = matches.Count, memories = matches });
        }

        private static MemoryScopeEnum? ReadScope(JsonElement arguments)
        {
            string? value = ReadString(arguments, "scope");
            if (value == null)
            {
                return null;
            }

            if (!MemoryStore.TryParseScope(value, out MemoryScopeEnum scope))
            {
                throw new ArgumentException("scope must be 'project' or 'global'.");
            }

            return scope;
        }

        private static string ScopeName(MemoryScopeEnum scope)
        {
            return scope == MemoryScopeEnum.Global ? "global" : "project";
        }

        private static string Clip(string text, int max)
        {
            return text.Length <= max ? text : text.Substring(0, max) + "...";
        }

        private static bool Is(string toolName, string expected)
        {
            return string.Equals(toolName, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static string? ReadString(JsonElement arguments, string name)
        {
            if (arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            {
                string? text = value.GetString();
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }

            return null;
        }

        private static ToolResult Ok(string toolName, object payload)
        {
            return new ToolResult { ToolCallId = toolName, Success = true, Content = JsonSerializer.Serialize(payload) };
        }

        private static ToolResult Error(string toolName, string code, string message)
        {
            return new ToolResult { ToolCallId = toolName, Success = false, Content = JsonSerializer.Serialize(new { error = code, message }) };
        }

        #endregion
    }
}
