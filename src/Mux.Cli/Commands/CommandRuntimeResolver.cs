namespace Mux.Cli.Commands
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
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
            "MCP is only supported in interactive mode. `mux print` and `mux probe` do not load MCP servers, so remove `--no-mcp` and do not rely on MCP tools there.";

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

            EndpointConfig endpoint = SettingsLoader.ResolveEndpoint(
                endpoints,
                settings.Endpoint,
                settings.Model,
                settings.BaseUrl,
                settings.AdapterType,
                settings.Temperature,
                settings.MaxTokens);

            string workingDirectory = settings.WorkingDirectory ?? Directory.GetCurrentDirectory();
            List<string> cliOverrides = GetCliOverrides(settings);
            string endpointSelectionSource = GetEndpointSelectionSource(endpoints, settings.Endpoint);

            BuiltInToolRegistry toolRegistry = new BuiltInToolRegistry();
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

            string systemPrompt = SettingsLoader.LoadSystemPrompt(settings.SystemPrompt, muxSettings);
            if (!toolsEnabled)
            {
                systemPrompt = "You are mux, an AI assistant. You help the user by reading, writing, and editing data including documents, code, and other types " +
                    "in their project.\n\n" +
                    "Your current working directory is: {WorkingDirectory}\n\n" +
                    "Guidelines:\n" +
                    "- Explain your reasoning when making non-trivial suggestions.\n" +
                    "- If a task is ambiguous, ask for clarification before proceeding.";
            }

            systemPrompt = systemPrompt
                .Replace("{WorkingDirectory}", workingDirectory)
                .Replace("{ToolDescriptions}", toolDescBuilder.ToString().TrimEnd());

            ApprovalPolicyEnum approvalPolicy = settings.Yolo
                ? ApprovalPolicyEnum.AutoApprove
                : ApprovalPolicyEnum.Deny;

            if (!string.IsNullOrWhiteSpace(settings.ApprovalPolicy))
            {
                string normalizedPolicy = settings.ApprovalPolicy.Trim().ToLowerInvariant();
                approvalPolicy = normalizedPolicy switch
                {
                    "ask" => ApprovalPolicyEnum.Ask,
                    "deny" => ApprovalPolicyEnum.Deny,
                    "auto" => ApprovalPolicyEnum.AutoApprove,
                    "autoapprove" => ApprovalPolicyEnum.AutoApprove,
                    _ => throw new InvalidOperationException($"Unsupported approval policy '{settings.ApprovalPolicy}'. Supported values: ask, auto, deny.")
                };
            }

            if (!allowAskApproval && approvalPolicy == ApprovalPolicyEnum.Ask)
            {
                throw new InvalidOperationException(
                    $"Approval policy 'ask' is not supported in non-interactive `{commandName}` mode. Use `--approval-policy auto`, `--yolo`, or `--approval-policy deny`.");
            }

            return new ResolvedRuntime
            {
                Endpoint = endpoint,
                MuxSettings = muxSettings,
                WorkingDirectory = workingDirectory,
                SystemPrompt = systemPrompt,
                ApprovalPolicy = approvalPolicy,
                Metadata = new RuntimeMetadata
                {
                    CommandName = commandName,
                    ConfigDirectory = configDirectory,
                    EndpointSelectionSource = endpointSelectionSource,
                    CliOverridesApplied = cliOverrides,
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

            if (!string.IsNullOrWhiteSpace(settings.Endpoint)) overrides.Add("endpoint");
            if (!string.IsNullOrWhiteSpace(settings.Model)) overrides.Add("model");
            if (!string.IsNullOrWhiteSpace(settings.BaseUrl)) overrides.Add("baseUrl");
            if (!string.IsNullOrWhiteSpace(settings.AdapterType)) overrides.Add("adapterType");
            if (settings.Temperature.HasValue) overrides.Add("temperature");
            if (settings.MaxTokens.HasValue) overrides.Add("maxTokens");
            if (!string.IsNullOrWhiteSpace(settings.WorkingDirectory)) overrides.Add("workingDirectory");
            if (!string.IsNullOrWhiteSpace(settings.SystemPrompt)) overrides.Add("systemPrompt");
            if (settings.Yolo) overrides.Add("yolo");
            if (!string.IsNullOrWhiteSpace(settings.ApprovalPolicy)) overrides.Add("approvalPolicy");

            return overrides;
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
        /// Effective working directory.
        /// </summary>
        public string WorkingDirectory { get; set; } = string.Empty;

        /// <summary>
        /// Effective system prompt.
        /// </summary>
        public string SystemPrompt { get; set; } = string.Empty;

        /// <summary>
        /// Effective approval policy.
        /// </summary>
        public ApprovalPolicyEnum ApprovalPolicy { get; set; } = ApprovalPolicyEnum.Deny;

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
