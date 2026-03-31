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
            SettingsLoader.EnsureConfigDirectory();
            List<EndpointConfig> endpoints = SettingsLoader.LoadEndpoints();
            MuxSettings muxSettings = SettingsLoader.LoadSettings();

            EndpointConfig endpoint = SettingsLoader.ResolveEndpoint(
                endpoints,
                settings.Endpoint,
                settings.Model,
                settings.BaseUrl,
                settings.AdapterType,
                settings.Temperature,
                settings.MaxTokens);

            string workingDirectory = settings.WorkingDirectory ?? Directory.GetCurrentDirectory();

            BuiltInToolRegistry toolRegistry = new BuiltInToolRegistry();
            List<ToolDefinition> builtInTools = toolRegistry.GetToolDefinitions();
            bool toolsEnabled = endpoint.Quirks?.SupportsTools ?? true;

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

            return new ResolvedRuntime
            {
                Endpoint = endpoint,
                MuxSettings = muxSettings,
                WorkingDirectory = workingDirectory,
                SystemPrompt = systemPrompt,
                ApprovalPolicy = approvalPolicy
            };
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
    }
}
