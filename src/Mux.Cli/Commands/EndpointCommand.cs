namespace Mux.Cli.Commands
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Mux.Core.Models;
    using Mux.Core.Settings;
    using Spectre.Console;
    using Spectre.Console.Cli;

    /// <summary>
    /// Settings for non-interactive endpoint inspection commands.
    /// </summary>
    public class EndpointSettings : CommandSettings
    {
        /// <summary>
        /// The endpoint inspection action to perform.
        /// </summary>
        [Description("Endpoint action: list or show.")]
        [CommandArgument(0, "<action>")]
        public string Action { get; set; } = string.Empty;

        /// <summary>
        /// Optional endpoint name for the show action.
        /// </summary>
        [Description("Endpoint name for the show action.")]
        [CommandArgument(1, "[name]")]
        public string? Name { get; set; }

        /// <summary>
        /// Output format for the inspection result.
        /// </summary>
        [Description("Output format: text or json.")]
        [CommandOption("--output-format")]
        public string? OutputFormat { get; set; }

        /// <summary>
        /// Override the active config directory used for endpoint inspection.
        /// </summary>
        [Description("Override the active config directory.")]
        [CommandOption("--config-dir")]
        public string? ConfigDir { get; set; }
    }

    /// <summary>
    /// Lists or shows configured endpoints in a machine-readable form.
    /// </summary>
    public class EndpointCommand : AsyncCommand<EndpointSettings>
    {
        /// <inheritdoc />
        public override Task<int> ExecuteAsync(CommandContext context, EndpointSettings settings, CancellationToken cancellationToken)
        {
            using IDisposable configScope = SettingsLoader.PushConfigDirectoryOverride(settings.ConfigDir);

            OutputFormatEnum outputFormat;
            try
            {
                outputFormat = CommandRuntimeResolver.ParseOutputFormat(settings.OutputFormat, OutputFormatEnum.Text, OutputFormatEnum.Json);
            }
            catch (Exception ex)
            {
                EmitError(settings.OutputFormat, "invalid_argument", ex.Message, SettingsLoader.GetConfigDirectory());
                return Task.FromResult(1);
            }

            try
            {
                SettingsLoader.EnsureConfigDirectory();
                string configDirectory = SettingsLoader.GetConfigDirectory();
                List<EndpointConfig> endpoints = SettingsLoader.LoadEndpoints();
                string action = (settings.Action ?? string.Empty).Trim().ToLowerInvariant();

                switch (action)
                {
                    case "list":
                        return Task.FromResult(HandleList(outputFormat, configDirectory, endpoints));
                    case "show":
                        return Task.FromResult(HandleShow(outputFormat, configDirectory, endpoints, settings.Name));
                    default:
                        EmitError(outputFormat, "invalid_argument", "Usage: mux endpoint list [--output-format json] or mux endpoint show <name> [--output-format json].", configDirectory);
                        return Task.FromResult(1);
                }
            }
            catch (Exception ex)
            {
                EmitError(outputFormat, "endpoint_error", ex.Message, SettingsLoader.GetConfigDirectory());
                return Task.FromResult(1);
            }
        }

        private static int HandleList(OutputFormatEnum outputFormat, string configDirectory, List<EndpointConfig> endpoints)
        {
            List<EndpointInspectionRecord> inspected = endpoints.Select(ToInspectionRecord).ToList();
            EndpointListResult result = new EndpointListResult
            {
                Success = true,
                ConfigDirectory = configDirectory,
                Endpoints = inspected
            };

            if (outputFormat == OutputFormatEnum.Json)
            {
                Console.WriteLine(StructuredOutputFormatter.FormatObject(result));
            }
            else
            {
                Table table = new Table();
                table.AddColumn("Name");
                table.AddColumn("Adapter");
                table.AddColumn("Model");
                table.AddColumn("Base URL");
                table.AddColumn("Default");
                table.AddColumn("Tools");

                foreach (EndpointInspectionRecord endpoint in inspected)
                {
                    table.AddRow(
                        endpoint.Name,
                        endpoint.AdapterType,
                        endpoint.Model,
                        endpoint.BaseUrl,
                        endpoint.IsDefault ? "[green]yes[/]" : "no",
                        endpoint.ToolsEnabled ? "[green]enabled[/]" : "[yellow]disabled[/]");
                }

                AnsiConsole.Write(table);
            }

            return 0;
        }

        private static int HandleShow(OutputFormatEnum outputFormat, string configDirectory, List<EndpointConfig> endpoints, string? endpointName)
        {
            if (string.IsNullOrWhiteSpace(endpointName))
            {
                EmitError(outputFormat, "invalid_argument", "Usage: mux endpoint show <name> [--output-format json].", configDirectory);
                return 1;
            }

            EndpointConfig? found = endpoints.FirstOrDefault(
                endpoint => string.Equals(endpoint.Name, endpointName, StringComparison.OrdinalIgnoreCase));

            if (found == null)
            {
                EmitError(outputFormat, "endpoint_not_found", $"No endpoint named '{endpointName}' was found in {System.IO.Path.Combine(configDirectory, "endpoints.json")}.", configDirectory);
                return 1;
            }

            EndpointInspectionRecord inspected = ToInspectionRecord(found);
            EndpointShowResult result = new EndpointShowResult
            {
                Success = true,
                ConfigDirectory = configDirectory,
                Endpoint = inspected
            };

            if (outputFormat == OutputFormatEnum.Json)
            {
                Console.WriteLine(StructuredOutputFormatter.FormatObject(result));
            }
            else
            {
                WriteTextEndpoint(inspected);
            }

            return 0;
        }

        private static EndpointInspectionRecord ToInspectionRecord(EndpointConfig endpoint)
        {
            BackendQuirks quirks = endpoint.Quirks ?? Defaults.QuirksForAdapter(endpoint.AdapterType);
            Dictionary<string, string> headers = endpoint.Headers ?? new Dictionary<string, string>();

            return new EndpointInspectionRecord
            {
                Name = endpoint.Name,
                AdapterType = endpoint.AdapterType.ToString(),
                BaseUrl = endpoint.BaseUrl,
                Model = endpoint.Model,
                IsDefault = endpoint.IsDefault,
                MaxTokens = endpoint.MaxTokens,
                Temperature = endpoint.Temperature,
                ContextWindow = endpoint.ContextWindow,
                TimeoutMs = endpoint.TimeoutMs,
                ToolsEnabled = quirks.SupportsTools,
                HeaderNames = headers.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToList(),
                Headers = headers.ToDictionary(
                    pair => pair.Key,
                    pair => string.IsNullOrWhiteSpace(pair.Value) ? string.Empty : "[redacted]")
            };
        }

        private static void WriteTextEndpoint(EndpointInspectionRecord endpoint)
        {
            AnsiConsole.MarkupLine($"[bold]Name:[/] {Markup.Escape(endpoint.Name)}");
            AnsiConsole.MarkupLine($"[bold]Adapter:[/] {Markup.Escape(endpoint.AdapterType)}");
            AnsiConsole.MarkupLine($"[bold]Base URL:[/] {Markup.Escape(endpoint.BaseUrl)}");
            AnsiConsole.MarkupLine($"[bold]Model:[/] {Markup.Escape(endpoint.Model)}");
            AnsiConsole.MarkupLine($"[bold]Default:[/] {(endpoint.IsDefault ? "[green]yes[/]" : "no")}");
            AnsiConsole.MarkupLine($"[bold]Max Tokens:[/] {endpoint.MaxTokens}");
            AnsiConsole.MarkupLine($"[bold]Temperature:[/] {endpoint.Temperature}");
            AnsiConsole.MarkupLine($"[bold]Context Window:[/] {endpoint.ContextWindow}");
            AnsiConsole.MarkupLine($"[bold]Timeout:[/] {endpoint.TimeoutMs}ms");
            AnsiConsole.MarkupLine($"[bold]Tool Calling:[/] {(endpoint.ToolsEnabled ? "[green]enabled[/]" : "[yellow]disabled[/]")}");
            if (endpoint.HeaderNames.Count > 0)
            {
                AnsiConsole.MarkupLine($"[bold]Headers:[/] {Markup.Escape(string.Join(", ", endpoint.HeaderNames))}");
            }
            else
            {
                AnsiConsole.MarkupLine("[bold]Headers:[/] none");
            }
        }

        private static void EmitError(OutputFormatEnum outputFormat, string errorCode, string message, string configDirectory)
        {
            if (outputFormat == OutputFormatEnum.Json)
            {
                Console.WriteLine(StructuredOutputFormatter.FormatObject(new EndpointInspectionErrorResult
                {
                    Success = false,
                    ErrorCode = errorCode,
                    ErrorMessage = message,
                    ConfigDirectory = configDirectory
                }));
            }
            else
            {
                Console.Error.WriteLine(message);
            }
        }

        private static void EmitError(string? outputFormatValue, string errorCode, string message, string configDirectory)
        {
            bool wantsJson = string.Equals(outputFormatValue, "json", StringComparison.OrdinalIgnoreCase);
            EmitError(wantsJson ? OutputFormatEnum.Json : OutputFormatEnum.Text, errorCode, message, configDirectory);
        }
    }

    /// <summary>
    /// Machine-readable endpoint list payload.
    /// </summary>
    public class EndpointListResult
    {
        /// <summary>
        /// Whether the command succeeded.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// The effective config directory used for the inspection.
        /// </summary>
        public string ConfigDirectory { get; set; } = string.Empty;

        /// <summary>
        /// The configured endpoints.
        /// </summary>
        public List<EndpointInspectionRecord> Endpoints { get; set; } = new List<EndpointInspectionRecord>();
    }

    /// <summary>
    /// Machine-readable endpoint show payload.
    /// </summary>
    public class EndpointShowResult
    {
        /// <summary>
        /// Whether the command succeeded.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// The effective config directory used for the inspection.
        /// </summary>
        public string ConfigDirectory { get; set; } = string.Empty;

        /// <summary>
        /// The inspected endpoint.
        /// </summary>
        public EndpointInspectionRecord Endpoint { get; set; } = new EndpointInspectionRecord();
    }

    /// <summary>
    /// Machine-readable endpoint metadata with redacted header values.
    /// </summary>
    public class EndpointInspectionRecord
    {
        /// <summary>
        /// Endpoint name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Adapter type.
        /// </summary>
        public string AdapterType { get; set; } = string.Empty;

        /// <summary>
        /// Base URL.
        /// </summary>
        public string BaseUrl { get; set; } = string.Empty;

        /// <summary>
        /// Model identifier.
        /// </summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>
        /// Whether this is the default endpoint.
        /// </summary>
        public bool IsDefault { get; set; }

        /// <summary>
        /// Maximum output tokens.
        /// </summary>
        public int MaxTokens { get; set; }

        /// <summary>
        /// Sampling temperature.
        /// </summary>
        public double Temperature { get; set; }

        /// <summary>
        /// Context window size.
        /// </summary>
        public int ContextWindow { get; set; }

        /// <summary>
        /// Timeout in milliseconds.
        /// </summary>
        public int TimeoutMs { get; set; }

        /// <summary>
        /// Whether tool calling is enabled.
        /// </summary>
        public bool ToolsEnabled { get; set; }

        /// <summary>
        /// Header names configured for the endpoint.
        /// </summary>
        public List<string> HeaderNames { get; set; } = new List<string>();

        /// <summary>
        /// Header values with secrets redacted.
        /// </summary>
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Machine-readable endpoint inspection error payload.
    /// </summary>
    public class EndpointInspectionErrorResult
    {
        /// <summary>
        /// Whether the command succeeded.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Error code.
        /// </summary>
        public string ErrorCode { get; set; } = string.Empty;

        /// <summary>
        /// Human-readable error message.
        /// </summary>
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>
        /// Effective config directory.
        /// </summary>
        public string ConfigDirectory { get; set; } = string.Empty;
    }
}
