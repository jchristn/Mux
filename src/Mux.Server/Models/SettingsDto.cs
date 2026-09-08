namespace Mux.Server.Models
{
    using Mux.Core.Models;

    /// <summary>
    /// The REST-server portion of the editable settings, with the API key masked (never returned in
    /// plaintext).
    /// </summary>
    public class RestSettingsDto
    {
        /// <summary>Whether the tray agent auto-starts the server.</summary>
        public bool Enabled { get; set; }

        /// <summary>Bind host.</summary>
        public string Hostname { get; set; } = "127.0.0.1";

        /// <summary>Bind port.</summary>
        public int Port { get; set; } = 8710;

        /// <summary>Bind with SSL.</summary>
        public bool Ssl { get; set; }

        /// <summary>CORS allow-origin.</summary>
        public string CorsAllowOrigin { get; set; } = "*";

        /// <summary>Whether an API key is set (read side; the value is never returned).</summary>
        public bool ApiKeySet { get; set; }

        /// <summary>Write side only: a new API key to set. Ignored on read; ignored on write when blank.</summary>
        public string? ApiKey { get; set; }
    }

    /// <summary>
    /// The editable subset of <see cref="MuxSettings"/> surfaced to the dashboard settings form. Secrets are
    /// masked; the raw settings model is never returned wholesale.
    /// </summary>
    public class SettingsDto
    {
        /// <summary>Default approval policy (ask/auto/deny).</summary>
        public string DefaultApprovalPolicy { get; set; } = "ask";

        /// <summary>Max agent iterations (1-100).</summary>
        public int MaxAgentIterations { get; set; } = 50;

        /// <summary>Max concurrent jobs (1-32).</summary>
        public int MaxConcurrency { get; set; } = 3;

        /// <summary>Per-tool timeout (ms).</summary>
        public int ToolTimeoutMs { get; set; } = 30000;

        /// <summary>Per-process timeout (ms).</summary>
        public int ProcessTimeoutMs { get; set; } = 120000;

        /// <summary>Auto-compaction enabled.</summary>
        public bool AutoCompactEnabled { get; set; } = true;

        /// <summary>Compaction strategy (summary/trim).</summary>
        public string CompactionStrategy { get; set; } = "summary";

        /// <summary>Turns preserved during compaction (1-10).</summary>
        public int CompactionPreserveTurns { get; set; } = 3;

        /// <summary>Context warning threshold percent (50-95).</summary>
        public int ContextWarningThresholdPercent { get; set; } = 80;

        /// <summary>Skills enabled.</summary>
        public bool SkillsEnabled { get; set; } = true;

        /// <summary>Task planning enabled.</summary>
        public bool TaskPlanningEnabled { get; set; } = true;

        /// <summary>Task parallelism enabled.</summary>
        public bool TaskParallelismEnabled { get; set; }

        /// <summary>Ignore TLS certificate errors.</summary>
        public bool IgnoreCertErrors { get; set; }

        /// <summary>Show interactive boundary lines.</summary>
        public bool ShowBoundaryLines { get; set; }

        /// <summary>Default enqueue behavior.</summary>
        public string DefaultEnqueueBehavior { get; set; } = "ask";

        /// <summary>REST server settings (API key masked).</summary>
        public RestSettingsDto Rest { get; set; } = new RestSettingsDto();

        /// <summary>
        /// Project a <see cref="MuxSettings"/> into a masked DTO.
        /// </summary>
        /// <param name="settings">Source settings.</param>
        /// <returns>A DTO safe to return to the dashboard.</returns>
        public static SettingsDto FromSettings(MuxSettings settings)
        {
            return new SettingsDto
            {
                DefaultApprovalPolicy = settings.DefaultApprovalPolicy,
                MaxAgentIterations = settings.MaxAgentIterations,
                MaxConcurrency = settings.MaxConcurrency,
                ToolTimeoutMs = settings.ToolTimeoutMs,
                ProcessTimeoutMs = settings.ProcessTimeoutMs,
                AutoCompactEnabled = settings.AutoCompactEnabled,
                CompactionStrategy = settings.CompactionStrategy,
                CompactionPreserveTurns = settings.CompactionPreserveTurns,
                ContextWarningThresholdPercent = settings.ContextWarningThresholdPercent,
                SkillsEnabled = settings.SkillsEnabled,
                TaskPlanningEnabled = settings.TaskPlanningEnabled,
                TaskParallelismEnabled = settings.TaskParallelismEnabled,
                IgnoreCertErrors = settings.IgnoreCertErrors,
                ShowBoundaryLines = settings.ShowBoundaryLines,
                DefaultEnqueueBehavior = settings.DefaultEnqueueBehavior,
                Rest = new RestSettingsDto
                {
                    Enabled = settings.Rest.Enabled,
                    Hostname = settings.Rest.Hostname,
                    Port = settings.Rest.Port,
                    Ssl = settings.Rest.Ssl,
                    CorsAllowOrigin = settings.Rest.CorsAllowOrigin,
                    ApiKeySet = !string.IsNullOrEmpty(settings.Rest.ApiKey),
                    ApiKey = null
                }
            };
        }

        /// <summary>
        /// Apply this DTO onto a <see cref="MuxSettings"/> instance (property setters clamp/normalize). The
        /// REST API key is only changed when a non-blank value is supplied.
        /// </summary>
        /// <param name="settings">Target settings to mutate.</param>
        public void ApplyTo(MuxSettings settings)
        {
            settings.DefaultApprovalPolicy = DefaultApprovalPolicy;
            settings.MaxAgentIterations = MaxAgentIterations;
            settings.MaxConcurrency = MaxConcurrency;
            settings.ToolTimeoutMs = ToolTimeoutMs;
            settings.ProcessTimeoutMs = ProcessTimeoutMs;
            settings.AutoCompactEnabled = AutoCompactEnabled;
            settings.CompactionStrategy = CompactionStrategy;
            settings.CompactionPreserveTurns = CompactionPreserveTurns;
            settings.ContextWarningThresholdPercent = ContextWarningThresholdPercent;
            settings.SkillsEnabled = SkillsEnabled;
            settings.TaskPlanningEnabled = TaskPlanningEnabled;
            settings.TaskParallelismEnabled = TaskParallelismEnabled;
            settings.IgnoreCertErrors = IgnoreCertErrors;
            settings.ShowBoundaryLines = ShowBoundaryLines;
            settings.DefaultEnqueueBehavior = DefaultEnqueueBehavior;

            if (Rest != null)
            {
                settings.Rest.Enabled = Rest.Enabled;
                settings.Rest.Hostname = Rest.Hostname;
                settings.Rest.Port = Rest.Port;
                settings.Rest.Ssl = Rest.Ssl;
                settings.Rest.CorsAllowOrigin = Rest.CorsAllowOrigin;
                if (!string.IsNullOrWhiteSpace(Rest.ApiKey))
                {
                    settings.Rest.ApiKey = Rest.ApiKey;
                }
            }
        }
    }
}
