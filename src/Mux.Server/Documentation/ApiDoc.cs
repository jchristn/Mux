namespace Mux.Server.Documentation
{
    using System;
    using System.Collections.Generic;
    using WatsonWebserver;
    using WatsonWebserver.Core.OpenApi;
    using M = WatsonWebserver.Core.OpenApi.OpenApiSchemaMetadata;

    /// <summary>
    /// The single source of OpenAPI 3.0 documentation for mux's local REST API. This centralizes the document
    /// info, tag groups, the bearer security scheme, the reusable component schemas (with example values), and
    /// the per-route metadata so the route registrars stay focused on behavior and the generated
    /// <c>/openapi.json</c> and <c>/swagger</c> UI are complete and example-rich.
    ///
    /// <para>The document and Swagger UI routes are registered in Watson's <c>PreAuthentication</c> group by
    /// Watson's <c>UseOpenApi</c> extension and carry no per-handler auth check, so they are reachable without
    /// an API key — the usual expectation for machine-readable API docs.
    /// Every documented operation still advertises the bearer requirement so generated clients send the key.</para>
    /// </summary>
    public static class ApiDoc
    {
        #region Tags

        /// <summary>Health and readiness.</summary>
        public const string TagHealth = "Health";

        /// <summary>Home/overview aggregate.</summary>
        public const string TagOverview = "Overview";

        /// <summary>Endpoint (model connection) configuration.</summary>
        public const string TagEndpoints = "Endpoints";

        /// <summary>Chat and model warm-up.</summary>
        public const string TagChat = "Chat";

        /// <summary>Saved conversations.</summary>
        public const string TagSessions = "Sessions";

        /// <summary>Turn-level undo/redo checkpoints.</summary>
        public const string TagCheckpoints = "Checkpoints";

        /// <summary>Prompt profiles.</summary>
        public const string TagPrompts = "Prompts";

        /// <summary>Subagent definitions.</summary>
        public const string TagSubagents = "Subagents";

        /// <summary>Event hooks and custom commands.</summary>
        public const string TagPlugins = "Plugins";

        /// <summary>Keybinding overrides.</summary>
        public const string TagKeybindings = "Keybindings";

        /// <summary>MCP server configuration.</summary>
        public const string TagMcp = "MCP Servers";

        /// <summary>Skills.</summary>
        public const string TagSkills = "Skills";

        /// <summary>Server settings.</summary>
        public const string TagSettings = "Settings";

        /// <summary>Usage telemetry and pricing.</summary>
        public const string TagUsage = "Usage";

        /// <summary>Run lifecycle: list, inspect, and cancel active runs.</summary>
        public const string TagRuns = "Runs";

        #endregion

        #region Configuration

        /// <summary>
        /// Registers the OpenAPI document (<c>/openapi.json</c>) and Swagger UI (<c>/swagger</c>) on the
        /// webserver, fully configured with the API info, tag groups, bearer security scheme, and the reusable
        /// component schemas. Both routes are unauthenticated.
        /// </summary>
        /// <param name="app">The Watson webserver.</param>
        /// <param name="version">The product version, surfaced as the API document version.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="app"/> is null.</exception>
        public static void Configure(Webserver app, string version)
        {
            if (app == null) throw new ArgumentNullException(nameof(app));

            OpenApiSettings settings = new OpenApiSettings
            {
                DocumentPath = "/openapi.json",
                SwaggerUiPath = "/swagger",
                EnableSwaggerUi = true,
                Info = new OpenApiInfo
                {
                    Title = "mux Local API",
                    Version = string.IsNullOrWhiteSpace(version) ? "1.0.0" : version,
                    Description =
                        "The optional local REST + Server-Sent-Events API exposed by `mux serve`. It binds to " +
                        "loopback by default and lets the desktop app, web dashboard, VS Code extension, and " +
                        "automation manage endpoints, sessions, prompts, subagents, MCP servers, skills, " +
                        "settings, and run chats against any configured (OpenAI-compatible or native) model.\n\n" +
                        "**Authentication.** When an API key is configured, send it as a bearer token: " +
                        "`Authorization: Bearer <key>`. When no key is configured the server runs open " +
                        "(loopback dev mode) and the key is not required. This documentation and the Swagger UI " +
                        "are always reachable without a key.\n\n" +
                        "**Conventions.** JSON is PascalCase. Collections are returned as `{ \"Items\": [...], " +
                        "\"Count\": n }`. Secrets are never returned: a `*Set` boolean reports whether one is " +
                        "stored, the value field is write-only, and sending it blank preserves the stored value.",
                    Contact = new OpenApiContact
                    {
                        Name = "mux",
                        Url = "https://usemux.ai"
                    },
                    License = new OpenApiLicense
                    {
                        Name = "MIT",
                        Url = "https://github.com/jchristn/Mux/blob/main/LICENSE.md"
                    }
                },
                ExternalDocs = new OpenApiExternalDocs
                {
                    Description = "mux documentation and source",
                    Url = "https://github.com/jchristn/Mux"
                },
                IncludePreAuthRoutes = true,
                IncludePostAuthRoutes = true,
                IncludeContentRoutes = false
            };

            settings.SecuritySchemes["bearerAuth"] = new OpenApiSecurityScheme
            {
                Type = "http",
                Scheme = "bearer",
                BearerFormat = "opaque",
                Description = "The configured local API key, sent as `Authorization: Bearer <key>`. Omit when the server runs without a key."
            };

            // Advertise the bearer requirement globally so generated clients attach the key to every call. The
            // scheme is satisfied trivially when the server runs without a key.
            settings.Security.Add(new Dictionary<string, List<string>> { ["bearerAuth"] = new List<string>() });

            settings.Tags = new List<OpenApiTag>
            {
                new OpenApiTag { Name = TagHealth, Description = "Liveness, version, and API contract negotiation." },
                new OpenApiTag { Name = TagOverview, Description = "A single aggregate of counts, environment facts, and recent sessions for a home screen." },
                new OpenApiTag { Name = TagEndpoints, Description = "Create, read, update, and delete the model endpoints mux can run against." },
                new OpenApiTag { Name = TagChat, Description = "Run streaming or non-streaming completions, warm a model, and answer tool-approval prompts." },
                new OpenApiTag { Name = TagSessions, Description = "List, read, upsert, export, and delete saved conversations shared across every mux surface." },
                new OpenApiTag { Name = TagCheckpoints, Description = "Undo or redo the file changes a chat turn made in a git working directory." },
                new OpenApiTag { Name = TagPrompts, Description = "Manage prompt profiles and which one is active." },
                new OpenApiTag { Name = TagSubagents, Description = "Manage subagent definitions used for isolated child runs." },
                new OpenApiTag { Name = TagPlugins, Description = "Manage lifecycle event hooks and custom slash commands." },
                new OpenApiTag { Name = TagKeybindings, Description = "Read and set keybinding overrides." },
                new OpenApiTag { Name = TagMcp, Description = "Manage Model Context Protocol servers (stdio and HTTP)." },
                new OpenApiTag { Name = TagSkills, Description = "Discover, enable/disable, create, edit, and delete skills." },
                new OpenApiTag { Name = TagSettings, Description = "Read and update the editable server settings (secrets masked)." },
                new OpenApiTag { Name = TagUsage, Description = "Query usage telemetry (summary, time series, breakdowns, events) and manage pricing." },
                new OpenApiTag { Name = TagRuns, Description = "List active and recently-finished runs, inspect a run's state and task plan, and cancel a run." }
            };

            RegisterSchemas(settings.Schemas);

            app.UseOpenApi(settings);
        }

        #endregion

        #region Route-Metadata

        // --- Health ---

        /// <summary>Metadata for <c>GET /v1.0/api/health</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> HealthGet = m => Init(m, TagHealth,
            "Health and readiness",
            "Returns liveness, product and API-contract versions, process id, and uptime. Unauthenticated so a supervisor can probe readiness. Clients negotiate compatibility on `ContractVersion`.",
            operationId: "getHealth")
            .WithResponse(200, Ok("HealthResponse"));

        // --- Overview ---

        /// <summary>Metadata for <c>GET /v1.0/api/overview</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> OverviewGet = m => Sec(Init(m, TagOverview,
            "Home overview aggregate",
            "Returns counts (endpoints, sessions, skills, hooks, …), the default endpoint, environment facts, recent sessions, and derived notices — everything a home screen needs in one call. Computed from config files, health, and the session store; no telemetry.",
            operationId: "getOverview"))
            .WithResponse(200, Ok("OverviewDto"))
            .WithResponse(401, Unauthorized());

        // --- Endpoints ---

        /// <summary>Metadata for <c>GET /v1.0/api/endpoints</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> EndpointsList = m => Sec(Init(m, TagEndpoints,
            "List endpoints",
            "Returns the configured endpoints as safe summaries (name, adapter, base URL, model, default flag). Never carries secrets.",
            operationId: "listEndpoints"))
            .WithResponse(200, OkList("EndpointSummary"))
            .WithResponse(401, Unauthorized());

        /// <summary>Metadata for <c>GET /v1.0/api/endpoints/detail</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> EndpointsDetail = m => Sec(Init(m, TagEndpoints,
            "List endpoints (editable detail)",
            "Returns the full, editable form of every endpoint for a configuration UI. API keys are masked (`ApiKeySet` reports whether one is stored) and header values are blanked; their keys are preserved.",
            operationId: "listEndpointDetails"))
            .WithResponse(200, OkList("EndpointDto"))
            .WithResponse(401, Unauthorized());

        /// <summary>Metadata for <c>PUT /v1.0/api/endpoints</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> EndpointsPut = m => Sec(Init(m, TagEndpoints,
            "Replace endpoints",
            "Replaces the endpoint set with the supplied list (full-set upsert). A blank `ApiKey` or blank header value preserves the stored secret for that endpoint/header; a non-blank value replaces it. Exactly one endpoint may be `IsDefault`.",
            operationId: "putEndpoints"))
            .WithRequestBody(BodyList("EndpointDto", "The complete desired endpoint set."))
            .WithResponse(200, OkList("EndpointDto"))
            .WithResponse(400, BadRequest())
            .WithResponse(401, Unauthorized());

        /// <summary>Metadata for <c>DELETE /v1.0/api/endpoints</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> EndpointsDelete = m => Sec(Init(m, TagEndpoints,
            "Delete an endpoint",
            "Deletes the endpoint named by the `name` query parameter.",
            operationId: "deleteEndpoint"))
            .WithParameter(Query("name", "The endpoint name to delete.", true))
            .WithResponse(200, OkList("EndpointDto"))
            .WithResponse(400, BadRequest())
            .WithResponse(401, Unauthorized())
            .WithResponse(404, NotFound());

        // --- Chat ---

        /// <summary>Metadata for <c>POST /v1.0/api/chat</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> ChatPost = m => Sec(Init(m, TagChat,
            "Run a chat completion",
            "Runs a completion against a configured endpoint and returns the assistant reply plus per-turn timing/token statistics. Pass the full conversation (including the new user turn) in `Messages`. Supply `Id` to associate the run with a session for telemetry; supply `WorkingDirectory` to resolve tool paths in a specific workspace.",
            operationId: "chat"))
            .WithRequestBody(Body("ChatRequest", "The endpoint and conversation to run."))
            .WithResponse(200, Ok("ChatReply"))
            .WithResponse(400, BadRequest())
            .WithResponse(401, Unauthorized())
            .WithResponse(404, NotFound());

        /// <summary>Metadata for <c>POST /v1.0/api/chat/stream</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> ChatStream = m => Sec(Init(m, TagChat,
            "Run a streaming chat completion (SSE)",
            "Runs a completion and streams the result as Server-Sent Events. Event names: `token` (a text delta), `thinking` (a reasoning delta), `tool` (a tool lifecycle event), `approval` (a tool-approval prompt, only when interactive tools are enabled), `done` (terminal, carries the final content and stats), and `error`. The request body is identical to `POST /v1.0/api/chat`. The response is `text/event-stream`, not JSON.",
            operationId: "chatStream"))
            .WithRequestBody(Body("ChatRequest", "The endpoint and conversation to run."))
            .WithResponse(200, new OpenApiResponseMetadata
            {
                Description = "An event stream. Each event is `event: <name>\\ndata: <json>\\n\\n`.",
                Content = new Dictionary<string, OpenApiMediaTypeMetadata>
                {
                    ["text/event-stream"] = new OpenApiMediaTypeMetadata { Schema = M.String(), Example = "event: token\ndata: \"Hello\"\n\nevent: done\ndata: {\"Id\":\"…\",\"Content\":\"Hello there.\",\"Stats\":{\"TotalMs\":812}}\n\n" }
                }
            })
            .WithResponse(400, BadRequest())
            .WithResponse(401, Unauthorized());

        /// <summary>Metadata for <c>POST /v1.0/api/chat/approve</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> ChatApprove = m => Sec(Init(m, TagChat,
            "Answer a tool-approval prompt",
            "Resolves an `approval` event raised during an interactive streaming run. `Decision` is `y` (approve once), `always` (approve and remember), or `n` (deny). Only meaningful when the server runs with interactive web tools enabled.",
            operationId: "chatApprove"))
            .WithRequestBody(Body("ChatApproveRequest", "The approval decision."))
            .WithResponse(200, RespJson("The decision was applied.", Obj(new Dictionary<string, M> { ["ok"] = Pbool("Always true on success.") }, new Dictionary<string, object?> { ["ok"] = true })))
            .WithResponse(400, BadRequest())
            .WithResponse(401, Unauthorized())
            .WithResponse(404, RespJson("No pending approval matched the run and tool-call id.", Ref("ApiError")));

        /// <summary>Metadata for <c>POST /v1.0/api/model/load</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> ModelLoad = m => Sec(Init(m, TagChat,
            "Warm (pre-load) a model",
            "Best-effort probe that asks the endpoint's model to load so the next chat's first token is fast. Reports whether the probe succeeded and whether the endpoint was reachable at all. Only `Endpoint` is read from the body.",
            operationId: "loadModel"))
            .WithRequestBody(Body("ChatRequest", "The endpoint to warm (only `Endpoint` is used)."))
            .WithResponse(200, Ok("ModelLoadReply"))
            .WithResponse(400, BadRequest())
            .WithResponse(401, Unauthorized())
            .WithResponse(404, NotFound());

        // --- Sessions ---

        /// <summary>Metadata for <c>GET /v1.0/api/sessions</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> SessionsList = m => Sec(Init(m, TagSessions,
            "List sessions",
            "Returns saved conversations as summaries, most-recently-updated first. Sessions are shared across the terminal UI, desktop app, web dashboard, and VS Code extension.",
            operationId: "listSessions"))
            .WithResponse(200, OkList("SessionSummary"))
            .WithResponse(401, Unauthorized());

        /// <summary>Metadata for <c>GET /v1.0/api/sessions/detail</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> SessionsDetail = m => Sec(Init(m, TagSessions,
            "Read a session transcript",
            "Returns the full transcript (oldest message first, tool-call structure preserved) of the session named by the `id` query parameter.",
            operationId: "getSession"))
            .WithParameter(Query("id", "The session id to read.", true))
            .WithResponse(200, Ok("SessionDetailDto"))
            .WithResponse(400, BadRequest())
            .WithResponse(401, Unauthorized())
            .WithResponse(404, NotFound());

        /// <summary>Metadata for <c>PUT /v1.0/api/sessions</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> SessionsPut = m => Sec(Init(m, TagSessions,
            "Create or update a session",
            "Upserts a saved conversation. When `Id` is empty a new session is created and its server-authored id is returned; otherwise the existing session is replaced. A blank `Title` is derived from the first user message.",
            operationId: "putSession"))
            .WithRequestBody(Body("SessionSaveRequest", "The conversation to persist."))
            .WithResponse(200, Ok("SessionSummary"))
            .WithResponse(400, BadRequest())
            .WithResponse(401, Unauthorized());

        /// <summary>Metadata for <c>GET /v1.0/api/sessions/export</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> SessionsExport = m => Sec(Init(m, TagSessions,
            "Export a session",
            "Renders the session named by `id` to a downloadable document. `format` is `md` (default) or `html`. Returns the rendered content and a suggested filename.",
            operationId: "exportSession"))
            .WithParameter(Query("id", "The session id to export.", true))
            .WithParameter(Query("format", "Export format: `md` (default) or `html`.", false, M.String()))
            .WithResponse(200, Ok("SessionExportDto"))
            .WithResponse(400, BadRequest())
            .WithResponse(401, Unauthorized())
            .WithResponse(404, NotFound());

        /// <summary>Metadata for <c>DELETE /v1.0/api/sessions</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> SessionsDelete = m => Sec(Init(m, TagSessions,
            "Delete a session",
            "Deletes the session named by the `id` query parameter.",
            operationId: "deleteSession"))
            .WithParameter(Query("id", "The session id to delete.", true))
            .WithResponse(200, OkList("SessionSummary"))
            .WithResponse(400, BadRequest())
            .WithResponse(401, Unauthorized())
            .WithResponse(404, NotFound());

        // --- Checkpoints ---

        /// <summary>Metadata for <c>GET /v1.0/api/checkpoints</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> CheckpointsGet = m => Sec(Init(m, TagCheckpoints,
            "Checkpoint availability",
            "Reports whether the working directory (given by the `workingDirectory` query parameter, or the server's directory) is a git repository and whether a turn's changes can currently be undone or redone.",
            operationId: "getCheckpointStatus"))
            .WithParameter(Query("workingDirectory", "The working directory to inspect. Defaults to the server's current directory.", false, M.String()))
            .WithResponse(200, Ok("CheckpointStatus"))
            .WithResponse(401, Unauthorized());

        /// <summary>Metadata for <c>POST /v1.0/api/checkpoints/undo</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> CheckpointsUndo = m => Sec(Init(m, TagCheckpoints,
            "Undo the last turn",
            "Restores the working tree to before the last chat turn's file changes, using git shadow refs only (your branch, history, and stash are untouched). Returns the new undo/redo availability.",
            operationId: "undoCheckpoint"))
            .WithRequestBody(Body("CheckpointActionRequest", "The working directory to act on."))
            .WithResponse(200, Ok("CheckpointActionResult"))
            .WithResponse(400, BadRequest())
            .WithResponse(401, Unauthorized());

        /// <summary>Metadata for <c>POST /v1.0/api/checkpoints/redo</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> CheckpointsRedo = m => Sec(Init(m, TagCheckpoints,
            "Redo an undone turn",
            "Re-applies the file changes of a turn that was undone. Returns the new undo/redo availability.",
            operationId: "redoCheckpoint"))
            .WithRequestBody(Body("CheckpointActionRequest", "The working directory to act on."))
            .WithResponse(200, Ok("CheckpointActionResult"))
            .WithResponse(400, BadRequest())
            .WithResponse(401, Unauthorized());

        // --- Prompts ---

        /// <summary>Metadata for <c>GET /v1.0/api/prompts</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> PromptsGet = m => Sec(Init(m, TagPrompts,
            "List prompt profiles",
            "Returns all prompt profiles and which one is active.",
            operationId: "listPrompts"))
            .WithResponse(200, OkList("PromptProfileDto"))
            .WithResponse(401, Unauthorized());

        /// <summary>Metadata for <c>PUT /v1.0/api/prompts</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> PromptsPut = m => Sec(Init(m, TagPrompts,
            "Replace prompt profiles",
            "Replaces the prompt-profile set with the supplied list. At most one profile should be `IsActive`; a blank `SystemPrompt` inherits the built-in default.",
            operationId: "putPrompts"))
            .WithRequestBody(BodyList("PromptProfileDto", "The complete desired prompt-profile set."))
            .WithResponse(200, OkList("PromptProfileDto"))
            .WithResponse(400, BadRequest())
            .WithResponse(401, Unauthorized());

        // --- Subagents ---

        /// <summary>Metadata for <c>GET /v1.0/api/subagents</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> SubagentsGet = m => Sec(Init(m, TagSubagents,
            "List subagents",
            "Returns all subagent definitions.",
            operationId: "listSubagents"))
            .WithResponse(200, OkList("SubagentDto"))
            .WithResponse(401, Unauthorized());

        /// <summary>Metadata for <c>PUT /v1.0/api/subagents</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> SubagentsPut = m => Sec(Init(m, TagSubagents,
            "Replace subagents",
            "Replaces the subagent set with the supplied list. `EndpointName` null inherits the active endpoint; `AllowedTools` limits the child to matching tool-name globs; `MaxIterations` null inherits the global cap.",
            operationId: "putSubagents"))
            .WithRequestBody(BodyList("SubagentDto", "The complete desired subagent set."))
            .WithResponse(200, OkList("SubagentDto"))
            .WithResponse(400, BadRequest())
            .WithResponse(401, Unauthorized());

        // --- Plugins (hooks / commands) ---

        /// <summary>Metadata for <c>GET /v1.0/api/hooks</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> HooksGet = m => Sec(Init(m, TagPlugins,
            "Read plugin configuration",
            "Returns the event hooks and custom commands.",
            operationId: "getPlugins"))
            .WithResponse(200, Ok("PluginConfigDto"))
            .WithResponse(401, Unauthorized());

        /// <summary>Metadata for <c>PUT /v1.0/api/hooks</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> HooksPut = m => Sec(Init(m, TagPlugins,
            "Replace plugin configuration",
            "Replaces the event hooks and custom commands. Hook events are `session-start`, `user-prompt-submit`, and `session-end`; a `Blocking` hook can veto a vetoable event by exiting non-zero.",
            operationId: "putPlugins"))
            .WithRequestBody(Body("PluginConfigDto", "The complete desired hooks and commands."))
            .WithResponse(200, Ok("PluginConfigDto"))
            .WithResponse(400, BadRequest())
            .WithResponse(401, Unauthorized());

        // --- Keybindings ---

        /// <summary>Metadata for <c>GET /v1.0/api/keybindings</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> KeybindingsGet = m => Sec(Init(m, TagKeybindings,
            "List keybinding overrides",
            "Returns the keybinding overrides (command id → chord).",
            operationId: "listKeybindings"))
            .WithResponse(200, OkList("KeybindingDto"))
            .WithResponse(401, Unauthorized());

        /// <summary>Metadata for <c>PUT /v1.0/api/keybindings</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> KeybindingsPut = m => Sec(Init(m, TagKeybindings,
            "Replace keybinding overrides",
            "Replaces the keybinding overrides with the supplied list. A null/blank `Chord` unbinds the command.",
            operationId: "putKeybindings"))
            .WithRequestBody(BodyList("KeybindingDto", "The complete desired keybinding overrides."))
            .WithResponse(200, OkList("KeybindingDto"))
            .WithResponse(400, BadRequest())
            .WithResponse(401, Unauthorized());

        // --- MCP ---

        /// <summary>Metadata for <c>GET /v1.0/api/mcp-servers</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> McpGet = m => Sec(Init(m, TagMcp,
            "List MCP servers",
            "Returns the configured Model Context Protocol servers. Auth secrets are masked (`AuthSecretSet` reports whether one is stored).",
            operationId: "listMcpServers"))
            .WithResponse(200, OkList("McpServerDto"))
            .WithResponse(401, Unauthorized());

        /// <summary>Metadata for <c>PUT /v1.0/api/mcp-servers</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> McpPut = m => Sec(Init(m, TagMcp,
            "Replace MCP servers",
            "Replaces the MCP server set with the supplied list. `Transport` is `stdio` (uses `Command`/`Args`/`Env`) or `http` (uses `Url`/`McpPath`). A blank `AuthSecret` preserves the stored secret.",
            operationId: "putMcpServers"))
            .WithRequestBody(BodyList("McpServerDto", "The complete desired MCP server set."))
            .WithResponse(200, OkList("McpServerDto"))
            .WithResponse(400, BadRequest())
            .WithResponse(401, Unauthorized());

        /// <summary>Metadata for <c>DELETE /v1.0/api/mcp-servers</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> McpDelete = m => Sec(Init(m, TagMcp,
            "Delete an MCP server",
            "Deletes the MCP server named by the `name` query parameter.",
            operationId: "deleteMcpServer"))
            .WithParameter(Query("name", "The MCP server name to delete.", true))
            .WithResponse(200, OkList("McpServerDto"))
            .WithResponse(400, BadRequest())
            .WithResponse(401, Unauthorized())
            .WithResponse(404, NotFound());

        // --- Skills ---

        /// <summary>Metadata for <c>GET /v1.0/api/skills</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> SkillsList = m => Sec(Init(m, TagSkills,
            "List skills",
            "Returns the discovered skills with their enabled/valid/mutating state and command counts. The SKILL.md body is omitted here.",
            operationId: "listSkills"))
            .WithResponse(200, OkList("SkillDto"))
            .WithResponse(401, Unauthorized());

        /// <summary>Metadata for <c>GET /v1.0/api/skills/detail</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> SkillsDetail = m => Sec(Init(m, TagSkills,
            "Read a skill",
            "Returns a single skill including its SKILL.md `Body`, selected by the `name` query parameter.",
            operationId: "getSkill"))
            .WithParameter(Query("name", "The skill name to read.", true))
            .WithResponse(200, Ok("SkillDto"))
            .WithResponse(400, BadRequest())
            .WithResponse(401, Unauthorized())
            .WithResponse(404, NotFound());

        /// <summary>Metadata for <c>PUT /v1.0/api/skills/enabled</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> SkillsEnabled = m => Sec(Init(m, TagSkills,
            "Enable or disable a skill",
            "Sets the enabled state of the skill named by `Id`. Returns the refreshed skill list.",
            operationId: "setSkillEnabled"))
            .WithRequestBody(Body("SkillToggleRequest", "The skill id and desired enabled state."))
            .WithResponse(200, OkList("SkillDto"))
            .WithResponse(400, BadRequest())
            .WithResponse(401, Unauthorized());

        /// <summary>Metadata for <c>POST /v1.0/api/skills</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> SkillsCreate = m => Sec(Init(m, TagSkills,
            "Create a skill",
            "Creates a new skill from a `Name` and a SKILL.md `Body`. Returns the refreshed skill list.",
            operationId: "createSkill"))
            .WithRequestBody(Body("SkillEditRequest", "The new skill's name and SKILL.md body."))
            .WithResponse(200, OkList("SkillDto"))
            .WithResponse(400, BadRequest())
            .WithResponse(401, Unauthorized());

        /// <summary>Metadata for <c>PUT /v1.0/api/skills/body</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> SkillsBody = m => Sec(Init(m, TagSkills,
            "Edit a skill's body",
            "Replaces the SKILL.md `Body` of the skill named by `Id` (or `Name`). Returns the refreshed skill list.",
            operationId: "setSkillBody"))
            .WithRequestBody(Body("SkillEditRequest", "The target skill and its new SKILL.md body."))
            .WithResponse(200, OkList("SkillDto"))
            .WithResponse(400, BadRequest())
            .WithResponse(401, Unauthorized());

        /// <summary>Metadata for <c>DELETE /v1.0/api/skills</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> SkillsDelete = m => Sec(Init(m, TagSkills,
            "Delete a skill",
            "Deletes the skill named by the `name` query parameter. Returns the refreshed skill list.",
            operationId: "deleteSkill"))
            .WithParameter(Query("name", "The skill name to delete.", true))
            .WithResponse(200, OkList("SkillDto"))
            .WithResponse(400, BadRequest())
            .WithResponse(401, Unauthorized())
            .WithResponse(404, NotFound());

        // --- Settings ---

        /// <summary>Metadata for <c>GET /v1.0/api/settings</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> SettingsGet = m => Sec(Init(m, TagSettings,
            "Read settings",
            "Returns the editable server settings. The REST API key is masked (`Rest.ApiKeySet` reports whether one is stored; `Rest.ApiKey` is never returned).",
            operationId: "getSettings"))
            .WithResponse(200, Ok("SettingsDto"))
            .WithResponse(401, Unauthorized());

        /// <summary>Metadata for <c>PUT /v1.0/api/settings</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> SettingsPut = m => Sec(Init(m, TagSettings,
            "Update settings",
            "Updates the server settings. Values are clamped/normalized on write. The REST API key changes only when a non-blank `Rest.ApiKey` is supplied.",
            operationId: "putSettings"))
            .WithRequestBody(Body("SettingsDto", "The desired settings."))
            .WithResponse(200, Ok("SettingsDto"))
            .WithResponse(400, BadRequest())
            .WithResponse(401, Unauthorized());

        // --- Usage ---

        /// <summary>Metadata for <c>GET /v1.0/api/usage/summary</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> UsageSummary = m => Sec(Init(m, TagUsage,
            "Usage summary",
            "Returns aggregate usage metrics (calls, tokens, cost, latency/TTFT distributions) for a window, optionally filtered by endpoint and/or model. When telemetry is disabled, returns empty metrics.",
            operationId: "getUsageSummary"))
            .WithParameter(Query("range", "Convenience window: `hour`, `day`, `week`, `month`, or `all`. Ignored when `from`/`to` are supplied.", false, M.String()))
            .WithParameter(Query("from", "Window start (Unix ms). Overrides `range`.", false, M.Integer("int64")))
            .WithParameter(Query("to", "Window end (Unix ms). Overrides `range`.", false, M.Integer("int64")))
            .WithParameter(Query("endpoint", "Filter to a single endpoint name.", false, M.String()))
            .WithParameter(Query("model", "Filter to a single model.", false, M.String()))
            .WithResponse(200, Ok("UsageSummary"))
            .WithResponse(401, Unauthorized());

        /// <summary>Metadata for <c>GET /v1.0/api/usage/timeseries</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> UsageTimeseries = m => Sec(Init(m, TagUsage,
            "Usage time series",
            "Returns an evenly-spaced series of usage buckets for charting a window (60×1-min for the hour, 96×15-min for the day, 84×2-hour for the week, 60×12-hour for the month), optionally filtered by endpoint and/or model.",
            operationId: "getUsageTimeseries"))
            .WithParameter(Query("range", "Convenience window: `hour`, `day`, `week`, `month`, or `all`.", false, M.String()))
            .WithParameter(Query("from", "Window start (Unix ms). Overrides `range`.", false, M.Integer("int64")))
            .WithParameter(Query("to", "Window end (Unix ms). Overrides `range`.", false, M.Integer("int64")))
            .WithParameter(Query("endpoint", "Filter to a single endpoint name.", false, M.String()))
            .WithParameter(Query("model", "Filter to a single model.", false, M.String()))
            .WithResponse(200, OkList("UsageBucket"))
            .WithResponse(401, Unauthorized());

        /// <summary>Metadata for <c>GET /v1.0/api/usage/breakdown</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> UsageBreakdown = m => Sec(Init(m, TagUsage,
            "Usage breakdown",
            "Returns usage grouped by a dimension (`model`, `endpoint`, `callKind`, …) for a window.",
            operationId: "getUsageBreakdown"))
            .WithParameter(Query("dimension", "Group-by dimension: `model` (default), `endpoint`, or `callKind`.", false, M.String()))
            .WithParameter(Query("range", "Convenience window: `hour`, `day`, `week`, `month`, or `all`.", false, M.String()))
            .WithParameter(Query("endpoint", "Filter to a single endpoint name.", false, M.String()))
            .WithParameter(Query("model", "Filter to a single model.", false, M.String()))
            .WithResponse(200, OkList("UsageBreakdownRow"))
            .WithResponse(401, Unauthorized());

        /// <summary>Metadata for <c>GET /v1.0/api/usage/events</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> UsageEvents = m => Sec(Init(m, TagUsage,
            "Usage event history",
            "Returns the paginated raw usage events for a window.",
            operationId: "getUsageEvents"))
            .WithParameter(Query("range", "Convenience window: `hour`, `day`, `week`, `month`, or `all`.", false, M.String()))
            .WithParameter(Query("page", "1-based page number (default 1).", false, M.Integer()))
            .WithParameter(Query("pageSize", "Rows per page (default 25).", false, M.Integer()))
            .WithResponse(200, Ok("UsageEventPage"))
            .WithResponse(401, Unauthorized());

        /// <summary>Metadata for <c>DELETE /v1.0/api/usage/events</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> UsageEventsDelete = m => Sec(Init(m, TagUsage,
            "Delete a usage event",
            "Deletes the single usage event identified by the numeric `id` query parameter.",
            operationId: "deleteUsageEvent"))
            .WithParameter(Query("id", "The numeric usage-event id to delete.", true, M.Integer("int64")))
            .WithResponse(200, Ok("UsageDeleteResult"))
            .WithResponse(400, BadRequest())
            .WithResponse(401, Unauthorized());

        /// <summary>Metadata for <c>GET /v1.0/api/usage/filters</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> UsageFilters = m => Sec(Init(m, TagUsage,
            "Usage filter values",
            "Returns the distinct endpoints and models seen in the telemetry store (for populating filter controls), plus whether telemetry is enabled.",
            operationId: "getUsageFilters"))
            .WithResponse(200, Ok("UsageFiltersDto"))
            .WithResponse(401, Unauthorized());

        /// <summary>Metadata for <c>GET /v1.0/api/usage/pricing</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> UsagePricingGet = m => Sec(Init(m, TagUsage,
            "Read pricing table",
            "Returns the per-model pricing table used to compute cost. Served from `pricing.json` independently of telemetry.",
            operationId: "getPricing"))
            .WithResponse(200, Ok("PricingTable"))
            .WithResponse(401, Unauthorized());

        /// <summary>Metadata for <c>PUT /v1.0/api/usage/pricing</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> UsagePricingPut = m => Sec(Init(m, TagUsage,
            "Update pricing table",
            "Replaces the per-model pricing table and persists it to `pricing.json`.",
            operationId: "putPricing"))
            .WithRequestBody(Body("PricingTable", "The desired pricing table."))
            .WithResponse(200, Ok("PricingTable"))
            .WithResponse(400, BadRequest())
            .WithResponse(401, Unauthorized());

        // --- Runs ---

        /// <summary>Metadata for <c>GET /v1.0/api/runs</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> RunsList = m => Sec(Init(m, TagRuns,
            "List runs",
            "Returns active and recently-finished runs as summaries, most-recently-started first. Terminal runs are retained briefly (a few minutes) for inspection, then evicted.",
            operationId: "listRuns"))
            .WithResponse(200, OkList("RunSummaryDto"))
            .WithResponse(401, Unauthorized());

        /// <summary>Metadata for <c>GET /v1.0/api/runs/{runId}</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> RunsGet = m => Sec(Init(m, TagRuns,
            "Inspect a run",
            "Returns the full live state of a run: status, counters, the currently executing tool, the last error, and the current task-plan checklist. The `{runId}` path segment is the run id from the chat stream's `run` event.",
            operationId: "getRun"))
            .WithResponse(200, Ok("RunStateReply"))
            .WithResponse(401, Unauthorized())
            .WithResponse(404, NotFound());

        /// <summary>Metadata for <c>POST /v1.0/api/runs/{runId}/cancel</c>.</summary>
        public static readonly Action<OpenApiRouteMetadata> RunsCancel = m => Sec(Init(m, TagRuns,
            "Cancel a run",
            "Requests cooperative cancellation of an active run: the run's cancellation token is tripped so the agent loop and any in-flight tool stop at the next check, and a terminal `run_completed` (status `canceled`) is published to any stream subscribers. Returns 404 when the run is unknown or already finished.",
            operationId: "cancelRun"))
            .WithResponse(200, Ok("RunCancelReply"))
            .WithResponse(401, Unauthorized())
            .WithResponse(404, RespJson("No active run with that id.", Ref("ApiError")));

        #endregion

        #region Private-Builders

        /// <summary>Applies the common summary/description/tag/operationId and returns the metadata for chaining.</summary>
        private static OpenApiRouteMetadata Init(OpenApiRouteMetadata m, string tag, string summary, string description, string operationId)
        {
            m.Summary = summary;
            m.Description = description;
            m.OperationId = operationId;
            m.WithTag(tag);
            return m;
        }

        /// <summary>Attaches the global bearer security requirement to an authenticated operation.</summary>
        private static OpenApiRouteMetadata Sec(OpenApiRouteMetadata m)
        {
            m.Security = new List<string> { "bearerAuth" };
            return m;
        }

        private static M Ref(string name) => M.CreateRef(name);

        /// <summary>The <c>{ Items: [ $ref ], Count }</c> envelope mux wraps array payloads in.</summary>
        private static M ListSchema(string itemSchemaName) => new M
        {
            Type = "object",
            Description = "A self-describing list envelope.",
            Properties = new Dictionary<string, M>
            {
                ["Items"] = M.CreateArray(Ref(itemSchemaName)),
                ["Count"] = Pint("The number of items.")
            }
        };

        private static OpenApiResponseMetadata Ok(string schemaName) => OpenApiResponseMetadata.Json("Success.", Ref(schemaName));

        private static OpenApiResponseMetadata OkList(string itemSchemaName) => OpenApiResponseMetadata.Json("Success.", ListSchema(itemSchemaName));

        private static OpenApiResponseMetadata RespJson(string description, M schema) => OpenApiResponseMetadata.Json(description, schema);

        private static OpenApiResponseMetadata Unauthorized() => OpenApiResponseMetadata.Json("Authentication required (an API key is configured and was missing or wrong).", Ref("ApiError"));

        private static OpenApiResponseMetadata BadRequest() => OpenApiResponseMetadata.Json("The request was malformed or failed validation.", Ref("ApiError"));

        private static OpenApiResponseMetadata NotFound() => OpenApiResponseMetadata.Json("The referenced resource was not found.", Ref("ApiError"));

        private static OpenApiRequestBodyMetadata Body(string schemaName, string description) => new OpenApiRequestBodyMetadata
        {
            Description = description,
            Required = true,
            Content = new Dictionary<string, OpenApiMediaTypeMetadata>
            {
                ["application/json"] = new OpenApiMediaTypeMetadata { Schema = Ref(schemaName) }
            }
        };

        private static OpenApiRequestBodyMetadata BodyList(string itemSchemaName, string description) => new OpenApiRequestBodyMetadata
        {
            Description = description,
            Required = true,
            Content = new Dictionary<string, OpenApiMediaTypeMetadata>
            {
                ["application/json"] = new OpenApiMediaTypeMetadata { Schema = ListSchema(itemSchemaName) }
            }
        };

        private static OpenApiParameterMetadata Query(string name, string description, bool required, M? schema = null)
            => OpenApiParameterMetadata.Query(name, description, required, schema ?? M.String());

        #endregion

        #region Schema-Helpers

        private static M Pstr(string desc) => new M { Type = "string", Description = desc };
        private static M Pstr(string desc, string format) => new M { Type = "string", Description = desc, Format = format };
        private static M PstrNullable(string desc) => new M { Type = "string", Description = desc, Nullable = true };
        private static M Pint(string desc, string format = "int32") => new M { Type = "integer", Format = format, Description = desc };
        private static M PintNullable(string desc, string format = "int32") => new M { Type = "integer", Format = format, Description = desc, Nullable = true };
        private static M Pbool(string desc) => new M { Type = "boolean", Description = desc };
        private static M Pnum(string desc) => new M { Type = "number", Description = desc };
        private static M Parr(M items, string desc) => new M { Type = "array", Items = items, Description = desc };

        private static M Obj(Dictionary<string, M> props, object? example = null, List<string>? required = null, string? description = null) => new M
        {
            Type = "object",
            Description = description,
            Properties = props,
            Required = required,
            Example = example
        };

        #endregion

        #region Component-Schemas

        /// <summary>
        /// Registers every reusable component schema (with an example value) under <c>components.schemas</c>.
        /// Route responses and request bodies reference these by name.
        /// </summary>
        private static void RegisterSchemas(Dictionary<string, M> s)
        {
            s["ApiError"] = Obj(new Dictionary<string, M>
            {
                ["Error"] = Pstr("Short machine-readable error code."),
                ["Message"] = Pstr("Human-readable message.")
            }, new Dictionary<string, object?> { ["Error"] = "BadRequest", ["Message"] = "'endpoint' is required." },
               new List<string> { "Error", "Message" }, "The standard error envelope returned for 4xx/5xx responses.");

            s["HealthResponse"] = Obj(new Dictionary<string, M>
            {
                ["Status"] = Pstr("Fixed status literal (`healthy`)."),
                ["Product"] = Pstr("Product name (`mux`)."),
                ["Version"] = Pstr("Product version."),
                ["ContractVersion"] = Pstr("API contract version clients negotiate against."),
                ["Pid"] = Pint("Process id hosting the server."),
                ["StartedUtc"] = Pstr("UTC start time.", "date-time"),
                ["Uptime"] = Pstr("Uptime, formatted d.hh:mm:ss."),
                ["TimestampUtc"] = Pstr("UTC time this response was produced.", "date-time")
            }, new Dictionary<string, object?>
            {
                ["Status"] = "healthy", ["Product"] = "mux", ["Version"] = "0.11.0", ["ContractVersion"] = "1.0",
                ["Pid"] = 41288, ["Uptime"] = "0.00:12:03"
            });

            s["EndpointSummary"] = Obj(new Dictionary<string, M>
            {
                ["Name"] = Pstr("Endpoint name."),
                ["AdapterType"] = Pstr("Adapter type (kebab-case)."),
                ["BaseUrl"] = PstrNullable("Base URL, if set."),
                ["Model"] = Pstr("Model identifier."),
                ["IsDefault"] = Pbool("Whether this endpoint is the configured default.")
            }, new Dictionary<string, object?>
            {
                ["Name"] = "local-ollama", ["AdapterType"] = "ollama", ["BaseUrl"] = "http://localhost:11434",
                ["Model"] = "qwen2.5-coder:7b", ["IsDefault"] = true
            });

            s["HeaderDto"] = Obj(new Dictionary<string, M>
            {
                ["Key"] = Pstr("Header name."),
                ["Value"] = Pstr("Header value (blank on read; blank on write preserves the stored value)."),
                ["ValueSet"] = Pbool("Whether a value is stored for this header (read-only).")
            }, new Dictionary<string, object?> { ["Key"] = "X-Org", ["Value"] = "", ["ValueSet"] = true });

            s["EndpointDto"] = Obj(new Dictionary<string, M>
            {
                ["Name"] = Pstr("Unique endpoint name."),
                ["AdapterType"] = Pstr("Adapter type (kebab-case): `ollama`, `openai`, `azure-openai`, `anthropic`, `vertex`, `bedrock`, …"),
                ["BaseUrl"] = Pstr("Base URL."),
                ["Model"] = Pstr("Model identifier (or Azure deployment name)."),
                ["IsDefault"] = Pbool("Whether this endpoint is the default."),
                ["MaxTokens"] = Pint("Max output tokens."),
                ["Temperature"] = Pnum("Sampling temperature."),
                ["ContextWindow"] = Pint("Model context window."),
                ["TimeoutMs"] = Pint("HTTP timeout in milliseconds."),
                ["AutoApproveTools"] = Pbool("Auto-approve tool calls when this endpoint is active."),
                ["MaxAgentIterations"] = PintNullable("Per-endpoint agent-iteration override; null inherits the global setting."),
                ["ShowThinking"] = Pbool("Whether the model's reasoning is displayed."),
                ["ReasoningEffort"] = PstrNullable("Reasoning-effort level (off/minimal/low/medium/high), or null."),
                ["ApiKeySet"] = Pbool("Whether a stored API key exists (read-only)."),
                ["ApiKey"] = PstrNullable("New API key to store; blank/null preserves the existing key. Never returned on read."),
                ["Region"] = PstrNullable("Cloud region (vertex/bedrock)."),
                ["Project"] = PstrNullable("Google Cloud project id (vertex)."),
                ["ApiVersion"] = PstrNullable("Azure OpenAI api-version."),
                ["Headers"] = Parr(Ref("HeaderDto"), "Custom header entries.")
            }, new Dictionary<string, object?>
            {
                ["Name"] = "openai-gpt4o", ["AdapterType"] = "openai", ["BaseUrl"] = "https://api.openai.com/v1",
                ["Model"] = "gpt-4o", ["IsDefault"] = false, ["MaxTokens"] = 4096, ["Temperature"] = 0.2,
                ["ContextWindow"] = 128000, ["TimeoutMs"] = 120000, ["AutoApproveTools"] = false,
                ["ShowThinking"] = false, ["ApiKeySet"] = true, ["ApiKey"] = "", ["Headers"] = new object[0]
            }, description: "A configured endpoint projected for editing. Secrets are masked; a blank secret on write preserves the stored value.");

            s["SessionSummary"] = Obj(new Dictionary<string, M>
            {
                ["Id"] = Pstr("Session id."),
                ["Title"] = Pstr("Session title."),
                ["EndpointName"] = Pstr("Endpoint name captured with the session."),
                ["Model"] = Pstr("Model captured with the session."),
                ["CreatedUtc"] = Pstr("UTC creation time.", "date-time"),
                ["UpdatedUtc"] = Pstr("UTC last-updated time.", "date-time"),
                ["MessageCount"] = Pint("Number of messages in the conversation.")
            }, new Dictionary<string, object?>
            {
                ["Id"] = "9f1c…", ["Title"] = "Refactor the auth module", ["EndpointName"] = "openai-gpt4o",
                ["Model"] = "gpt-4o", ["MessageCount"] = 8
            });

            s["ChatToolCallDto"] = Obj(new Dictionary<string, M>
            {
                ["Id"] = Pstr("Tool-call id (correlates the call with its result)."),
                ["Name"] = Pstr("Tool name."),
                ["Arguments"] = Pstr("Raw JSON arguments string.")
            }, new Dictionary<string, object?> { ["Id"] = "call_1", ["Name"] = "read_file", ["Arguments"] = "{\"path\":\"README.md\"}" });

            s["ChatMessageDto"] = Obj(new Dictionary<string, M>
            {
                ["Role"] = Pstr("Role: `user`, `assistant`, `system`, or `tool`."),
                ["Content"] = Pstr("Message content."),
                ["Reasoning"] = PstrNullable("The assistant's reasoning (\"thinking\") for this message, or null. Persisted with the conversation so every surface can show it; never sent back to the model."),
                ["ToolCalls"] = new M { Type = "array", Items = Ref("ChatToolCallDto"), Nullable = true, Description = "Tool calls requested by the assistant, or null." },
                ["ToolCallId"] = PstrNullable("The id of the tool call this message answers, or null.")
            }, new Dictionary<string, object?> { ["Role"] = "user", ["Content"] = "Explain this function." },
               new List<string> { "Role", "Content" }, "A single chat message. Tool-call structure is preserved across surfaces.");

            s["ChatRequest"] = Obj(new Dictionary<string, M>
            {
                ["Endpoint"] = Pstr("Configured endpoint name to run against."),
                ["Id"] = PstrNullable("Optional conversation/session id, used to tag usage telemetry."),
                ["WorkingDirectory"] = PstrNullable("Optional working directory the run's tools resolve paths against. Must exist when set."),
                ["Messages"] = Parr(Ref("ChatMessageDto"), "The conversation so far, including the new user turn.")
            }, new Dictionary<string, object?>
            {
                ["Endpoint"] = "openai-gpt4o",
                ["Id"] = "9f1c…",
                ["WorkingDirectory"] = "C:/Code/Mux",
                ["Messages"] = new object[]
                {
                    new Dictionary<string, object?> { ["Role"] = "user", ["Content"] = "Summarize the README." }
                }
            }, new List<string> { "Endpoint", "Messages" }, "Run a completion against a configured endpoint.");

            s["ChatStats"] = Obj(new Dictionary<string, M>
            {
                ["TtftMs"] = Pint("Milliseconds to the first streamed token (−1 when none streamed).", "int64"),
                ["StreamingMs"] = Pint("Milliseconds spent streaming.", "int64"),
                ["TotalMs"] = Pint("Total wall-clock milliseconds.", "int64"),
                ["InputTokens"] = Pint("Provider-reported prompt tokens (0 when unreported)."),
                ["OutputTokens"] = Pint("Provider-reported completion tokens (0 when unreported)."),
                ["TotalTokens"] = Pint("Provider-reported total tokens (0 when unreported).")
            }, new Dictionary<string, object?> { ["TtftMs"] = 240, ["StreamingMs"] = 572, ["TotalMs"] = 812, ["InputTokens"] = 1200, ["OutputTokens"] = 320, ["TotalTokens"] = 1520 });

            s["ChatReply"] = Obj(new Dictionary<string, M>
            {
                ["Role"] = Pstr("Always `assistant`."),
                ["Id"] = Pstr("The session id the turn was persisted under."),
                ["Content"] = Pstr("Assistant text."),
                ["Endpoint"] = Pstr("Endpoint the reply came from."),
                ["Model"] = Pstr("Model the reply came from."),
                ["Stats"] = Ref("ChatStats")
            }, new Dictionary<string, object?>
            {
                ["Role"] = "assistant", ["Id"] = "9f1c…", ["Content"] = "The README documents…",
                ["Endpoint"] = "openai-gpt4o", ["Model"] = "gpt-4o"
            });

            s["ChatApproveRequest"] = Obj(new Dictionary<string, M>
            {
                ["RunId"] = Pstr("The run id from the approval prompt."),
                ["ToolCallId"] = Pstr("The tool-call id from the approval prompt."),
                ["Decision"] = Pstr("`y` (approve once), `always` (approve and remember), or `n` (deny).")
            }, new Dictionary<string, object?> { ["RunId"] = "run_7", ["ToolCallId"] = "call_1", ["Decision"] = "y" },
               new List<string> { "RunId", "ToolCallId", "Decision" }, "A decision answering a tool-approval prompt.");

            s["ModelLoadReply"] = Obj(new Dictionary<string, M>
            {
                ["Ok"] = Pbool("Whether the model responded to the warm probe."),
                ["Reachable"] = Pbool("Whether the endpoint was reachable at all."),
                ["Error"] = PstrNullable("Error detail when the probe did not succeed, or null.")
            }, new Dictionary<string, object?> { ["Ok"] = true, ["Reachable"] = true });

            s["SessionDetailDto"] = Obj(new Dictionary<string, M>
            {
                ["Id"] = Pstr("Session id."),
                ["Title"] = Pstr("Session title."),
                ["EndpointName"] = Pstr("Endpoint name captured with the session."),
                ["Model"] = Pstr("Model captured with the session."),
                ["Messages"] = Parr(Ref("ChatMessageDto"), "The conversation messages, oldest first.")
            }, new Dictionary<string, object?>
            {
                ["Id"] = "9f1c…", ["Title"] = "Refactor the auth module", ["EndpointName"] = "openai-gpt4o", ["Model"] = "gpt-4o",
                ["Messages"] = new object[] { new Dictionary<string, object?> { ["Role"] = "user", ["Content"] = "Let's start." } }
            });

            s["SessionSaveRequest"] = Obj(new Dictionary<string, M>
            {
                ["Id"] = PstrNullable("Session id to update; empty to create a new one."),
                ["Title"] = PstrNullable("Optional title; blank derives one from the first user message."),
                ["EndpointName"] = Pstr("Endpoint name to record."),
                ["Model"] = Pstr("Model to record."),
                ["Messages"] = Parr(Ref("ChatMessageDto"), "The conversation messages to persist, oldest first.")
            }, new Dictionary<string, object?>
            {
                ["Id"] = "", ["Title"] = "", ["EndpointName"] = "openai-gpt4o", ["Model"] = "gpt-4o",
                ["Messages"] = new object[]
                {
                    new Dictionary<string, object?> { ["Role"] = "user", ["Content"] = "Hello" },
                    new Dictionary<string, object?> { ["Role"] = "assistant", ["Content"] = "Hi! How can I help?" }
                }
            }, new List<string> { "EndpointName", "Model", "Messages" }, "Create or update (upsert) a saved conversation.");

            s["SessionExportDto"] = Obj(new Dictionary<string, M>
            {
                ["Format"] = Pstr("Format extension (`md` or `html`)."),
                ["Filename"] = Pstr("Suggested download filename."),
                ["Content"] = Pstr("The rendered document content.")
            }, new Dictionary<string, object?> { ["Format"] = "md", ["Filename"] = "refactor-the-auth-module.md", ["Content"] = "# Refactor the auth module\n\n**user:** …" });

            s["CheckpointStatus"] = Obj(new Dictionary<string, M>
            {
                ["WorkingDirectory"] = Pstr("The working directory queried."),
                ["IsRepository"] = Pbool("Whether the directory is a git repository."),
                ["CanUndo"] = Pbool("Whether a turn's changes can be undone."),
                ["CanRedo"] = Pbool("Whether an undone turn can be redone.")
            }, new Dictionary<string, object?> { ["WorkingDirectory"] = "C:/Code/Mux", ["IsRepository"] = true, ["CanUndo"] = true, ["CanRedo"] = false });

            s["CheckpointActionRequest"] = Obj(new Dictionary<string, M>
            {
                ["WorkingDirectory"] = Pstr("The working directory whose checkpoints to act on.")
            }, new Dictionary<string, object?> { ["WorkingDirectory"] = "C:/Code/Mux" }, new List<string> { "WorkingDirectory" }, "A request to undo or redo the last turn's file changes.");

            s["CheckpointActionResult"] = Obj(new Dictionary<string, M>
            {
                ["Restored"] = Pbool("Whether a checkpoint was restored (false when nothing to undo/redo)."),
                ["Label"] = PstrNullable("The label of the restored checkpoint, or null."),
                ["CanUndo"] = Pbool("Whether a further undo is available."),
                ["CanRedo"] = Pbool("Whether a redo is available.")
            }, new Dictionary<string, object?> { ["Restored"] = true, ["Label"] = "turn 3", ["CanUndo"] = false, ["CanRedo"] = true });

            s["PromptProfileDto"] = Obj(new Dictionary<string, M>
            {
                ["Name"] = Pstr("Profile name."),
                ["IsActive"] = Pbool("Whether this profile is active."),
                ["SystemPrompt"] = Pstr("System prompt override (blank inherits the built-in default).")
            }, new Dictionary<string, object?> { ["Name"] = "concise", ["IsActive"] = true, ["SystemPrompt"] = "Be concise and cite files." });

            s["SubagentDto"] = Obj(new Dictionary<string, M>
            {
                ["Name"] = Pstr("Unique subagent name."),
                ["Description"] = Pstr("Short description surfaced to the model."),
                ["SystemPrompt"] = Pstr("System prompt for the isolated child run."),
                ["EndpointName"] = PstrNullable("Endpoint override, or null to inherit."),
                ["AllowedTools"] = Parr(Pstr("A tool-name glob."), "Tool-name globs the child is limited to."),
                ["MaxIterations"] = PintNullable("Iteration cap, or null to inherit.")
            }, new Dictionary<string, object?>
            {
                ["Name"] = "researcher", ["Description"] = "Reads code and answers questions.",
                ["SystemPrompt"] = "You are a read-only research agent.", ["EndpointName"] = null,
                ["AllowedTools"] = new object[] { "read_file", "grep" }, ["MaxIterations"] = 10
            });

            s["HookDto"] = Obj(new Dictionary<string, M>
            {
                ["Name"] = Pstr("Optional name."),
                ["Event"] = Pstr("Lifecycle event: `session-start`, `user-prompt-submit`, or `session-end`."),
                ["Command"] = Pstr("Executable to run."),
                ["Args"] = Parr(Pstr("An argument."), "Command arguments."),
                ["Blocking"] = Pbool("Whether a non-zero exit vetoes a vetoable event."),
                ["TimeoutMs"] = Pint("Per-run timeout in milliseconds.")
            }, new Dictionary<string, object?> { ["Name"] = "log-start", ["Event"] = "session-start", ["Command"] = "echo", ["Args"] = new object[] { "started" }, ["Blocking"] = false, ["TimeoutMs"] = 15000 });

            s["CustomCommandDto"] = Obj(new Dictionary<string, M>
            {
                ["Name"] = Pstr("Slash-command name (no leading slash)."),
                ["Description"] = Pstr("Short description."),
                ["Command"] = Pstr("Executable to run."),
                ["Args"] = Parr(Pstr("An argument."), "Command arguments."),
                ["TimeoutMs"] = Pint("Per-run timeout in milliseconds.")
            }, new Dictionary<string, object?> { ["Name"] = "build", ["Description"] = "Build the solution", ["Command"] = "dotnet", ["Args"] = new object[] { "build" }, ["TimeoutMs"] = 30000 });

            s["PluginConfigDto"] = Obj(new Dictionary<string, M>
            {
                ["Hooks"] = Parr(Ref("HookDto"), "Event hooks."),
                ["Commands"] = Parr(Ref("CustomCommandDto"), "Custom commands.")
            }, new Dictionary<string, object?> { ["Hooks"] = new object[0], ["Commands"] = new object[0] });

            s["KeybindingDto"] = Obj(new Dictionary<string, M>
            {
                ["CommandId"] = Pstr("Command id."),
                ["Chord"] = PstrNullable("Chord, or null/blank to unbind.")
            }, new Dictionary<string, object?> { ["CommandId"] = "chat.submit", ["Chord"] = "Ctrl+Enter" });

            s["McpServerDto"] = Obj(new Dictionary<string, M>
            {
                ["Name"] = Pstr("Unique server name."),
                ["Transport"] = Pstr("Transport: `stdio` or `http`."),
                ["Command"] = PstrNullable("Executable for stdio servers."),
                ["Args"] = Parr(Pstr("An argument."), "Args for stdio servers."),
                ["Env"] = Parr(Pstr("A KEY=VALUE entry."), "Environment entries for stdio servers."),
                ["Url"] = PstrNullable("Base URL for http servers."),
                ["McpPath"] = PstrNullable("Streamable HTTP MCP path."),
                ["AuthType"] = Pstr("Auth type: `none`, `bearer`, or `apikey`."),
                ["AuthHeader"] = PstrNullable("Header name for apikey auth."),
                ["AuthSecretSet"] = Pbool("Whether an auth secret is stored (read-only)."),
                ["AuthSecret"] = PstrNullable("New auth secret; blank/null preserves the stored secret. Never returned on read.")
            }, new Dictionary<string, object?>
            {
                ["Name"] = "filesystem", ["Transport"] = "stdio", ["Command"] = "npx",
                ["Args"] = new object[] { "-y", "@modelcontextprotocol/server-filesystem", "C:/Code" },
                ["Env"] = new object[0], ["AuthType"] = "none", ["AuthSecretSet"] = false
            }, description: "An MCP server projected for editing. The auth secret is masked.");

            s["RestSettingsDto"] = Obj(new Dictionary<string, M>
            {
                ["Enabled"] = Pbool("Whether the tray agent auto-starts the server."),
                ["Hostname"] = Pstr("Bind host."),
                ["Port"] = Pint("Bind port."),
                ["Ssl"] = Pbool("Bind with SSL."),
                ["CorsAllowOrigin"] = Pstr("CORS allow-origin."),
                ["ApiKeySet"] = Pbool("Whether an API key is set (the value is never returned)."),
                ["ApiKey"] = PstrNullable("Write-only: a new API key to set. Ignored on read; ignored on write when blank.")
            }, new Dictionary<string, object?> { ["Enabled"] = true, ["Hostname"] = "127.0.0.1", ["Port"] = 8710, ["Ssl"] = false, ["CorsAllowOrigin"] = "*", ["ApiKeySet"] = true, ["ApiKey"] = "" });

            s["SettingsDto"] = Obj(new Dictionary<string, M>
            {
                ["DefaultApprovalPolicy"] = Pstr("Default approval policy (`ask`/`auto`/`deny`)."),
                ["MaxAgentIterations"] = Pint("Max agent iterations (1-100)."),
                ["MaxConcurrency"] = Pint("Max concurrent jobs (1-32)."),
                ["ToolTimeoutMs"] = Pint("Per-tool timeout (ms)."),
                ["ProcessTimeoutMs"] = Pint("Per-process timeout (ms)."),
                ["AutoCompactEnabled"] = Pbool("Auto-compaction enabled."),
                ["CompactionStrategy"] = Pstr("Compaction strategy (`summary`/`trim`)."),
                ["CompactionPreserveTurns"] = Pint("Turns preserved during compaction (1-10)."),
                ["ContextWarningThresholdPercent"] = Pint("Context warning threshold percent (50-95)."),
                ["SkillsEnabled"] = Pbool("Skills enabled."),
                ["TaskPlanningEnabled"] = Pbool("Task planning enabled."),
                ["TaskParallelismEnabled"] = Pbool("Task parallelism enabled."),
                ["IgnoreCertErrors"] = Pbool("Ignore TLS certificate errors."),
                ["ShowBoundaryLines"] = Pbool("Show interactive boundary lines."),
                ["DefaultEnqueueBehavior"] = Pstr("Default enqueue behavior."),
                ["Rest"] = Ref("RestSettingsDto")
            }, new Dictionary<string, object?>
            {
                ["DefaultApprovalPolicy"] = "ask", ["MaxAgentIterations"] = 50, ["MaxConcurrency"] = 3,
                ["ToolTimeoutMs"] = 30000, ["ProcessTimeoutMs"] = 120000, ["AutoCompactEnabled"] = true,
                ["CompactionStrategy"] = "summary", ["CompactionPreserveTurns"] = 3, ["ContextWarningThresholdPercent"] = 80,
                ["SkillsEnabled"] = true, ["TaskPlanningEnabled"] = true, ["TaskParallelismEnabled"] = false,
                ["IgnoreCertErrors"] = false, ["ShowBoundaryLines"] = false, ["DefaultEnqueueBehavior"] = "ask"
            }, description: "The editable server settings (secrets masked).");

            s["SkillDto"] = Obj(new Dictionary<string, M>
            {
                ["Name"] = Pstr("Skill id/name."),
                ["Title"] = Pstr("Human title."),
                ["Description"] = Pstr("Description."),
                ["Enabled"] = Pbool("Whether the skill is enabled."),
                ["Valid"] = Pbool("Whether the skill validates."),
                ["Mutating"] = Pbool("Whether the skill is mutating."),
                ["Commands"] = Pint("Number of commands."),
                ["Errors"] = Parr(Pstr("A validation error."), "Validation errors, if any."),
                ["Body"] = PstrNullable("The SKILL.md body (only on the detail route).")
            }, new Dictionary<string, object?>
            {
                ["Name"] = "commit", ["Title"] = "Git commit", ["Description"] = "Craft and make a git commit.",
                ["Enabled"] = true, ["Valid"] = true, ["Mutating"] = true, ["Commands"] = 2, ["Errors"] = new object[0]
            });

            s["SkillToggleRequest"] = Obj(new Dictionary<string, M>
            {
                ["Id"] = Pstr("The skill id/name."),
                ["Enabled"] = Pbool("Desired enabled state.")
            }, new Dictionary<string, object?> { ["Id"] = "commit", ["Enabled"] = true }, new List<string> { "Id", "Enabled" }, "Enable or disable a skill.");

            s["SkillEditRequest"] = Obj(new Dictionary<string, M>
            {
                ["Name"] = Pstr("Skill name (for create, or to target an edit)."),
                ["Id"] = Pstr("Skill id (to target an edit)."),
                ["Body"] = Pstr("The SKILL.md body.")
            }, new Dictionary<string, object?> { ["Name"] = "my-skill", ["Id"] = "", ["Body"] = "---\nname: my-skill\n---\n\nInstructions…" },
               new List<string> { "Body" }, "Create a skill or replace an existing skill's SKILL.md body.");

            s["OverviewNotice"] = Obj(new Dictionary<string, M>
            {
                ["Level"] = Pstr("Severity: `info`, `warning`, or `success`."),
                ["Text"] = Pstr("The message text.")
            }, new Dictionary<string, object?> { ["Level"] = "warning", ["Text"] = "No default endpoint is set." });

            s["OverviewDto"] = Obj(new Dictionary<string, M>
            {
                ["Version"] = Pstr("Product version."),
                ["ConfigDir"] = Pstr("Active configuration directory."),
                ["Uptime"] = Pstr("Server uptime, formatted."),
                ["AuthEnabled"] = Pbool("Whether the REST API requires an API key."),
                ["Endpoints"] = Pint("Configured endpoint count."),
                ["DefaultEndpoint"] = PstrNullable("The default endpoint's name, or null."),
                ["DefaultAdapter"] = PstrNullable("The default endpoint's adapter type, or null."),
                ["DefaultModel"] = PstrNullable("The default endpoint's model, or null."),
                ["McpServers"] = Pint("Configured MCP server count."),
                ["Prompts"] = Pint("Prompt profile count."),
                ["ActivePrompt"] = PstrNullable("The active prompt profile name, or null."),
                ["Subagents"] = Pint("Subagent count."),
                ["SkillsTotal"] = Pint("Total skills discovered."),
                ["SkillsEnabled"] = Pint("Enabled skills."),
                ["SkillsInvalid"] = Pint("Skills that fail validation."),
                ["Hooks"] = Pint("Event hook count."),
                ["Commands"] = Pint("Custom command count."),
                ["Keybindings"] = Pint("Keybinding override count."),
                ["Sessions"] = Pint("Saved session count."),
                ["TotalMessages"] = Pint("Total messages across all saved sessions."),
                ["RecentSessions"] = Parr(Ref("SessionSummary"), "The most recently updated sessions."),
                ["Notices"] = Parr(Ref("OverviewNotice"), "Derived next-step and health notices.")
            }, new Dictionary<string, object?>
            {
                ["Version"] = "0.11.0", ["ConfigDir"] = "C:/Users/you/.mux", ["Uptime"] = "0.01:02:03",
                ["AuthEnabled"] = true, ["Endpoints"] = 3, ["DefaultEndpoint"] = "local-ollama", ["DefaultAdapter"] = "ollama",
                ["DefaultModel"] = "qwen2.5-coder:7b", ["McpServers"] = 1, ["Prompts"] = 2, ["ActivePrompt"] = "concise",
                ["Subagents"] = 1, ["SkillsTotal"] = 12, ["SkillsEnabled"] = 9, ["SkillsInvalid"] = 0,
                ["Hooks"] = 0, ["Commands"] = 1, ["Keybindings"] = 0, ["Sessions"] = 24, ["TotalMessages"] = 310,
                ["RecentSessions"] = new object[0], ["Notices"] = new object[0]
            }, description: "The home overview aggregate.");

            s["UsageFiltersDto"] = Obj(new Dictionary<string, M>
            {
                ["Enabled"] = Pbool("Whether usage telemetry is enabled and queryable."),
                ["Endpoints"] = Parr(Pstr("An endpoint name."), "The distinct endpoint names seen in the store."),
                ["Models"] = Parr(Pstr("A model."), "The distinct models seen in the store.")
            }, new Dictionary<string, object?> { ["Enabled"] = true, ["Endpoints"] = new object[] { "openai-gpt4o", "local-ollama" }, ["Models"] = new object[] { "gpt-4o", "qwen2.5-coder:7b" } });

            s["UsageDeleteResult"] = Obj(new Dictionary<string, M>
            {
                ["Deleted"] = Pint("The number of rows deleted (0 or 1 for a single-id delete).")
            }, new Dictionary<string, object?> { ["Deleted"] = 1 });

            s["UsageDistribution"] = Obj(new Dictionary<string, M>
            {
                ["Min"] = Pnum("Minimum."),
                ["Avg"] = Pnum("Average."),
                ["P95"] = Pnum("95th percentile."),
                ["P99"] = Pnum("99th percentile."),
                ["Max"] = Pnum("Maximum."),
                ["Count"] = Pint("Sample count.")
            }, new Dictionary<string, object?> { ["Min"] = 120, ["Avg"] = 240, ["P95"] = 610, ["P99"] = 820, ["Max"] = 900, ["Count"] = 42 }, description: "A min/avg/p95/p99/max distribution.");

            s["UsageMetrics"] = Obj(new Dictionary<string, M>
            {
                ["Calls"] = Pint("Number of calls."),
                ["Errors"] = Pint("Number of failed calls."),
                ["ErrorRate"] = Pnum("Fraction of calls that failed (0–1)."),
                ["InputTokens"] = Pint("Prompt tokens.", "int64"),
                ["CachedTokens"] = Pint("Cached prompt tokens.", "int64"),
                ["OutputTokens"] = Pint("Completion tokens.", "int64"),
                ["TotalTokens"] = Pint("Total tokens.", "int64"),
                ["CostUsd"] = Pnum("Estimated cost in USD."),
                ["AvgTtftMs"] = Pnum("Average time-to-first-token (ms)."),
                ["AvgTotalMs"] = Pnum("Average total latency (ms)."),
                ["AvgStreamMs"] = Pnum("Average streaming duration (ms)."),
                ["AvgTokensPerSec"] = Pnum("Average output throughput (tokens/second)."),
                ["TotalMsDist"] = Ref("UsageDistribution"),
                ["TtftMsDist"] = Ref("UsageDistribution"),
                ["StreamMsDist"] = Ref("UsageDistribution"),
                ["ThroughputDist"] = Ref("UsageDistribution")
            }, new Dictionary<string, object?>
            {
                ["Calls"] = 42, ["Errors"] = 0, ["ErrorRate"] = 0.0, ["InputTokens"] = 50400, ["CachedTokens"] = 0,
                ["OutputTokens"] = 13400, ["TotalTokens"] = 63800, ["CostUsd"] = 0.1912, ["AvgTtftMs"] = 240,
                ["AvgTotalMs"] = 812, ["AvgStreamMs"] = 572, ["AvgTokensPerSec"] = 23.4
            }, description: "Aggregate usage metrics.");

            s["UsageSummary"] = Obj(new Dictionary<string, M>
            {
                ["FromUnixMs"] = Pint("Window start (Unix ms).", "int64"),
                ["ToUnixMs"] = Pint("Window end (Unix ms).", "int64"),
                ["Metrics"] = Ref("UsageMetrics")
            }, new Dictionary<string, object?> { ["FromUnixMs"] = 1757836800000, ["ToUnixMs"] = 1757923200000 });

            s["UsageBucket"] = Obj(new Dictionary<string, M>
            {
                ["BucketStartUnixMs"] = Pint("Bucket start (Unix ms).", "int64"),
                ["Metrics"] = Ref("UsageMetrics")
            }, new Dictionary<string, object?> { ["BucketStartUnixMs"] = 1757836800000 }, description: "One time bucket in a usage series.");

            s["UsageBreakdownRow"] = Obj(new Dictionary<string, M>
            {
                ["Key"] = Pstr("The dimension value (e.g. a model or endpoint name)."),
                ["Metrics"] = Ref("UsageMetrics")
            }, new Dictionary<string, object?> { ["Key"] = "gpt-4o" }, description: "One row of a usage breakdown by dimension.");

            s["UsageEvent"] = Obj(new Dictionary<string, M>
            {
                ["Id"] = Pint("Event id.", "int64"),
                ["TimestampUnixMs"] = Pint("When the call occurred (Unix ms).", "int64"),
                ["Endpoint"] = Pstr("Endpoint name."),
                ["Model"] = Pstr("Model."),
                ["CallKind"] = Pstr("Call kind."),
                ["Success"] = Pbool("Whether the call succeeded."),
                ["InputTokens"] = Pint("Prompt tokens."),
                ["OutputTokens"] = Pint("Completion tokens."),
                ["TotalMs"] = Pint("Total latency (ms).", "int64"),
                ["CostUsd"] = Pnum("Estimated cost in USD.")
            }, new Dictionary<string, object?>
            {
                ["Id"] = 1201, ["Endpoint"] = "openai-gpt4o", ["Model"] = "gpt-4o", ["CallKind"] = "chat",
                ["Success"] = true, ["InputTokens"] = 1200, ["OutputTokens"] = 320, ["TotalMs"] = 812, ["CostUsd"] = 0.006
            }, description: "One raw usage event.");

            s["UsageEventPage"] = Obj(new Dictionary<string, M>
            {
                ["Items"] = Parr(Ref("UsageEvent"), "The events on this page."),
                ["Page"] = Pint("1-based page number."),
                ["PageSize"] = Pint("Rows per page."),
                ["Total"] = Pint("Total matching rows.", "int64")
            }, new Dictionary<string, object?> { ["Items"] = new object[0], ["Page"] = 1, ["PageSize"] = 25, ["Total"] = 0 }, description: "A page of usage events.");

            s["PricingEntry"] = Obj(new Dictionary<string, M>
            {
                ["InputPerMillion"] = Pnum("USD per million input tokens."),
                ["CachedPerMillion"] = Pnum("USD per million cached input tokens."),
                ["OutputPerMillion"] = Pnum("USD per million output tokens.")
            }, new Dictionary<string, object?> { ["InputPerMillion"] = 2.5, ["CachedPerMillion"] = 1.25, ["OutputPerMillion"] = 10.0 }, description: "Per-million-token pricing for one model.");

            s["PricingTable"] = new M
            {
                Type = "object",
                Description = "A map of model identifier to its per-million-token pricing.",
                Properties = new Dictionary<string, M>
                {
                    ["Models"] = new M
                    {
                        Type = "object",
                        Description = "Model identifier → pricing entry.",
                        Properties = new Dictionary<string, M> { ["gpt-4o"] = Ref("PricingEntry") }
                    }
                },
                Example = new Dictionary<string, object?>
                {
                    ["Models"] = new Dictionary<string, object?>
                    {
                        ["gpt-4o"] = new Dictionary<string, object?> { ["InputPerMillion"] = 2.5, ["CachedPerMillion"] = 1.25, ["OutputPerMillion"] = 10.0 }
                    }
                }
            };

            s["RunTaskDto"] = Obj(new Dictionary<string, M>
            {
                ["Id"] = Pstr("The task id."),
                ["Title"] = Pstr("The task title."),
                ["Status"] = Pstr("Task status: `pending`, `in_progress`, `completed`, `failed`, `skipped`, or `blocked`.")
            }, new Dictionary<string, object?> { ["Id"] = "t1", ["Title"] = "Add the interface", ["Status"] = "in_progress" });

            s["RunSummaryDto"] = Obj(new Dictionary<string, M>
            {
                ["RunId"] = Pstr("The run correlation id."),
                ["SessionId"] = Pstr("The session id the run belongs to (may be empty)."),
                ["EndpointName"] = Pstr("The endpoint the run targets."),
                ["Model"] = Pstr("The model the run targets."),
                ["Status"] = Pstr("Lifecycle status: `running`, `awaiting_approval`, `completed`, `failed`, or `canceled`."),
                ["IsTerminal"] = Pbool("Whether the run has reached a terminal status."),
                ["StartedUtc"] = Pstr("When the run started (UTC).", "date-time"),
                ["CompletedUtc"] = new M { Type = "string", Format = "date-time", Nullable = true, Description = "When the run finished (UTC), or null while running." }
            }, new Dictionary<string, object?>
            {
                ["RunId"] = "8b2f…", ["SessionId"] = "9f1c…", ["EndpointName"] = "openai-gpt4o", ["Model"] = "gpt-4o",
                ["Status"] = "running", ["IsTerminal"] = false, ["StartedUtc"] = "2026-09-15T12:00:00Z"
            });

            s["RunStateReply"] = Obj(new Dictionary<string, M>
            {
                ["RunId"] = Pstr("The run correlation id."),
                ["SessionId"] = Pstr("The session id the run belongs to (may be empty)."),
                ["EndpointName"] = Pstr("The endpoint the run targets."),
                ["Model"] = Pstr("The model the run targets."),
                ["Status"] = Pstr("Lifecycle status: `running`, `awaiting_approval`, `completed`, `failed`, or `canceled`."),
                ["IsTerminal"] = Pbool("Whether the run has reached a terminal status."),
                ["StartedUtc"] = Pstr("When the run started (UTC).", "date-time"),
                ["CompletedUtc"] = new M { Type = "string", Format = "date-time", Nullable = true, Description = "When the run finished (UTC), or null while running." },
                ["IterationsCompleted"] = Pint("Iterations completed (populated at run completion)."),
                ["ToolCallCount"] = Pint("Tool calls handled (populated at run completion)."),
                ["ErrorCount"] = Pint("Error events observed during the run."),
                ["InputTokens"] = Pint("Provider-reported input tokens."),
                ["OutputTokens"] = Pint("Provider-reported output tokens."),
                ["TotalTokens"] = Pint("Provider-reported total tokens."),
                ["FinalEstimatedTokens"] = Pint("Estimated final context tokens."),
                ["CurrentToolName"] = PstrNullable("The tool currently executing, or null."),
                ["LastError"] = PstrNullable("The most recent error message, or null."),
                ["TotalTaskCount"] = Pint("Total tasks in the current task-plan snapshot."),
                ["CompletedTaskCount"] = Pint("Completed tasks in the current task-plan snapshot."),
                ["Tasks"] = Parr(Ref("RunTaskDto"), "The current task-plan checklist (empty when the run has no plan).")
            }, new Dictionary<string, object?>
            {
                ["RunId"] = "8b2f…", ["SessionId"] = "9f1c…", ["EndpointName"] = "openai-gpt4o", ["Model"] = "gpt-4o",
                ["Status"] = "running", ["IsTerminal"] = false, ["StartedUtc"] = "2026-09-15T12:00:00Z",
                ["IterationsCompleted"] = 0, ["ToolCallCount"] = 0, ["ErrorCount"] = 0, ["TotalTaskCount"] = 3, ["CompletedTaskCount"] = 1
            });

            s["RunCancelReply"] = Obj(new Dictionary<string, M>
            {
                ["Ok"] = Pbool("Always true on success."),
                ["RunId"] = Pstr("The run id that was canceled."),
                ["Status"] = Pstr("The resulting status (`canceled`).")
            }, new Dictionary<string, object?> { ["Ok"] = true, ["RunId"] = "8b2f…", ["Status"] = "canceled" });
        }

        #endregion
    }
}
