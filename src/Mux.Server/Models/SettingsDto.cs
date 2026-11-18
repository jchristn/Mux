namespace Mux.Server.Models
{
    using Mux.Core.Models;

    /// <summary>
    /// The REST-server portion of the editable settings, with the API key masked (never returned in
    /// plaintext).
    /// </summary>
    public class RestSettingsDto
    {
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
    /// The Mux Cloud portion of the editable settings. The machine secret is never returned; signing in and out goes
    /// through <c>POST /v1.0/api/cloud/login</c> and <c>/logout</c>.
    /// </summary>
    public class CloudSettingsDto
    {
        /// <summary>Whether this machine connects to Mux Cloud (only effective once signed in).</summary>
        public bool Enabled { get; set; }

        /// <summary>Mux Cloud server URL.</summary>
        public string ServerUrl { get; set; } = MuxCloud.Protocol.TunnelProtocol.DefaultServerUrl;

        /// <summary>Machine display name, or null for the host name.</summary>
        public string? MachineName { get; set; }

        /// <summary>Read only: enrolled machine id, or null.</summary>
        public string? MachineId { get; set; }

        /// <summary>Read only: account email, or null.</summary>
        public string? AccountEmail { get; set; }

        /// <summary>Read only: whether a machine secret is stored (the value is never returned).</summary>
        public bool MachineSecretSet { get; set; }

        /// <summary>Whether remote users may approve tools on this machine. Default true.</summary>
        public bool AllowRemoteToolApproval { get; set; } = true;

        /// <summary>Heartbeat interval in seconds (5-300).</summary>
        public int HeartbeatSeconds { get; set; } = 20;

        /// <summary>Largest reconnect delay in seconds (5-3600).</summary>
        public int ReconnectMaxDelaySeconds { get; set; } = 60;

        /// <summary>Accept invalid TLS certificates from a self-hosted server.</summary>
        public bool AllowInvalidCertificates { get; set; }
    }

    /// <summary>
    /// The editable subset of <see cref="MuxSettings"/> surfaced to the dashboard settings form. Secrets are
    /// masked; the raw settings model is never returned wholesale.
    /// </summary>
    public class SettingsDto
    {
        /// <summary>Default approval policy (ask/auto/deny).</summary>
        public string DefaultApprovalPolicy { get; set; } = "ask";

        /// <summary>Interactive tool approval countdown in seconds; zero disables.</summary>
        public int ToolAutoApproveSeconds { get; set; }

        /// <summary>Max agent iterations (1-1024).</summary>
        public int MaxAgentIterations { get; set; } = 256;

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

        /// <summary>Whether the first-run setup wizard has been completed or dismissed on this machine.</summary>
        public bool SetupCompleted { get; set; }

        /// <summary>Ignore TLS certificate errors.</summary>
        public bool IgnoreCertErrors { get; set; }

        /// <summary>Show interactive boundary lines.</summary>
        public bool ShowBoundaryLines { get; set; }

        /// <summary>Default enqueue behavior.</summary>
        public string DefaultEnqueueBehavior { get; set; } = "ask";

        /// <summary>Large-file mode (map/summarize/truncate).</summary>
        public string LargeFileMode { get; set; } = "map";

        /// <summary>Inline size gate in bytes (fallback when the endpoint's context window is unknown).</summary>
        public int InlineThresholdBytes { get; set; } = 65536;

        /// <summary>Fraction of the endpoint's context window a file may occupy before it is mapped/summarized.</summary>
        public double InlineContextWindowFraction { get; set; } = 0.25;

        /// <summary>Lines per summarizer chunk.</summary>
        public int SummaryChunkLines { get; set; } = 400;

        /// <summary>Whether summarized large-file context is cached.</summary>
        public bool SummaryCacheEnabled { get; set; } = true;

        /// <summary>Summary-cache retention in days.</summary>
        public int SummaryCacheRetentionDays { get; set; } = 7;

        /// <summary>REST server settings (API key masked).</summary>
        public RestSettingsDto Rest { get; set; } = new RestSettingsDto();

        /// <summary>Mux Cloud connection settings (secret masked).</summary>
        public CloudSettingsDto? Cloud { get; set; }

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
                ToolAutoApproveSeconds = settings.ToolAutoApproveSeconds,
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
                SetupCompleted = settings.SetupCompleted,
                IgnoreCertErrors = settings.IgnoreCertErrors,
                ShowBoundaryLines = settings.ShowBoundaryLines,
                DefaultEnqueueBehavior = settings.DefaultEnqueueBehavior,
                LargeFileMode = settings.Context.LargeFileMode,
                InlineThresholdBytes = settings.Context.InlineThresholdBytes,
                InlineContextWindowFraction = settings.Context.InlineContextWindowFraction,
                SummaryChunkLines = settings.Context.SummaryChunkLines,
                SummaryCacheEnabled = settings.Context.SummaryCacheEnabled,
                SummaryCacheRetentionDays = settings.Context.SummaryCacheRetentionDays,
                Rest = new RestSettingsDto
                {
                    Hostname = settings.Rest.Hostname,
                    Port = settings.Rest.Port,
                    Ssl = settings.Rest.Ssl,
                    CorsAllowOrigin = settings.Rest.CorsAllowOrigin,
                    ApiKeySet = !string.IsNullOrEmpty(settings.Rest.ApiKey),
                    ApiKey = null
                },
                Cloud = new CloudSettingsDto
                {
                    Enabled = settings.Cloud.Enabled,
                    ServerUrl = settings.Cloud.ServerUrl,
                    MachineName = settings.Cloud.MachineName,
                    MachineId = settings.Cloud.MachineId,
                    AccountEmail = settings.Cloud.AccountEmail,
                    MachineSecretSet = !string.IsNullOrEmpty(settings.Cloud.MachineSecret),
                    AllowRemoteToolApproval = settings.Cloud.AllowRemoteToolApproval,
                    HeartbeatSeconds = settings.Cloud.HeartbeatSeconds,
                    ReconnectMaxDelaySeconds = settings.Cloud.ReconnectMaxDelaySeconds,
                    AllowInvalidCertificates = settings.Cloud.AllowInvalidCertificates
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
            settings.ToolAutoApproveSeconds = ToolAutoApproveSeconds;
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
            settings.SetupCompleted = SetupCompleted;
            settings.IgnoreCertErrors = IgnoreCertErrors;
            settings.ShowBoundaryLines = ShowBoundaryLines;
            settings.DefaultEnqueueBehavior = DefaultEnqueueBehavior;
            settings.Context.LargeFileMode = LargeFileMode;
            settings.Context.InlineThresholdBytes = InlineThresholdBytes;
            settings.Context.InlineContextWindowFraction = InlineContextWindowFraction;
            settings.Context.SummaryChunkLines = SummaryChunkLines;
            settings.Context.SummaryCacheEnabled = SummaryCacheEnabled;
            settings.Context.SummaryCacheRetentionDays = SummaryCacheRetentionDays;

            if (Rest != null)
            {
                settings.Rest.Hostname = Rest.Hostname;
                settings.Rest.Port = Rest.Port;
                settings.Rest.Ssl = Rest.Ssl;
                settings.Rest.CorsAllowOrigin = Rest.CorsAllowOrigin;
                if (!string.IsNullOrWhiteSpace(Rest.ApiKey))
                {
                    settings.Rest.ApiKey = Rest.ApiKey;
                }
            }

            // Every cloud field except the credential is editable from any surface (including remotely, by the
            // parity rule). The machine id, secret, and account email change only by signing in or out.
            if (Cloud != null)
            {
                settings.Cloud.Enabled = Cloud.Enabled;
                settings.Cloud.ServerUrl = Cloud.ServerUrl;
                settings.Cloud.MachineName = Cloud.MachineName;
                settings.Cloud.AllowRemoteToolApproval = Cloud.AllowRemoteToolApproval;
                settings.Cloud.HeartbeatSeconds = Cloud.HeartbeatSeconds;
                settings.Cloud.ReconnectMaxDelaySeconds = Cloud.ReconnectMaxDelaySeconds;
                settings.Cloud.AllowInvalidCertificates = Cloud.AllowInvalidCertificates;
            }
        }
    }
}
