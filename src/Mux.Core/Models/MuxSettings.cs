namespace Mux.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Global settings for mux.
    /// </summary>
    public class MuxSettings
    {
        #region Private-Members

        private string? _SystemPromptPath = null;
        private string _DefaultApprovalPolicy = "ask";
        private int _ToolTimeoutMs = 30000;
        private int _ProcessTimeoutMs = 120000;
        private int _ContextWindowSafetyMarginPercent = 15;
        private double _TokenEstimationRatio = 3.5;
        private bool _AutoCompactEnabled = true;
        private int _ContextWarningThresholdPercent = 80;
        private string _CompactionStrategy = "summary";
        private int _CompactionPreserveTurns = 3;
        private int _MaxAgentIterations = 50;
        private int? _MaxTokenBudget = null;
        private int _MaxConcurrency = 3;
        private string _DefaultEnqueueBehavior = "ask";
        private bool _IgnoreCertErrors = false;
        private bool _ShowBoundaryLines = false;
        private string _TuiTheme = "mux";
        private bool _SkillsEnabled = true;
        private int _SkillRefreshIntervalSeconds = 30;
        private string? _SkillsDirectory = null;
        private bool _ProjectSkillsEnabled = true;
        private List<string> _ProjectSkillRoots = DefaultProjectSkillRoots();
        private string _SkillListingMode = "relevant";
        private bool _ProjectInstructionsEnabled = true;
        private int _ProjectInstructionsMaxBytes = 32768;
        private int _FileMentionMaxBytes = Mux.Core.Context.FileMentionResolver.DefaultMaxBytes;
        private int _LoopMaxIterations = Mux.Core.Jobs.LoopScheduler.DefaultMaxIterations;
        private int _LoopMinIntervalSeconds = Mux.Core.Jobs.LoopScheduler.DefaultMinIntervalSeconds;
        private int _BackgroundProcessMaxConcurrent = Mux.Core.Processes.BackgroundProcessRegistry.DefaultMaxConcurrent;
        private int _BackgroundProcessOutputBytes = Mux.Core.Processes.BackgroundProcessRegistry.DefaultOutputCapacity;
        private bool _MemoryEnabled = true;
        private int _MemoryMaxBytes = DefaultMemoryMaxBytes;
        private string _SkillProdPattern = DefaultSkillProdPattern;
        private bool _TaskPlanningEnabled = true;
        private bool _TaskParallelismEnabled = false;
        private bool _SetupCompleted = false;
        private ExternalSearchSettings _ExternalSearch = new ExternalSearchSettings();
        private ContextSettings _Context = new ContextSettings();
        private RestServerSettings _Rest = new RestServerSettings();
        private TelemetrySettings _Telemetry = new TelemetrySettings();
        private ObservabilitySettings _Observability = new ObservabilitySettings();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Initializes a new instance of the <see cref="MuxSettings"/> class with default values.
        /// </summary>
        public MuxSettings()
        {
        }

        #endregion

        #region Public-Members

        /// <summary>
        /// The file path to the system prompt, or null to use the built-in default.
        /// </summary>
        [JsonPropertyName("systemPromptPath")]
        public string? SystemPromptPath
        {
            get => _SystemPromptPath;
            set => _SystemPromptPath = value;
        }

        /// <summary>
        /// The default approval policy for tool execution. Common values: "ask", "auto", "deny".
        /// </summary>
        [JsonPropertyName("defaultApprovalPolicy")]
        public string DefaultApprovalPolicy
        {
            get => _DefaultApprovalPolicy;
            set => _DefaultApprovalPolicy = value ?? "ask";
        }

        /// <summary>
        /// The timeout in milliseconds for individual tool executions.
        /// Clamped to the range 1000-300000.
        /// </summary>
        [JsonPropertyName("toolTimeoutMs")]
        public int ToolTimeoutMs
        {
            get => _ToolTimeoutMs;
            set => _ToolTimeoutMs = Math.Clamp(value, 1000, 300000);
        }

        /// <summary>
        /// The timeout in milliseconds for spawned processes.
        /// Clamped to the range 1000-600000.
        /// </summary>
        [JsonPropertyName("processTimeoutMs")]
        public int ProcessTimeoutMs
        {
            get => _ProcessTimeoutMs;
            set => _ProcessTimeoutMs = Math.Clamp(value, 1000, 600000);
        }

        /// <summary>
        /// The percentage of the context window to reserve as a safety margin.
        /// Clamped to the range 5-50.
        /// </summary>
        [JsonPropertyName("contextWindowSafetyMarginPercent")]
        public int ContextWindowSafetyMarginPercent
        {
            get => _ContextWindowSafetyMarginPercent;
            set => _ContextWindowSafetyMarginPercent = Math.Clamp(value, 5, 50);
        }

        /// <summary>
        /// The estimated ratio of characters to tokens used for quick token estimation.
        /// Clamped to the range 2.0-6.0.
        /// </summary>
        [JsonPropertyName("tokenEstimationRatio")]
        public double TokenEstimationRatio
        {
            get => _TokenEstimationRatio;
            set => _TokenEstimationRatio = Math.Clamp(value, 2.0, 6.0);
        }

        /// <summary>
        /// Whether mux should automatically compact persisted conversation history before a run
        /// when the estimated prompt would exceed the usable context budget.
        /// </summary>
        [JsonPropertyName("autoCompactEnabled")]
        public bool AutoCompactEnabled
        {
            get => _AutoCompactEnabled;
            set => _AutoCompactEnabled = value;
        }

        /// <summary>
        /// The percentage of the usable input budget at which mux starts warning that context
        /// pressure is getting high. Clamped to the range 50-95.
        /// </summary>
        [JsonPropertyName("contextWarningThresholdPercent")]
        public int ContextWarningThresholdPercent
        {
            get => _ContextWarningThresholdPercent;
            set => _ContextWarningThresholdPercent = Math.Clamp(value, 50, 95);
        }

        /// <summary>
        /// The automatic/manual compaction strategy. Supported values are "summary" and "trim".
        /// Any other value falls back to "summary".
        /// </summary>
        [JsonPropertyName("compactionStrategy")]
        public string CompactionStrategy
        {
            get => _CompactionStrategy;
            set
            {
                _CompactionStrategy = TryNormalizeCompactionStrategy(value, out string normalized)
                    ? normalized
                    : "summary";
            }
        }

        /// <summary>
        /// The number of recent user-led turns to preserve during compaction.
        /// Clamped to the range 1-10.
        /// </summary>
        [JsonPropertyName("compactionPreserveTurns")]
        public int CompactionPreserveTurns
        {
            get => _CompactionPreserveTurns;
            set => _CompactionPreserveTurns = Math.Clamp(value, 1, 10);
        }

        /// <summary>
        /// The maximum number of agent loop iterations before forcing a stop.
        /// Clamped to the range 1-100.
        /// </summary>
        [JsonPropertyName("maxAgentIterations")]
        public int MaxAgentIterations
        {
            get => _MaxAgentIterations;
            set => _MaxAgentIterations = Math.Clamp(value, 1, 100);
        }

        /// <summary>
        /// Optional ceiling on the estimated working-context tokens for a single run. When set and the
        /// estimate exceeds this value before a model call, the run stops cleanly with a
        /// <c>budget_exceeded</c> error rather than continuing. This is a backend-agnostic guard based on
        /// mux's token estimate, not a provider billing figure. Null (the default) disables the cap.
        /// Values are clamped to a minimum of 1 when set.
        /// </summary>
        [JsonPropertyName("maxTokenBudget")]
        public int? MaxTokenBudget
        {
            get => _MaxTokenBudget;
            set => _MaxTokenBudget = value.HasValue ? Math.Max(1, value.Value) : (int?)null;
        }

        /// <summary>
        /// The maximum number of jobs that may run concurrently.
        /// Clamped to the range 1-32. Defaults to 3.
        /// </summary>
        [JsonPropertyName("maxConcurrency")]
        public int MaxConcurrency
        {
            get => _MaxConcurrency;
            set => _MaxConcurrency = Math.Clamp(value, 1, 32);
        }

        /// <summary>
        /// The default behavior when submitting while another job is active.
        /// Supported values are "ask", "run_now", "queue_after", and "add_to_focused".
        /// </summary>
        [JsonPropertyName("defaultEnqueueBehavior")]
        public string DefaultEnqueueBehavior
        {
            get => _DefaultEnqueueBehavior;
            set
            {
                _DefaultEnqueueBehavior = TryNormalizeDefaultEnqueueBehavior(value, out string normalized)
                    ? normalized
                    : "ask";
            }
        }

        /// <summary>
        /// Whether mux-owned network requests should bypass TLS certificate validation.
        /// Intended for enterprise networks that intercept TLS with private certificates.
        /// </summary>
        [JsonPropertyName("ignoreCertErrors")]
        public bool IgnoreCertErrors
        {
            get => _IgnoreCertErrors;
            set => _IgnoreCertErrors = value;
        }

        /// <summary>
        /// Whether the interactive shell draws dark-grey boundary lines: a horizontal rule above the
        /// prompt input, a horizontal rule above the queued-messages strip, and a vertical rule in the
        /// gutter to the left of the sidebar. Defaults to false (off). Toggle live with <c>/borders</c>.
        /// </summary>
        [JsonPropertyName("showBoundaryLines")]
        public bool ShowBoundaryLines
        {
            get => _ShowBoundaryLines;
            set => _ShowBoundaryLines = value;
        }

        /// <summary>
        /// The interactive shell's color theme: <c>mux</c> (default), <c>dark</c>, <c>light</c>, or
        /// <c>highcontrast</c>. Toggle live with <c>/dark</c> and <c>/light</c>. Unknown values fall back to
        /// <c>mux</c>.
        /// </summary>
        [JsonPropertyName("tuiTheme")]
        public string TuiTheme
        {
            get => _TuiTheme;
            set
            {
                switch ((value ?? string.Empty).Trim().ToLowerInvariant())
                {
                    case "dark":
                        _TuiTheme = "dark";
                        break;
                    case "light":
                        _TuiTheme = "light";
                        break;
                    case "highcontrast":
                        _TuiTheme = "highcontrast";
                        break;
                    default:
                        _TuiTheme = "mux";
                        break;
                }
            }
        }

        /// <summary>
        /// Whether user-authored skills are loaded and exposed to the model in the interactive shell.
        /// Defaults to true.
        /// </summary>
        [JsonPropertyName("skillsEnabled")]
        public bool SkillsEnabled
        {
            get => _SkillsEnabled;
            set => _SkillsEnabled = value;
        }

        /// <summary>
        /// How often the interactive shell re-scans the skills directory for changes, in seconds. Clamped to
        /// a minimum of 5. Defaults to 30.
        /// </summary>
        [JsonPropertyName("skillRefreshIntervalSeconds")]
        public int SkillRefreshIntervalSeconds
        {
            get => _SkillRefreshIntervalSeconds;
            set => _SkillRefreshIntervalSeconds = Math.Max(5, value);
        }

        /// <summary>
        /// An optional override for the skills directory, letting a team point mux at a shared, version-
        /// controlled library instead of the default <c>~/.mux/skills</c>. Null uses the default.
        /// </summary>
        [JsonPropertyName("skillsDirectory")]
        public string? SkillsDirectory
        {
            get => _SkillsDirectory;
            set => _SkillsDirectory = string.IsNullOrWhiteSpace(value) ? null : value;
        }

        /// <summary>
        /// Whether skills checked into the current project are discovered from <see cref="ProjectSkillRoots"/>
        /// in addition to the user skills directory. A project skill shadows a user skill with the same id.
        /// Project skills with runnable commands load only after the project is trusted. Defaults to true.
        /// </summary>
        [JsonPropertyName("projectSkillsEnabled")]
        public bool ProjectSkillsEnabled
        {
            get => _ProjectSkillsEnabled;
            set => _ProjectSkillsEnabled = value;
        }

        /// <summary>
        /// The directories, relative to the repository root (or the working directory outside a repository),
        /// searched for project skills, in precedence order. Defaults to <c>.mux/skills</c>,
        /// <c>.claude/skills</c>, and <c>.agents/skills</c>. Null or an empty list restores the defaults;
        /// blank, rooted, and parent-escaping entries are dropped.
        /// </summary>
        [JsonPropertyName("projectSkillRoots")]
        public List<string> ProjectSkillRoots
        {
            get => _ProjectSkillRoots;
            set => _ProjectSkillRoots = NormalizeProjectSkillRoots(value);
        }

        /// <summary>
        /// Which enabled skills are advertised to the model in the system prompt. <c>relevant</c> (the
        /// default) lists skills without <c>appliesTo</c> globs plus those whose globs match a file in the
        /// project; <c>all</c> lists every enabled skill; <c>none</c> lists none (skills stay callable through
        /// the <c>skill</c> tool and by name). Any other value falls back to <c>relevant</c>.
        /// </summary>
        [JsonPropertyName("skillListingMode")]
        public string SkillListingMode
        {
            get => _SkillListingMode;
            set => _SkillListingMode = TryNormalizeSkillListingMode(value, out string normalized) ? normalized : "relevant";
        }

        /// <summary>
        /// The default <see cref="SkillProdPattern"/>.
        /// </summary>
        public const string DefaultSkillProdPattern = "prod|production|live";

        /// <summary>
        /// A case-insensitive regular expression that marks a deployment target (Kubernetes context, cloud profile or
        /// project, Terraform workspace, Helm release target) as production. Skills that change infrastructure refuse
        /// to act on a matching target (exit 3) unless their arguments repeat the target name with
        /// <c>--confirm &lt;name&gt;</c>. Defaults to <c>prod|production|live</c>; blank or invalid patterns fall back
        /// to the default.
        /// </summary>
        [JsonPropertyName("skillProdPattern")]
        public string SkillProdPattern
        {
            get => _SkillProdPattern;
            set => _SkillProdPattern = IsValidPattern(value) ? value!.Trim() : DefaultSkillProdPattern;
        }

        /// <summary>
        /// Whether project instruction files (<c>MUX.md</c>, <c>AGENTS.md</c>, <c>CLAUDE.md</c>, plus the
        /// user-level <c>MUX.md</c> in the config directory) are loaded into the system prompt. Defaults to true.
        /// </summary>
        [JsonPropertyName("projectInstructionsEnabled")]
        public bool ProjectInstructionsEnabled
        {
            get => _ProjectInstructionsEnabled;
            set => _ProjectInstructionsEnabled = value;
        }

        /// <summary>
        /// The maximum combined size, in UTF-8 bytes, of the project instruction files placed in the system
        /// prompt. Default 32768, minimum 0, maximum 1048576. Zero disables loading. When the cap is reached,
        /// the files farthest from the working directory are dropped first.
        /// </summary>
        [JsonPropertyName("projectInstructionsMaxBytes")]
        public int ProjectInstructionsMaxBytes
        {
            get => _ProjectInstructionsMaxBytes;
            set => _ProjectInstructionsMaxBytes = Math.Clamp(value, 0, 1048576);
        }

        /// <summary>
        /// The most text, in UTF-8 bytes, that <c>@path</c> mentions in a prompt may attach. A mention that does not fit
        /// is left out with a note; <c>0</c> turns attachments off. Clamped to 0..16777216. Defaults to 262144.
        /// </summary>
        [JsonPropertyName("fileMentionMaxBytes")]
        public int FileMentionMaxBytes
        {
            get => _FileMentionMaxBytes;
            set => _FileMentionMaxBytes = Math.Clamp(value, 0, Mux.Core.Context.FileMentionResolver.MaxBytesLimit);
        }

        /// <summary>
        /// The iteration cap for a recurring prompt (<c>/loop</c>, <c>mux print --loop</c>) that does not ask for
        /// one, and the most any loop may ask for. Clamped to 1..1000. Defaults to 50.
        /// </summary>
        [JsonPropertyName("loopMaxIterations")]
        public int LoopMaxIterations
        {
            get => _LoopMaxIterations;
            set => _LoopMaxIterations = Math.Clamp(value, 1, Mux.Core.Jobs.LoopScheduler.MaxIterationsLimit);
        }

        /// <summary>
        /// The shortest interval a fixed loop may use, and the shortest delay the model may pass to
        /// <c>schedule_next</c> in a self-paced loop, in seconds. Clamped to 1..3600. Defaults to 30.
        /// </summary>
        [JsonPropertyName("loopMinIntervalSeconds")]
        public int LoopMinIntervalSeconds
        {
            get => _LoopMinIntervalSeconds;
            set => _LoopMinIntervalSeconds = Math.Clamp(value, 1, Mux.Core.Jobs.LoopScheduler.MaxDelaySeconds);
        }

        /// <summary>
        /// The most background processes (<c>process_start</c>) a session may run at once. Clamped to 1..64. Defaults
        /// to 8.
        /// </summary>
        [JsonPropertyName("backgroundProcessMaxConcurrent")]
        public int BackgroundProcessMaxConcurrent
        {
            get => _BackgroundProcessMaxConcurrent;
            set => _BackgroundProcessMaxConcurrent = Math.Clamp(value, 1, Mux.Core.Processes.BackgroundProcessRegistry.MaxConcurrentLimit);
        }

        /// <summary>
        /// Whether persistent memory is on: the <c>remember</c>, <c>forget</c>, and <c>recall</c> tools, the memory index in
        /// the system prompt, <c>#</c> quick-add, and <c>/memory</c>. Defaults to true.
        /// </summary>
        [JsonPropertyName("memoryEnabled")]
        public bool MemoryEnabled
        {
            get => _MemoryEnabled;
            set => _MemoryEnabled = value;
        }

        /// <summary>
        /// The default <see cref="MemoryMaxBytes"/>.
        /// </summary>
        public const int DefaultMemoryMaxBytes = 16384;

        /// <summary>
        /// The most UTF-8 bytes the memory index may add to the system prompt; the oldest entries are left out first.
        /// <c>0</c> leaves the index out (the memory tools still work). Clamped to 0..262144. Defaults to 16384.
        /// </summary>
        [JsonPropertyName("memoryMaxBytes")]
        public int MemoryMaxBytes
        {
            get => _MemoryMaxBytes;
            set => _MemoryMaxBytes = Math.Clamp(value, 0, 262144);
        }

        /// <summary>
        /// The output kept per background process, newest first; older output is dropped. Clamped to 16384..16777216.
        /// Defaults to 1048576 (1 MB).
        /// </summary>
        [JsonPropertyName("backgroundProcessOutputBytes")]
        public int BackgroundProcessOutputBytes
        {
            get => _BackgroundProcessOutputBytes;
            set => _BackgroundProcessOutputBytes = Math.Clamp(value, Mux.Core.Processes.BackgroundProcessRegistry.MinOutputCapacity, Mux.Core.Processes.BackgroundProcessRegistry.MaxOutputCapacity);
        }

        /// <summary>
        /// The bearer key HTTP clients of <c>mux mcp serve --http</c> must send when <c>--api-key</c> is not given.
        /// Null or empty means no key is required. Ignored by the stdio transport.
        /// </summary>
        [JsonPropertyName("mcpServeApiKey")]
        public string? McpServeApiKey { get; set; }

        /// <summary>
        /// Whether the model may decompose a job into a tracked plan of background tasks and advance
        /// them with the <c>plan_tasks</c> and <c>update_task</c> tools. When true, the two task tools
        /// are offered to the model and the system prompt teaches when to plan; when false, neither the
        /// tools nor the guidance are surfaced. Defaults to true.
        /// </summary>
        [JsonPropertyName("taskPlanningEnabled")]
        public bool TaskPlanningEnabled
        {
            get => _TaskPlanningEnabled;
            set => _TaskPlanningEnabled = value;
        }

        /// <summary>
        /// Whether dependency-ready tasks in an orchestrated plan may be dispatched as their own
        /// concurrent jobs, subject to <see cref="MaxConcurrency"/> and the shared workspace write
        /// lease. Has no effect unless <see cref="TaskPlanningEnabled"/> is also true. Defaults to
        /// false, so task planning tracks work by default and only fans out to parallel jobs when the
        /// operator opts in.
        /// </summary>
        [JsonPropertyName("taskParallelismEnabled")]
        public bool TaskParallelismEnabled
        {
            get => _TaskParallelismEnabled;
            set => _TaskParallelismEnabled = value;
        }

        /// <summary>
        /// Whether the first-run setup wizard has been completed (or explicitly dismissed) on this machine.
        /// Once true, the wizard is not shown automatically again; a user can still re-run it from any surface.
        /// Defaults to false so a fresh install is guided through defining an endpoint, checking connectivity,
        /// and sending a first prompt. See <see cref="Mux.Core.Setup.SetupState"/>.
        /// </summary>
        [JsonPropertyName("setupCompleted")]
        public bool SetupCompleted
        {
            get => _SetupCompleted;
            set => _SetupCompleted = value;
        }

        /// <summary>
        /// External web-search provider configuration.
        /// </summary>
        [JsonPropertyName("externalSearch")]
        public ExternalSearchSettings ExternalSearch
        {
            get => _ExternalSearch;
            set => _ExternalSearch = value ?? new ExternalSearchSettings();
        }

        /// <summary>
        /// Large-file context configuration: how oversized files are mapped, summarized, or truncated, and how
        /// the summary cache behaves.
        /// </summary>
        [JsonPropertyName("context")]
        public ContextSettings Context
        {
            get => _Context;
            set => _Context = value ?? new ContextSettings();
        }

        /// <summary>
        /// Configuration for the optional local REST server and system-tray agent. Opt-in; never started by a
        /// plain interactive or headless run.
        /// </summary>
        [JsonPropertyName("rest")]
        public RestServerSettings Rest
        {
            get => _Rest;
            set => _Rest = value ?? new RestServerSettings();
        }

        /// <summary>
        /// Configuration for durable usage telemetry (token/latency/cost history persisted to a local
        /// SQLite database and surfaced in the TUI and dashboard). Enabled by default.
        /// </summary>
        [JsonPropertyName("telemetry")]
        public TelemetrySettings Telemetry
        {
            get => _Telemetry;
            set => _Telemetry = value ?? new TelemetrySettings();
        }

        /// <summary>
        /// Configuration for exporting OpenTelemetry metrics, traces, and logs to an observability stack
        /// (OTLP collector, Prometheus, Loki). Export is off by default; see <c>TELEMETRY.md</c>.
        /// </summary>
        [JsonPropertyName("observability")]
        public ObservabilitySettings Observability
        {
            get => _Observability;
            set => _Observability = value ?? new ObservabilitySettings();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Resolves the effective maximum agent iteration count for an endpoint.
        /// Endpoint-scoped values override the global default when set.
        /// </summary>
        /// <param name="endpoint">The endpoint whose override should be considered.</param>
        /// <returns>The effective clamped maximum agent iteration count.</returns>
        public int GetEffectiveMaxAgentIterations(EndpointConfig? endpoint)
        {
            return endpoint?.MaxAgentIterations ?? MaxAgentIterations;
        }

        /// <summary>
        /// Normalizes a compaction strategy string.
        /// </summary>
        /// <param name="value">The raw strategy value.</param>
        /// <param name="normalized">The normalized value when successful.</param>
        /// <returns>True if the input matched a supported strategy; otherwise false.</returns>
        public static bool TryNormalizeCompactionStrategy(string? value, out string normalized)
        {
            string candidate = (value ?? string.Empty).Trim().ToLowerInvariant();

            switch (candidate)
            {
                case "summary":
                    normalized = "summary";
                    return true;
                case "trim":
                    normalized = "trim";
                    return true;
                default:
                    normalized = "summary";
                    return false;
            }
        }

        /// <summary>
        /// Normalizes a skill listing mode string.
        /// </summary>
        /// <param name="value">The raw mode value.</param>
        /// <param name="normalized">The normalized value (<c>relevant</c>, <c>all</c>, or <c>none</c>).</param>
        /// <returns>True if the input matched a supported mode; otherwise false.</returns>
        public static bool TryNormalizeSkillListingMode(string? value, out string normalized)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "relevant":
                    normalized = "relevant";
                    return true;
                case "all":
                    normalized = "all";
                    return true;
                case "none":
                case "off":
                    normalized = "none";
                    return true;
                default:
                    normalized = "relevant";
                    return false;
            }
        }

        private static bool IsValidPattern(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            try
            {
                _ = new System.Text.RegularExpressions.Regex(value);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        /// <summary>
        /// Returns a new list holding the default project skill roots.
        /// </summary>
        /// <returns>The defaults: <c>.mux/skills</c>, <c>.claude/skills</c>, <c>.agents/skills</c>.</returns>
        public static List<string> DefaultProjectSkillRoots()
        {
            return new List<string> { ".mux/skills", ".claude/skills", ".agents/skills" };
        }

        /// <summary>
        /// Normalizes a list of project skill roots: trims entries, converts backslashes to forward slashes,
        /// drops blank, rooted, and parent-escaping entries and duplicates, and restores the defaults when
        /// nothing usable remains.
        /// </summary>
        /// <param name="roots">The raw roots. May be null.</param>
        /// <returns>The normalized roots; never null or empty.</returns>
        public static List<string> NormalizeProjectSkillRoots(List<string>? roots)
        {
            List<string> result = new List<string>();
            if (roots != null)
            {
                foreach (string raw in roots)
                {
                    string candidate = (raw ?? string.Empty).Trim().Replace('\\', '/').TrimEnd('/');
                    if (candidate.Length == 0
                        || candidate.StartsWith("/", StringComparison.Ordinal)
                        || System.IO.Path.IsPathRooted(candidate)
                        || candidate.Split('/').Contains(".."))
                    {
                        continue;
                    }

                    if (!result.Contains(candidate))
                    {
                        result.Add(candidate);
                    }
                }
            }

            return result.Count > 0 ? result : DefaultProjectSkillRoots();
        }

        /// <summary>
        /// Normalizes a default enqueue behavior string.
        /// </summary>
        /// <param name="value">The raw behavior value.</param>
        /// <param name="normalized">The normalized value when successful.</param>
        /// <returns>True if the input matched a supported behavior; otherwise false.</returns>
        public static bool TryNormalizeDefaultEnqueueBehavior(string? value, out string normalized)
        {
            string candidate = (value ?? string.Empty).Trim().ToLowerInvariant().Replace("-", "_");

            switch (candidate)
            {
                case "ask":
                    normalized = "ask";
                    return true;
                case "run_now":
                case "runnow":
                case "parallel":
                    normalized = "run_now";
                    return true;
                case "queue_after":
                case "queueafter":
                case "queue":
                    normalized = "queue_after";
                    return true;
                case "add_to_focused":
                case "addtofocused":
                case "focused":
                    normalized = "add_to_focused";
                    return true;
                default:
                    normalized = "ask";
                    return false;
            }
        }

        #endregion
    }
}
