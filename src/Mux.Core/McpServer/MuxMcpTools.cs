namespace Mux.Core.McpServer
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Sessions;
    using Mux.Core.Skills;
    using Voltaic.Core;
    using Voltaic.Mcp;

    /// <summary>
    /// The tools mux publishes as an MCP server: <c>run</c> (one headless agent turn), the read-only
    /// <c>list_sessions</c>, <c>get_session</c>, <c>list_endpoints</c>, and <c>list_skills</c>, and, only when the
    /// operator allows it, <c>run_skill</c>. Handlers validate their arguments, never return secrets, and report errors
    /// the calling model can act on as <see cref="McpToolException"/>. Transport-independent: the host registers the
    /// handlers on Voltaic's stdio or HTTP server.
    /// </summary>
    public sealed class MuxMcpTools
    {
        #region Private-Members

        private const int MaxMessageChars = 8000;

        private static readonly JsonSerializerOptions _Json = new JsonSerializerOptions { WriteIndented = false };

        private readonly MuxMcpServerOptions _Options;
        private readonly IMcpRunExecutor _Executor;
        private readonly Func<List<EndpointConfig>> _Endpoints;
        private readonly SessionStore _Sessions;
        private readonly SkillRuntime? _Skills;
        private readonly SemaphoreSlim _RunGate = new SemaphoreSlim(1, 1);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="MuxMcpTools"/> class.
        /// </summary>
        /// <param name="options">The server options. Must not be null.</param>
        /// <param name="executor">Runs the agent turn behind <c>run</c>. Must not be null.</param>
        /// <param name="endpoints">Returns the configured endpoints. Must not be null.</param>
        /// <param name="sessions">The shared session store. Must not be null.</param>
        /// <param name="skills">The skills runtime, or null when skills are disabled.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public MuxMcpTools(MuxMcpServerOptions options, IMcpRunExecutor executor, Func<List<EndpointConfig>> endpoints, SessionStore sessions, SkillRuntime? skills)
        {
            _Options = options ?? throw new ArgumentNullException(nameof(options));
            _Executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _Endpoints = endpoints ?? throw new ArgumentNullException(nameof(endpoints));
            _Sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
            _Skills = skills;
        }

        #endregion

        #region Public-Members

        /// <summary>The names of the tools this instance registers, in registration order.</summary>
        public IReadOnlyList<string> ToolNames
        {
            get
            {
                List<string> names = new List<string> { "run", "list_sessions", "get_session", "list_endpoints", "list_skills" };
                if (_Options.AllowSkills) names.Add("run_skill");
                return names;
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Registers every tool through a registration callback (Voltaic's <c>RegisterTool</c> on either server type).
        /// </summary>
        /// <param name="register">Receives the tool name, description, input schema, and handler.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="register"/> is null.</exception>
        public void RegisterAll(Action<string, string, object, Func<RpcParameters, CancellationToken, Task<object>>> register)
        {
            if (register == null) throw new ArgumentNullException(nameof(register));

            register("run",
                "Run a prompt through mux's agent (with its tools, skills, and project instructions) and return the final answer plus a run summary. The server's approval ceiling ("
                    + PolicyName(_Options.MaxApprovalPolicy) + ") limits which tools may change files or run commands.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        prompt = new { type = "string", description = "The task or question for mux." },
                        endpoint = new { type = "string", description = "Optional endpoint name (see list_endpoints)." },
                        working_directory = new { type = "string", description = "Optional working directory for the run." },
                        approval_policy = new { type = "string", @enum = new[] { "deny", "auto-safe", "auto" }, description = "Optional approval policy; it can only be as permissive as the server allows." },
                        max_turns = new { type = "integer", minimum = 1, maximum = 200, description = "Optional cap on agent iterations." }
                    },
                    required = new[] { "prompt" }
                },
                RunAsync);

            register("list_sessions", "List recent mux sessions (newest first) with their titles, endpoints, and message counts.",
                new { type = "object", properties = new { limit = new { type = "integer", minimum = 1, maximum = 200, description = "How many sessions to return (default 20)." } } },
                ListSessionsAsync);

            register("get_session", "Read one mux session's metadata and its most recent messages.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        id = new { type = "string", description = "The session id (see list_sessions)." },
                        max_messages = new { type = "integer", minimum = 1, maximum = 500, description = "How many of the newest messages to return (default 50)." }
                    },
                    required = new[] { "id" }
                },
                GetSessionAsync);

            register("list_endpoints", "List the model endpoints mux can use. API keys and headers are never included.",
                new { type = "object", properties = new { } },
                ListEndpointsAsync);

            register("list_skills", "List the mux skills available in a working directory, with each skill's category. Pass category to list only one category.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        working_directory = new { type = "string", description = "Optional working directory (project skills are included when trusted)." },
                        category = new { type = "string", description = "Optional category filter, for example review, testing, or cloud." }
                    }
                },
                ListSkillsAsync);

            if (_Options.AllowSkills)
            {
                register("run_skill", "Run one command of a mux skill deterministically and return its stdout, stderr, and exit code.",
                    new
                    {
                        type = "object",
                        properties = new
                        {
                            name = new { type = "string", description = "The skill name (see list_skills)." },
                            command = new { type = "string", description = "The command name within the skill." },
                            args = new { type = "array", items = new { type = "string" }, description = "Arguments passed to the command." },
                            working_directory = new { type = "string", description = "Optional working directory." }
                        },
                        required = new[] { "name", "command" }
                    },
                    RunSkillAsync);
            }
        }

        /// <summary>
        /// Resolves the approval policy for a <c>run</c> call: the requested policy, or the server ceiling when none is
        /// requested, refusing anything more permissive than the ceiling.
        /// </summary>
        /// <param name="requested">The requested policy name (<c>deny</c>, <c>auto-safe</c>, <c>auto</c>), or null.</param>
        /// <param name="ceiling">The server's ceiling.</param>
        /// <returns>The effective policy.</returns>
        /// <exception cref="McpToolException">Thrown when the name is unknown or more permissive than the ceiling.</exception>
        public static ApprovalPolicyEnum ResolvePolicy(string? requested, ApprovalPolicyEnum ceiling)
        {
            if (string.IsNullOrWhiteSpace(requested))
            {
                return ceiling;
            }

            ApprovalPolicyEnum policy;
            switch (requested.Trim().ToLowerInvariant())
            {
                case "deny":
                    policy = ApprovalPolicyEnum.Deny;
                    break;
                case "auto-safe":
                case "autosafe":
                case "auto_safe":
                    policy = ApprovalPolicyEnum.AutoSafe;
                    break;
                case "auto":
                case "auto-approve":
                case "yolo":
                    policy = ApprovalPolicyEnum.AutoApprove;
                    break;
                default:
                    throw new McpToolException("approval_policy must be deny, auto-safe, or auto (got '" + requested + "'). 'ask' is not available because no person is attached.");
            }

            if (Rank(policy) > Rank(ceiling))
            {
                throw new McpToolException("approval_policy '" + PolicyName(policy) + "' is more permissive than this server allows (" + PolicyName(ceiling) + "). Restart the server with --approval-policy to raise it.");
            }

            return policy;
        }

        /// <summary>
        /// Masks credentials embedded in a URL: user information and query values.
        /// </summary>
        /// <param name="url">The URL.</param>
        /// <returns>The URL with secrets replaced by <c>***</c>.</returns>
        public static string MaskUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            {
                return url ?? string.Empty;
            }

            UriBuilder builder = new UriBuilder(uri);
            if (!string.IsNullOrEmpty(builder.UserName) || !string.IsNullOrEmpty(builder.Password))
            {
                builder.UserName = "***";
                builder.Password = string.Empty;
            }

            if (!string.IsNullOrEmpty(builder.Query) && builder.Query.Length > 1)
            {
                List<string> parts = new List<string>();
                foreach (string pair in builder.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    int eq = pair.IndexOf('=');
                    parts.Add(eq < 0 ? pair : pair.Substring(0, eq) + "=***");
                }

                builder.Query = string.Join("&", parts);
            }

            string masked = builder.Uri.ToString();
            return uri.AbsolutePath == "/" && !url.EndsWith("/", StringComparison.Ordinal) && masked.EndsWith("/", StringComparison.Ordinal) && string.IsNullOrEmpty(builder.Query)
                ? masked.TrimEnd('/')
                : masked;
        }

        #endregion

        #region Private-Methods

        private async Task<object> RunAsync(RpcParameters? args, CancellationToken token)
        {
            using JsonDocument doc = Parse(args);
            JsonElement root = doc.RootElement;
            string prompt = OptionalString(root, "prompt") ?? throw new McpToolException("prompt is required.");
            McpRunRequest request = new McpRunRequest
            {
                Prompt = prompt,
                Endpoint = OptionalString(root, "endpoint") ?? _Options.DefaultEndpoint,
                WorkingDirectory = ResolveDirectory(OptionalString(root, "working_directory")),
                ApprovalPolicy = ResolvePolicy(OptionalString(root, "approval_policy"), _Options.MaxApprovalPolicy),
                MaxTurns = OptionalInt(root, "max_turns", 1, 200)
            };

            McpToolCallContext? call = McpToolCallContext.Current;
            int step = 0;
            Func<string, Task> progress = async (string message) =>
            {
                if (call == null) return;
                step++;
                try { await call.ReportProgressAsync(step, null, message, token).ConfigureAwait(false); }
                catch (Exception) when (!token.IsCancellationRequested) { }
            };

            await _RunGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                McpRunResult result = await _Executor.RunAsync(request, progress, token).ConfigureAwait(false);
                return Serialize(new
                {
                    answer = result.Answer,
                    status = result.Status,
                    endpoint = result.Endpoint,
                    model = result.Model,
                    approval_policy = PolicyName(request.ApprovalPolicy),
                    working_directory = request.WorkingDirectory,
                    iterations = result.Iterations,
                    tool_calls = result.ToolCalls,
                    errors = result.Errors,
                    duration_ms = result.DurationMs,
                    input_tokens = result.InputTokens,
                    output_tokens = result.OutputTokens,
                    error = result.ErrorMessage
                });
            }
            finally
            {
                _RunGate.Release();
            }
        }

        private async Task<object> ListSessionsAsync(RpcParameters? args, CancellationToken token)
        {
            using JsonDocument doc = Parse(args);
            int limit = OptionalInt(doc.RootElement, "limit", 1, 200) ?? 20;
            List<SessionSnapshot> sessions = new List<SessionSnapshot>(await _Sessions.ListAsync(token).ConfigureAwait(false));
            sessions.Sort((SessionSnapshot a, SessionSnapshot b) => b.UpdatedUtc.CompareTo(a.UpdatedUtc));
            List<object> rows = new List<object>();
            foreach (SessionSnapshot session in sessions)
            {
                if (rows.Count >= limit) break;
                rows.Add(new
                {
                    id = session.Id,
                    title = session.Title,
                    endpoint = session.EndpointName,
                    model = session.Model,
                    messages = session.ConversationHistory.Count,
                    working_directory = session.WorkingDirectory,
                    updated_utc = session.UpdatedUtc.ToString("o", CultureInfo.InvariantCulture)
                });
            }

            return Serialize(new { total = sessions.Count, sessions = rows });
        }

        private async Task<object> GetSessionAsync(RpcParameters? args, CancellationToken token)
        {
            using JsonDocument doc = Parse(args);
            string id = OptionalString(doc.RootElement, "id") ?? throw new McpToolException("id is required.");
            int max = OptionalInt(doc.RootElement, "max_messages", 1, 500) ?? 50;
            if (id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id.Contains("..", StringComparison.Ordinal))
            {
                throw new McpToolException("No session has the id '" + id + "'.");
            }

            SessionSnapshot? session;
            try
            {
                session = await _Sessions.LoadAsync(id, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is ArgumentException || ex is UnauthorizedAccessException)
            {
                session = null;
            }

            if (session == null)
            {
                throw new McpToolException("No session has the id '" + id + "'. Use list_sessions to see the ids.");
            }

            List<ConversationMessage> history = session.ConversationHistory;
            int start = Math.Max(0, history.Count - max);
            List<object> messages = new List<object>();
            for (int i = start; i < history.Count; i++)
            {
                ConversationMessage message = history[i];
                string content = message.Content ?? string.Empty;
                if (content.Length > MaxMessageChars)
                {
                    content = content.Substring(0, MaxMessageChars) + "\n[mux: message cut at " + MaxMessageChars + " characters]";
                }

                messages.Add(new { role = message.Role.ToString().ToLowerInvariant(), content });
            }

            return Serialize(new
            {
                id = session.Id,
                title = session.Title,
                endpoint = session.EndpointName,
                model = session.Model,
                working_directory = session.WorkingDirectory,
                created_utc = session.CreatedUtc.ToString("o", CultureInfo.InvariantCulture),
                updated_utc = session.UpdatedUtc.ToString("o", CultureInfo.InvariantCulture),
                total_messages = history.Count,
                messages
            });
        }

        private Task<object> ListEndpointsAsync(RpcParameters? args, CancellationToken token)
        {
            List<object> rows = new List<object>();
            foreach (EndpointConfig endpoint in _Endpoints())
            {
                rows.Add(new
                {
                    name = endpoint.Name,
                    adapter = endpoint.AdapterType.ToString(),
                    model = endpoint.Model,
                    base_url = MaskUrl(endpoint.BaseUrl),
                    is_default = endpoint.IsDefault,
                    is_server_default = !string.IsNullOrEmpty(_Options.DefaultEndpoint) && string.Equals(endpoint.Name, _Options.DefaultEndpoint, StringComparison.OrdinalIgnoreCase)
                });
            }

            return Task.FromResult<object>(Serialize(new { endpoints = rows }));
        }

        private Task<object> ListSkillsAsync(RpcParameters? args, CancellationToken token)
        {
            if (_Skills == null)
            {
                return Task.FromResult<object>(Serialize(new { enabled = false, skills = new List<object>() }));
            }

            using JsonDocument doc = Parse(args);
            string directory = ResolveDirectory(OptionalString(doc.RootElement, "working_directory"));
            string? categoryFilter = OptionalString(doc.RootElement, "category");
            string? wanted = categoryFilter == null ? null : SkillCategories.Normalize(categoryFilter);
            if (categoryFilter != null && (wanted == null || !SkillCategories.IsValidFormat(wanted)))
            {
                throw new McpToolException("category must be a kebab-case category such as review, testing, or cloud.");
            }

            List<object> rows = new List<object>();
            foreach (Skill skill in _Skills.GetInvocableSkills(directory))
            {
                if (wanted != null && !string.Equals(skill.Category, wanted, StringComparison.Ordinal))
                {
                    continue;
                }

                List<string> commands = new List<string>();
                foreach (SkillCommand command in skill.Manifest.Commands) commands.Add(command.Name);
                rows.Add(new
                {
                    name = skill.Manifest.Name,
                    description = skill.Manifest.Description,
                    category = skill.Category,
                    playbook = skill.Manifest.IsPlaybook,
                    scope = skill.Scope == SkillScopeEnum.Project ? "project" : "user",
                    commands
                });
            }

            return Task.FromResult<object>(Serialize(new { enabled = true, run_skill_allowed = _Options.AllowSkills, category = wanted, skills = rows }));
        }

        private async Task<object> RunSkillAsync(RpcParameters? args, CancellationToken token)
        {
            if (!_Options.AllowSkills)
            {
                throw new McpToolException("run_skill is disabled on this server. Start it with --allow-skills to enable it.");
            }

            if (_Skills == null)
            {
                throw new McpToolException("Skills are disabled in this mux configuration (settings.json: skillsEnabled).");
            }

            using JsonDocument doc = Parse(args);
            JsonElement root = doc.RootElement;
            string name = OptionalString(root, "name") ?? throw new McpToolException("name is required.");
            string command = OptionalString(root, "command") ?? throw new McpToolException("command is required.");
            string directory = ResolveDirectory(OptionalString(root, "working_directory"));
            if (!_Skills.TryGetSkill(name, directory, out Skill _))
            {
                throw new McpToolException("No enabled skill named '" + name + "' is available here. Use list_skills to see them.");
            }

            List<string> arguments = new List<string>();
            if (root.TryGetProperty("args", out JsonElement argsElement))
            {
                if (argsElement.ValueKind != JsonValueKind.Array) throw new McpToolException("args must be an array of strings.");
                foreach (JsonElement element in argsElement.EnumerateArray())
                {
                    if (element.ValueKind != JsonValueKind.String) throw new McpToolException("args must be an array of strings.");
                    arguments.Add(element.GetString() ?? string.Empty);
                }
            }

            string json = JsonSerializer.Serialize(new { name, command, args = arguments, working_directory = directory });
            using JsonDocument request = JsonDocument.Parse(json);
            ToolResult result = await _Skills.ExecuteAsync("run_skill", request.RootElement, directory, token).ConfigureAwait(false);
            return result.Content;
        }

        private string ResolveDirectory(string? requested)
        {
            string fallback = string.IsNullOrWhiteSpace(_Options.DefaultWorkingDirectory) ? Directory.GetCurrentDirectory() : _Options.DefaultWorkingDirectory!;
            if (string.IsNullOrWhiteSpace(requested))
            {
                return Path.GetFullPath(fallback);
            }

            string full = Path.IsPathRooted(requested) ? Path.GetFullPath(requested) : Path.GetFullPath(Path.Combine(fallback, requested));
            if (!Directory.Exists(full))
            {
                throw new McpToolException("working_directory '" + requested + "' does not exist.");
            }

            return full;
        }

        private static JsonDocument Parse(RpcParameters? args)
        {
            string raw = args?.RawJson ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw)) raw = "{}";
            try
            {
                JsonDocument doc = JsonDocument.Parse(raw);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    doc.Dispose();
                    throw new McpToolException("Arguments must be a JSON object.");
                }

                return doc;
            }
            catch (JsonException)
            {
                throw new McpToolException("Arguments are not valid JSON.");
            }
        }

        private static string? OptionalString(JsonElement root, string name)
        {
            if (!root.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            if (value.ValueKind != JsonValueKind.String)
            {
                throw new McpToolException(name + " must be a string.");
            }

            string? text = value.GetString();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        private static int? OptionalInt(JsonElement root, string name, int min, int max)
        {
            if (!root.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int number) || number < min || number > max)
            {
                throw new McpToolException(name + " must be a whole number from " + min + " to " + max + ".");
            }

            return number;
        }

        private static int Rank(ApprovalPolicyEnum policy)
        {
            return policy switch
            {
                ApprovalPolicyEnum.AutoApprove => 2,
                ApprovalPolicyEnum.AutoSafe => 1,
                _ => 0
            };
        }

        private static string PolicyName(ApprovalPolicyEnum policy)
        {
            return policy switch
            {
                ApprovalPolicyEnum.AutoApprove => "auto",
                ApprovalPolicyEnum.AutoSafe => "auto-safe",
                _ => "deny"
            };
        }

        private static string Serialize(object value)
        {
            return JsonSerializer.Serialize(value, _Json);
        }

        #endregion
    }
}
