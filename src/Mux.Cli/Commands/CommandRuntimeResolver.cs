namespace Mux.Cli.Commands
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using Mux.Core.Agent;
    using Mux.Core.Enums;
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Mux.Core.Tools;

    /// <summary>
    /// Shared helpers for resolving effective CLI runtime settings.
    /// </summary>
    public static class CommandRuntimeResolver
    {
        private const string InteractiveModeOnlyMcpMessage =
            "`--no-mcp` applies to interactive mode. `mux print` loads MCP only when `--mcp-config` is supplied, and `mux probe` never loads MCP, so `--no-mcp` has no effect here — remove it.";

        /// <summary>
        /// Parses a user-provided output format string.
        /// </summary>
        public static OutputFormatEnum ParseOutputFormat(string? value, params OutputFormatEnum[] supportedFormats)
        {
            OutputFormatEnum parsed = string.IsNullOrWhiteSpace(value)
                ? OutputFormatEnum.Text
                : value.Trim().ToLowerInvariant() switch
                {
                    "text" => OutputFormatEnum.Text,
                    "json" => OutputFormatEnum.Json,
                    "jsonl" => OutputFormatEnum.Jsonl,
                    _ => throw new InvalidOperationException($"Unsupported output format '{value}'. Supported values: {string.Join(", ", supportedFormats.Select(f => f.ToString().ToLowerInvariant()))}.")
                };

            if (!supportedFormats.Contains(parsed))
            {
                throw new InvalidOperationException($"Output format '{parsed.ToString().ToLowerInvariant()}' is not supported for this command. Supported values: {string.Join(", ", supportedFormats.Select(f => f.ToString().ToLowerInvariant()))}.");
            }

            return parsed;
        }

        /// <summary>
        /// Resolves the effective endpoint and mux settings used by command execution.
        /// </summary>
        public static ResolvedRuntime ResolveRuntime(CommonSettings settings)
        {
            return ResolveRuntime(settings, "print", supportsMcp: false, allowAskApproval: false);
        }

        /// <summary>
        /// Resolves the effective endpoint and mux settings used by command execution.
        /// </summary>
        public static ResolvedRuntime ResolveRuntime(
            CommonSettings settings,
            string commandName,
            bool supportsMcp,
            bool allowAskApproval)
        {
            SettingsLoader.EnsureConfigDirectory();
            string configDirectory = SettingsLoader.GetConfigDirectory();
            List<EndpointConfig> endpoints = SettingsLoader.LoadEndpoints();
            MuxSettings muxSettings = SettingsLoader.LoadSettings();
            List<McpServerConfig> mcpServers = SettingsLoader.LoadMcpServers();

            ValidateCommandSettings(settings, commandName, supportsMcp, allowAskApproval);
            ApplyMuxSettingsOverrides(settings, muxSettings);

            EndpointConfig endpoint = SettingsLoader.ResolveEndpoint(
                endpoints,
                settings.Endpoint,
                settings.Model,
                settings.BaseUrl,
                settings.AdapterType,
                settings.Temperature,
                settings.MaxTokens);

            ApplyReasoningOverride(endpoint, settings);

            string workingDirectory = settings.WorkingDirectory ?? Directory.GetCurrentDirectory();

            if (!ToolGovernance.TryParsePosture(settings.Sandbox, out SandboxPostureEnum sandboxPosture))
            {
                throw new InvalidOperationException(
                    $"Unsupported sandbox posture '{settings.Sandbox}'. Supported values: none, read-only, workspace-write.");
            }

            List<string> allowedTools = SplitPatterns(settings.AllowTools);
            List<string> deniedTools = SplitPatterns(settings.DenyTools);
            List<string> additionalDirectories = (settings.AddDir ?? new List<string>())
                .Where((string d) => !string.IsNullOrWhiteSpace(d))
                .Select((string d) => Path.GetFullPath(d.Trim()))
                .ToList();

            List<string> cliOverrides = GetCliOverrides(settings);
            string endpointSelectionSource = GetEndpointSelectionSource(endpoints, settings.Endpoint);

            BuiltInToolRegistry toolRegistry = new BuiltInToolRegistry(muxSettings);
            List<ToolDefinition> builtInTools = toolRegistry.GetToolDefinitions();
            bool toolsEnabled = endpoint.Quirks?.SupportsTools ?? true;
            int builtInToolCount = builtInTools.Count;
            int effectiveToolCount = toolsEnabled ? builtInToolCount : 0;

            StringBuilder toolDescBuilder = new StringBuilder();
            if (toolsEnabled)
            {
                foreach (ToolDefinition tool in builtInTools)
                {
                    toolDescBuilder.AppendLine($"- {tool.Name}: {tool.Description}");
                }
            }

            PromptProfile activePromptProfile = SettingsLoader.GetActivePromptProfile();

            string systemPrompt = SettingsLoader.LoadSystemPrompt(settings.SystemPrompt, muxSettings);
            if (!toolsEnabled)
            {
                // No tools: use the active profile's tools-disabled prompt, falling back to the built-in.
                systemPrompt = string.IsNullOrWhiteSpace(activePromptProfile.ToolsDisabledPrompt)
                    ? Defaults.ToolsDisabledSystemPrompt
                    : activePromptProfile.ToolsDisabledPrompt;
            }

            string taskPlanningGuidance = (toolsEnabled && muxSettings.TaskPlanningEnabled)
                ? Defaults.TaskPlanningGuidance
                : string.Empty;

            systemPrompt = systemPrompt
                .Replace("{WorkingDirectory}", workingDirectory)
                .Replace("{ToolDescriptions}", toolDescBuilder.ToString().TrimEnd())
                .Replace("{TaskPlanningGuidance}", taskPlanningGuidance);

            // Append caller-supplied system-prompt text after all placeholder substitution so it survives
            // profile switches and is never consumed by a placeholder.
            if (!string.IsNullOrWhiteSpace(settings.AppendSystemPrompt))
            {
                systemPrompt = string.IsNullOrEmpty(systemPrompt)
                    ? settings.AppendSystemPrompt!.Trim()
                    : systemPrompt + Environment.NewLine + Environment.NewLine + settings.AppendSystemPrompt!.Trim();
            }

            string compactionSystemPrompt = activePromptProfile.CompactionPrompt ?? string.Empty;

            ApprovalPolicyEnum approvalPolicy = ResolveApprovalPolicy(settings, endpoint, allowAskApproval);

            if (!allowAskApproval && approvalPolicy == ApprovalPolicyEnum.Ask)
            {
                throw new InvalidOperationException(
                    $"Approval policy 'ask' is not supported in non-interactive `{commandName}` mode. Use `--approval-policy auto`, `--yolo`, or `--approval-policy deny`.");
            }

            return new ResolvedRuntime
            {
                Endpoint = endpoint,
                MuxSettings = muxSettings,
                MaxAgentIterations = settings.MaxTurns.HasValue
                    ? Math.Clamp(settings.MaxTurns.Value, 1, 100)
                    : muxSettings.GetEffectiveMaxAgentIterations(endpoint),
                WorkingDirectory = workingDirectory,
                SystemPrompt = systemPrompt,
                CompactionSystemPrompt = compactionSystemPrompt,
                ApprovalPolicy = approvalPolicy,
                SandboxPosture = sandboxPosture,
                AllowedTools = allowedTools,
                DeniedTools = deniedTools,
                AdditionalDirectories = additionalDirectories,
                Metadata = new RuntimeMetadata
                {
                    CommandName = commandName,
                    ConfigDirectory = configDirectory,
                    EndpointSelectionSource = endpointSelectionSource,
                    CliOverridesApplied = cliOverrides,
                    IgnoreCertErrors = muxSettings.IgnoreCertErrors,
                    EndpointsFilePresent = File.Exists(Path.Combine(configDirectory, "endpoints.json")),
                    SettingsFilePresent = File.Exists(Path.Combine(configDirectory, "settings.json")),
                    McpServersFilePresent = File.Exists(Path.Combine(configDirectory, "mcp-servers.json"))
                },
                Capabilities = new RuntimeCapabilities
                {
                    ToolsEnabled = toolsEnabled,
                    BuiltInToolCount = builtInToolCount,
                    EffectiveToolCount = effectiveToolCount,
                    McpSupported = supportsMcp,
                    McpConfigured = mcpServers.Count > 0,
                    McpServerCount = mcpServers.Count
                }
            };
        }

        /// <summary>
        /// Computes the fully-substituted system prompt and the compaction prompt for a prompt profile,
        /// honoring the tools-disabled variant and the built-in fallbacks for empty fields. Used to apply a
        /// profile to a running session without re-resolving the whole runtime.
        /// </summary>
        /// <param name="profile">The prompt profile to apply.</param>
        /// <param name="toolsEnabled">Whether the active endpoint supports tools.</param>
        /// <param name="workingDirectory">The working directory substituted for <c>{WorkingDirectory}</c>.</param>
        /// <param name="tools">The tools whose names/descriptions fill <c>{ToolDescriptions}</c>.</param>
        /// <returns>The substituted system prompt and the compaction system prompt.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="profile"/> is null.</exception>
        public static (string SystemPrompt, string CompactionSystemPrompt) ResolveProfilePrompts(
            PromptProfile profile,
            bool toolsEnabled,
            string workingDirectory,
            IReadOnlyList<ToolDefinition> tools)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));

            string raw = toolsEnabled
                ? (string.IsNullOrWhiteSpace(profile.SystemPrompt) ? Defaults.SystemPrompt : profile.SystemPrompt)
                : (string.IsNullOrWhiteSpace(profile.ToolsDisabledPrompt) ? Defaults.ToolsDisabledSystemPrompt : profile.ToolsDisabledPrompt);

            StringBuilder toolDescBuilder = new StringBuilder();
            if (toolsEnabled && tools != null)
            {
                foreach (ToolDefinition tool in tools)
                {
                    toolDescBuilder.AppendLine($"- {tool.Name}: {tool.Description}");
                }
            }

            bool taskPlanningActive = false;
            if (toolsEnabled && tools != null)
            {
                foreach (ToolDefinition tool in tools)
                {
                    if (string.Equals(tool.Name, "plan_tasks", StringComparison.Ordinal))
                    {
                        taskPlanningActive = true;
                        break;
                    }
                }
            }

            string systemPrompt = raw
                .Replace("{WorkingDirectory}", workingDirectory ?? string.Empty)
                .Replace("{ToolDescriptions}", toolDescBuilder.ToString().TrimEnd())
                .Replace("{TaskPlanningGuidance}", taskPlanningActive ? Defaults.TaskPlanningGuidance : string.Empty);

            string compaction = string.IsNullOrWhiteSpace(profile.CompactionPrompt)
                ? Defaults.CompactionSystemPrompt
                : profile.CompactionPrompt;

            return (systemPrompt, compaction);
        }

        internal static ApprovalPolicyEnum ResolveApprovalPolicy(CommonSettings settings, EndpointConfig endpoint, bool allowAskApproval = false)
        {
            if (settings.Yolo)
            {
                return ApprovalPolicyEnum.AutoApprove;
            }

            if (!string.IsNullOrWhiteSpace(settings.ApprovalPolicy))
            {
                string normalizedPolicy = settings.ApprovalPolicy.Trim().ToLowerInvariant();
                return normalizedPolicy switch
                {
                    "ask" => ApprovalPolicyEnum.Ask,
                    "deny" => ApprovalPolicyEnum.Deny,
                    "auto" => ApprovalPolicyEnum.AutoApprove,
                    "autoapprove" => ApprovalPolicyEnum.AutoApprove,
                    _ => throw new InvalidOperationException($"Unsupported approval policy '{settings.ApprovalPolicy}'. Supported values: ask, auto, deny.")
                };
            }

            if (endpoint.AutoApproveTools)
            {
                return ApprovalPolicyEnum.AutoApprove;
            }

            // Interactive mode defaults to Ask (the caller remaps it to AutoSafe: read-only tools run
            // automatically, mutating tools escalate to the approval modal). Non-interactive modes cannot
            // prompt, so they default to the safe Deny.
            return allowAskApproval ? ApprovalPolicyEnum.Ask : ApprovalPolicyEnum.Deny;
        }

        private static void ValidateCommandSettings(
            CommonSettings settings,
            string commandName,
            bool supportsMcp,
            bool allowAskApproval)
        {
            if (!supportsMcp && settings.NoMcp)
            {
                throw new InvalidOperationException(InteractiveModeOnlyMcpMessage);
            }

            if (!allowAskApproval
                && !string.IsNullOrWhiteSpace(settings.ApprovalPolicy)
                && string.Equals(settings.ApprovalPolicy.Trim(), "ask", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Approval policy 'ask' is not supported in non-interactive `{commandName}` mode. Use `--approval-policy auto`, `--yolo`, or `--approval-policy deny`.");
            }
        }

        private static string GetEndpointSelectionSource(List<EndpointConfig> endpoints, string? requestedEndpointName)
        {
            if (!string.IsNullOrWhiteSpace(requestedEndpointName))
            {
                return "named_endpoint";
            }

            if (endpoints.Any((EndpointConfig endpoint) => endpoint.IsDefault))
            {
                return "default_endpoint";
            }

            if (endpoints.Count > 0)
            {
                return "first_configured_endpoint";
            }

            return "internal_default";
        }

        private static List<string> GetCliOverrides(CommonSettings settings)
        {
            List<string> overrides = new List<string>();

            if (!string.IsNullOrWhiteSpace(settings.ConfigDir)) overrides.Add("configDir");
            if (!string.IsNullOrWhiteSpace(settings.Endpoint)) overrides.Add("endpoint");
            if (!string.IsNullOrWhiteSpace(settings.Model)) overrides.Add("model");
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl)) overrides.Add("baseUrl");
            if (!string.IsNullOrWhiteSpace(settings.AdapterType)) overrides.Add("adapterType");
            if (settings.Temperature.HasValue) overrides.Add("temperature");
            if (settings.MaxTokens.HasValue) overrides.Add("maxTokens");
            if (HasReasoningOverride(settings)) overrides.Add("reasoningEffort");
            if (!string.IsNullOrWhiteSpace(settings.WorkingDirectory)) overrides.Add("workingDirectory");
            if (!string.IsNullOrWhiteSpace(settings.SystemPrompt)) overrides.Add("systemPrompt");
            if (!string.IsNullOrWhiteSpace(settings.AppendSystemPrompt)) overrides.Add("appendSystemPrompt");
            if (settings.MaxTurns.HasValue) overrides.Add("maxTurns");
            if (settings.MaxTokenBudget.HasValue) overrides.Add("maxTokenBudget");
            if (!string.IsNullOrWhiteSpace(settings.AllowTools)) overrides.Add("allowTools");
            if (!string.IsNullOrWhiteSpace(settings.DenyTools)) overrides.Add("denyTools");
            if (settings.AddDir != null && settings.AddDir.Count > 0) overrides.Add("addDir");
            if (!string.IsNullOrWhiteSpace(settings.Sandbox)) overrides.Add("sandbox");
            if (settings.Yolo) overrides.Add("yolo");
            if (!string.IsNullOrWhiteSpace(settings.ApprovalPolicy)) overrides.Add("approvalPolicy");
            if (!string.IsNullOrWhiteSpace(settings.CompactionStrategy)) overrides.Add("compactionStrategy");
            if (settings.IgnoreCertErrors) overrides.Add("ignoreCertErrors");

            return overrides;
        }

        internal static bool HasReasoningOverride(CommonSettings settings)
        {
            return !string.IsNullOrWhiteSpace(settings.Effort)
                || !string.IsNullOrWhiteSpace(settings.EffortOpenAiValue)
                || settings.EffortGeminiBudget.HasValue
                || !string.IsNullOrWhiteSpace(settings.EffortOllamaThink);
        }

        /// <summary>
        /// Applies any CLI reasoning-effort overrides onto the resolved endpoint. A level flag replaces the
        /// endpoint's level; "off"/"none" clears it entirely; the per-provider flags override individual
        /// values but only when a level is active (from config or the flag), so they stay inert otherwise.
        /// </summary>
        /// <param name="endpoint">The resolved endpoint to mutate.</param>
        /// <param name="settings">The CLI settings carrying any effort overrides.</param>
        internal static void ApplyReasoningOverride(EndpointConfig endpoint, CommonSettings settings)
        {
            string? effortFlag = settings.Effort?.Trim().ToLowerInvariant();
            if (!HasReasoningOverride(settings))
            {
                return;
            }

            if (effortFlag == "off" || effortFlag == "none")
            {
                endpoint.ReasoningEffort = null;
                return;
            }

            ReasoningEffortConfig config = endpoint.ReasoningEffort?.Clone() ?? new ReasoningEffortConfig();

            if (!string.IsNullOrEmpty(effortFlag)
                && ReasoningLevelEnumConverter.TryParse(effortFlag, out ReasoningLevelEnum level))
            {
                config.Level = level;
            }

            if (!config.IsActive())
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(settings.EffortOpenAiValue)) config.OpenAiValue = settings.EffortOpenAiValue;
            if (settings.EffortGeminiBudget.HasValue) config.GeminiThinkingBudget = settings.EffortGeminiBudget;
            if (!string.IsNullOrWhiteSpace(settings.EffortOllamaThink)) config.OllamaThink = settings.EffortOllamaThink;
            endpoint.ReasoningEffort = config;
        }

        private static List<string> SplitPatterns(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return new List<string>();
            }

            return value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }

        /// <summary>
        /// Applies CLI overrides that target mux settings rather than endpoint selection.
        /// </summary>
        /// <param name="settings">The parsed command settings.</param>
        /// <param name="muxSettings">The loaded mux settings instance to mutate.</param>
        public static void ApplyMuxSettingsOverrides(CommonSettings settings, MuxSettings muxSettings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (muxSettings == null) throw new ArgumentNullException(nameof(muxSettings));

            if (!string.IsNullOrWhiteSpace(settings.CompactionStrategy))
            {
                if (!MuxSettings.TryNormalizeCompactionStrategy(settings.CompactionStrategy, out string normalizedStrategy))
                {
                    throw new InvalidOperationException(
                        $"Unsupported compaction strategy '{settings.CompactionStrategy}'. Supported values: summary, trim.");
                }

                muxSettings.CompactionStrategy = normalizedStrategy;
            }

            if (settings.IgnoreCertErrors)
            {
                muxSettings.IgnoreCertErrors = true;
            }

            if (settings.MaxTokenBudget.HasValue)
            {
                muxSettings.MaxTokenBudget = settings.MaxTokenBudget.Value;
            }
        }
    }

    /// <summary>
    /// Effective runtime values resolved from config and CLI arguments.
    /// </summary>
    public class ResolvedRuntime
    {
        /// <summary>
        /// Effective endpoint configuration.
        /// </summary>
        public EndpointConfig Endpoint { get; set; } = new EndpointConfig();

        /// <summary>
        /// Loaded mux settings.
        /// </summary>
        public MuxSettings MuxSettings { get; set; } = new MuxSettings();

        /// <summary>
        /// Effective maximum agent iterations after endpoint overrides and global settings are resolved.
        /// </summary>
        public int MaxAgentIterations { get; set; } = 50;

        /// <summary>
        /// Effective working directory.
        /// </summary>
        public string WorkingDirectory { get; set; } = string.Empty;

        /// <summary>
        /// Effective system prompt.
        /// </summary>
        public string SystemPrompt { get; set; } = string.Empty;

        /// <summary>
        /// Effective compaction-sidecar system prompt (empty inherits the built-in default).
        /// </summary>
        public string CompactionSystemPrompt { get; set; } = string.Empty;

        /// <summary>
        /// Effective approval policy.
        /// </summary>
        public ApprovalPolicyEnum ApprovalPolicy { get; set; } = ApprovalPolicyEnum.Deny;

        /// <summary>
        /// Effective application-level confinement posture.
        /// </summary>
        public SandboxPostureEnum SandboxPosture { get; set; } = SandboxPostureEnum.None;

        /// <summary>
        /// Effective allow list of tool-name glob patterns (empty allows all non-denied tools).
        /// </summary>
        public List<string> AllowedTools { get; set; } = new List<string>();

        /// <summary>
        /// Effective deny list of tool-name glob patterns (empty denies nothing).
        /// </summary>
        public List<string> DeniedTools { get; set; } = new List<string>();

        /// <summary>
        /// Effective additional writable roots (absolute) honored under the workspace-write posture.
        /// </summary>
        public List<string> AdditionalDirectories { get; set; } = new List<string>();

        /// <summary>
        /// Effective non-interactive capability information.
        /// </summary>
        public RuntimeCapabilities Capabilities { get; set; } = new RuntimeCapabilities();

        /// <summary>
        /// Effective runtime metadata useful for automation diagnostics.
        /// </summary>
        public RuntimeMetadata Metadata { get; set; } = new RuntimeMetadata();
    }

    /// <summary>
    /// Effective runtime capabilities for the current command invocation.
    /// </summary>
    public class RuntimeCapabilities
    {
        /// <summary>
        /// Whether built-in tool calling is enabled for the selected endpoint.
        /// </summary>
        public bool ToolsEnabled { get; set; }

        /// <summary>
        /// Number of built-in tools compiled into mux.
        /// </summary>
        public int BuiltInToolCount { get; set; }

        /// <summary>
        /// Number of tools effectively exposed to the model after endpoint capability filtering.
        /// </summary>
        public int EffectiveToolCount { get; set; }

        /// <summary>
        /// Whether the command supports MCP integration.
        /// </summary>
        public bool McpSupported { get; set; }

        /// <summary>
        /// Whether MCP servers are configured in the active config directory.
        /// </summary>
        public bool McpConfigured { get; set; }

        /// <summary>
        /// Number of configured MCP servers in the active config directory.
        /// </summary>
        public int McpServerCount { get; set; }
    }

    /// <summary>
    /// Effective runtime metadata for automation diagnostics and reproducibility.
    /// </summary>
    public class RuntimeMetadata
    {
        /// <summary>
        /// The command mode executing this runtime.
        /// </summary>
        public string CommandName { get; set; } = string.Empty;

        /// <summary>
        /// The effective mux configuration directory.
        /// </summary>
        public string ConfigDirectory { get; set; } = string.Empty;

        /// <summary>
        /// How mux selected the effective endpoint.
        /// </summary>
        public string EndpointSelectionSource { get; set; } = string.Empty;

        /// <summary>
        /// The CLI override categories applied to the resolved runtime.
        /// </summary>
        public List<string> CliOverridesApplied { get; set; } = new List<string>();

        /// <summary>
        /// Whether TLS certificate validation is disabled for mux-owned network requests.
        /// </summary>
        public bool IgnoreCertErrors { get; set; }

        /// <summary>
        /// Whether endpoints.json exists in the active config directory.
        /// </summary>
        public bool EndpointsFilePresent { get; set; }

        /// <summary>
        /// Whether settings.json exists in the active config directory.
        /// </summary>
        public bool SettingsFilePresent { get; set; }

        /// <summary>
        /// Whether mcp-servers.json exists in the active config directory.
        /// </summary>
        public bool McpServersFilePresent { get; set; }
    }
}
